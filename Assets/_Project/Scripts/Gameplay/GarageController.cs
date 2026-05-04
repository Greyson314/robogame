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
        [SerializeField] private Vector3 _spawnPosition = new Vector3(0f, 1.5f, 0f);

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

        /// <summary>The build-mode controller hosted on this object. Lazily created.</summary>
        public BuildModeController BuildMode => _buildMode;

        private void OnEnable()
        {
            GameStateController state = GameStateController.Instance;
            if (state != null) state.PresetChanged += HandlePresetChanged;
        }

        private void OnDisable()
        {
            GameStateController state = GameStateController.Instance;
            if (state != null) state.PresetChanged -= HandlePresetChanged;
        }

        private void HandlePresetChanged(int index)
        {
            // The state controller has already swapped CurrentBlueprint; just
            // tear down the old chassis and rebuild from the new one.
            Respawn();
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
            DisableWeapons(Chassis);
            BindFollowCamera(Chassis);
            EnsureBuildModeWired();
        }

        private void Start()
        {
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
                    "Run Robogame > Scaffold > Gameplay > Build All Pass A to create the " +
                    "default blueprints and wire them onto the Bootstrap scene.",
                    this);
                return;
            }
            if (state.Library == null)
            {
                Debug.LogError(
                    "[Robogame] GarageController: GameStateController has no BlockDefinitionLibrary. " +
                    "Run Robogame > Scaffold > Gameplay > Build All Pass A.",
                    this);
                return;
            }

            Chassis = SpawnChassis(state);
            ClampToHoverHeight(Chassis);
            ParkChassis(Chassis);
            DisableWeapons(Chassis);
            BindFollowCamera(Chassis);
            EnsureBuildModeWired();
        }

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
        private static void DisableWeapons(GameObject chassis)
        {
            if (chassis == null) return;
            ProjectileGun[] guns = chassis.GetComponentsInChildren<ProjectileGun>(includeInactive: true);
            foreach (ProjectileGun g in guns)
            {
                if (g != null) g.enabled = false;
            }
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
            if (_buildMode == null) _buildMode = gameObject.AddComponent<BuildModeController>();
            _buildMode.SetChassis(Chassis != null ? Chassis.transform : null);

            if (_editor == null) _editor = gameObject.AddComponent<BlockEditor>();
            _editor.BuildMode = _buildMode;

            if (_hotbar == null) _hotbar = gameObject.AddComponent<BuildHotbar>();
            _hotbar.BuildMode = _buildMode;
            _hotbar.Editor = _editor;
            _editor.Hotbar = _hotbar;
        }

        /// <summary>Toggle build mode. Forwarded by HUD button + hotkey.</summary>
        public void ToggleBuildMode()
        {
            EnsureBuildModeWired();
            if (_buildMode != null) _buildMode.Toggle();
        }

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
