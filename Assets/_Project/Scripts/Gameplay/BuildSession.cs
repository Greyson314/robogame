using System;
using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// The mutable, plain-C# model the build-mode driver components
    /// (editor, mirror toggle, variant panel, hotbar) all read and
    /// write through. One source of truth for "what is the player
    /// currently editing": which grid, which block id is selected,
    /// what dims / pitch will the next placement use, is mirror on,
    /// across which axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why plain C#.</b> Build-mode logic should be testable without
    /// a scene. Placement decisions, variant cache lifecycles, and
    /// mirror policy are all data — none of them care about Unity's
    /// MonoBehaviour lifecycle. Editor/UI components remain
    /// MonoBehaviours; they just delegate the model state to here.
    /// </para>
    /// <para>
    /// <b>Atomic verbs.</b> <see cref="TryPlace"/> and <see cref="TryRemove"/>
    /// route through <see cref="PlacementRules"/>, mutate the live
    /// <see cref="BlockGrid"/>, and (on success) call <see cref="SyncBlueprint"/>
    /// so the persisted blueprint stays in lockstep with the grid.
    /// </para>
    /// </remarks>
    public sealed class BuildSession
    {
        // -----------------------------------------------------------------
        // Live grid + blueprint reference
        // -----------------------------------------------------------------

        public BlockGrid Grid { get; private set; }
        public ChassisBlueprint Blueprint { get; private set; }
        public BlockDefinitionLibrary Library { get; private set; }

        public void Bind(BlockGrid grid, ChassisBlueprint blueprint, BlockDefinitionLibrary library)
        {
            Grid = grid;
            Blueprint = blueprint;
            Library = library;
        }

        // -----------------------------------------------------------------
        // Selected block + variant cache
        // -----------------------------------------------------------------

        public string SelectedBlockId { get; private set; }

        /// <summary>Raised after <see cref="SetSelectedBlock"/> mutates the selection.</summary>
        public event Action<string> SelectedBlockChanged;

        /// <summary>
        /// The single placed block currently bound for per-instance editing
        /// (set by the Edit-mode click), or null in normal placement mode.
        /// When set, a variant-slider change applies to THIS block only;
        /// when null, no placed block is touched — the variant cache only
        /// shapes the next placement. So the player can retune one rotor's
        /// RPM without touching the others, and without deleting/orphaning
        /// it. Session 125; propagate-to-all fallback retired by the
        /// span-isolation session. Unity's null check covers the block
        /// being destroyed while bound.
        /// </summary>
        public BlockBehaviour EditingInstance { get; private set; }

        /// <summary>Raised when <see cref="EditingInstance"/> is set or cleared (highlight + title react).</summary>
        public event Action<BlockBehaviour> EditingInstanceChanged;

        public void SetEditingInstance(BlockBehaviour block)
        {
            if (ReferenceEquals(EditingInstance, block)) return;
            EditingInstance = block;
            EditingInstanceChanged?.Invoke(block);
        }

        public void SetSelectedBlock(string blockId)
        {
            if (SelectedBlockId == blockId) return;
            SelectedBlockId = blockId;
            SelectedBlockChanged?.Invoke(blockId);
        }

        // Per-block-id "next placement" caches. Vector3.zero / 0f
        // mean "use the block's authored defaults" — the consuming
        // component decides what those are at place time.
        private readonly Dictionary<string, Vector3> _dimsByBlockId = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, float> _pitchByBlockId = new Dictionary<string, float>();
        // Per-block foil teeter tilt (deg, world-intent in the cache —
        // normalized to local-frame per side at placement, like pitch).
        private readonly Dictionary<string, float> _teeterByBlockId = new Dictionary<string, float>();
        // Per-block server-authoritative scalar (thruster thrust / rudder
        // authority / rotor RPM). 0 = use the block's historical default.
        private readonly Dictionary<string, float> _configByBlockId = new Dictionary<string, float>();
        // Per-block concoction id (explosive weapons). "" = no concoction →
        // baseline stats. Rides the same per-id "next placement" cache. ADR-0004.
        private readonly Dictionary<string, string> _concoctionByBlockId = new Dictionary<string, string>();

        /// <summary>Raised when any per-block variant config changes.</summary>
        public event Action<string> VariantChanged;

        public Vector3 GetVariantDims(string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return Vector3.zero;
            _dimsByBlockId.TryGetValue(blockId, out Vector3 v);
            return v;
        }

        public float GetVariantPitch(string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return 0f;
            _pitchByBlockId.TryGetValue(blockId, out float v);
            return v;
        }

        public void SetVariantDims(string blockId, Vector3 dims)
        {
            if (string.IsNullOrEmpty(blockId)) return;
            _dimsByBlockId[blockId] = dims;
            VariantChanged?.Invoke(blockId);
        }

        public void SetVariantPitch(string blockId, float pitchDeg)
        {
            if (string.IsNullOrEmpty(blockId)) return;
            _pitchByBlockId[blockId] = pitchDeg;
            VariantChanged?.Invoke(blockId);
        }

        public float GetVariantTeeter(string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return 0f;
            _teeterByBlockId.TryGetValue(blockId, out float v);
            return v;
        }

        public void SetVariantTeeter(string blockId, float teeterDeg)
        {
            if (string.IsNullOrEmpty(blockId)) return;
            _teeterByBlockId[blockId] = teeterDeg;
            VariantChanged?.Invoke(blockId);
        }

        public float GetVariantConfig(string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return 0f;
            _configByBlockId.TryGetValue(blockId, out float v);
            return v;
        }

        public void SetVariantConfig(string blockId, float value)
        {
            if (string.IsNullOrEmpty(blockId)) return;
            _configByBlockId[blockId] = value;
            VariantChanged?.Invoke(blockId);
        }

        public string GetVariantConcoctionId(string blockId)
        {
            if (string.IsNullOrEmpty(blockId)) return string.Empty;
            return _concoctionByBlockId.TryGetValue(blockId, out string v) && v != null ? v : string.Empty;
        }

        public void SetVariantConcoctionId(string blockId, string concoctionId)
        {
            if (string.IsNullOrEmpty(blockId)) return;
            _concoctionByBlockId[blockId] = concoctionId ?? string.Empty;
            VariantChanged?.Invoke(blockId);
        }

        public void ResetVariantCaches()
        {
            _dimsByBlockId.Clear();
            _pitchByBlockId.Clear();
            _teeterByBlockId.Clear();
            _configByBlockId.Clear();
            _concoctionByBlockId.Clear();
        }

        // -----------------------------------------------------------------
        // Explicit Apply — panel button pushes caches onto placed blocks.
        // -----------------------------------------------------------------

        /// <summary>
        /// Push the variant caches for <paramref name="blockId"/> onto every
        /// placed block of that id, then sync the blueprint. Returns how
        /// many blocks were written. This is the panel's explicit Apply
        /// button (LOG-172, pogo-power report) — deliberately NOT wired to
        /// the cache-write events: implicit all-blocks propagation was
        /// retired by the span-isolation session (one foil's span drag must
        /// never silently rewrite every foil), and an explicit click is the
        /// sanctioned exception. Callers seed the caches on selection
        /// (<see cref="SeedVariantCachesFromPlacedBlock"/>) so what the
        /// sliders show is what gets applied.
        /// </summary>
        public int ApplyVariantCachesToPlacedBlocks(string blockId)
        {
            if (Grid == null || string.IsNullOrEmpty(blockId)) return 0;
            if (EditingInstance != null) return 0; // bound flow is live already
            float worldPitch = GetVariantPitch(blockId);
            float worldTeeter = GetVariantTeeter(blockId);
            Vector3 dims = GetVariantDims(blockId);
            float config = GetVariantConfig(blockId);
            string concoction = GetVariantConcoctionId(blockId);
            int count = 0;
            foreach (KeyValuePair<Vector3Int, BlockBehaviour> kvp in Grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null || b.Definition.Id != blockId) continue;
                // Cache angles are world-intent; placed blocks store
                // local-frame — same conversion the tune-mode bind flow does.
                b.SetPitch(BlockOrientation.NormalizePitchForUp(b.Definition, worldPitch, b.Up));
                b.SetTeeter(BlockOrientation.NormalizePitchForUp(b.Definition, worldTeeter, b.Up));
                b.SetDims(dims);
                b.ConfigValue = config;
                b.ConcoctionId = concoction;
                count++;
            }
            if (count > 0) SyncBlueprint();
            return count;
        }

        /// <summary>
        /// Seed the variant caches for <paramref name="blockId"/> from the
        /// first placed block of that id, so the unbound panel's sliders
        /// show the bot's CURRENT tune instead of session defaults — and so
        /// Apply pushes what the player sees, not stale sentinels. Same
        /// stored-local → world-intent conversion as <see cref="TryMove"/>'s
        /// cache seed. No-op (returns false) when nothing of the id is
        /// placed, which keeps the pure next-placement dial flow intact.
        /// </summary>
        public bool SeedVariantCachesFromPlacedBlock(string blockId)
        {
            if (Grid == null || string.IsNullOrEmpty(blockId)) return false;
            foreach (KeyValuePair<Vector3Int, BlockBehaviour> kvp in Grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null || b.Definition.Id != blockId) continue;
                BlockDefinition def = b.Definition;
                SetVariantDims(def.Id, b.Dims);
                SetVariantPitch(def.Id, BlockOrientation.NormalizePitchForUp(def, b.PitchDeg, b.Up));
                SetVariantTeeter(def.Id, BlockOrientation.NormalizePitchForUp(def, b.TeeterDeg, b.Up));
                SetVariantConfig(def.Id, b.ConfigValue);
                SetVariantConcoctionId(def.Id, b.ConcoctionId);
                return true;
            }
            return false;
        }


        // -----------------------------------------------------------------
        // Mirror state
        // -----------------------------------------------------------------

        public bool MirrorEnabled { get; private set; }
        public MirrorAxis MirrorAxis { get; private set; } = Robogame.Block.MirrorAxis.X;

        /// <summary>Raised when mirror enabled / axis changes — ghost rebuild keys on this.</summary>
        public event Action MirrorChanged;

        public void SetMirrorEnabled(bool enabled)
        {
            if (MirrorEnabled == enabled) return;
            MirrorEnabled = enabled;
            MirrorChanged?.Invoke();
        }

        public void SetMirrorAxis(MirrorAxis axis)
        {
            if (MirrorAxis == axis) return;
            MirrorAxis = axis;
            MirrorChanged?.Invoke();
        }

        public void ToggleMirror() => SetMirrorEnabled(!MirrorEnabled);

        // Player-chosen yaw (deg about the mount/Up axis) for the NEXT placement.
        // Set by the build editor's rotate key; normalized to 0/90/180/270.
        // Session 120 block rotation.
        public int PlaceYaw { get; private set; }
        public void SetPlaceYaw(int yawDeg) => PlaceYaw = ((yawDeg % 360) + 360) % 360 / 90 * 90;
        public void CyclePlaceYaw(int steps = 1) => SetPlaceYaw(PlaceYaw + steps * 90);

        // -----------------------------------------------------------------
        // Placement / removal verbs
        // -----------------------------------------------------------------

        // Reusable BFS scratch + reachable-cell snapshot. The session is
        // the chokepoint that runs the rules engine, so the buffers
        // belong here.
        private readonly BlockGraph.Buffers _buffers = new BlockGraph.Buffers();
        private readonly HashSet<Vector3Int> _cpuReachable = new HashSet<Vector3Int>(64);
        private bool _cpuReachableValid;

        public IReadOnlyCollection<Vector3Int> CpuReachable => _cpuReachableValid ? _cpuReachable : null;

        /// <summary>
        /// Rebuild <see cref="CpuReachable"/> from the live grid. Hot-path
        /// callers should run this once per Update tick before evaluating
        /// multiple candidate cells.
        /// </summary>
        public void RefreshCpuReachable()
        {
            _cpuReachableValid = false;
            _cpuReachable.Clear();
            if (Grid == null) return;
            Vector3Int? cpu = BlockGraph.FindCpuCell(Grid);
            if (!cpu.HasValue) return;
            BlockGraph.BfsFrom(Grid, cpu.Value, _buffers);
            foreach (Vector3Int v in _buffers.Visited) _cpuReachable.Add(v);
            _cpuReachableValid = true;
        }

        public PlacementRules.PlacementError EvaluatePlacement(in PlacementRules.Candidate candidate)
        {
            return PlacementRules.EvaluatePlacement(Grid, in candidate, _cpuReachableValid ? _cpuReachable : null);
        }

        public PlacementRules.PlacementError EvaluateRemoval(Vector3Int cell)
        {
            return PlacementRules.EvaluateRemoval(Grid, cell, _buffers, out _);
        }

        /// <summary>
        /// Atomic place: rule check → grid mutation → blueprint sync →
        /// optional mirrored placement. Returns the per-side result so
        /// the caller's UI layer can decide whether to play the buzzer
        /// once, twice, or not at all.
        /// </summary>
        public readonly struct PlaceOutcome
        {
            public readonly PlacementRules.PlacementError Primary;
            public readonly PlacementRules.PlacementError Mirror;
            public readonly bool MirrorAttempted;
            public PlaceOutcome(PlacementRules.PlacementError primary, PlacementRules.PlacementError mirror, bool mirrorAttempted)
            {
                Primary = primary; Mirror = mirror; MirrorAttempted = mirrorAttempted;
            }
            public bool PrimarySucceeded => Primary == PlacementRules.PlacementError.None;
        }

        /// <summary>
        /// Place using the per-id variant caches for dims + world-intent pitch.
        /// This is the entry point the build-mode UI uses — the variant panel
        /// has already written its slider state into the session cache, so the
        /// editor doesn't have to pass dims/pitch explicitly. Scripted callers
        /// (tests, editor scaffolders) should use the explicit overload
        /// <see cref="TryPlace(BlockDefinition,Vector3Int,Vector3Int,Vector3,float,float)"/>.
        /// </summary>
        public PlaceOutcome TryPlace(BlockDefinition def, Vector3Int cell, Vector3Int up)
            => TryPlace(def, cell, up, GetVariantDims(def?.Id), GetVariantPitch(def?.Id), GetVariantTeeter(def?.Id));

        /// <summary>
        /// Atomic place with explicit per-instance dims + world-intent pitch.
        /// Runs the rules engine, mutates the grid, auto-places structural
        /// companions (rotor mechanism cube, ...), best-effort mirrors, and
        /// resyncs the blueprint. Single entry point — every placement in
        /// the project (editor UI, tests, scripted scaffolders) flows here.
        /// </summary>
        public PlaceOutcome TryPlace(BlockDefinition def, Vector3Int cell, Vector3Int up,
            Vector3 dims, float worldPitch, float worldTeeter = 0f)
        {
            if (Grid == null || def == null)
                return new PlaceOutcome(PlacementRules.PlacementError.HostMissing, PlacementRules.PlacementError.None, false);

            // World-intent pitch (positive = tilt tip toward sky on every
            // face) is normalized to the block's local frame for both the
            // rule check and the placed instance. See BlockOrientation.
            // Teeter is the chord-axis rotation the same per-side sign rule
            // was originally derived for, so it shares the normalization.
            float localPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, up);
            float localTeeter = BlockOrientation.NormalizePitchForUp(def, worldTeeter, up);
            var candidate = new PlacementRules.Candidate(def, cell, up, dims, localPitch);

            RefreshCpuReachable();
            PlacementRules.PlacementError primary = PlacementRules.EvaluatePlacement(Grid, in candidate, _cpuReachableValid ? _cpuReachable : null);
            if (primary != PlacementRules.PlacementError.None)
                return new PlaceOutcome(primary, PlacementRules.PlacementError.None, false);

            BlockBehaviour placed = Grid.PlaceBlock(def, cell, up, dims, localPitch, PlaceYaw);
            if (placed == null)
                return new PlaceOutcome(PlacementRules.PlacementError.WouldOverlapNeighbour, PlacementRules.PlacementError.None, false);
            // Per-block server-authoritative scalar from the variant cache
            // (0 = block default). Rides the same per-id cache Dims/Pitch
            // do; SyncBlueprint persists it onto the Entry.
            placed.ConfigValue = GetVariantConfig(def.Id);
            // Per-block concoction (explosive weapons). Rides the same per-id
            // cache; SyncBlueprint persists it onto the Entry. ADR-0004.
            placed.ConcoctionId = GetVariantConcoctionId(def.Id);
            // Foil teeter tilt, post-place like ConfigValue. SetTeeter (not
            // a bare field write) so the foil's TeeterChanged subscriber
            // refreshes the wing mesh immediately.
            placed.SetTeeter(localTeeter);

            // Auto-place structural companions a primary block needs to be
            // usable. Rotor → mechanism cube on its spin-axis face. Owned
            // here (not in the editor) so every consumer of TryPlace gets
            // the same cascade — without this, scripted scaffolders would
            // have to hand-author the mechanism cube and drift away from
            // what the player produces with a single rotor placement.
            AutoPlaceCompanionsOf(def, cell, up);

            // Best-effort mirror placement. Skipped silently if the
            // mirror cell is on-plane, the same cell, or any rule
            // rejects it; the caller decides whether to surface that.
            PlacementRules.PlacementError mirrorErr = PlacementRules.PlacementError.None;
            bool mirrorAttempted = false;
            if (MirrorEnabled && !BlockMirror.IsOnPlane(cell, MirrorAxis))
            {
                Vector3Int mCell = BlockMirror.MirrorCell(cell, MirrorAxis);
                Vector3Int mUp   = BlockMirror.MirrorUp(up, MirrorAxis);
                // Each side normalizes the same world-intent pitch/teeter
                // for its own up — no separate mirror-axis sign rule.
                float mLocalPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, mUp);
                float mLocalTeeter = BlockOrientation.NormalizePitchForUp(def, worldTeeter, mUp);
                if (mCell != cell)
                {
                    mirrorAttempted = true;
                    var mirrorCandidate = new PlacementRules.Candidate(def, mCell, mUp, dims, mLocalPitch);
                    RefreshCpuReachable();
                    mirrorErr = PlacementRules.EvaluatePlacement(Grid, in mirrorCandidate, _cpuReachableValid ? _cpuReachable : null);
                    if (mirrorErr == PlacementRules.PlacementError.None)
                    {
                        BlockBehaviour mPlaced = Grid.PlaceBlock(def, mCell, mUp, dims, mLocalPitch, PlaceYaw);
                        if (mPlaced != null)
                        {
                            mPlaced.ConfigValue = GetVariantConfig(def.Id);
                            // Same per-id variant cache as the primary —
                            // the mirror side must not silently drop the
                            // dialed concoction. ADR-0004.
                            mPlaced.ConcoctionId = GetVariantConcoctionId(def.Id);
                            mPlaced.SetTeeter(mLocalTeeter);
                        }
                        AutoPlaceCompanionsOf(def, mCell, mUp);
                    }
                }
            }

            SyncBlueprint();
            return new PlaceOutcome(primary, mirrorErr, mirrorAttempted);
        }

        // -----------------------------------------------------------------
        // Companion auto-placement
        // -----------------------------------------------------------------

        /// <summary>
        /// Drop the structural companions a primary block needs to be
        /// usable. Currently: rotor → cube on its spin-axis face (per
        /// session 48 — a rotor's only connective face is the spin axis,
        /// so a from-scratch rotor can't host blades until the cube goes
        /// down).
        /// </summary>
        /// <remarks>
        /// Bypasses the rules engine — the companion is a part of the
        /// primary placement, not an independent action. A library-lookup
        /// failure or occupied target cell is silently skipped (the
        /// primary block already landed; the caller decides whether to
        /// retry after fixing the obstruction).
        /// </remarks>
        private void AutoPlaceCompanionsOf(BlockDefinition def, Vector3Int cell, Vector3Int up)
        {
            if (def == null || Grid == null || Library == null) return;
            // ADR-0008: the companion pairing rides the definition (the
            // rotor authors Cube). Companions sit at cell + mount-up by
            // contract; an occupied cell is adopted as-is.
            if (!def.HasCompanion) return;
            BlockDefinition companionDef = Library.Get(def.CompanionBlockId);
            if (companionDef == null) return;
            Vector3Int companionCell = cell + up;
            if (Grid.HasBlock(companionCell)) return;
            Grid.PlaceBlock(companionDef, companionCell, up, Vector3.zero, 0f);
        }

        /// <summary>
        /// If <paramref name="cell"/> hosts a rotor's auto-placed mechanism
        /// cube, return the owning rotor's cell; otherwise return
        /// <paramref name="cell"/> unchanged. The mechanism cube's host
        /// mesh is always hidden (see <c>RotorBlock</c>), so what the
        /// player SEES at that cell is the rotor's upper mast — cursor
        /// verbs that target "the block the player is looking at"
        /// (eyedropper, instance-edit bind, removal) should route through
        /// this so the invisible cube never swallows the click.
        /// Ownership test: a cube at <c>rotor.cell + rotor.Up</c> IS the
        /// mechanism cube by placement semantics — the auto-companion
        /// cascade either placed it there or adopted a pre-existing cube
        /// in that cell (see <see cref="AutoPlaceCompanionsOf"/>).
        /// </summary>
        public static Vector3Int ResolveMechanismOwnerCell(
            IReadOnlyDictionary<Vector3Int, BlockBehaviour> blocks, Vector3Int cell)
        {
            if (blocks == null) return cell;
            if (!blocks.TryGetValue(cell, out BlockBehaviour b) || b == null) return cell;
            if (b.Definition == null) return cell;
            // ADR-0008: a block is a companion when some adjacent owner
            // declares this id as its CompanionBlockId and points its
            // mount-up at this cell.
            for (int i = 0; i < s_cascadeFaces.Length; i++)
            {
                Vector3Int d = s_cascadeFaces[i];
                if (!blocks.TryGetValue(cell - d, out BlockBehaviour r) || r == null) continue;
                if (r.Definition == null || !r.Definition.HasCompanion) continue;
                if (r.Definition.CompanionBlockId != b.Definition.Id) continue;
                if (r.Up == d) return cell - d;
            }
            return cell;
        }

        /// <summary>
        /// If <paramref name="cell"/> hosts a rotor whose mechanism cube
        /// would be orphaned by its removal, return the mechanism cell so
        /// removal can cascade. Returns null when the rotor has dependents
        /// on the cube (blades, chained structure) — in that case the
        /// orphan check on the rotor alone correctly blocks the removal
        /// until the dependents go first.
        /// </summary>
        public Vector3Int? ResolveRotorCascadeCell(Vector3Int cell)
        {
            if (Grid == null) return null;
            if (!Grid.TryGetBlock(cell, out BlockBehaviour block) || block == null) return null;
            // ADR-0008: cascade applies to any block with a declared
            // companion (the rotor's mechanism cube is the model case).
            if (block.Definition == null || !block.Definition.HasCompanion) return null;
            Vector3Int spinAxis = block.Up == Vector3Int.zero ? Vector3Int.up : block.Up;
            Vector3Int mechCell = cell + spinAxis;
            if (!Grid.TryGetBlock(mechCell, out BlockBehaviour mech) || mech == null) return null;
            if (mech.Definition == null || mech.Definition.Id != block.Definition.CompanionBlockId) return null;
            // Only cascade when the cube has no neighbours other than
            // this rotor — otherwise blades or chained structure are at
            // stake, and the user should be the one to clear them.
            if (CountAdjacentBlocksExcluding(mechCell, cell) > 0) return null;
            return mechCell;
        }

        private static readonly Vector3Int[] s_cascadeFaces =
        {
            new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
        };

        private int CountAdjacentBlocksExcluding(Vector3Int cell, Vector3Int excluded)
        {
            if (Grid == null) return 0;
            int count = 0;
            for (int i = 0; i < s_cascadeFaces.Length; i++)
            {
                Vector3Int n = cell + s_cascadeFaces[i];
                if (n == excluded) continue;
                if (Grid.HasBlock(n)) count++;
            }
            return count;
        }

        /// <summary>
        /// Removal verb. Mirrors the place-side return shape; the
        /// caller's UI policy decides whether a "would orphan" or
        /// "CPU is sacred" rejection plays the buzzer once or twice.
        /// </summary>
        public readonly struct RemoveOutcome
        {
            public readonly PlacementRules.PlacementError Primary;
            public readonly PlacementRules.PlacementError Mirror;
            public readonly bool MirrorAttempted;
            public RemoveOutcome(PlacementRules.PlacementError primary, PlacementRules.PlacementError mirror, bool mirrorAttempted)
            {
                Primary = primary; Mirror = mirror; MirrorAttempted = mirrorAttempted;
            }
            public bool PrimarySucceeded => Primary == PlacementRules.PlacementError.None;
        }

        // CPU is sacred — never removable. Identify it by the CpuBlockMarker
        // component, which BlockGrid attaches to every Cpu-category block at
        // placement time. Keying off the marker (not just Definition.Category)
        // means a null/stale BlockBehaviour.Definition can't bypass the guard:
        // session 120 playtest, a right-click on the CPU slipped past the
        // Definition-only check, removed the CPU, and the resulting CPU-loss
        // cascade tore the whole bot apart. Category is a belt-and-braces
        // fallback for any CPU block that somehow lacks the marker.
        private static bool IsCpu(BlockBehaviour b)
            => b != null
               && (b.GetComponent<CpuBlockMarker>() != null
                   || (b.Definition != null && b.Definition.Category == BlockCategory.Cpu));

        public RemoveOutcome TryRemove(Vector3Int cell)
        {
            if (Grid == null || !Grid.TryGetBlock(cell, out BlockBehaviour block) || block == null)
                return new RemoveOutcome(PlacementRules.PlacementError.HostMissing, PlacementRules.PlacementError.None, false);

            // CPU is sacred. Removal-policy rule kept inside the verb so
            // every consumer enforces it; the caller's UI maps it to a buzzer.
            if (IsCpu(block))
                return new RemoveOutcome(PlacementRules.PlacementError.HostFaceRejectsBlockType, PlacementRules.PlacementError.None, false);

            // Rotor cascade: removing a rotor co-removes its auto-placed
            // mechanism cube IF the cube has no other dependents. Without
            // this, the orphan check would block every rotor removal —
            // the cube was placed for the rotor and has no other neighbours
            // initially. Mirrors the auto-companion logic in TryPlace.
            Vector3Int? cascadeCell = ResolveRotorCascadeCell(cell);

            PlacementRules.PlacementError primary = cascadeCell.HasValue
                ? PlacementRules.EvaluateRemoval(Grid, cell, cascadeCell.Value, _buffers, out _)
                : PlacementRules.EvaluateRemoval(Grid, cell, _buffers, out _);
            if (primary != PlacementRules.PlacementError.None)
                return new RemoveOutcome(primary, PlacementRules.PlacementError.None, false);

            Grid.RemoveBlock(cell);
            if (cascadeCell.HasValue) Grid.RemoveBlock(cascadeCell.Value);

            PlacementRules.PlacementError mirrorErr = PlacementRules.PlacementError.None;
            bool mirrorAttempted = false;
            if (MirrorEnabled && !BlockMirror.IsOnPlane(cell, MirrorAxis))
            {
                Vector3Int mCell = BlockMirror.MirrorCell(cell, MirrorAxis);
                if (mCell != cell && Grid.TryGetBlock(mCell, out BlockBehaviour mBlock) && mBlock != null)
                {
                    mirrorAttempted = true;
                    if (IsCpu(mBlock))
                    {
                        // CPU-sacred — silently skip the mirror, no buzzer.
                    }
                    else
                    {
                        Vector3Int? mirrorCascade = ResolveRotorCascadeCell(mCell);
                        mirrorErr = mirrorCascade.HasValue
                            ? PlacementRules.EvaluateRemoval(Grid, mCell, mirrorCascade.Value, _buffers, out _)
                            : PlacementRules.EvaluateRemoval(Grid, mCell, _buffers, out _);
                        if (mirrorErr == PlacementRules.PlacementError.None)
                        {
                            Grid.RemoveBlock(mCell);
                            if (mirrorCascade.HasValue) Grid.RemoveBlock(mirrorCascade.Value);
                        }
                    }
                }
            }

            SyncBlueprint();
            return new RemoveOutcome(primary, mirrorErr, mirrorAttempted);
        }

        // -----------------------------------------------------------------
        // Move (session 169)
        // -----------------------------------------------------------------

        /// <summary>
        /// Atomic move: pick the block at <paramref name="fromCell"/> up and
        /// re-place it at <paramref name="toCell"/> / <paramref name="toUp"/>
        /// with every per-instance setting preserved — dims, pitch, teeter,
        /// yaw, config scalar, concoction. Built entirely from
        /// <see cref="TryRemove"/> + <see cref="TryPlace"/> so the rules
        /// engine, the companion cascade and the blueprint sync are exactly
        /// what a manual remove + re-place would run, with one guarantee on
        /// top: if the destination is rejected the block is restored at the
        /// source untouched. A failed move must never eat a tuned part.
        /// </summary>
        /// <remarks>
        /// Mirror is suspended for the duration — v1 moves exactly one
        /// block; what "moving half a mirrored pair" means is undesigned.
        /// The per-id variant caches are seeded with the carried values
        /// (TryPlace reads config / concoction from them). That side effect
        /// is kept deliberately: after a move, the next fresh placement of
        /// the same id inherits the moved block's settings — the same
        /// contract as the middle-click eyedropper.
        /// </remarks>
        public PlacementRules.PlacementError TryMove(Vector3Int fromCell, Vector3Int toCell, Vector3Int toUp)
        {
            if (Grid == null || !Grid.TryGetBlock(fromCell, out BlockBehaviour block)
                || block == null || block.Definition == null)
                return PlacementRules.PlacementError.HostMissing;
            if (fromCell == toCell && block.Up == toUp)
                return PlacementRules.PlacementError.None; // no-op

            BlockDefinition def = block.Definition;
            Vector3Int fromUp = block.Up;
            Vector3 dims = block.Dims;
            int yaw = block.Yaw;
            // Stored angles are local-frame; convert to world intent so the
            // destination face re-normalizes them exactly like placement
            // does (a side-mount → top-mount move keeps "tip toward sky").
            float worldPitch  = BlockOrientation.NormalizePitchForUp(def, block.PitchDeg, fromUp);
            float worldTeeter = BlockOrientation.NormalizePitchForUp(def, block.TeeterDeg, fromUp);

            SetVariantDims(def.Id, dims);
            SetVariantPitch(def.Id, worldPitch);
            SetVariantTeeter(def.Id, worldTeeter);
            SetVariantConfig(def.Id, block.ConfigValue);
            SetVariantConcoctionId(def.Id, block.ConcoctionId);

            bool mirrorWas = MirrorEnabled;
            int yawWas = PlaceYaw;
            SetMirrorEnabled(false);
            SetPlaceYaw(yaw);
            try
            {
                RemoveOutcome removed = TryRemove(fromCell);
                if (removed.Primary != PlacementRules.PlacementError.None)
                    return removed.Primary;

                PlaceOutcome placed = TryPlace(def, toCell, toUp, dims, worldPitch, worldTeeter);
                if (placed.Primary == PlacementRules.PlacementError.None)
                    return PlacementRules.PlacementError.None;

                // Destination rejected — put the block back. The source
                // cell was freed by the remove above and nothing else has
                // mutated, so this cannot reasonably fail; if it somehow
                // does, scream: that IS the data-loss case this verb exists
                // to prevent.
                PlaceOutcome restored = TryPlace(def, fromCell, fromUp, dims, worldPitch, worldTeeter);
                if (restored.Primary != PlacementRules.PlacementError.None)
                    Debug.LogError($"[Robogame] TryMove rollback FAILED for '{def.Id}' at {fromCell}: {restored.Primary}. Block lost — report this.");
                return placed.Primary;
            }
            finally
            {
                SetMirrorEnabled(mirrorWas);
                SetPlaceYaw(yawWas);
            }
        }

        // -----------------------------------------------------------------
        // Blueprint sync
        // -----------------------------------------------------------------

        /// <summary>
        /// Rewrite <see cref="Blueprint"/> from the live grid.
        /// <see cref="ChassisBlueprint.SetEntries"/> canonical-sorts as
        /// a side effect, so block-index ordering remains the netcode
        /// contract on every save / load / mid-edit flush.
        /// </summary>
        /// <remarks>
        /// Also auto-derives <see cref="ChassisBlueprint.RotorsGenerateLift"/>
        /// from the live grid contents: a chassis with one or more
        /// <see cref="BlockIds.Rotor"/> cells flips the flag so the
        /// next chassis spawn sets <c>RotorBlock.GeneratesLift = true</c>
        /// and the rotor adopts adjacent foils. Per-rotor opt-in lands
        /// when the blueprint format supports per-cell config; until
        /// then, "this chassis has rotors" is the right granularity.
        /// </remarks>
        public void SyncBlueprint()
        {
            if (Blueprint == null || Grid == null) return;
            var list = new List<ChassisBlueprint.Entry>(Grid.Blocks.Count);
            bool hasRotor = false;
            foreach (var kvp in Grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                var entry = new ChassisBlueprint.Entry(b.Definition.Id, kvp.Key, b.Up, b.Dims, b.PitchDeg, b.ConfigValue, b.ConcoctionId);
                entry.Yaw = b.Yaw;
                entry.Teeter = b.TeeterDeg;
                list.Add(entry);
                if (b.Definition.Id == BlockIds.Rotor) hasRotor = true;
            }
            Blueprint.SetEntries(list.ToArray());
            // Assign unconditionally — `if (hasRotor) = true` with no else left
            // the flag latched on after the last rotor was removed.
            Blueprint.RotorsGenerateLift = hasRotor;
        }
    }
}
