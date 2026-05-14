using System.Collections.Generic;
using Robogame.Block;
using Robogame.Movement;
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
        // Unit mount-up for the candidate placement at _targetPlaceCell.
        // Stored separately from (_targetPlaceCell - _targetHitCell) because
        // the rope-tip redirect can produce a multi-cell delta there, while
        // PlacementRules.ResolveHostCell requires a unit axis on c.Up.
        // UpdateTarget sets this in lockstep with _targetPlaceCell; TryPlace
        // and DriveGhostRenderer / DriveFeedbackHud all read it from here.
        private Vector3Int _targetPlaceUp = Vector3Int.up;
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
            Vector3Int faceStep = RoundToAxis(localN);
            _targetPlaceCell = block.GridPosition + faceStep;

            // Rope-chain hit special case: the rope's chain visual spans
            // multiple cells but the hit collider belongs to the rope
            // cell. Mapping +rope.up hits to rope.cell + 1*up would
            // place the tip block one cell from the rope (under the
            // chain), not at the chain's free end where the player aimed.
            // Two triggers for the redirect to the tip cell:
            //   (a) RopeTipAimTarget hit — generous sphere collider at
            //       the chain free end. Any hit direction snaps; the
            //       cylinder's tiny end-cap was painful to aim through.
            //   (b) Cylinder-end-cap hit — faceStep already lines up
            //       with rope.up, so the player's intent is clear.
            // Either way, only fires when the selected block IS a tip
            // block — other selections keep the standard adjacent-cell
            // candidate so existing rejection paths fire at the same
            // cell as before.
            RopeTipAimTarget tipAim = hit.collider != null ? hit.collider.GetComponent<RopeTipAimTarget>() : null;
            bool ropeHit = block.Definition != null && block.Definition.Id == BlockIds.Rope;
            if (ropeHit && IsTipBlockSelected()
                && (tipAim != null || faceStep == block.Up))
            {
                // Force faceStep to rope.up so the downstream placeUp
                // matches the rope's mount-up — required by
                // PlacementRules.ResolveHostCell, which walks back
                // along -c.Up looking for the rope at matching distance.
                faceStep = block.Up;
                _targetPlaceCell = block.GridPosition + block.Up * RopeGeometry.ChainCellCount(block);
            }

            _hasTarget = true;
            // Cache the unit mount-up for downstream consumers (click
            // handler, ghost renderer, feedback HUD). Computing it from
            // (_targetPlaceCell - _targetHitCell) at click time would be
            // wrong for the rope-tip case, where _targetPlaceCell sits
            // ChainCellCount cells away — TryPlace would then hand a
            // multi-cell vector to BuildSession.TryPlace and the rules
            // engine would reject the placement (host walk-back distance
            // mismatch).
            _targetPlaceUp = faceStep == Vector3Int.zero ? Vector3Int.up : faceStep;
            // EvaluatePlacement returns the specific failure reason so
            // the feedback HUD can render "Host is leaf at (1,1,0)" and
            // similar diagnostics. _validPlacement stays the bool fast
            // path the click handler reads.
            _lastPlacementError = EvaluatePlacement(_targetPlaceCell, _targetPlaceUp);
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

        private bool IsTipBlockSelected()
        {
            BlockDefinition def = GetSelectedDefinition();
            if (def == null) return false;
            return def.Id == BlockIds.Hook || def.Id == BlockIds.Mace || def.Id == BlockIds.Magnet;
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
            float worldPitch = _variantPanel != null ? _variantPanel.GetPitchForBlock(targetId) : 0f;
            Vector3Int targetCell = _hasTarget ? _targetPlaceCell : Vector3Int.zero;
            // Read the cached unit mount-up populated by UpdateTarget so
            // the ghost orients the same way the click handler will place.
            Vector3Int targetUp = _hasTarget ? _targetPlaceUp : Vector3Int.up;
            if (targetUp == Vector3Int.zero) targetUp = Vector3Int.up;
            // Ghost factory expects local-frame pitch (same as the placed
            // block uses). World-intent → local conversion happens here
            // so the ghost matches what the player will actually place.
            float targetLocalPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, targetUp);

            bool showMirror = false;
            Vector3Int mCell = default, mUp = default;
            float mLocalPitch = 0f;
            bool mValid = false;
            if (_hasTarget && _mirrorMode != null && _mirrorMode.Enabled)
            {
                MirrorAxis axis = _mirrorMode.Axis;
                if (!BlockMirror.IsOnPlane(targetCell, axis))
                {
                    mCell = BlockMirror.MirrorCell(targetCell, axis);
                    mUp = BlockMirror.MirrorUp(targetUp, axis);
                    // Same world-intent → local-pitch conversion as the
                    // primary side, just with the mirrored up.
                    mLocalPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, mUp);
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
                pitchDeg: targetLocalPitch,
                cell: targetCell,
                up: targetUp,
                valid: _validPlacement,
                showMirror: showMirror,
                mirrorCell: mCell,
                mirrorUp: mUp,
                mirrorPitchDeg: mLocalPitch,
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
            // Unit mount-up cached by UpdateTarget — host = place - up.
            Vector3Int up = _targetPlaceUp == Vector3Int.zero ? Vector3Int.up : _targetPlaceUp;
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
            if (_session == null)
            {
                Debug.LogWarning("[Robogame] BlockEditor: no BuildSession bound — placement skipped.");
                return;
            }
            string id = _hotbar != null ? _hotbar.SelectedBlockId : BlockIds.Cube;

            GameStateController state = GameStateController.Instance;
            if (state == null || state.Library == null) return;
            BlockDefinition def = state.Library.Get(id);
            if (def == null)
            {
                Debug.LogWarning($"[Robogame] BlockEditor: unknown block id '{id}'.");
                return;
            }

            // Per-block "variable part" dims + world-intent pitch come from
            // the variant panel (foils: span/thickness/chord/pitch; rotors:
            // collective; ropes: length-in-cells). Zero means "use the
            // block's authored default". The session normalizes world-intent
            // pitch to local-frame internally per side.
            Vector3 dims = _variantPanel != null ? _variantPanel.GetDimsForBlock(id) : Vector3.zero;
            float worldPitch = _variantPanel != null ? _variantPanel.GetPitchForBlock(id) : 0f;

            // Push mirror state onto the session so its TryPlace handles
            // the mirrored side too — single source of truth for the rule
            // check, the grid mutation, the auto-companion cascade, and
            // the blueprint sync. Editor is a thin driver from here on.
            _session.SetMirrorEnabled(_mirrorMode != null && _mirrorMode.Enabled);
            _session.SetMirrorAxis(_mirrorMode != null ? _mirrorMode.Axis : Robogame.Block.MirrorAxis.X);

            // Use the cached unit mount-up — same value the ghost preview
            // and placement-rule evaluator saw, so ghost-valid = click-valid.
            BuildSession.PlaceOutcome outcome = _session.TryPlace(def, _targetPlaceCell, _targetPlaceUp, dims, worldPitch);
            if (outcome.PrimarySucceeded)
            {
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.BlockPlace);
            }
            else
            {
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
            }
        }

        private void TryRemove()
        {
            if (_session == null || _grid == null) return;
            if (!_grid.HasBlock(_targetHitCell)) return;

            _session.SetMirrorEnabled(_mirrorMode != null && _mirrorMode.Enabled);
            _session.SetMirrorAxis(_mirrorMode != null ? _mirrorMode.Axis : Robogame.Block.MirrorAxis.X);

            BuildSession.RemoveOutcome outcome = _session.TryRemove(_targetHitCell);
            if (outcome.PrimarySucceeded)
            {
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.BlockRemove);
            }
            else
            {
                // CPU-sacred / would-orphan / etc. — session rejected. The
                // log line preserves the diagnostic the old direct path
                // emitted.
                if (outcome.Primary == PlacementRules.PlacementError.WouldOrphanOnRemoval)
                    Debug.Log("[Robogame] BlockEditor: removal blocked — would orphan one or more blocks.");
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
            }
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

    }
}
