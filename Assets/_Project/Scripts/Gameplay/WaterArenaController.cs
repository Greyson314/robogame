using Robogame.Block;
using Robogame.Player;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Lives in the WaterArena scene. Spawns the player's chassis from
    /// <see cref="GameStateController.CurrentBlueprint"/>, attaches a
    /// <see cref="BuoyancyController"/> so it interacts with the
    /// <see cref="WaterVolume"/> in the scene, binds the
    /// <see cref="FollowCamera"/>, and exposes <see cref="Return"/> so the
    /// scene HUD can drop back to the garage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately a separate component from <see cref="ArenaController"/>:
    /// the combat arena spawns a stationary dummy and assumes a flat
    /// terrain with obstacles, while the water arena is a quiet sandbox
    /// with no targets. Splitting them keeps the responsibilities small
    /// and means tweaking water-spawn behaviour can't regress combat.
    /// </para>
    /// <para>
    /// Spawn position is set well above the water surface so a chassis
    /// drops in (visible splash window) rather than spawning underwater.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WaterArenaController : MonoBehaviour
    {
        [Header("Player spawn")]
        [Tooltip("Ground-chassis spawn position. Y is above the water " +
                 "surface so the chassis drops in.")]
        [SerializeField] private Vector3 _groundSpawnPosition = new Vector3(0f, 6f, 0f);

        [Tooltip("Plane-chassis spawn position (well above the water).")]
        [SerializeField] private Vector3 _planeSpawnPosition = new Vector3(0f, 24f, -14f);

        [Tooltip("Initial forward speed (m/s) for plane-kind blueprints.")]
        [SerializeField] private float _planeSpawnForwardSpeed = 14f;

        [Tooltip("Name of the spawned chassis GameObject.")]
        [SerializeField] private string _chassisName = "Robot";

        public GameObject Chassis { get; private set; }

        private void Start()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                Debug.LogError(
                    "[Robogame] WaterArenaController: no GameStateController found. " +
                    "You probably pressed Play from WaterArena.unity directly. " +
                    "Open Assets/_Project/Scenes/Bootstrap.unity and press Play " +
                    "from there (the bootstrap scene owns the persistent state).",
                    this);
                return;
            }

            if (state.CurrentBlueprint == null || state.Library == null)
            {
                Debug.LogError(
                    "[Robogame] WaterArenaController: GameStateController is missing its " +
                    "blueprint or block-definition library. Run Robogame > Scaffold > " +
                    "Gameplay > Build All Pass A.",
                    this);
                return;
            }

            Chassis = SpawnPlayerChassis(state);
            BindFollowCamera(Chassis);
            BindBuoyancy(Chassis);
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

        private static void BindBuoyancy(GameObject chassis)
        {
            if (chassis == null) return;
            // BuoyancyController has [RequireComponent] on Rigidbody +
            // BlockGrid; ChassisFactory.Build always provides those.
            if (chassis.GetComponent<BuoyancyController>() == null)
                chassis.AddComponent<BuoyancyController>();
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
