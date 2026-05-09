using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode block placement and removal. Active only while
    /// <see cref="BuildModeController.IsActive"/>; subscribes to its
    /// Entered/Exited events to show/hide the ghost preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Targeting model: raycast from the screen mouse → if the ray hits a
    /// <see cref="BlockBehaviour"/>, the target cell is the hit-block's
    /// grid position offset by the face-normal direction. Right-click
    /// targets the hit cell directly (for removal).
    /// </para>
    /// <para>
    /// On every successful place/remove, the <see cref="ChassisBlueprint"/>
    /// owned by <see cref="GameStateController"/> is rewritten from the
    /// live <see cref="BlockGrid"/> contents — so Save / Launch always
    /// see the current edits even if the player skips a manual save.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockEditor : MonoBehaviour
    {
        [Tooltip("Build-mode controller this editor follows.")]
        [SerializeField] private BuildModeController _buildMode;

        [Tooltip("Hotbar that selects which block ID is placed on left-click.")]
        [SerializeField] private BuildHotbar _hotbar;

        [Tooltip("Optional variant config panel that supplies per-block dims (foil span/thickness/chord, rope segment count). Falls back to block defaults when null.")]
        [SerializeField] private VariantConfigPanel _variantPanel;

        [Tooltip("Optional mirror-mode toggle. When enabled, every place / remove " +
                 "is duplicated across the chosen chassis-local plane.")]
        [SerializeField] private BuildMirrorMode _mirrorMode;

        // The plain-C# build-mode model — placement evaluation, variant
        // cache, mirror state, blueprint sync. The editor consults the
        // session for variant data and routes mutations back through
        // the session so editor / panel / mirror agree on one answer.
        private BuildSession _session;
        public BuildSession Session
        {
            get => _session;
            set => _session = value;
        }

        public VariantConfigPanel VariantPanel { get => _variantPanel; set => _variantPanel = value; }
        public BuildMirrorMode MirrorMode
        {
            get => _mirrorMode;
            set
            {
                if (_mirrorMode != null) _mirrorMode.Changed -= HandleMirrorChanged;
                _mirrorMode = value;
                if (_mirrorMode != null) _mirrorMode.Changed += HandleMirrorChanged;
            }
        }

        private void HandleMirrorChanged()
        {
            // No-op: BlockGhostRenderer detects input changes on its
            // own (showMirror flag in GhostRequest changes when this
            // event fires). The signature stays so subscribers keep
            // working — kept for callers that want to react to mirror
            // toggles for non-rendering reasons.
        }

        [Tooltip("Layer mask used by the targeting raycast. Default: everything.")]
        [SerializeField] private LayerMask _raycastMask = ~0;

        [Tooltip("Maximum picking distance.")]
        [SerializeField, Min(1f)] private float _raycastDistance = 100f;

        [Tooltip("Reference CPU budget contributed per CPU block on the chassis. " +
                 "Used only to compute the cap shown in the BuildHotbar readout — " +
                 "placements are NOT rejected when the cap is exceeded; the readout " +
                 "just turns hot to flag it.")]
        [SerializeField, Min(0)] private int _cpuBudgetPerCpu = 250;

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode == value) return;
                Unsubscribe();
                _buildMode = value;
                Subscribe();
            }
        }
        public BuildHotbar Hotbar { get => _hotbar; set => _hotbar = value; }

        /// <summary>Snapshot of CPU usage for HUD display.</summary>
        public readonly struct CpuUsage
        {
            public readonly int Used;
            public readonly int Cap;
            public CpuUsage(int used, int cap) { Used = used; Cap = cap; }
            public bool OverBudget => Used > Cap;
        }

        /// <summary>Snapshot of chassis aggregates for HUD display.</summary>
        public readonly struct ChassisStats
        {
            public readonly int CpuUsed, CpuCap, BlockCount;
            public readonly float TotalMass;
            public ChassisStats(int cpuUsed, int cpuCap, int blockCount, float mass)
            { CpuUsed = cpuUsed; CpuCap = cpuCap; BlockCount = blockCount; TotalMass = mass; }
            public bool OverBudget => CpuUsed > CpuCap;
        }

        /// <summary>Live CPU usage of the chassis. Returns (0,0) if no grid.</summary>
        public CpuUsage GetCpuUsage()
        {
            if (_grid == null) return new CpuUsage(0, 0);
            int used = 0, cpus = 0;
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                used += Mathf.Max(0, b.Definition.CpuCost);
                if (b.Definition.Category == BlockCategory.Cpu) cpus++;
            }
            return new CpuUsage(used, cpus * _cpuBudgetPerCpu);
        }

        /// <summary>
        /// Live chassis aggregates: CPU used / cap, block count, total mass.
        /// Used by the BuildHotbar's stats overlay so the player has a
        /// single-glance read on what the current build looks like before
        /// they leave build mode.
        /// </summary>
        public ChassisStats GetChassisStats()
        {
            if (_grid == null) return new ChassisStats(0, 0, 0, 0f);
            int used = 0, cpus = 0, count = 0;
            float mass = 0f;
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                used += Mathf.Max(0, b.Definition.CpuCost);
                if (b.Definition.Category == BlockCategory.Cpu) cpus++;
                mass += b.Definition.Mass;
                count++;
            }
            return new ChassisStats(used, cpus * _cpuBudgetPerCpu, count, mass);
        }

        // Visual feedback components — own ghost lifecycle + error
        // overlay. The editor just feeds them per-frame data.
        private BlockGhostRenderer _ghostRenderer;
        private PlacementFeedbackHud _feedbackHud;

        public BlockGhostRenderer GhostRenderer { get => _ghostRenderer; set => _ghostRenderer = value; }
        public PlacementFeedbackHud FeedbackHud { get => _feedbackHud; set => _feedbackHud = value; }

        // Targeting state -------------------------------------------------
        private BlockGrid _grid;
        private bool _hasTarget;
        private Vector3Int _targetPlaceCell;
        private Vector3Int _targetHitCell;
        private bool _validPlacement;
        private PlacementRules.PlacementError _lastPlacementError;
        // Mirror ghost validity is tracked independently because the
        // mirror placement may fail (overlap, leaf neighbour) even when
        // the original is fine. The renderer does the visual side; the
        // editor tracks the bool so TryPlace knows whether to fire the
        // mirror placement.
        private bool _mirrorGhostValid;
        private bool _mirrorGhostShown;

        private bool _subscribed;

        // Reusable BFS scratch — IsValidPlacement runs every frame and
        // BlockGraph.WouldOrphanIfRemoved fires on every right-click.
        // Holding the buffers as fields keeps the hot path
        // allocation-free per CLAUDE.md invariant 6. BlockGraph is the
        // shared primitive every other connectivity consumer uses.
        private readonly BlockGraph.Buffers _bfsBuffers = new BlockGraph.Buffers();
        // RefreshCpuReachable mirrors the BFS visited set into this
        // dedicated collection so a downstream WouldOrphanIfRemoved call
        // (also BFS-driven) doesn't stomp the reachability snapshot.
        private readonly HashSet<Vector3Int> _cpuReachable = new HashSet<Vector3Int>(64);
        private bool _cpuReachableValid;

        private void OnEnable()
        {
            Subscribe();
            // If build mode is already active when we wake up, behave as if Entered just fired.
            if (_buildMode != null && _buildMode.IsActive) HandleEntered();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (_ghostRenderer != null) _ghostRenderer.Clear();
            if (_feedbackHud != null) _feedbackHud.Hide();
        }

        private void Subscribe()
        {
            if (_subscribed || _buildMode == null) return;
            _buildMode.Entered += HandleEntered;
            _buildMode.Exited  += HandleExited;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _buildMode == null) return;
            _buildMode.Entered -= HandleEntered;
            _buildMode.Exited  -= HandleExited;
            _subscribed = false;
        }

        private void HandleEntered()
        {
            // Re-resolve grid in case the chassis was respawned.
            _grid = _buildMode.Chassis != null ? _buildMode.Chassis.GetComponent<BlockGrid>() : null;
        }

        private void HandleExited()
        {
            _grid = null;
            if (_ghostRenderer != null) _ghostRenderer.Clear();
            if (_feedbackHud != null) _feedbackHud.Hide();
        }

        private void Update()
        {
            // Lazy-subscribe in case BuildMode was assigned after OnEnable ran.
            if (!_subscribed && _buildMode != null)
            {
                Subscribe();
                if (_buildMode.IsActive) HandleEntered();
            }
            if (_buildMode == null || !_buildMode.IsActive) return;
            if (_grid == null)
            {
                _grid = _buildMode.Chassis != null ? _buildMode.Chassis.GetComponent<BlockGrid>() : null;
                if (_grid == null) return;
            }
            UpdateTarget();
            // Drive the ghost renderer + feedback HUD with the freshly
            // picked target. The renderer figures out whether to rebuild
            // meshes itself.
            DriveGhostRenderer();
            DriveFeedbackHud();
            HandleClicks();
        }

        // -----------------------------------------------------------------
        // Targeting
        // -----------------------------------------------------------------

        private void UpdateTarget()
        {
            _hasTarget = false;
            _validPlacement = false;

            Mouse mouse = Mouse.current;
            Camera cam = Camera.main;
            if (mouse == null || cam == null) return;

            // Ignore picks while the cursor is over the build HUD.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, _raycastDistance, _raycastMask, QueryTriggerInteraction.Ignore))
                return;

            // Only react to picks that land on blocks belonging to OUR chassis
            // (not on the ground, walls, podium, etc.). Use grid-membership
            // rather than transform parenting because rotor-adopted foils
            // get reparented to a kinematic hub at scene root — they're
            // still in the chassis grid (and thus still legitimate edit
            // targets) but their transform isn't a child of the chassis
            // any more.
            BlockBehaviour block = hit.collider != null
                ? hit.collider.GetComponentInParent<BlockBehaviour>()
                : null;
            if (block == null) return;
            if (_grid == null) return;
            if (!_grid.Blocks.TryGetValue(block.GridPosition, out BlockBehaviour gridEntry) || gridEntry != block) return;

            _targetHitCell = block.GridPosition;

            // Convert hit normal to a grid step. World → local → round.
            Vector3 localN = _buildMode.Chassis.InverseTransformDirection(hit.normal);
            _targetPlaceCell = block.GridPosition + RoundToAxis(localN);

            _hasTarget = true;
            // EvaluatePlacement returns the specific failure reason so
            // the feedback HUD can render "Host is leaf at (1,1,0)" and
            // similar diagnostics. _validPlacement stays the bool fast
            // path the click handler reads.
            Vector3Int placeUp = _targetPlaceCell - _targetHitCell;
            if (placeUp == Vector3Int.zero) placeUp = Vector3Int.up;
            _lastPlacementError = EvaluatePlacement(_targetPlaceCell, placeUp);
            _validPlacement = _lastPlacementError == PlacementRules.PlacementError.None;
        }

        private static Vector3Int RoundToAxis(Vector3 dir)
        {
            // Pick the axis with the largest absolute component.
            float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y), az = Mathf.Abs(dir.z);
            if (ax >= ay && ax >= az) return new Vector3Int(dir.x >= 0f ? 1 : -1, 0, 0);
            if (ay >= az)              return new Vector3Int(0, dir.y >= 0f ? 1 : -1, 0);
            return                            new Vector3Int(0, 0, dir.z >= 0f ? 1 : -1);
        }

        // Mirror placement and ghost rendering ask "would this specific
        // (cell, up) tuple pass the placement rules?" — separate from
        // the targeting-derived primary check that fills _validPlacement
        // / _lastPlacementError.
        private bool IsValidPlacement(Vector3Int cell, Vector3Int up)
        {
            return EvaluatePlacement(cell, up) == PlacementRules.PlacementError.None;
        }

        /// <summary>
        /// Evaluate a candidate placement and return the specific reason
        /// it was rejected (or <see cref="PlacementRules.PlacementError.None"/>
        /// when valid). Single shared rule library — the validator runs
        /// the same checks at blueprint-load time per
        /// <see cref="BlueprintValidator.Validate"/>, so editor and
        /// validator can't diverge on what "legal" means.
        /// </summary>
        private PlacementRules.PlacementError EvaluatePlacement(Vector3Int cell, Vector3Int up)
        {
            if (_grid == null) return PlacementRules.PlacementError.None;
            BlockDefinition selected = GetSelectedDefinition();
            Vector3 candidateDims = (_variantPanel != null && selected != null)
                ? _variantPanel.GetDimsForBlock(selected.Id)
                : Vector3.zero;
            float candidatePitch = (_variantPanel != null && selected != null)
                ? _variantPanel.GetPitchForBlock(selected.Id)
                : 0f;
            var candidate = new PlacementRules.Candidate(selected, cell, up, candidateDims, candidatePitch);
            RefreshCpuReachable();
            return PlacementRules.EvaluatePlacement(
                _grid, in candidate,
                _cpuReachableValid ? _cpuReachable : null);
        }

        private BlockDefinition GetSelectedDefinition()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null || state.Library == null || _hotbar == null) return null;
            return state.Library.Get(_hotbar.SelectedBlockId);
        }

        // -----------------------------------------------------------------
        // Ghost / feedback HUD orchestration. Visual rendering lives on
        // BlockGhostRenderer + PlacementFeedbackHud; the editor just
        // marshals state into a per-frame request.
        // -----------------------------------------------------------------

        private void DriveGhostRenderer()
        {
            if (_ghostRenderer == null) return;

            BlockDefinition def = GetSelectedDefinition();
            string targetId = def != null ? def.Id : BlockIds.Cube;
            Vector3 targetDims = _variantPanel != null ? _variantPanel.GetDimsForBlock(targetId) : Vector3.zero;
            float targetPitch = _variantPanel != null ? _variantPanel.GetPitchForBlock(targetId) : 0f;
            Vector3Int targetCell = _hasTarget ? _targetPlaceCell : Vector3Int.zero;
            Vector3Int targetUp = _hasTarget ? (_targetPlaceCell - _targetHitCell) : Vector3Int.up;
            if (targetUp == Vector3Int.zero) targetUp = Vector3Int.up;

            bool showMirror = false;
            Vector3Int mCell = default, mUp = default;
            float mPitch = 0f;
            bool mValid = false;
            if (_hasTarget && _mirrorMode != null && _mirrorMode.Enabled)
            {
                MirrorAxis axis = _mirrorMode.Axis;
                if (!BlockMirror.IsOnPlane(targetCell, axis))
                {
                    mCell = BlockMirror.MirrorCell(targetCell, axis);
                    mUp = BlockMirror.MirrorUp(targetUp, axis);
                    // Pitch needs the SOURCE up to decide negation —
                    // same rule the mirror place path uses.
                    mPitch = BlockMirror.MirrorPitch(targetPitch, targetUp, axis);
                    mValid = IsValidPlacement(mCell, mUp);
                    showMirror = true;
                }
            }
            _mirrorGhostValid = mValid;
            _mirrorGhostShown = showMirror;

            var request = new GhostRequest(
                hasTarget: _hasTarget,
                definition: def,
                dims: targetDims,
                pitchDeg: targetPitch,
                cell: targetCell,
                up: targetUp,
                valid: _validPlacement,
                showMirror: showMirror,
                mirrorCell: mCell,
                mirrorUp: mUp,
                mirrorPitchDeg: mPitch,
                mirrorValid: mValid,
                chassisRoot: _buildMode != null ? _buildMode.Chassis : null,
                grid: _grid);
            _ghostRenderer.Render(in request);
        }

        private void DriveFeedbackHud()
        {
            if (_feedbackHud == null) return;
            if (!_hasTarget || _validPlacement)
            {
                _feedbackHud.Hide();
                return;
            }
            Vector3Int up = _targetPlaceCell - _targetHitCell;
            if (up == Vector3Int.zero) up = Vector3Int.up;
            Vector3Int hostCell = _targetPlaceCell - up;
            _feedbackHud.Show(_lastPlacementError, _targetPlaceCell, hostCell);
        }

        // -----------------------------------------------------------------
        // Place / remove
        // -----------------------------------------------------------------

        private void HandleClicks()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !_hasTarget) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (mouse.leftButton.wasPressedThisFrame)  TryPlace();
            if (mouse.rightButton.wasPressedThisFrame) TryRemove();
        }

        private void TryPlace()
        {
            if (!_validPlacement) return;
            string id = _hotbar != null ? _hotbar.SelectedBlockId : BlockIds.Cube;

            GameStateController state = GameStateController.Instance;
            if (state == null || state.Library == null) return;
            BlockDefinition def = state.Library.Get(id);
            if (def == null)
            {
                Debug.LogWarning($"[Robogame] BlockEditor: unknown block id '{id}'.");
                return;
            }

            // Block placing a 2nd CPU.
            if (def.Category == BlockCategory.Cpu && HasAnyCpu())
            {
                Debug.Log("[Robogame] BlockEditor: chassis already has a CPU; placement skipped.");
                return;
            }

            // Per-block "variable part" dims + pitch come from the variant
            // panel (foils: span/thickness/chord/pitch; rotors: collective;
            // ropes: segment count). Defaults (zero) tell PlaceBlock + the
            // consuming component to fall back to block defaults.
            Vector3 dims = _variantPanel != null ? _variantPanel.GetDimsForBlock(id) : Vector3.zero;
            float pitch = _variantPanel != null ? _variantPanel.GetPitchForBlock(id) : 0f;
            // Up = face normal of the cell we're attaching to. Without
            // this, every placement would orient as +Y and a wing on a
            // side-mount face would point along chassis +Y instead of
            // outward from the face.
            Vector3Int placeUp = _targetPlaceCell - _targetHitCell;
            if (placeUp == Vector3Int.zero) placeUp = Vector3Int.up;
            bool placed = _grid.PlaceBlock(def, _targetPlaceCell, placeUp, dims, pitch) != null;
            if (placed)
            {
                AutoPlaceCompanionsOf(def, _targetPlaceCell, placeUp);
                TryMirrorPlace(def, _targetPlaceCell, placeUp, dims, pitch);
                SyncBlueprintFromGrid();
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.BlockPlace);
            }
            else
            {
                // Placement rejected by the grid (collision / off-grid /
                // unauthorised cell). Surface the buzzer so the user
                // hears the same "no" they see.
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
            }
        }

        private void TryRemove()
        {
            if (!_grid.Blocks.TryGetValue(_targetHitCell, out BlockBehaviour block) || block == null) return;

            // Per Pass-3 design: CPU is sacred while editing.
            if (block.Definition != null && block.Definition.Category == BlockCategory.Cpu)
            {
                Debug.Log("[Robogame] BlockEditor: CPU cannot be removed in build mode.");
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
                return;
            }

            // Rotor cascade: removing a rotor co-removes its auto-placed
            // mechanism cube IF the cube is otherwise dependent on the
            // rotor. Without this, the orphan check would block every
            // rotor removal — the cube was placed for the rotor and has
            // no other neighbours initially.
            Vector3Int? cascadeCell = ResolveRotorCascadeCell(block, _targetHitCell);

            int orphanCount;
            bool wouldOrphan = cascadeCell.HasValue
                ? BlockGraph.WouldOrphanIfRemoved(_grid, _targetHitCell, cascadeCell.Value, _bfsBuffers, out orphanCount)
                : BlockGraph.WouldOrphanIfRemoved(_grid, _targetHitCell, _bfsBuffers, out orphanCount);

            if (wouldOrphan)
            {
                Debug.Log($"[Robogame] BlockEditor: removal blocked — would orphan {orphanCount} block(s).");
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
                return;
            }

            if (_grid.RemoveBlock(_targetHitCell))
            {
                if (cascadeCell.HasValue) _grid.RemoveBlock(cascadeCell.Value);
                TryMirrorRemove(_targetHitCell);
                SyncBlueprintFromGrid();
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.BlockRemove);
            }
        }

        /// <summary>
        /// If <paramref name="block"/> is a rotor whose mechanism cube
        /// would be orphaned by its removal, return the mechanism cell
        /// so the caller can cascade-remove both. Returns null when the
        /// rotor has dependents on the cube (blades, chained structure)
        /// — in that case the orphan check on the rotor alone correctly
        /// blocks the removal until the dependents go first.
        /// </summary>
        private Vector3Int? ResolveRotorCascadeCell(BlockBehaviour block, Vector3Int blockCell)
        {
            if (_grid == null || block == null || block.Definition == null) return null;
            if (block.Definition.Id != BlockIds.Rotor) return null;
            Vector3Int spinAxis = block.Up == Vector3Int.zero ? Vector3Int.up : block.Up;
            Vector3Int mechCell = blockCell + spinAxis;
            if (!_grid.TryGetBlock(mechCell, out BlockBehaviour mech) || mech == null) return null;
            if (mech.Definition == null || mech.Definition.Id != BlockIds.Cube) return null;
            // Only cascade when the cube has no neighbours other than
            // this rotor — otherwise blades or chained structure are at
            // stake, and the user should be the one to clear them.
            if (CountAdjacentBlocksExcluding(mechCell, blockCell) > 0) return null;
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
            if (_grid == null) return 0;
            int count = 0;
            for (int i = 0; i < s_cascadeFaces.Length; i++)
            {
                Vector3Int n = cell + s_cascadeFaces[i];
                if (n == excluded) continue;
                if (_grid.HasBlock(n)) count++;
            }
            return count;
        }

        // -----------------------------------------------------------------
        // Mirror mode — best-effort dual placement / removal. The original
        // is the source of truth; the mirror is a soft attempt that skips
        // (silently, no buzzer) if it would violate any placement rule.
        // -----------------------------------------------------------------

        private bool MirrorActive =>
            _mirrorMode != null && _mirrorMode.Enabled;

        /// <summary>
        /// Auto-place the structural companions a primary block needs to
        /// be usable. Called once per successful placement (primary side
        /// + once per mirror side), best-effort — silent if any
        /// companion fails its own placement (cell occupied, etc.).
        /// </summary>
        /// <remarks>
        /// <b>Rotor.</b> A rotor's only connective face is its spin
        /// axis (per <see cref="BlockConnectivity.IsConnectiveFace"/>),
        /// so a from-scratch rotor is unusable until a structural cube
        /// goes on that face — that's the cell where the rotor's disc +
        /// crossbars actually live, and the cube hosts the four blades.
        /// The default-helicopter preset does this implicitly via
        /// <see cref="BlueprintBuilder.RotorWithFoils"/>; the build-mode
        /// editor mirrors that behaviour so a placed rotor immediately
        /// behaves like the preset's.
        /// </remarks>
        private void AutoPlaceCompanionsOf(BlockDefinition def, Vector3Int cell, Vector3Int up)
        {
            if (def == null || _grid == null) return;
            if (def.Id == BlockIds.Rotor)
            {
                GameStateController state = GameStateController.Instance;
                if (state == null || state.Library == null) return;
                BlockDefinition cubeDef = state.Library.Get(BlockIds.Cube);
                if (cubeDef == null) return;
                Vector3Int mechanismCell = cell + up;
                if (_grid.HasBlock(mechanismCell)) return;
                _grid.PlaceBlock(cubeDef, mechanismCell, up, Vector3.zero, 0f);
            }
        }

        private void TryMirrorPlace(BlockDefinition def, Vector3Int cell, Vector3Int up, Vector3 dims, float pitchDeg)
        {
            if (!MirrorActive) return;
            MirrorAxis axis = _mirrorMode.Axis;
            if (BlockMirror.IsOnPlane(cell, axis)) return;
            Vector3Int mCell = BlockMirror.MirrorCell(cell, axis);
            Vector3Int mUp   = BlockMirror.MirrorUp(up, axis);
            float mPitch     = BlockMirror.MirrorPitch(pitchDeg, up, axis);
            if (mCell == cell) return;
            if (!IsValidPlacement(mCell, mUp)) return;
            BlockBehaviour mPlaced = _grid.PlaceBlock(def, mCell, mUp, dims, mPitch);
            if (mPlaced != null) AutoPlaceCompanionsOf(def, mCell, mUp);
        }

        private void TryMirrorRemove(Vector3Int cell)
        {
            if (!MirrorActive) return;
            MirrorAxis axis = _mirrorMode.Axis;
            if (BlockMirror.IsOnPlane(cell, axis)) return;
            Vector3Int mCell = BlockMirror.MirrorCell(cell, axis);
            if (mCell == cell) return;
            if (!_grid.Blocks.TryGetValue(mCell, out BlockBehaviour mBlock) || mBlock == null) return;
            if (mBlock.Definition != null && mBlock.Definition.Category == BlockCategory.Cpu) return;

            // Mirror cascade: same rotor → cube logic as the primary
            // path so a mirrored rotor + cube pair removes cleanly when
            // the user right-clicks one rotor in mirror mode.
            Vector3Int? cascadeCell = ResolveRotorCascadeCell(mBlock, mCell);

            bool wouldOrphan = cascadeCell.HasValue
                ? BlockGraph.WouldOrphanIfRemoved(_grid, mCell, cascadeCell.Value, _bfsBuffers, out _)
                : BlockGraph.WouldOrphanIfRemoved(_grid, mCell, _bfsBuffers, out _);
            if (wouldOrphan) return;

            _grid.RemoveBlock(mCell);
            if (cascadeCell.HasValue) _grid.RemoveBlock(cascadeCell.Value);
        }

        /// <summary>
        /// Re-run BFS from the CPU and cache the reachable-cell set into
        /// <see cref="_cpuReachable"/>. Sets <see cref="_cpuReachableValid"/>
        /// to false when the chassis has no CPU; the caller treats that as
        /// "empty-grid bootstrap mode" so the first CPU placement is
        /// allowed.
        /// </summary>
        /// <remarks>
        /// Plain physical-adjacency BFS. Earlier sessions skipped leaves
        /// as bridges here as a "no building past a wing" defense-in-depth
        /// measure, but the strict-host check (<c>IsLeaf(host) → reject</c>)
        /// in <see cref="IsValidPlacement(Vector3Int,Vector3Int)"/> already
        /// covers that intent. Skipping leaves here ALSO blocked
        /// legitimate placements downstream of authored leaf chains — e.g.
        /// the helicopter's mechanism cube is only reachable through the
        /// rotor (a leaf), so the player couldn't extend the rotor area at
        /// all. Drop the skip and let the strict-host check do the gating.
        /// </remarks>
        private void RefreshCpuReachable()
        {
            _cpuReachableValid = false;
            _cpuReachable.Clear();
            if (_grid == null) return;
            Vector3Int? cpu = BlockGraph.FindCpuCell(_grid);
            if (!cpu.HasValue) return;
            BlockGraph.BfsFrom(_grid, cpu.Value, _bfsBuffers);
            // Snapshot into our dedicated set so a downstream WouldOrphan
            // call (also on _bfsBuffers) doesn't stomp it mid-frame.
            foreach (Vector3Int v in _bfsBuffers.Visited) _cpuReachable.Add(v);
            _cpuReachableValid = true;
        }

        private bool HasAnyCpu()
        {
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b != null && b.Definition != null && b.Definition.Category == BlockCategory.Cpu)
                    return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // Blueprint sync
        // -----------------------------------------------------------------

        private void SyncBlueprintFromGrid()
        {
            // Route through the session so canonical-sort + on-disk
            // shape happen in one place. Falls back to direct sync if
            // no session is bound (legacy scenes / standalone editor).
            if (_session != null && _session.Blueprint != null)
            {
                _session.SyncBlueprint();
                return;
            }
            GameStateController state = GameStateController.Instance;
            if (state == null || state.CurrentBlueprint == null || _grid == null) return;

            var list = new List<ChassisBlueprint.Entry>(_grid.Blocks.Count);
            bool hasRotor = false;
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                list.Add(new ChassisBlueprint.Entry(b.Definition.Id, kvp.Key, b.Up, b.Dims, b.PitchDeg));
                if (b.Definition.Id == BlockIds.Rotor) hasRotor = true;
            }
            state.CurrentBlueprint.SetEntries(list.ToArray());
            // See BuildSession.SyncBlueprint for rationale: "this
            // chassis has rotors" auto-derives RotorsGenerateLift
            // until per-cell config lands.
            if (hasRotor) state.CurrentBlueprint.RotorsGenerateLift = true;
        }
    }
}
