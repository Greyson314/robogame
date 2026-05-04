using Robogame.Block;
using Robogame.Core;
using Robogame.Movement;
using Robogame.Player;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Header("Stress test — rotor tower")]
        [Tooltip("Optional spinning-rotor stress-test target. Spawned when " +
                 "the Stress.RotorTower tweakable crosses 0.5 (drag the slider " +
                 "in the settings panel or dev HUD). Use to profile rotor + " +
                 "rope cost under load — see docs/PHYSICS_PLAN.md.")]
        [SerializeField] private ChassisBlueprint _stressTowerBlueprint;
        [SerializeField] private Vector3 _stressTowerPosition = new Vector3(40f, 0.5f, 18f);
        [SerializeField] private string _stressTowerName = "StressRotorTower";

        [Header("Dumbbell test dummy")]
        [Tooltip("Hookable / smackable dumbbell-shaped target. Spawned " +
                 "off to the player's left so the existing combat dummy " +
                 "stays dead-ahead. Built from Blueprint_DumbbellDummy.")]
        // FormerlySerializedAs preserves the scene wire-up across the
        // session-22 rename (was _barbellBlueprint / _barbellPosition /
        // _barbellName). Without it, opening Arena.unity would reset the
        // value to null + default and require a Build Everything pass.
        [FormerlySerializedAs("_barbellBlueprint")]
        [SerializeField] private ChassisBlueprint _dumbbellBlueprint;
        [FormerlySerializedAs("_barbellPosition")]
        [SerializeField] private Vector3 _dumbbellPosition = new Vector3(-25f, 1.5f, 18f);
        [FormerlySerializedAs("_barbellName")]
        [SerializeField] private string _dumbbellName = "DumbbellDummy";

        private GameObject _stressTowerGo;

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
            SpawnDumbbell(state);
            BindFollowCamera(Chassis);

            // Stress tower: optional. Read the tweakable on entry and
            // (de)spawn live as the slider moves. Subscribing here means
            // dragging Stress.RotorTower in the settings panel pops the
            // tower in/out without re-entering the arena.
            ApplyStressTowerState(state);
            Tweakables.Changed += OnTweakablesChanged;
        }

        private void OnDestroy()
        {
            Tweakables.Changed -= OnTweakablesChanged;
        }

        private void OnTweakablesChanged()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            ApplyStressTowerState(state);
            // RPM changes too — re-push override values onto every rotor
            // in the stress tower so the slider drives them live without
            // tearing the chassis down.
            UpdateStressTowerRpm();
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

        private void SpawnDumbbell(GameStateController state)
        {
            if (_dumbbellBlueprint == null) return; // optional — missing wire-up is fine
            if (state.Library == null) return;

            GameObject existing = GameObject.Find(_dumbbellName);
            if (existing != null) Destroy(existing);

            var go = new GameObject(_dumbbellName);
            go.transform.position = _dumbbellPosition;
            Robogame.Robots.Robot dumbbell = ChassisFactory.BuildTarget(go, _dumbbellBlueprint, state.Library);
            int blockCount = dumbbell != null ? dumbbell.BlockCount : 0;
            Debug.Log($"[Robogame] Dumbbell dummy spawned at {_dumbbellPosition} with {blockCount} blocks.", go);
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

        // -----------------------------------------------------------------
        // Stress tower (optional spinning-rotor stress-test target)
        // -----------------------------------------------------------------

        /// <summary>
        /// Reads <see cref="Tweakables.StressRotorTower"/> and brings the
        /// stress-test rotor tower into existence (or tears it down) to
        /// match. Safe to call any time after <see cref="Start"/>.
        /// </summary>
        public void ApplyStressTowerState(GameStateController state)
        {
            bool wantTower = Tweakables.Get(Tweakables.StressRotorTower) >= 0.5f;
            if (wantTower) SpawnStressTower(state);
            else           DespawnStressTower();
        }

        /// <summary>Force-respawn the stress tower (tears down any existing instance).</summary>
        public void RespawnStressTower()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            DespawnStressTower();
            // Flip the tweakable on so the user's intent is reflected and
            // future Tweakables.Changed callbacks don't immediately undo us.
            Tweakables.Set(Tweakables.StressRotorTower, 1f);
            SpawnStressTower(state);
        }

        public void DespawnStressTower()
        {
            if (_stressTowerGo != null)
            {
                Destroy(_stressTowerGo);
                _stressTowerGo = null;
            }
        }

        private void SpawnStressTower(GameStateController state)
        {
            if (_stressTowerBlueprint == null)
            {
                Debug.LogWarning(
                    "[Robogame] ArenaController: stress tower requested but " +
                    "_stressTowerBlueprint is unassigned. Re-run Robogame › Scaffold " +
                    "› Gameplay › Build All Pass A.", this);
                return;
            }
            if (state.Library == null) return;
            if (_stressTowerGo != null) return; // already spawned

            // Reuse the dummy name path — GameObject.Find picks up the
            // first match, but we keep our own ref in _stressTowerGo so
            // Find isn't on the despawn hot path.
            GameObject existing = GameObject.Find(_stressTowerName);
            if (existing != null) Destroy(existing);

            _stressTowerGo = new GameObject(_stressTowerName);
            _stressTowerGo.transform.position = _stressTowerPosition;
            Robogame.Robots.Robot tower = ChassisFactory.BuildTarget(_stressTowerGo, _stressTowerBlueprint, state.Library);
            int blockCount = tower != null ? tower.BlockCount : 0;
            Debug.Log($"[Robogame] Stress rotor tower spawned at {_stressTowerPosition} with {blockCount} blocks. " +
                      "Drag Stress.TowerRpm in settings to spin it up.", _stressTowerGo);
            UpdateStressTowerRpm();
        }

        /// <summary>
        /// Push the current <see cref="Tweakables.StressRotorTowerRpm"/>
        /// onto every <see cref="RotorBlock"/> under the spawned tower,
        /// using the per-instance <see cref="RotorBlock.RpmOverride"/>
        /// hatch so the tower spins independently of the player's chassis
        /// rotors.
        /// </summary>
        private void UpdateStressTowerRpm()
        {
            if (_stressTowerGo == null) return;
            float rpm = Tweakables.Get(Tweakables.StressRotorTowerRpm);
            RotorBlock[] rotors = _stressTowerGo.GetComponentsInChildren<RotorBlock>(includeInactive: true);
            for (int i = 0; i < rotors.Length; i++) rotors[i].RpmOverride = rpm;
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
