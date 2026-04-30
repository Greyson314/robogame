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
        [Tooltip("Where the chassis spawns inside the garage.")]
        [SerializeField] private Vector3 _spawnPosition = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Initial chassis facing.")]
        [SerializeField] private Vector3 _spawnEuler = Vector3.zero;

        [Tooltip("Name of the spawned chassis GameObject. Used by " +
                 "Robot.RebuildByName / DevHud.")]
        [SerializeField] private string _chassisName = "Robot";

        public GameObject Chassis { get; private set; }

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
            BindFollowCamera(Chassis);
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
