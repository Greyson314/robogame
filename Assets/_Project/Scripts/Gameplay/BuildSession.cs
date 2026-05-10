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

        public void ClearBindings()
        {
            Grid = null;
            Blueprint = null;
            Library = null;
        }

        // -----------------------------------------------------------------
        // Selected block + variant cache
        // -----------------------------------------------------------------

        public string SelectedBlockId { get; private set; }

        /// <summary>Raised after <see cref="SetSelectedBlock"/> mutates the selection.</summary>
        public event Action<string> SelectedBlockChanged;

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

        public void ResetVariantCaches()
        {
            _dimsByBlockId.Clear();
            _pitchByBlockId.Clear();
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

        public PlaceOutcome TryPlace(BlockDefinition def, Vector3Int cell, Vector3Int up)
        {
            if (Grid == null || def == null)
                return new PlaceOutcome(PlacementRules.PlacementError.HostMissing, PlacementRules.PlacementError.None, false);

            Vector3 dims = GetVariantDims(def.Id);
            // Variant cache stores world-intent pitch (positive =
            // tilt toward sky); convert to local frame per side.
            float worldPitch = GetVariantPitch(def.Id);
            float localPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, up);
            var candidate = new PlacementRules.Candidate(def, cell, up, dims, localPitch);

            RefreshCpuReachable();
            PlacementRules.PlacementError primary = PlacementRules.EvaluatePlacement(Grid, in candidate, _cpuReachableValid ? _cpuReachable : null);
            if (primary != PlacementRules.PlacementError.None)
                return new PlaceOutcome(primary, PlacementRules.PlacementError.None, false);

            BlockBehaviour placed = Grid.PlaceBlock(def, cell, up, dims, localPitch);
            if (placed == null)
                return new PlaceOutcome(PlacementRules.PlacementError.WouldOverlapNeighbour, PlacementRules.PlacementError.None, false);

            // Best-effort mirror placement. Skipped silently if the
            // mirror cell is on-plane, the same cell, or any rule
            // rejects it; the caller decides whether to surface that.
            PlacementRules.PlacementError mirrorErr = PlacementRules.PlacementError.None;
            bool mirrorAttempted = false;
            if (MirrorEnabled && !BlockMirror.IsOnPlane(cell, MirrorAxis))
            {
                Vector3Int mCell = BlockMirror.MirrorCell(cell, MirrorAxis);
                Vector3Int mUp   = BlockMirror.MirrorUp(up, MirrorAxis);
                // Each side normalizes the same world-intent pitch
                // for its own up — no separate mirror-axis sign rule.
                float mLocalPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, mUp);
                if (mCell != cell)
                {
                    mirrorAttempted = true;
                    var mirrorCandidate = new PlacementRules.Candidate(def, mCell, mUp, dims, mLocalPitch);
                    RefreshCpuReachable();
                    mirrorErr = PlacementRules.EvaluatePlacement(Grid, in mirrorCandidate, _cpuReachableValid ? _cpuReachable : null);
                    if (mirrorErr == PlacementRules.PlacementError.None)
                    {
                        Grid.PlaceBlock(def, mCell, mUp, dims, mLocalPitch);
                    }
                }
            }

            SyncBlueprint();
            return new PlaceOutcome(primary, mirrorErr, mirrorAttempted);
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

        public RemoveOutcome TryRemove(Vector3Int cell)
        {
            if (Grid == null || !Grid.TryGetBlock(cell, out BlockBehaviour block) || block == null)
                return new RemoveOutcome(PlacementRules.PlacementError.HostMissing, PlacementRules.PlacementError.None, false);

            // Removal-policy rules (CPU sacred, etc.) live in the caller
            // because they're product decisions, not graph facts. The
            // session enforces the graph-fact rule (orphan check).
            PlacementRules.PlacementError primary = PlacementRules.EvaluateRemoval(Grid, cell, _buffers, out _);
            if (primary != PlacementRules.PlacementError.None)
                return new RemoveOutcome(primary, PlacementRules.PlacementError.None, false);

            Grid.RemoveBlock(cell);

            PlacementRules.PlacementError mirrorErr = PlacementRules.PlacementError.None;
            bool mirrorAttempted = false;
            if (MirrorEnabled && !BlockMirror.IsOnPlane(cell, MirrorAxis))
            {
                Vector3Int mCell = BlockMirror.MirrorCell(cell, MirrorAxis);
                if (mCell != cell && Grid.TryGetBlock(mCell, out BlockBehaviour mBlock) && mBlock != null)
                {
                    mirrorAttempted = true;
                    if (mBlock.Definition != null && mBlock.Definition.Category == BlockCategory.Cpu)
                    {
                        // CPU-sacred — silently skip the mirror, no buzzer.
                    }
                    else
                    {
                        mirrorErr = PlacementRules.EvaluateRemoval(Grid, mCell, _buffers, out _);
                        if (mirrorErr == PlacementRules.PlacementError.None)
                        {
                            Grid.RemoveBlock(mCell);
                        }
                    }
                }
            }

            SyncBlueprint();
            return new RemoveOutcome(primary, mirrorErr, mirrorAttempted);
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
                list.Add(new ChassisBlueprint.Entry(b.Definition.Id, kvp.Key, b.Up, b.Dims, b.PitchDeg));
                if (b.Definition.Id == BlockIds.Rotor) hasRotor = true;
            }
            Blueprint.SetEntries(list.ToArray());
            if (hasRotor) Blueprint.RotorsGenerateLift = true;
        }
    }
}
