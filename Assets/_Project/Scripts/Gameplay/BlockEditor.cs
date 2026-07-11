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
            set
            {
                if (_session == value) return;
                if (_session != null)
                {
                    _session.VariantChanged -= PropagateVariantToLiveBlocks;
                    _session.EditingInstanceChanged -= HandleEditingInstanceChanged;
                }
                _session = value;
                if (_session != null)
                {
                    _session.VariantChanged += PropagateVariantToLiveBlocks;
                    _session.EditingInstanceChanged += HandleEditingInstanceChanged;
                }
            }
        }

        // Cursor policy for tune mode: the reticle stays LOCKED while the
        // player aims (the Minecraft/Space Engineers feel is deliberate —
        // see session 138 round 3); the cursor frees only while a part is
        // actually bound, because that's when the sliders need it. Driven
        // off the session event so every unbind path (click, Escape rung,
        // mode exit, build exit) releases the hold consistently.
        private void HandleEditingInstanceChanged(BlockBehaviour bound)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                var freeCam = cam.GetComponent<Robogame.Player.BuildFreeCam>();
                if (freeCam != null) freeCam.ExternalCursorHold = bound != null;
            }
            // Unbinding tears down the highlight + panel title here (not
            // only in ClearInstanceEdit) so external callers that clear
            // straight through the session — e.g. the Escape rung in
            // PauseMenuHud — get the full cleanup too.
            if (bound == null)
            {
                if (_instanceHighlight != null)
                {
                    Destroy(_instanceHighlight);
                    _instanceHighlight = null;
                }
                if (_variantPanel != null && _hotbar != null)
                    _variantPanel.RefreshForBlock(_hotbar.SelectedBlockId);
            }
        }

        // VariantConfigPanel writes pitch / dims into the session cache.
        // A placed block updates ONLY when it is bound for per-instance
        // editing (session 125 Edit mode); with no instance bound the cache
        // is purely "next placement" state, previewed by the ghost. The
        // pre-125 fallback — pushing the cache onto every placed block of
        // the type (session-96 live-mid-edit) — was retired because it made
        // per-block tuning impossible: changing one wing's span silently
        // rewrote every wing on the bot.
        private void PropagateVariantToLiveBlocks(string blockId)
        {
            if (_session == null || string.IsNullOrEmpty(blockId)) return;
            BlockBehaviour block = _session.EditingInstance;
            if (block == null || block.Definition == null) return;
            if (block.Definition.Id != blockId) return;
            float worldPitch = _session.GetVariantPitch(blockId);
            float worldTeeter = _session.GetVariantTeeter(blockId);
            Vector3 dims = _session.GetVariantDims(blockId);
            float config = _session.GetVariantConfig(blockId);
            // The cache holds WORLD-INTENT angles; placed blocks store
            // local-frame. Normalize per block's own mount-up — the
            // same conversion placement does. (Previously the raw
            // world value was pushed, silently flipping the sign on
            // lateral-mounted foils relative to what placement wrote.)
            block.SetPitch(BlockOrientation.NormalizePitchForUp(block.Definition, worldPitch, block.Up));
            block.SetTeeter(BlockOrientation.NormalizePitchForUp(block.Definition, worldTeeter, block.Up));
            block.SetDims(dims);
            // ConfigValue (rotor RPM / thruster max thrust / module
            // power) is read live by its consumer each tick or at
            // fire time — a plain field write is enough, no Changed
            // event. 0 means "use authored default", same convention
            // the pitch/dims paths follow.
            block.ConfigValue = config;
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

        private BuildEditMode _editMode;
        public BuildEditMode EditMode
        {
            get => _editMode;
            set
            {
                if (_editMode != null) _editMode.Changed -= HandleEditModeChanged;
                _editMode = value;
                if (_editMode != null) _editMode.Changed += HandleEditModeChanged;
            }
        }

        // Turning the Tune-part toggle OFF drops any bound instance + its
        // highlight, returning to plain placement. Turning it on clears the
        // placement ghost — in tune mode the hover highlight is the
        // affordance, and a lingering ghost reads as "I can still place".
        private void HandleEditModeChanged(bool enabled)
        {
            if (!enabled)
            {
                ClearInstanceEdit();
                HideHoverHighlight();
            }
            else
            {
                if (_ghostRenderer != null) _ghostRenderer.Clear();
                if (_feedbackHud != null) _feedbackHud.Hide();
            }
        }

        [Tooltip("Layer mask used by the targeting raycast. Default: everything.")]
        [SerializeField] private LayerMask _raycastMask = ~0;

        [Tooltip("Maximum picking distance.")]
        [SerializeField, Min(1f)] private float _raycastDistance = 100f;

        // CPU budget cap shape lives in Block.CpuBudget (one source of truth,
        // shared with the spawn-time enforcer). The garage readout is
        // advisory — placements aren't rejected over-cap; ArenaController
        // strips to fit at match start.

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
                // Per-instance effective cost (rotor RPM scaling +
                // concoction surcharge), same pricing core TrimToFit
                // charges at spawn — the bar must not under-promise.
                used += Block.CpuBudget.EffectiveCpuCost(b);
                if (b.Definition.Category == BlockCategory.Cpu) cpus++;
            }
            return new CpuUsage(used, cpus * Block.CpuBudget.BudgetPerCpuBlock);
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
                used += Block.CpuBudget.EffectiveCpuCost(b);
                if (b.Definition.Category == BlockCategory.Cpu) cpus++;
                mass += b.Definition.Mass;
                count++;
            }
            return new ChassisStats(used, cpus * Block.CpuBudget.BudgetPerCpuBlock, count, mass);
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
            OnEnable_RehookSession();
            // If build mode is already active when we wake up, behave as if Entered just fired.
            if (_buildMode != null && _buildMode.IsActive) HandleEntered();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (_session != null)
            {
                _session.VariantChanged -= PropagateVariantToLiveBlocks;
                _session.EditingInstanceChanged -= HandleEditingInstanceChanged;
            }
            if (_editMode != null) _editMode.Changed -= HandleEditModeChanged;
            HideHoverHighlight();
            if (_ghostRenderer != null) _ghostRenderer.Clear();
            if (_feedbackHud != null) _feedbackHud.Hide();
        }

        private void OnEnable_RehookSession()
        {
            // Re-hook on enable (in case OnDisable detached us but the
            // Session reference is still live). Unsubscribe first so we're
            // idempotent regardless of whether the Session setter already
            // subscribed before OnEnable ran.
            if (_session == null) return;
            _session.VariantChanged -= PropagateVariantToLiveBlocks;
            _session.VariantChanged += PropagateVariantToLiveBlocks;
            _session.EditingInstanceChanged -= HandleEditingInstanceChanged;
            _session.EditingInstanceChanged += HandleEditingInstanceChanged;
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
            ClearInstanceEdit();
            HideHoverHighlight();
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
            if (_editMode != null && _editMode.Enabled)
            {
                // Tune mode: no placement ghost / error HUD (cleared on the
                // mode transition) — the hover highlight marks what's
                // clickable instead.
                DriveHoverHighlight();
            }
            else
            {
                HideHoverHighlight();
                // Drive the ghost renderer + feedback HUD with the freshly
                // picked target. The renderer figures out whether to rebuild
                // meshes itself.
                DriveGhostRenderer();
                DriveFeedbackHud();
            }
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
            float worldTeeter = _variantPanel != null ? _variantPanel.GetTeeterForBlock(targetId) : 0f;
            Vector3Int targetCell = _hasTarget ? _targetPlaceCell : Vector3Int.zero;
            // Read the cached unit mount-up populated by UpdateTarget so
            // the ghost orients the same way the click handler will place.
            Vector3Int targetUp = _hasTarget ? _targetPlaceUp : Vector3Int.up;
            if (targetUp == Vector3Int.zero) targetUp = Vector3Int.up;
            // Ghost factory expects local-frame pitch/teeter (same as the
            // placed block uses). World-intent → local conversion happens
            // here so the ghost matches what the player will actually place.
            float targetLocalPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, targetUp);
            float targetLocalTeeter = BlockOrientation.NormalizePitchForUp(def, worldTeeter, targetUp);

            bool showMirror = false;
            Vector3Int mCell = default, mUp = default;
            float mLocalPitch = 0f, mLocalTeeter = 0f;
            bool mValid = false;
            if (_hasTarget && _mirrorMode != null && _mirrorMode.Enabled)
            {
                MirrorAxis axis = _mirrorMode.Axis;
                if (!BlockMirror.IsOnPlane(targetCell, axis))
                {
                    mCell = BlockMirror.MirrorCell(targetCell, axis);
                    mUp = BlockMirror.MirrorUp(targetUp, axis);
                    // Same world-intent → local conversion as the
                    // primary side, just with the mirrored up.
                    mLocalPitch = BlockOrientation.NormalizePitchForUp(def, worldPitch, mUp);
                    mLocalTeeter = BlockOrientation.NormalizePitchForUp(def, worldTeeter, mUp);
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
                teeterDeg: targetLocalTeeter,
                cell: targetCell,
                up: targetUp,
                yaw: _session != null ? _session.PlaceYaw : 0,
                valid: _validPlacement,
                showMirror: showMirror,
                mirrorCell: mCell,
                mirrorUp: mUp,
                mirrorPitchDeg: mLocalPitch,
                mirrorTeeterDeg: mLocalTeeter,
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
            // Rotate the pending placement about the mount axis (R), in 90° steps.
            // Works without a hover target so the player can pre-rotate. Skipped
            // while a UI text field is focused so it doesn't eat keystrokes.
            Keyboard kb = Keyboard.current;
            // Text-field-only guard — the old any-selection check left R
            // dead after every button/slider click (selection persists).
            bool typing = Robogame.Core.UguiNav.IsTextInputFocused();
            if (kb != null && kb.rKey.wasPressedThisFrame && _session != null && !typing)
            {
                _session.CyclePlaceYaw();
                DriveGhostRenderer(); // reflect the new yaw immediately
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // Middle button is shared with OrbitCamera's drag-pan, so the
            // eyedropper resolves on RELEASE and only when the cursor barely
            // moved — a middle-drag pans, a middle-click picks.
            if (mouse.middleButton.wasPressedThisFrame)
            {
                _middlePressPos = mouse.position.ReadValue();
                _middlePressActive = true;
            }
            if (mouse.middleButton.wasReleasedThisFrame && _middlePressActive)
            {
                _middlePressActive = false;
                const float clickSlopPixels = 5f;
                bool wasClick = (mouse.position.ReadValue() - _middlePressPos).sqrMagnitude
                    <= clickSlopPixels * clickSlopPixels;
                if (wasClick && _hasTarget) TryPickBlock();
            }

            // TUNE mode (the explicit toggle button): a left-click binds the
            // pointed block to the variant panel for in-place editing instead
            // of placing. Place / remove are suppressed so a tweak-click can't
            // accidentally drop or delete a block. Runs BEFORE the
            // has-target gate so empty-space clicks can deselect.
            if (_editMode != null && _editMode.Enabled)
            {
                HandleTuneClicks(mouse);
                return;
            }

            if (!_hasTarget) return;

            if (mouse.leftButton.wasPressedThisFrame)   TryPlace();
            if (mouse.rightButton.wasPressedThisFrame)  TryRemove();
        }

        // Right-click-vs-right-drag discrimination for tune mode. By
        // accumulated mouse DELTA, not cursor travel — BuildFreeCam locks
        // the cursor during a right-drag look, which freezes the position.
        private float _tuneRightAccumPx;
        private bool _tuneRightActive;

        // Tune-mode clicks: left on a tunable part binds it (re-targeting
        // works mid-session); left on empty space or a plain right-click
        // unbinds WITHOUT leaving the mode; right-drag is the camera look.
        private void HandleTuneClicks(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_hasTarget) TryBindInstanceForEdit();
                else DeselectTunedInstance();
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                _tuneRightActive = true;
                _tuneRightAccumPx = 0f;
            }
            if (_tuneRightActive && mouse.rightButton.isPressed)
                _tuneRightAccumPx += mouse.delta.ReadValue().magnitude;
            if (mouse.rightButton.wasReleasedThisFrame && _tuneRightActive)
            {
                _tuneRightActive = false;
                const float clickSlopPx = 8f;
                if (_tuneRightAccumPx <= clickSlopPx) DeselectTunedInstance();
            }
        }

        private void DeselectTunedInstance()
        {
            if (_session == null || _session.EditingInstance == null) return;
            ClearInstanceEdit();
            Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiBack);
        }

        // Middle-click vs middle-drag discrimination state (see HandleClicks).
        private Vector2 _middlePressPos;
        private bool _middlePressActive;

        /// <summary>
        /// Middle-click eyedropper: select the targeted block's type in the
        /// hotbar and load that instance's per-block settings (dims, pitch,
        /// teeter, scalar config) into the session variant caches, so the
        /// NEXT placement replicates the picked block. Does not bind the
        /// block for editing — that's the explicit Edit-mode flow below.
        /// </summary>
        private void TryPickBlock()
        {
            if (_session == null || _grid == null) return;
            // Route mechanism-cube hits to the owning rotor — the cube is
            // invisible; what the player sees at that cell is the rotor's
            // upper mast, so the pick must land on the rotor.
            Vector3Int pickCell = BuildSession.ResolveMechanismOwnerCell(_grid.Blocks, _targetHitCell);
            if (!_grid.Blocks.TryGetValue(pickCell, out BlockBehaviour b) || b == null) return;
            BlockDefinition def = b.Definition;
            if (def == null) return;

            // Hotbar first — if the type isn't player-placeable (e.g. an
            // auto-placed mechanism cube) decline the whole pick rather
            // than half-applying cache writes for an unselectable id.
            if (_hotbar == null || !_hotbar.SelectByBlockId(def.Id))
            {
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
                return;
            }

            LoadBlockSettingsIntoCache(def, b);
            if (_variantPanel != null) _variantPanel.RefreshForBlock(def.Id);
            Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiClick);
        }

        /// <summary>
        /// Edit-mode left-click: bind the targeted block as the single
        /// instance the variant sliders drive (live, no delete / orphaning).
        /// Only blocks that actually have tunable variants bind; others flash
        /// invalid so the player isn't left with an "EDITING" panel that has
        /// no sliders.
        /// </summary>
        private void TryBindInstanceForEdit()
        {
            if (_session == null || _grid == null) return;
            // Same mechanism-cube → rotor routing as the eyedropper.
            Vector3Int pickCell = BuildSession.ResolveMechanismOwnerCell(_grid.Blocks, _targetHitCell);
            if (!_grid.Blocks.TryGetValue(pickCell, out BlockBehaviour b) || b == null) return;
            BlockDefinition def = b.Definition;
            if (def == null) return;

            if (!VariantConfigPanel.IsVariableBlock(def.Id))
            {
                Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.InvalidPlacement);
                return;
            }

            // Bind BEFORE writing the caches: SetVariant* fires VariantChanged
            // → PropagateVariantToLiveBlocks, which targets only the bound
            // block (values echo back onto itself, a no-op). Binding after
            // would make the cache loads inert and the panel refresh see a
            // half-bound state.
            _session.SetEditingInstance(b);
            HighlightInstance(b);
            LoadBlockSettingsIntoCache(def, b);
            if (_variantPanel != null) _variantPanel.RefreshForBlock(def.Id);
            Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiClick);
        }

        // Copy a placed block's per-instance settings into the per-id variant
        // cache so the panel's sliders show them. Stored pitch / teeter are
        // local-frame; the panel + placement pipeline speak world-intent, and
        // NormalizePitchForUp is involutive so the same call inverts it.
        // Rotors bypass the scheme (pitch = collective, stored as-is) — the
        // def overload handles both.
        private void LoadBlockSettingsIntoCache(BlockDefinition def, BlockBehaviour b)
        {
            float worldPitch = BlockOrientation.NormalizePitchForUp(def, b.PitchDeg, b.Up);
            float worldTeeter = BlockOrientation.NormalizePitchForUp(def, b.TeeterDeg, b.Up);
            _session.SetVariantDims(def.Id, b.Dims);
            _session.SetVariantPitch(def.Id, worldPitch);
            _session.SetVariantTeeter(def.Id, worldTeeter);
            _session.SetVariantConfig(def.Id, b.ConfigValue);
        }

        // -----------------------------------------------------------------
        // Instance-edit highlight (session 125)
        // -----------------------------------------------------------------

        private GameObject _instanceHighlight;
        private static Material s_highlightMat;

        // Drop the per-instance edit binding and its highlight, then refresh
        // the panel title back to normal-placement wording. Safe to call when
        // nothing is bound.
        private void ClearInstanceEdit()
        {
            if (_session != null) _session.SetEditingInstance(null);
            if (_instanceHighlight != null)
            {
                Destroy(_instanceHighlight);
                _instanceHighlight = null;
            }
            if (_variantPanel != null && _hotbar != null)
                _variantPanel.RefreshForBlock(_hotbar.SelectedBlockId);
        }

        // Translucent box around the edited block so the player can see which
        // instance their sliders are driving. A bounding cube (not a shape
        // match) is enough to answer "which one"; parented to the block so it
        // tracks any reparent (rotor-adopted foils) and dies with the block.
        private void HighlightInstance(BlockBehaviour block)
        {
            if (_instanceHighlight != null) { Destroy(_instanceHighlight); _instanceHighlight = null; }
            if (block == null) return;

            if (s_highlightMat == null)
                s_highlightMat = Robogame.Core.RuntimeMaterials.UnlitTransparent(new Color(1f, 0.62f, 0.10f, 0.22f));

            _instanceHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _instanceHighlight.name = "InstanceEditHighlight";
            Collider col = _instanceHighlight.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = _instanceHighlight.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = s_highlightMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            FitShellToBlock(_instanceHighlight, block);
        }

        // Fit a highlight shell to the block's full RENDERED bounds — a
        // wing glows across its whole span, a rotor across its disc. The
        // old cell-sized shell read as a faint box at the mount point,
        // not a glow on the part (session 138 playtest). World-axis AABB
        // is fine for a glow read; the chassis is parked in build mode.
        // Parented afterwards (preserving world pose) so it tracks the
        // block and dies with it. Allocation only on hover/bind changes,
        // never per frame.
        private void FitShellToBlock(GameObject shell, BlockBehaviour block)
        {
            Transform t = shell.transform;
            t.SetParent(null, worldPositionStays: false);
            Bounds b = default;
            bool has = false;
            Renderer[] rends = block.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null) continue;
                // Never measure our own shells — a shell inside the bounds
                // pass would inflate itself by 8% per refit.
                GameObject go = r.gameObject;
                if (go == shell || go == _instanceHighlight || go == _hoverHighlight) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            float cell = _grid != null ? _grid.CellSize : 1f;
            if (!has) b = new Bounds(block.transform.position, Vector3.one * cell);
            t.SetPositionAndRotation(b.center, Quaternion.identity);
            // 8% swell + a small absolute pad so thin parts (wing sheets)
            // still get a visible halo instead of a coplanar z-fight.
            t.localScale = b.size * 1.08f + Vector3.one * 0.08f;
            t.SetParent(block.transform, worldPositionStays: true);
        }

        // -----------------------------------------------------------------
        // Tune-mode hover highlight — a fainter shell than the bound-
        // instance one, marking the tunable block under the cursor as
        // clickable. One shell object reused across hovers (invariant #6:
        // no per-frame allocations); it only reparents when the hovered
        // block changes.
        // -----------------------------------------------------------------

        private GameObject _hoverHighlight;
        private BlockBehaviour _hoverBlock;
        // Per-editor material instance (not the static shared one) so the
        // pulse below can animate alpha without touching other shells.
        private Material _hoverMat;
        private static readonly Color s_hoverBase = new Color(1f, 0.62f, 0.10f, 0.14f);

        private void DriveHoverHighlight()
        {
            PulseHoverHighlight();

            BlockBehaviour target = null;
            if (_hasTarget && _grid != null)
            {
                Vector3Int cell = BuildSession.ResolveMechanismOwnerCell(_grid.Blocks, _targetHitCell);
                if (_grid.Blocks.TryGetValue(cell, out BlockBehaviour b) && b != null
                    && b.Definition != null && VariantConfigPanel.IsVariableBlock(b.Definition.Id))
                    target = b;
            }
            // The bound instance already wears the stronger edit shell —
            // don't stack a second one on it.
            if (target != null && _session != null && target == _session.EditingInstance) target = null;
            if (target == _hoverBlock && (target == null || _hoverHighlight != null)) return;
            _hoverBlock = target;
            if (target == null)
            {
                HideHoverHighlight();
                return;
            }

            if (_hoverMat == null)
                _hoverMat = Robogame.Core.RuntimeMaterials.UnlitTransparent(s_hoverBase);
            if (_hoverHighlight == null)
            {
                _hoverHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _hoverHighlight.name = "TuneHoverHighlight";
                Collider col = _hoverHighlight.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var mr = _hoverHighlight.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = _hoverMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            FitShellToBlock(_hoverHighlight, target);
            _hoverHighlight.SetActive(true);
        }

        // Slow alpha breathe on the hover shell so tunable parts read as
        // "glowing" rather than faintly boxed. One material color write
        // per frame while tune mode is on — no allocation.
        private void PulseHoverHighlight()
        {
            if (_hoverMat == null || _hoverHighlight == null || !_hoverHighlight.activeSelf) return;
            Color c = s_hoverBase;
            c.a = 0.12f + 0.12f * Mathf.PingPong(Time.unscaledTime * 1.6f, 1f);
            _hoverMat.color = c;
        }

        private void HideHoverHighlight()
        {
            _hoverBlock = null;
            if (_hoverHighlight == null) return;
            // Detach so a later host-block destroy can't take the reusable
            // shell down with it.
            _hoverHighlight.transform.SetParent(null, worldPositionStays: false);
            _hoverHighlight.SetActive(false);
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
            float worldTeeter = _variantPanel != null ? _variantPanel.GetTeeterForBlock(id) : 0f;

            // Push mirror state onto the session so its TryPlace handles
            // the mirrored side too — single source of truth for the rule
            // check, the grid mutation, the auto-companion cascade, and
            // the blueprint sync. Editor is a thin driver from here on.
            _session.SetMirrorEnabled(_mirrorMode != null && _mirrorMode.Enabled);
            _session.SetMirrorAxis(_mirrorMode != null ? _mirrorMode.Axis : Robogame.Block.MirrorAxis.X);

            // Use the cached unit mount-up — same value the ghost preview
            // and placement-rule evaluator saw, so ghost-valid = click-valid.
            BuildSession.PlaceOutcome outcome = _session.TryPlace(def, _targetPlaceCell, _targetPlaceUp, dims, worldPitch, worldTeeter);
            if (outcome.PrimarySucceeded)
            {
                // Placing a fresh block leaves instance-edit — the player is
                // back to authoring, and a slider drag should shape the
                // next placement, not the just-deselected instance.
                if (_session.EditingInstance != null) ClearInstanceEdit();
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
            // Mechanism-cube → rotor routing: right-click on the invisible
            // cube region (the rotor's upper mast) removes the rotor — the
            // session's cascade then co-removes the cube.
            Vector3Int removeCell = BuildSession.ResolveMechanismOwnerCell(_grid.Blocks, _targetHitCell);
            if (!_grid.HasBlock(removeCell)) return;

            _session.SetMirrorEnabled(_mirrorMode != null && _mirrorMode.Enabled);
            _session.SetMirrorAxis(_mirrorMode != null ? _mirrorMode.Axis : Robogame.Block.MirrorAxis.X);

            BuildSession.RemoveOutcome outcome = _session.TryRemove(removeCell);
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
