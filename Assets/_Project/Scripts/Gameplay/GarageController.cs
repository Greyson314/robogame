using Robogame.Block;
using Robogame.Combat;
using Robogame.Player;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Lives in the Garage scene. On <see cref="Start"/> it spawns the
    /// player's current chassis from <see cref="GameStateController.CurrentBlueprint"/>,
    /// binds the <see cref="FollowCamera"/>, and exposes <see cref="Launch"/>
    /// so the scene HUD can transition to the arena.
    /// </summary>
    /// <remarks>
    /// Pass A scope: spawn-and-show. Pass B will add the in-garage editor
    /// (placement tool, save/load, validation) on top of this controller.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GarageController : MonoBehaviour
    {
        [Tooltip("Where the chassis spawns inside the garage. The Y is the " +
                 "*pivot* origin; the actual chassis is then offset so the " +
                 "lowest block sits at HoverHeight above the floor.")]
        // Sized so a default 0.5-radius wheel on a floor cube has a
        // half-cell suspension drop and rests on the ground (Y=0): cube
        // centre at Y=1, cube bottom at Y=0.5, wheel hub at Y=0.5,
        // wheel bottom at Y=0. Was Y=1.5 — the old 0.35-radius wheels
        // needed a 1.15 m suspension hang, which left a visible gap
        // between the wheel cell and the tyre once the stem became
        // visible in session 41.
        [SerializeField] private Vector3 _spawnPosition = new Vector3(0f, 1f, 0f);

        [Tooltip("Initial chassis facing.")]
        [SerializeField] private Vector3 _spawnEuler = Vector3.zero;

        [Tooltip("Height (in cells) the lowest block on the chassis is " +
                 "lifted above the garage floor. The bot 'hovers' at this " +
                 "height by default so the wheels/fins/wings (and any rope " +
                 "blocks dangling below the chassis) clear the floor and " +
                 "the player can see it from a low garage camera. Set to 0 " +
                 "to drop the chassis to ground-resting.")]
        [SerializeField, Min(0f)] private float _hoverHeightCells = 12f;

        [Tooltip("Name of the spawned chassis GameObject. Used by " +
                 "Robot.RebuildByName / DevHud.")]
        [SerializeField] private string _chassisName = "Robot";

        public GameObject Chassis { get; private set; }

        private BuildModeController _buildMode;
        private BlockEditor _editor;
        private BuildHotbar _hotbar;
        private VariantConfigPanel _variantPanel;
        private LabController _lab;
        private BuildMirrorMode _mirrorMode;
        private BuildEditMode _editMode;
        private BuildMoveMode _moveMode;
        private bool _concoctionLibrarySubscribed;
        private BlockGhostRenderer _ghostRenderer;
        private PlacementFeedbackHud _feedbackHud;
        private CenterOverlay _centerOverlay;
        private BuildModeFrame _buildFrame;
        // Plain-C# build-mode model. Owns the variant cache + mirror
        // state + place/remove verbs so the MonoBehaviour drivers stay
        // thin and EditMode tests can drive build-mode logic without
        // a scene. Lifetime is the Garage's — survives respawns.
        private BuildSession _buildSession;

        /// <summary>The build-mode controller hosted on this object. Lazily created.</summary>
        public BuildModeController BuildMode => _buildMode;

        /// <summary>The plain-C# build-mode model shared by every driver component.</summary>
        public BuildSession BuildSession => _buildSession;
        /// <summary>Live block editor (null until build mode is wired). Escape ladder consults it for the move-carry rung (169).</summary>
        public BlockEditor BlockEditor => _editor;

        private void OnEnable()
        {
            GameStateController state = GameStateController.Instance;
            if (state != null) state.PresetChanged += HandlePresetChanged;
        }

        private void OnDisable()
        {
            GameStateController state = GameStateController.Instance;
            if (state != null) state.PresetChanged -= HandlePresetChanged;
            // BuildModeController is on the same GameObject so we'd
            // ordinarily die together; explicit unsubscribe is for hot-
            // reload safety and component-replacement scenarios.
            if (_buildMode != null) _buildMode.Exited -= HandleBuildModeExited;
            if (_concoctionLibrarySubscribed)
            {
                Robogame.Block.ConcoctionLibrary.Changed -= HandleConcoctionLibraryChanged;
                _concoctionLibrarySubscribed = false;
            }
        }

        private void HandlePresetChanged(int index)
        {
            // The state controller has already swapped CurrentBlueprint; just
            // tear down the old chassis and rebuild from the new one.
            Respawn();
        }

        // Build-mode → garage lifecycle seam. Replaces the prior
        // BuildModeController.Exit's FindAnyObjectByType<GarageController>
        // back-reference.
        private void HandleBuildModeExited()
        {
            if (_buildMode == null) return;
            // TRACE[LOG-166]: build → hangar is the natural commit point.
            // Autosave the working USER blueprint so a forgotten Save (or a
            // later quit) can't drop a tuning pass. No-op for preset clones
            // and unsaved new robots — the Save button forks those.
            GameStateController.Instance?.AutosaveIfDirty();
            if (_buildMode.ExitRequestedRespawn) Respawn();
        }

        /// <summary>Destroy the current chassis (if any) and rebuild from <see cref="GameStateController.CurrentBlueprint"/>.</summary>
        public void Respawn()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null || state.CurrentBlueprint == null || state.Library == null) return;
            if (Chassis != null) Destroy(Chassis);
            Chassis = SpawnChassis(state);
            ClampToHoverHeight(Chassis);
            ParkChassis(Chassis);
            DisableCombat(Chassis);
            BindFollowCamera(Chassis);
            EnsureBuildModeWired();
        }

        private void Start()
        {
            // Apply the "bubble shield in space" look from code so it can't be
            // lost to a scene-file revert (the .unity kept reverting to the
            // plain walled bay). Idempotent + runs on every garage load.
            ApplyGarageDecor();

            // TRACE[LOG-148]: garage theme — public-domain MIDI through the
            // GM soundfont. Silent no-op until the bank finishes streaming,
            // and it stops itself when this scene unloads.
            Robogame.Core.GarageMusic.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // F7 auditions the other candidates in StreamingAssets/Midi/.
            if (GetComponent<GarageMusicDevCycle>() == null)
                gameObject.AddComponent<GarageMusicDevCycle>();
#endif

            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                Debug.LogError(
                    "[Robogame] GarageController: no GameStateController found. " +
                    "You probably pressed Play from Garage.unity directly. " +
                    "Open Assets/_Project/Scenes/Bootstrap.unity and press Play " +
                    "from there (the bootstrap scene owns the persistent state).",
                    this);
                return;
            }

            if (state.CurrentBlueprint == null)
            {
                Debug.LogError(
                    "[Robogame] GarageController: GameStateController has no CurrentBlueprint. " +
                    "Run Robogame > Build Everything (Ctrl+Shift+B) to create the " +
                    "default blueprints and wire them onto the Bootstrap scene.",
                    this);
                return;
            }
            if (state.Library == null)
            {
                Debug.LogError(
                    "[Robogame] GarageController: GameStateController has no BlockDefinitionLibrary. " +
                    "Run Robogame > Build Everything (Ctrl+Shift+B).",
                    this);
                return;
            }

            Chassis = SpawnChassis(state);
            ClampToHoverHeight(Chassis);
            ParkChassis(Chassis);
            DisableCombat(Chassis);
            BindFollowCamera(Chassis);
            EnsureBuildModeWired();
            // Session 121 (reverses the session-120 call): a FRESH garage
            // entry (boot / main menu) starts in drive mode — follow camera,
            // build UI hidden — and the player opts into build mode via the
            // HUD toggle / hotkey. RETURNING from a match instead opens
            // straight into build mode, since the player came back to tweak
            // their bot (session 125). The origin flag is set by
            // GameStateController.EnterGarage before the scene load.
            if (state.ReturningFromArena && _buildMode != null)
                _buildMode.Enter();
        }

        // -----------------------------------------------------------------
        // "Bubble shield in space" decor (code-applied; see Start)
        // -----------------------------------------------------------------

        /// <summary>
        /// Turn the plain walled garage bay into the floating shield-bubble
        /// platform — applied at runtime so it survives scene-file reverts.
        /// The build lives in <see cref="GarageDecor"/>; the animated bits
        /// (star drift, beacon blink, asteroid tumble) in
        /// <see cref="GarageAmbience"/>. Session 121 liveliness pass.
        /// </summary>
        private void ApplyGarageDecor() => GarageDecor.Apply();

        // -----------------------------------------------------------------
        // Build mode wiring
        // -----------------------------------------------------------------

        /// <summary>
        /// Pin the chassis Rigidbody so it's a static display while in the
        /// garage. Subsystem forces (ThrusterBlock idle thrust, gravity,
        /// etc.) are silently ignored on a kinematic body, so we don't need
        /// to disable any subsystems individually. Launch destroys this
        /// chassis and the Arena builds a fresh, unfrozen one.
        /// </summary>
        private static void ParkChassis(GameObject chassis)
        {
            if (chassis == null) return;
            Rigidbody rb = chassis.GetComponent<Rigidbody>();
            if (rb == null) return;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        /// <summary>
        /// Switch off every <see cref="ProjectileGun"/> on the chassis so
        /// the player can't fire while parked in the garage. The Arena
        /// builds a fresh chassis with guns enabled by default, so we
        /// don't need a re-enable counterpart — only the garage cares.
        /// </summary>
        /// <remarks>
        /// We disable the component (not the GameObject) so the turret
        /// rig and tracer pool stay intact for the inspector and any
        /// build-mode previews. Re-enabling later is just
        /// <c>gun.enabled = true</c> if a future feature wants it.
        /// </remarks>
        // Silence everything that reads fire/module input while parked in the
        // garage (playtest, session 120 — cannons, mortars, bombs, the grapple,
        // and modules were all still live). The SMG fires via ProjectileGun;
        // the cannon/mortar/bomb/grapple fire from their own behaviours; modules
        // activate from the single ModuleSystem on the robot. Launch destroys
        // this chassis and the Arena builds a fresh, unfrozen one, so these are
        // never re-enabled here.
        private static void DisableCombat(GameObject chassis)
        {
            if (chassis == null) return;
            DisableAll<ProjectileGun>(chassis);
            DisableAll<CannonBlock>(chassis);
            DisableAll<MortarBlock>(chassis);
            DisableAll<BombBayBlock>(chassis);
            DisableAll<GrappleMagnetBlock>(chassis);
            DisableAll<ModuleSystem>(chassis);
        }

        private static void DisableAll<T>(GameObject root) where T : MonoBehaviour
        {
            T[] comps = root.GetComponentsInChildren<T>(includeInactive: true);
            foreach (T c in comps)
                if (c != null) c.enabled = false;
        }

        /// <summary>
        /// Lift the chassis so its lowest block sits at <see cref="_hoverHeightCells"/>
        /// cell-units above the garage floor (Y=0). This is the "bot floats
        /// in the air for staging" beat — it also doubles as a ground-clamp
        /// guarantee: regardless of how a blueprint extends downward, the
        /// chassis pivot is offset such that no block clips into the floor.
        /// </summary>
        /// <remarks>
        /// Called once per spawn after <see cref="ChassisFactory.Build"/>
        /// has populated the grid. It does not run continuously: in build
        /// mode the chassis is kinematic and frozen, so the only thing that
        /// could move it is another respawn — which goes through this path.
        /// </remarks>
        private void ClampToHoverHeight(GameObject chassis)
        {
            if (chassis == null) return;
            BlockGrid grid = chassis.GetComponent<BlockGrid>();
            if (grid == null || grid.Count == 0) return;

            int minY = int.MaxValue;
            foreach (var kvp in grid.Blocks)
            {
                if (kvp.Key.y < minY) minY = kvp.Key.y;
            }
            if (minY == int.MaxValue) return;

            float cell = grid.CellSize;
            // World Y of the lowest block currently = chassis.position.y + minY * cell.
            // We want that to equal _hoverHeightCells * cell.
            float desiredChassisY = _hoverHeightCells * cell - minY * cell;
            Vector3 p = chassis.transform.position;
            chassis.transform.position = new Vector3(p.x, desiredChassisY, p.z);
        }

        /// <summary>Lazily creates the build-mode trio (controller + editor + hotbar) and rebinds them to the live chassis.</summary>
        private void EnsureBuildModeWired()
        {
            // BuildSession is plain C# — survive respawns by recreating
            // only when null. Its bindings re-point at the new grid /
            // blueprint each time the chassis spawns.
            if (_buildSession == null) _buildSession = new BuildSession();
            BlockGrid grid = Chassis != null ? Chassis.GetComponent<BlockGrid>() : null;
            GameStateController state = GameStateController.Instance;
            ChassisBlueprint blueprint = state != null ? state.CurrentBlueprint : null;
            BlockDefinitionLibrary library = state != null ? state.Library : null;
            _buildSession.Bind(grid, blueprint, library);

            if (_buildMode == null)
            {
                _buildMode = gameObject.AddComponent<BuildModeController>();
                // Build-mode no longer reaches into the garage via
                // FindAnyObjectByType — the lifecycle event is the seam.
                // Subscribe once; the build mode controller owns the
                // session lifecycle and we re-spawn from the saved
                // blueprint when it tells us it's done.
                _buildMode.Exited += HandleBuildModeExited;
            }
            _buildMode.SetChassis(Chassis != null ? Chassis.transform : null);

            if (_editor == null) _editor = gameObject.AddComponent<BlockEditor>();
            _editor.BuildMode = _buildMode;
            _editor.Session = _buildSession;

            if (_hotbar == null) _hotbar = gameObject.AddComponent<BuildHotbar>();
            _hotbar.BuildMode = _buildMode;
            _hotbar.Editor = _editor;
            _editor.Hotbar = _hotbar;

            // Variant config panel — shows foil/rope dimension sliders when
            // the matching block is selected in the hotbar. Lives on the
            // same GameObject so its setup mirrors the hotbar's lifecycle.
            if (_variantPanel == null) _variantPanel = gameObject.AddComponent<VariantConfigPanel>();
            _variantPanel.BuildMode = _buildMode;
            _variantPanel.Hotbar = _hotbar;
            _variantPanel.Session = _buildSession;
            _editor.VariantPanel = _variantPanel;

            // Laboratory — author explosive concoctions (ADR-0004). Load the
            // player's saved recipes into the runtime registry so the CPU bar
            // and the variant-panel dropdown see them; refresh live on save.
            Robogame.Block.ConcoctionRegistry.ReloadFromLibrary();
            if (!_concoctionLibrarySubscribed)
            {
                Robogame.Block.ConcoctionLibrary.Changed += HandleConcoctionLibraryChanged;
                _concoctionLibrarySubscribed = true;
            }
            if (_lab == null) _lab = gameObject.AddComponent<LabController>();
            _lab.BuildMode = _buildMode;

            // Mirror-mode toggle — hotkey + HUD banner. The editor
            // consults it for symmetric place / remove and ghost preview.
            if (_mirrorMode == null) _mirrorMode = gameObject.AddComponent<BuildMirrorMode>();
            _mirrorMode.BuildMode = _buildMode;
            _mirrorMode.Session = _buildSession;
            _editor.MirrorMode = _mirrorMode;

            // Tune-part toggle (button + T hotkey). When on, a left-click
            // binds the pointed block to the variant panel for in-place
            // retuning instead of placing, and the free-cam lends the HUD
            // the cursor. Replaces the session-125 middle-click
            // instance-edit.
            if (_editMode == null) _editMode = gameObject.AddComponent<BuildEditMode>();
            _editMode.BuildMode = _buildMode;
            _editor.EditMode = _editMode;

            // Move-part toggle (button + V hotkey, session 169). Pick a
            // placed block up with all its per-instance settings and
            // re-place it atomically. Exclusive with tune mode.
            if (_moveMode == null) _moveMode = gameObject.AddComponent<BuildMoveMode>();
            _moveMode.BuildMode = _buildMode;
            _editor.MoveMode = _moveMode;

            // Ghost preview + placement-error feedback are split out of
            // the editor so the editor's MonoBehaviour stays a thin
            // input/state driver. Both lifecycle-tied to the build-mode
            // GameObject.
            if (_ghostRenderer == null) _ghostRenderer = gameObject.AddComponent<BlockGhostRenderer>();
            _editor.GhostRenderer = _ghostRenderer;
            if (_feedbackHud == null) _feedbackHud = gameObject.AddComponent<PlacementFeedbackHud>();
            _editor.FeedbackHud = _feedbackHud;

            // CoM / CoL / CoT garage overlay (toggle G). Read-only; resolves the
            // live chassis through GarageController.Chassis each frame.
            if (_centerOverlay == null) _centerOverlay = gameObject.AddComponent<CenterOverlay>();
            _centerOverlay.Garage = this;
            _centerOverlay.BuildMode = _buildMode;

            // Screen-edge frame + "BUILD MODE" tag — the strong mode
            // signifier separating build from the hangar state (138 UX pass).
            if (_buildFrame == null) _buildFrame = gameObject.AddComponent<BuildModeFrame>();
            _buildFrame.BuildMode = _buildMode;
            // Module abilities are now per-block (the block type IS the
            // ability), so there's no chassis-level module picker — the
            // per-module power slider lives in the VariantConfigPanel.
        }

        /// <summary>Toggle build mode. Forwarded by HUD button + hotkey.</summary>
        public void ToggleBuildMode()
        {
            EnsureBuildModeWired();
            if (_buildMode != null) _buildMode.Toggle();
        }

        /// <summary>Open / close the Laboratory screen. Forwarded by the garage HUD's LAB button.</summary>
        public void ToggleLab()
        {
            EnsureBuildModeWired();
            if (_lab != null) _lab.Toggle();
        }

        // Refresh the runtime registry whenever the player saves / deletes a
        // concoction so the CPU bar + variant dropdown stay in sync mid-session.
        private void HandleConcoctionLibraryChanged()
            => Robogame.Block.ConcoctionRegistry.ReloadFromLibrary();

        private GameObject SpawnChassis(GameStateController state)
        {
            // Tear down any pre-existing chassis with the same name (e.g.
            // scaffolded into the saved scene).
            GameObject existing = GameObject.Find(_chassisName);
            if (existing != null) Destroy(existing);

            var go = new GameObject(_chassisName);
            go.transform.SetPositionAndRotation(_spawnPosition, Quaternion.Euler(_spawnEuler));

            ChassisFactory.Build(go, state.CurrentBlueprint, state.Library, state.InputActions);
            return go;
        }

        private static void BindFollowCamera(GameObject chassis)
        {
            if (chassis == null) return;
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            FollowCamera follow = mainCam.GetComponent<FollowCamera>();
            if (follow == null) follow = mainCam.gameObject.AddComponent<FollowCamera>();
            follow.Target = chassis.transform;
            // The hangar is a loadout screen: free cursor, parallax lean
            // instead of captured mouse-look. Arena scenes bind their own
            // FollowCamera and never set this.
            follow.HangarMode = true;

            if (mainCam.GetComponent<AimReticle>() == null)
                mainCam.gameObject.AddComponent<AimReticle>();
        }

        /// <summary>Transition to the arena scene with the current blueprint.</summary>
        public void Launch()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.EnterArena();
        }
    }
}
