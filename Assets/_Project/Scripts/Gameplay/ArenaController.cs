using Robogame.Block;
using Robogame.Player;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Lives in the Arena scene. On <see cref="Start"/> spawns the player's
    /// chassis from <see cref="GameStateController.CurrentBlueprint"/>,
    /// optionally spawns a stationary combat dummy, binds the
    /// <see cref="FollowCamera"/>, and exposes <see cref="Return"/> so the
    /// scene HUD can drop back to the garage.
    /// </summary>
    /// <remarks>
    /// Pass A scope: deterministic spawn + return. AI bots, round logic and
    /// scoring live in a later pass.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ArenaController : MonoBehaviour
    {
        [Header("Player spawn")]
        [Tooltip("Ground-chassis spawn position.")]
        [SerializeField] private Vector3 _groundSpawnPosition = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Plane-chassis spawn position (raised + behind centre).")]
        [SerializeField] private Vector3 _planeSpawnPosition = new Vector3(0f, 18f, -14f);

        [Tooltip("Initial forward speed (m/s) for plane-kind blueprints.")]
        [SerializeField] private float _planeSpawnForwardSpeed = 14f;

        [Tooltip("Name of the spawned chassis GameObject.")]
        [SerializeField] private string _chassisName = "Robot";

        [Header("Combat dummy")]
        [Tooltip("If assigned, a stationary target chassis is built from this " +
                 "blueprint at the position below.")]
        [SerializeField] private ChassisBlueprint _dummyBlueprint;

        // Centred at x=0 so the symmetric blueprint sits dead ahead of the
        // player spawn (0, 1.5, 0). y=0.5 puts the bottom row of cubes
        // resting on the ground (block centres are at half-cell offsets).
        // z=18 keeps it close enough to fire on immediately, far enough
        // that the player can drive around it.
        [SerializeField] private Vector3 _dummyPosition = new Vector3(0f, 0.5f, 18f);
        [SerializeField] private string _dummyName = "CombatDummy";

        public GameObject Chassis { get; private set; }

        private void Start()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                Debug.LogError(
                    "[Robogame] ArenaController: no GameStateController found. " +
                    "You probably pressed Play from Arena.unity directly. " +
                    "Open Assets/_Project/Scenes/Bootstrap.unity and press Play " +
                    "from there (the bootstrap scene owns the persistent state).",
                    this);
                return;
            }

            if (state.CurrentBlueprint == null || state.Library == null)
            {
                Debug.LogError(
                    "[Robogame] ArenaController: GameStateController is missing its blueprint " +
                    "or block-definition library. Run Robogame > Scaffold > Gameplay > Build All Pass A.",
                    this);
                return;
            }

            Chassis = SpawnPlayerChassis(state);
            SpawnDummy(state);
            BindFollowCamera(Chassis);
        }

        private GameObject SpawnPlayerChassis(GameStateController state)
        {
            GameObject existing = GameObject.Find(_chassisName);
            if (existing != null) Destroy(existing);

            ChassisBlueprint bp = state.CurrentBlueprint;
            bool isPlane = bp != null && bp.Kind == ChassisKind.Plane;
            Vector3 pos = isPlane ? _planeSpawnPosition : _groundSpawnPosition;

            var go = new GameObject(_chassisName);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);

            ChassisFactory.Build(go, bp, state.Library, state.InputActions);

            if (isPlane && _planeSpawnForwardSpeed > 0f)
            {
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.linearVelocity = go.transform.forward * _planeSpawnForwardSpeed;
            }

            return go;
        }

        private void SpawnDummy(GameStateController state)
        {
            if (_dummyBlueprint == null)
            {
                Debug.LogWarning(
                    "[Robogame] ArenaController: no _dummyBlueprint assigned on this scene's " +
                    "ArenaController. Re-run Robogame > Scaffold > Gameplay > Build All Pass A " +
                    "(it auto-wires Blueprint_CombatDummy.asset).",
                    this);
                return;
            }
            if (state.Library == null) return;

            GameObject existing = GameObject.Find(_dummyName);
            if (existing != null) Destroy(existing);

            var go = new GameObject(_dummyName);
            go.transform.position = _dummyPosition;
            Robogame.Robots.Robot dummy = ChassisFactory.BuildTarget(go, _dummyBlueprint, state.Library);
            int blockCount = dummy != null ? dummy.BlockCount : 0;
            Debug.Log($"[Robogame] Combat dummy spawned at {_dummyPosition} with {blockCount} blocks.", go);
        }

        /// <summary>
        /// Tear down the current combat dummy (if any) and rebuild it from the
        /// configured blueprint at <see cref="_dummyPosition"/>. Safe to call
        /// any time after <see cref="Start"/>; no-op if the GameStateController
        /// is missing.
        /// </summary>
        /// <remarks>
        /// Public so the settings HUD can offer a one-click respawn button
        /// without needing direct access to the spawn pipeline. Also wired
        /// to the menu shortcut <c>Robogame/Test/Respawn Dummy</c> in
        /// editor utilities.
        /// </remarks>
        public void RespawnDummy()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                Debug.LogWarning("[Robogame] ArenaController.RespawnDummy: no GameStateController.", this);
                return;
            }
            SpawnDummy(state);
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

        /// <summary>Transition back to the garage scene.</summary>
        public void Return()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.EnterGarage();
        }
    }
}
