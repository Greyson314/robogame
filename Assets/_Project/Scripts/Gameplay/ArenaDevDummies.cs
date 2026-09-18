using Robogame.Block;
using Robogame.Core;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Owns the Stress.* dev-dummy lifecycle that used to live on
    /// <see cref="ArenaController"/>: the spinning-rotor stress tower, the
    /// patrolling tank dummy, and the patrolling air dummy. All three exist
    /// only behind their Tweakables (default off) — dev-facing content, not
    /// anything a player meets (F-047, CHG-024). The friendly tank, the
    /// combat dummy and the arch dummy stay on ArenaController: they're
    /// spawned by default and tied to match orchestration.
    /// </summary>
    /// <remarks>
    /// Resolves its <see cref="ArenaController"/> from the same GameObject
    /// on <see cref="Awake"/> (before any Start on this GameObject runs, so
    /// ArenaController.Start's call into <see cref="ApplyAll"/> always sees
    /// a resolved reference). A missing ArenaController logs once and every
    /// method that needs it becomes a no-op.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ArenaDevDummies : MonoBehaviour
    {
        [Header("Stress test — rotor tower")]
        [Tooltip("Optional spinning-rotor stress-test target. Spawned when " +
                 "the Stress.RotorTower tweakable crosses 0.5 (drag the slider " +
                 "in the settings panel or dev HUD). Use to profile rotor + " +
                 "rope cost under load — see docs/subsystems/physics.md.")]
        [SerializeField] private ChassisBlueprint _stressTowerBlueprint;
        [SerializeField] private Vector3 _stressTowerPosition = new Vector3(55f, 0.5f, 30f);
        [SerializeField] private string _stressTowerName = "StressRotorTower";

        [Header("Tank dummy bot")]
        [Tooltip("Optional patrolling tank target. Spawned when Stress.TankDummy " +
                 "crosses 0.5. If left null, the controller uses the first " +
                 "Ground-kind preset from GameStateController.PresetBlueprints " +
                 "(typically 'Tank').")]
        [SerializeField] private ChassisBlueprint _tankDummyBlueprint;
        [SerializeField] private Vector3 _tankDummySpawn = new Vector3(28f, 2.0f, 0f);
        [SerializeField] private string _tankDummyName = "TankDummy";
        [Tooltip("Centre of the tank's patrol circle in world space.")]
        [SerializeField] private Vector3 _tankDummyPatrolCentre = Vector3.zero;
        [Tooltip("Radius of the patrol circle (m).")]
        [SerializeField, Min(8f)] private float _tankDummyPatrolRadius = 30f;

        [Header("Air dummy bot")]
        [Tooltip("Optional patrolling air bot. Spawned when Stress.AirDummy " +
                 "crosses 0.5. If left null, the controller uses the first " +
                 "Plane-kind preset from GameStateController.PresetBlueprints " +
                 "(typically the helicopter or plane).")]
        [SerializeField] private ChassisBlueprint _airDummyBlueprint;
        [SerializeField] private Vector3 _airDummySpawn = new Vector3(0f, 50f, 60f);
        [SerializeField] private string _airDummyName = "AirDummy";
        [Tooltip("Centre of the air bot's cruise circle in world space.")]
        [SerializeField] private Vector3 _airDummyCruiseCentre = Vector3.zero;
        [Tooltip("Radius of the air bot's cruise circle (m).")]
        [SerializeField, Min(20f)] private float _airDummyCruiseRadius = 80f;
        [Tooltip("Cruise altitude (m above world Y=0).")]
        [SerializeField, Min(10f)] private float _airDummyCruiseAltitude = 40f;

        private GameObject _stressTowerGo;
        private GameObject _tankDummyGo;
        private GroundBotInputSource _tankDummyAi;
        private GameObject _airDummyGo;
        private AirBotInputSource _airDummyAi;

        // TRACE[LOG-191]: ArenaController takes over registration + the
        // player-chassis fire target through this reference rather than
        // duplicating either lookup here (CHG-024 boundary: RegisterChassis
        // and PlayerChassisTransform stay owned by ArenaController).
        private ArenaController _arena;

        private void Awake()
        {
            _arena = GetComponent<ArenaController>();
            if (_arena == null)
            {
                Debug.LogError(
                    "[Robogame] ArenaDevDummies: no ArenaController on this GameObject. " +
                    "The stress tower, tank dummy and air dummy dev toggles will do nothing.",
                    this);
            }
        }

        private void Start()
        {
            Tweakables.Changed += OnTweakablesChanged;
        }

        private void OnDestroy()
        {
            Tweakables.Changed -= OnTweakablesChanged;
        }

        /// <summary>
        /// Applies every dev-dummy Tweakable's current spawned/despawned
        /// state. Called once from <see cref="ArenaController.Start"/> —
        /// the Tweakables.Changed subscription above only fires on later
        /// changes, not the scene's initial state.
        /// </summary>
        internal void ApplyAll(GameStateController state)
        {
            ApplyStressTowerState(state);
            ApplyTankDummyState(state);
            ApplyAirDummyState(state);
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
            ApplyTankDummyState(state);
            ApplyTankDummyFire();
            ApplyAirDummyState(state);
            ApplyAirDummyFire();
        }

        // -----------------------------------------------------------------
        // Stress tower (optional spinning-rotor stress-test target)
        // -----------------------------------------------------------------

        /// <summary>
        /// Reads <see cref="Tweakables.StressRotorTower"/> and brings the
        /// stress-test rotor tower into existence (or tears it down) to
        /// match. Safe to call any time after <see cref="Awake"/>.
        /// </summary>
        public void ApplyStressTowerState(GameStateController state)
        {
            bool wantTower = Tweakables.GetBool(Tweakables.StressRotorTower);
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
            Tweakables.SetBool(Tweakables.StressRotorTower, true);
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
                    "[Robogame] ArenaDevDummies: stress tower requested but " +
                    "_stressTowerBlueprint is unassigned. Re-run Robogame › " +
                    "Build Everything (Ctrl+Shift+B).", this);
                return;
            }
            if (state.Library == null) return;
            if (_stressTowerGo != null) return; // already spawned

            _stressTowerGo = SpawnDevDummy<Component>(
                _stressTowerName, _stressTowerPosition, _stressTowerBlueprint, state.Library,
                configureAi: null, out _, asTarget: true);
            Robogame.Robots.Robot tower = _stressTowerGo.GetComponent<Robogame.Robots.Robot>();
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

        // -----------------------------------------------------------------
        // Dev-dummy spawn/despawn shared shape. ChassisAssembler.Assemble
        // deactivates the root internally for the whole build regardless
        // of caller state and restores it after (see its "OnEnable
        // timing" remarks), so toggling active here just brackets AI
        // attachment ahead of that build — safe for both the Build (bot)
        // and BuildTarget (passive target, no AI) paths.
        // -----------------------------------------------------------------

        private GameObject SpawnDevDummy<TAi>(
            string name,
            Vector3 spawnPosition,
            ChassisBlueprint blueprint,
            BlockDefinitionLibrary library,
            System.Action<TAi> configureAi,
            out TAi ai,
            bool asTarget = false)
            where TAi : Component
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null) Destroy(existing);

            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            go.SetActive(false);
            ai = configureAi != null ? go.AddComponent<TAi>() : null;
            configureAi?.Invoke(ai);
            if (asTarget)
                ChassisFactory.BuildTarget(go, blueprint, library);
            else
                ChassisFactory.Build(
                    go, blueprint, library,
                    inputActions: null, addPlayerInputs: false);
            go.SetActive(true);
            return go;
        }

        private void DespawnDevDummy<TAi>(ref GameObject go, ref TAi ai) where TAi : Component
        {
            if (go != null)
            {
                Destroy(go);
                go = null;
                ai = null;
            }
        }

        // -----------------------------------------------------------------
        // Tank dummy bot (optional patrolling target)
        // -----------------------------------------------------------------

        public void ApplyTankDummyState(GameStateController state)
        {
            bool wantBot = Tweakables.GetBool(Tweakables.TankDummySpawn);
            if (wantBot) SpawnTankDummy(state);
            else         DespawnTankDummy();
        }

        public void DespawnTankDummy() => DespawnDevDummy(ref _tankDummyGo, ref _tankDummyAi);

        private void SpawnTankDummy(GameStateController state)
        {
            if (_tankDummyGo != null) return; // already alive
            if (state.Library == null) return;
            if (_arena == null) return;

            ChassisBlueprint bp = _tankDummyBlueprint != null ? _tankDummyBlueprint : ResolveTankDummyBlueprint(state);
            if (bp == null)
            {
                Debug.LogWarning(
                    "[Robogame] ArenaDevDummies: Tank dummy requested but no " +
                    "ChassisBlueprint resolved (assign _tankDummyBlueprint in the " +
                    "inspector or ensure GameStateController has a Ground preset).",
                    this);
                return;
            }

            // Build via the player path (with addPlayerInputs=false) so the
            // bot gets full GroundDriveSubsystem + WeaponMount + binders.
            // The AI input source is attached before activation so
            // PlayerController.Awake's GetComponent<IInputSource> resolves
            // to it.
            _tankDummyGo = SpawnDevDummy<GroundBotInputSource>(
                _tankDummyName, _tankDummySpawn, bp, state.Library,
                dummyAi =>
                {
                    dummyAi.CircleCentre = _tankDummyPatrolCentre;
                    dummyAi.CircleRadius = _tankDummyPatrolRadius;
                },
                out _tankDummyAi);

            ApplyTankDummyFire();
            // Register the dev-spawned tank dummy with the match too, so
            // a kill against it counts toward the player's score. The
            // dummy still respects the PHYSICS_PLAN § 1.5 rule — it's
            // gated by the existing Tweakable for spawn / fire convenience,
            // but its damage outcomes feed the same per-side score the
            // MatchConfig-driven bots do.
            _arena.RegisterChassis(_tankDummyGo, MatchSide.Enemy, "DUMMY TANK");
            Debug.Log($"[Robogame] Tank dummy spawned at {_tankDummySpawn} " +
                      $"(blueprint='{bp.name}', patrol r={_tankDummyPatrolRadius}m).",
                      _tankDummyGo);
        }

        /// <summary>
        /// Re-push the tank dummy's fire/target state. Internal (not
        /// private) because <see cref="ArenaController.RespawnPlayer"/>
        /// calls it after rebuilding the player chassis, so a live tank
        /// dummy re-binds to the fresh chassis transform instead of the
        /// destroyed one.
        /// </summary>
        internal void ApplyTankDummyFire()
        {
            if (_tankDummyAi == null || _arena == null) return;
            bool fire = Tweakables.GetBool(Tweakables.TankDummyFire);
            // Tweakable acts as the "go aggressive" master switch. With it
            // OFF the bot has no target → stays in Patrol. With it ON the
            // bot binds the player and switches to Pursue/Engage/fire. This
            // keeps the dev-spawn workflow predictable (toggle spawn, watch
            // it patrol, toggle fire when ready) and matches the new
            // round-start gate's "passive until I press FIGHT" feel.
            _tankDummyAi.FireAtTarget = fire;
            _tankDummyAi.Target = fire ? _arena.PlayerChassisTransform : null;
        }

        /// <summary>
        /// Prefer a preset whose name contains "Tank"; fall back to any
        /// Ground-kind preset. Internal static — ArenaController's own
        /// friendly-tank spawn (which stays on ArenaController; F-047's
        /// boundary keeps it there) reuses this exact search instead of
        /// carrying its own copy, the same way it already reuses the
        /// "check my own override field first" shape below.
        /// </summary>
        internal static ChassisBlueprint ResolveTankDummyBlueprint(GameStateController state)
        {
            for (int i = 0; i < state.PresetBlueprints.Count; i++)
            {
                ChassisBlueprint bp = state.PresetBlueprints[i];
                if (bp == null) continue;
                if (bp.name.IndexOf("Tank", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return bp;
            }
            for (int i = 0; i < state.PresetBlueprints.Count; i++)
            {
                ChassisBlueprint bp = state.PresetBlueprints[i];
                if (bp != null && bp.Kind == ChassisKind.Ground) return bp;
            }
            return null;
        }

        // -----------------------------------------------------------------
        // Air dummy bot (mirrors tank dummy)
        // -----------------------------------------------------------------

        public void ApplyAirDummyState(GameStateController state)
        {
            bool wantBot = Tweakables.GetBool(Tweakables.AirDummySpawn);
            if (wantBot) SpawnAirDummy(state);
            else         DespawnAirDummy();
        }

        public void DespawnAirDummy() => DespawnDevDummy(ref _airDummyGo, ref _airDummyAi);

        private void SpawnAirDummy(GameStateController state)
        {
            if (_airDummyGo != null) return; // already alive
            if (state.Library == null) return;
            if (_arena == null) return;

            ChassisBlueprint bp = ResolveAirDummyBlueprint(state);
            if (bp == null)
            {
                Debug.LogWarning(
                    "[Robogame] ArenaDevDummies: Air dummy requested but no " +
                    "ChassisBlueprint resolved (assign _airDummyBlueprint in the " +
                    "inspector or ensure GameStateController has a Plane preset).",
                    this);
                return;
            }

            _airDummyGo = SpawnDevDummy<AirBotInputSource>(
                _airDummyName, _airDummySpawn, bp, state.Library,
                dummyAi =>
                {
                    dummyAi.CircleCentre = _airDummyCruiseCentre;
                    dummyAi.CircleRadius = _airDummyCruiseRadius;
                    dummyAi.TargetAltitude = _airDummyCruiseAltitude;
                },
                out _airDummyAi);

            ApplyAirDummyFire();
            _arena.RegisterChassis(_airDummyGo, MatchSide.Enemy, "DUMMY AIR");
            Debug.Log($"[Robogame] Air dummy spawned at {_airDummySpawn} " +
                      $"(blueprint='{bp.name}', cruise r={_airDummyCruiseRadius}m alt={_airDummyCruiseAltitude}m).",
                      _airDummyGo);
        }

        private void ApplyAirDummyFire()
        {
            if (_airDummyAi == null || _arena == null) return;
            bool fire = Tweakables.GetBool(Tweakables.AirDummyFire);
            // Same passive/aggressive split as the tank dummy: target only
            // bound when the fire toggle is on. Bot Cruises peacefully
            // until the user flips the slider.
            _airDummyAi.FireAtTarget = fire;
            _airDummyAi.Target = fire ? _arena.PlayerChassisTransform : null;
        }

        private ChassisBlueprint ResolveAirDummyBlueprint(GameStateController state)
        {
            if (_airDummyBlueprint != null) return _airDummyBlueprint;
            // Prefer a preset whose name contains "Heli" or "Plane"; fall
            // back to any Plane-kind preset.
            for (int i = 0; i < state.PresetBlueprints.Count; i++)
            {
                ChassisBlueprint bp = state.PresetBlueprints[i];
                if (bp == null) continue;
                string n = bp.name;
                if (n.IndexOf("Heli", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Plane", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return bp;
            }
            for (int i = 0; i < state.PresetBlueprints.Count; i++)
            {
                ChassisBlueprint bp = state.PresetBlueprints[i];
                if (bp != null && bp.Kind == ChassisKind.Plane) return bp;
            }
            return null;
        }
    }
}
