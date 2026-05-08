using System.Collections;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Movement;
using Robogame.Player;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [Tooltip("Hotkey that respawns the player chassis at the configured spawn point. Skipped while a UI panel (Settings, Build) holds the cursor.")]
        [SerializeField] private Key _respawnKey = Key.K;

        [Tooltip("Hotkey that ends the warmup and starts the round. Pressed while the cursor is locked and the player is free-flying — no modal overlay to click through.")]
        [SerializeField] private Key _startMatchKey = Key.Backquote;

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

        [Header("Arch test dummy")]
        [Tooltip("Grappleable archway target — hook fits inside the top " +
                 "beam's 1 m mouth. Spawned off to the player's left so " +
                 "the existing combat dummy stays dead-ahead. Built from " +
                 "Blueprint_ArchDummy.")]
        // FormerlySerializedAs preserves the scene wire-up across the
        // session-22 (barbell→dumbbell) and session-24 (dumbbell→arch)
        // renames so Arena.unity's serialised value carries forward
        // without a Build Everything pass.
        [FormerlySerializedAs("_dumbbellBlueprint")]
        [FormerlySerializedAs("_barbellBlueprint")]
        [SerializeField] private ChassisBlueprint _archBlueprint;
        [FormerlySerializedAs("_dumbbellPosition")]
        [FormerlySerializedAs("_barbellPosition")]
        [SerializeField] private Vector3 _archPosition = new Vector3(-25f, 0.5f, 18f);
        [FormerlySerializedAs("_dumbbellName")]
        [FormerlySerializedAs("_barbellName")]
        [SerializeField] private string _archName = "ArchDummy";

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

        [Header("Repair pad")]
        [Tooltip("If true, a procedural healing AoE pad is spawned at the position below. Player drives " +
                 "onto it to gradually rebuild a damaged chassis from the frozen blueprint. Player-only.")]
        [SerializeField] private bool _spawnRepairPad = true;

        [Tooltip("Pad placement (corner of the arena by default).")]
        [SerializeField] private Vector3 _repairPadPosition = new Vector3(35f, 0.1f, 35f);

        [Header("Match")]
        [Tooltip("Round shape + bot roster for the singleplayer game loop. " +
                 "Embedded directly on the ArenaController so the data lives " +
                 "alongside the scene that uses it; PHYSICS_PLAN § 1.5 " +
                 "(no Tweakable affects gameplay outcomes) is satisfied because " +
                 "every value here is a designer-authored SerializeField, not " +
                 "a per-machine slider.")]
        [SerializeField] private MatchConfig _matchConfig = new MatchConfig();

        [Tooltip("Default ground-bot spawn position. Used for any GroundBots " +
                 "entry whose SpawnPositionOverride is Vector3.zero.")]
        [SerializeField] private Vector3 _groundBotSpawnDefault = new Vector3(28f, 2f, 0f);

        [Tooltip("Default air-bot spawn position. Used for any AirBots " +
                 "entry whose SpawnPositionOverride is Vector3.zero.")]
        [SerializeField] private Vector3 _airBotSpawnDefault = new Vector3(0f, 50f, 60f);

        private GameObject _stressTowerGo;
        private GameObject _tankDummyGo;
        private GroundBotInputSource _tankDummyAi;
        private GameObject _airDummyGo;
        private AirBotInputSource _airDummyAi;

        // -----------------------------------------------------------------
        // Match wiring
        // -----------------------------------------------------------------

        private MatchController _match;
        // Track every chassis we've registered with the match so a single
        // Destroyed event routes to the right side. Using a Dictionary keyed
        // on the Robot lets the lookup be O(1) on the destruction callback.
        private readonly Dictionary<Robot, MatchSide> _registeredChassis = new();
        // Pre-sized buffer for bot GameObjects so spawn / respawn doesn't
        // walk the scene to find them.
        private readonly List<GameObject> _matchBots = new();

        public GameObject Chassis { get; private set; }

        /// <summary>The match controller for the current arena session, or null if no MatchConfig was assigned.</summary>
        public MatchController Match => _match;

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
            SpawnArch(state);
            if (_spawnRepairPad) RepairPad.CreateProcedural(_repairPadPosition, transform);

            // Build the match controller BEFORE BindFollowCamera so the
            // ObjectiveHud + MatchEndOverlay added there get a non-null
            // controller via BindMatch — the canonical bind path. (Both
            // HUDs have a fallback FindFirstObjectByType, but binding
            // explicitly is one less Update-frame of empty state.)
            CreateMatch();
            BindFollowCamera(Chassis);
            RegisterChassis(Chassis, MatchSide.Player);
            SpawnMatchBots(state);

            // Stress tower: optional. Read the tweakable on entry and
            // (de)spawn live as the slider moves. Subscribing here means
            // dragging Stress.RotorTower in the settings panel pops the
            // tower in/out without re-entering the arena.
            ApplyStressTowerState(state);
            ApplyTankDummyState(state);
            ApplyAirDummyState(state);
            Tweakables.Changed += OnTweakablesChanged;
        }

        private void OnDestroy()
        {
            Tweakables.Changed -= OnTweakablesChanged;
            if (_match != null)
            {
                _match.MatchEnded -= HandleMatchEnded;
                _match.MatchStarted -= HandleMatchStarted;
            }
            // Robot.Destroyed is per-instance; the GameObjects are being
            // torn down with the scene so the subscriptions die naturally.
            // Clearing the dictionary keeps a stale ref from holding the
            // GameObject alive across scene loads.
            _registeredChassis.Clear();
            _matchBots.Clear();
        }

        // -----------------------------------------------------------------
        // Cursor lifecycle — coordinated with FollowCamera so the relock
        // guard in FollowCamera.LateUpdate doesn't fight us.
        // -----------------------------------------------------------------

        private void ReleaseCursorForUI()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return;
            FollowCamera follow = mainCam.GetComponent<FollowCamera>();
            if (follow != null) follow.ReleaseCursor();
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
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

        private void SpawnArch(GameStateController state)
        {
            if (_archBlueprint == null) return; // optional — missing wire-up is fine
            if (state.Library == null) return;

            GameObject existing = GameObject.Find(_archName);
            if (existing != null) Destroy(existing);

            var go = new GameObject(_archName);
            go.transform.position = _archPosition;
            // freezeRotation: true — the arch is a grounded structural
            // target, not a swing-around mass. The player flies through
            // it and grapples the top beam; the joint pulls on the chassis
            // (whose Rigidbody is freeze-rotation) so the arch stays
            // upright while the helicopter feels the tension.
            Robogame.Robots.Robot arch = ChassisFactory.BuildTarget(
                go, _archBlueprint, state.Library,
                freezeRotation: true);
            int blockCount = arch != null ? arch.BlockCount : 0;
            Debug.Log($"[Robogame] Arch dummy spawned at {_archPosition} with {blockCount} blocks.", go);
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

        /// <summary>
        /// Tear down the current player chassis and rebuild it at the
        /// configured spawn point. Bound to <see cref="_respawnKey"/> via
        /// <see cref="Update"/>; also exposed as a SettingsHud action button.
        /// </summary>
        public void RespawnPlayer()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                Debug.LogWarning("[Robogame] ArenaController.RespawnPlayer: no GameStateController.", this);
                return;
            }
            Chassis = SpawnPlayerChassis(state);
            BindFollowCamera(Chassis);
            // Re-bind every live AI's target to the new chassis. Without
            // this, bots keep their reference to the destroyed (fake-null)
            // old transform and stay in Patrol forever after a respawn.
            ApplyTankDummyFire();
            RebindBotTargets();
        }

        /// <summary>
        /// Push the current player <see cref="Chassis"/> transform onto every
        /// MatchConfig-spawned bot's <c>Target</c> property. Called from
        /// <see cref="RespawnPlayer"/> and from <see cref="RespawnBotAfterDelay"/>
        /// (since a freshly-respawned bot also needs the live player ref).
        /// </summary>
        private void RebindBotTargets()
        {
            Transform t = Chassis != null ? Chassis.transform : null;
            for (int i = 0; i < _matchBots.Count; i++)
            {
                GameObject bot = _matchBots[i];
                if (bot == null) continue;
                GroundBotInputSource ground = bot.GetComponent<GroundBotInputSource>();
                if (ground != null) ground.Target = t;
                AirBotInputSource air = bot.GetComponent<AirBotInputSource>();
                if (air != null) air.Target = t;
            }
        }

        private void Update()
        {
            // Tick the match controller every frame so its warmup + round
            // timer advances. MatchController is a plain C# class — no
            // automatic Update — so the host MonoBehaviour owns the cadence.
            if (_match != null) _match.Tick(Time.deltaTime);

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // Start-match hotkey. Fires only during warmup; cursor-lock
            // check keeps it from triggering while the user has the
            // settings panel open. No mouse click required — the player
            // can free-fly the whole warmup and tap the key when ready.
            if (_match != null
                && _match.State == MatchState.WarmingUp
                && kb[_startMatchKey].wasPressedThisFrame
                && Cursor.lockState == CursorLockMode.Locked)
            {
                _match.StartMatch();
            }

            // Hotkey-driven respawn. Cursor-locked check keeps this from
            // firing while the player types into a settings field. The
            // Settings panel doesn't currently host a text input, but this
            // is the right shape for when it does.
            if (kb[_respawnKey].wasPressedThisFrame
                && Cursor.lockState == CursorLockMode.Locked)
            {
                RespawnPlayer();
            }
        }

        // -----------------------------------------------------------------
        // Match controller lifecycle
        // -----------------------------------------------------------------

        private void CreateMatch()
        {
            if (_matchConfig == null)
            {
                Debug.LogWarning("[Robogame] ArenaController: no MatchConfig assigned — running in sandbox mode (no round timer / win conditions / score).", this);
                return;
            }
            _match = new MatchController(_matchConfig);
            _match.MatchEnded += HandleMatchEnded;
            _match.MatchStarted += HandleMatchStarted;

            // Cursor stays locked through warmup so the player can free-fly
            // / drive while the bots patrol passively. The StartMatchHud
            // shows a non-blocking corner prompt with the configured
            // hotkey; ArenaController.Update polls the key to start the
            // round. Cursor only unlocks again on MatchEnded so the
            // Return-to-Garage button is clickable.
        }

        private void HandleMatchStarted()
        {
            // Round goes hot: bind targets + fire toggles on every
            // MatchConfig-spawned bot. Until this point bots Patrolled
            // passively because Target was null on spawn. Now they enter
            // Pursue/Engage and shoot.
            Transform t = Chassis != null ? Chassis.transform : null;
            for (int i = 0; i < _matchBots.Count; i++)
            {
                GameObject bot = _matchBots[i];
                if (bot == null) continue;
                GroundBotInputSource ground = bot.GetComponent<GroundBotInputSource>();
                if (ground != null)
                {
                    ground.Target = t;
                    ground.FireAtTarget = true;
                }
                AirBotInputSource air = bot.GetComponent<AirBotInputSource>();
                if (air != null)
                {
                    air.Target = t;
                    air.FireAtTarget = true;
                }
            }
            // Cursor was already locked through warmup — nothing to do here.
            // Gameplay flows uninterrupted.
            Debug.Log("[Robogame] Match started — bots engaging.", this);

            // Stinger.
            Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.MatchStart);
        }

        private void HandleMatchEnded(MatchEndedArgs args)
        {
            // Free the cursor so the user can click "Return to Garage" on
            // the MatchEndOverlay. ReleaseCursorForUI routes through
            // FollowCamera.ReleaseCursor — a bare Cursor.lockState assignment
            // lasts exactly one frame because FollowCamera.LateUpdate
            // re-applies the lock when it sees _cursorWasLocked == true.
            // ReleaseCursor clears that flag, breaking the relock loop.
            // ArenaController.Update gates respawn hotkeys on cursor-lock,
            // so this also disables K-respawn — exactly the behaviour we
            // want once the round is over.
            ReleaseCursorForUI();

            // Suppress the chassis death overlay while the match-end
            // overlay is up; without this, "DESTROYED" stacks behind
            // "VICTORY" / "DEFEAT" and the player can't read either.
            // DeathOverlay lives in Robogame.Player, which can't see
            // MatchController types directly, so the suppression has to
            // come from the Gameplay-tier ArenaController.
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                DeathOverlay deathOverlay = mainCam.GetComponent<DeathOverlay>();
                if (deathOverlay != null) deathOverlay.enabled = false;
            }

            Debug.Log($"[Robogame] Match ended: winner={args.WinnerSide} reason={args.Reason} score {args.PlayerScore}-{args.EnemyScore}", this);

            // Outcome stinger. Player win → Victory; player loss → Defeat;
            // anything else (Draw, no winner) → Draw cue.
            Robogame.Core.AudioCue endCue = args.WinnerSide switch
            {
                MatchSide.Player => Robogame.Core.AudioCue.MatchEndVictory,
                MatchSide.Enemy  => Robogame.Core.AudioCue.MatchEndDefeat,
                _                => Robogame.Core.AudioCue.MatchEndDraw,
            };
            Robogame.Core.AudioRouter.PlayUI(endCue);
        }

        // -----------------------------------------------------------------
        // Chassis registration
        // -----------------------------------------------------------------

        private void RegisterChassis(GameObject go, MatchSide side)
        {
            if (go == null) return;
            Robot r = go.GetComponent<Robot>();
            if (r == null)
            {
                Debug.LogWarning($"[Robogame] ArenaController: chassis '{go.name}' has no Robot component; skipping match registration.", go);
                return;
            }
            if (_registeredChassis.ContainsKey(r)) return;
            _registeredChassis[r] = side;
            r.Destroyed += HandleRobotDestroyed;
        }

        private void HandleRobotDestroyed(Robot victim)
        {
            if (victim == null) return;
            if (!_registeredChassis.TryGetValue(victim, out MatchSide victimSide)) return;

            // Killer attribution: in the singleplayer 1-vs-N case, the
            // killer is always "the other side." Multi-bot or team modes
            // would need a real damage-source tracker on the Robot itself
            // (track-which-side-dealt-the-final-block-of-damage). MP debt.
            MatchSide killerSide = victimSide == MatchSide.Player ? MatchSide.Enemy : MatchSide.Player;

            if (_match != null)
            {
                _match.RegisterKill(killerSide, victimSide);
            }

            if (victimSide == MatchSide.Player)
            {
                // Decrement the player's life. If we run out, end the match
                // by elimination. Otherwise schedule a respawn.
                int livesLeft = _match != null ? _match.DecrementPlayerLives() : 1;
                if (livesLeft <= 0)
                {
                    if (_match != null) _match.NotifyPlayerLivesExhausted();
                }
                else if (_match != null && _match.Config.PlayerRespawnDelay > 0f && isActiveAndEnabled)
                {
                    StartCoroutine(RespawnPlayerAfterDelay(_match.Config.PlayerRespawnDelay));
                }
            }
            else if (victimSide == MatchSide.Enemy)
            {
                // Bot respawn (if enabled and the match is still going).
                if (_match != null
                    && _match.State == MatchState.InProgress
                    && _match.Config.BotRespawnDelay > 0f
                    && isActiveAndEnabled)
                {
                    // Each bot remembers its origin via the GameObject still
                    // in _matchBots; we identify it by the destroyed Robot's
                    // GameObject and re-spawn from the same MatchConfig entry.
                    GameObject botGo = victim.gameObject;
                    StartCoroutine(RespawnBotAfterDelay(botGo, _match.Config.BotRespawnDelay));
                }
            }

            _registeredChassis.Remove(victim);
            // r.Destroyed unsubscribes naturally as the Robot is destroyed.
        }

        private IEnumerator RespawnPlayerAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_match != null && _match.State == MatchState.RoundEnded) yield break;
            RespawnPlayer();
            // Re-register the new player chassis so subsequent kills route
            // through MatchController correctly.
            RegisterChassis(Chassis, MatchSide.Player);
        }

        private IEnumerator RespawnBotAfterDelay(GameObject staleBotGo, float delay)
        {
            // Capture which slot this bot was in so we can re-spawn from
            // the same MatchConfig entry. We index by GameObject reference
            // — if the user removed the bot from _matchBots manually
            // (Despawn from settings panel) this bails cleanly.
            int botIndex = _matchBots.IndexOf(staleBotGo);
            yield return new WaitForSeconds(delay);
            if (_match == null || _match.State == MatchState.RoundEnded) yield break;
            if (botIndex < 0) yield break;

            // Tear down the corpse if it's still around (Robot.Destroyed
            // disables the chassis but leaves the GameObject for debris
            // scatter; we destroy it so the next spawn doesn't pile up).
            if (staleBotGo != null) Destroy(staleBotGo);

            // Resolve the entry that produced this bot. Ground bots come
            // first in _matchBots (we spawn them in order in
            // SpawnMatchBots); air bots follow. The split point is
            // _matchConfig.GroundBots.Length.
            int groundCount = _matchConfig != null && _matchConfig.GroundBots != null ? _matchConfig.GroundBots.Length : 0;
            GameStateController state = GameStateController.Instance;
            if (state == null) yield break;
            GameObject fresh;
            if (botIndex < groundCount)
            {
                fresh = SpawnGroundBot(state, _matchConfig.GroundBots[botIndex]);
            }
            else
            {
                int airIdx = botIndex - groundCount;
                if (_matchConfig.AirBots == null || airIdx >= _matchConfig.AirBots.Length) yield break;
                fresh = SpawnAirBot(state, _matchConfig.AirBots[airIdx]);
            }
            if (fresh != null)
            {
                _matchBots[botIndex] = fresh;
                RegisterChassis(fresh, MatchSide.Enemy);
            }
        }

        // -----------------------------------------------------------------
        // Match-config-driven bot spawning
        // -----------------------------------------------------------------

        private void SpawnMatchBots(GameStateController state)
        {
            if (_matchConfig == null) return;
            _matchBots.Clear();

            if (_matchConfig.GroundBots != null)
            {
                for (int i = 0; i < _matchConfig.GroundBots.Length; i++)
                {
                    GameObject go = SpawnGroundBot(state, _matchConfig.GroundBots[i]);
                    _matchBots.Add(go);
                    if (go != null) RegisterChassis(go, MatchSide.Enemy);
                }
            }

            if (_matchConfig.AirBots != null)
            {
                for (int i = 0; i < _matchConfig.AirBots.Length; i++)
                {
                    GameObject go = SpawnAirBot(state, _matchConfig.AirBots[i]);
                    _matchBots.Add(go);
                    if (go != null) RegisterChassis(go, MatchSide.Enemy);
                }
            }
        }

        private GameObject SpawnGroundBot(GameStateController state, MatchConfig.BotEntry entry)
        {
            if (entry.Blueprint == null) return null;
            Vector3 pos = entry.SpawnPositionOverride != Vector3.zero
                ? entry.SpawnPositionOverride
                : _groundBotSpawnDefault;
            Vector3 patrol = entry.PatrolCentreOverride != Vector3.zero
                ? entry.PatrolCentreOverride
                : Vector3.zero;

            string name = $"GroundBot_{System.Guid.NewGuid().ToString().Substring(0, 6)}";
            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            go.SetActive(false);
            GroundBotInputSource ai = go.AddComponent<GroundBotInputSource>();
            ai.CircleCentre = patrol;
            // Spawn passive: no target, no fire. ArenaController.HandleMatchStarted
            // binds Target + FireAtTarget when the match transitions to InProgress
            // (either via the warmup timer or a "FIGHT!" button click). Until
            // then the bot Patrols around its circle centre — visibly present
            // but harmless, which is what the round-start gate requires.
            ai.FireAtTarget = false;
            ai.Target = null;
            ChassisFactory.Build(
                go, entry.Blueprint, state.Library,
                inputActions: null, addPlayerInputs: false);
            go.SetActive(true);
            return go;
        }

        private GameObject SpawnAirBot(GameStateController state, MatchConfig.BotEntry entry)
        {
            if (entry.Blueprint == null) return null;
            Vector3 pos = entry.SpawnPositionOverride != Vector3.zero
                ? entry.SpawnPositionOverride
                : _airBotSpawnDefault;
            Vector3 patrol = entry.PatrolCentreOverride != Vector3.zero
                ? entry.PatrolCentreOverride
                : Vector3.zero;

            string name = $"AirBot_{System.Guid.NewGuid().ToString().Substring(0, 6)}";
            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            go.SetActive(false);
            AirBotInputSource ai = go.AddComponent<AirBotInputSource>();
            ai.CircleCentre = patrol;
            // Same passive-spawn rule as ground bots — see comment in
            // SpawnGroundBot.
            ai.FireAtTarget = false;
            ai.Target = null;
            ChassisFactory.Build(
                go, entry.Blueprint, state.Library,
                inputActions: null, addPlayerInputs: false);
            go.SetActive(true);
            return go;
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

        // -----------------------------------------------------------------
        // Tank dummy bot (optional patrolling target)
        // -----------------------------------------------------------------

        public void ApplyTankDummyState(GameStateController state)
        {
            bool wantBot = Tweakables.GetBool(Tweakables.TankDummySpawn);
            if (wantBot) SpawnTankDummy(state);
            else         DespawnTankDummy();
        }

        public void DespawnTankDummy()
        {
            if (_tankDummyGo != null)
            {
                Destroy(_tankDummyGo);
                _tankDummyGo = null;
                _tankDummyAi = null;
            }
        }

        private void SpawnTankDummy(GameStateController state)
        {
            if (_tankDummyGo != null) return; // already alive
            if (state.Library == null) return;

            ChassisBlueprint bp = ResolveTankDummyBlueprint(state);
            if (bp == null)
            {
                Debug.LogWarning(
                    "[Robogame] ArenaController: Tank dummy requested but no " +
                    "ChassisBlueprint resolved (assign _tankDummyBlueprint in the " +
                    "inspector or ensure GameStateController has a Ground preset).",
                    this);
                return;
            }

            GameObject existing = GameObject.Find(_tankDummyName);
            if (existing != null) Destroy(existing);

            // Build via the player path (with addPlayerInputs=false) so the
            // bot gets full GroundDriveSubsystem + WeaponMount + binders.
            // We attach the AI input source manually before activation so
            // PlayerController.Awake's GetComponent<IInputSource> resolves
            // to it.
            _tankDummyGo = new GameObject(_tankDummyName);
            _tankDummyGo.transform.SetPositionAndRotation(_tankDummySpawn, Quaternion.identity);
            _tankDummyGo.SetActive(false);
            _tankDummyAi = _tankDummyGo.AddComponent<GroundBotInputSource>();
            _tankDummyAi.CircleCentre = _tankDummyPatrolCentre;
            _tankDummyAi.CircleRadius = _tankDummyPatrolRadius;
            ChassisFactory.Build(
                _tankDummyGo, bp, state.Library,
                inputActions: null, addPlayerInputs: false);
            _tankDummyGo.SetActive(true);

            ApplyTankDummyFire();
            // Register the dev-spawned tank dummy with the match too, so
            // a kill against it counts toward the player's score. The
            // dummy still respects the PHYSICS_PLAN § 1.5 rule — it's
            // gated by the existing Tweakable for spawn / fire convenience,
            // but its damage outcomes feed the same per-side score the
            // MatchConfig-driven bots do.
            RegisterChassis(_tankDummyGo, MatchSide.Enemy);
            Debug.Log($"[Robogame] Tank dummy spawned at {_tankDummySpawn} " +
                      $"(blueprint='{bp.name}', patrol r={_tankDummyPatrolRadius}m).",
                      _tankDummyGo);
        }

        private void ApplyTankDummyFire()
        {
            if (_tankDummyAi == null) return;
            bool fire = Tweakables.GetBool(Tweakables.TankDummyFire);
            // Tweakable acts as the "go aggressive" master switch. With it
            // OFF the bot has no target → stays in Patrol. With it ON the
            // bot binds the player and switches to Pursue/Engage/fire. This
            // keeps the dev-spawn workflow predictable (toggle spawn, watch
            // it patrol, toggle fire when ready) and matches the new
            // round-start gate's "passive until I press FIGHT" feel.
            _tankDummyAi.FireAtTarget = fire;
            _tankDummyAi.Target = fire
                ? (Chassis != null ? Chassis.transform : GameObject.Find(_chassisName)?.transform)
                : null;
        }

        private ChassisBlueprint ResolveTankDummyBlueprint(GameStateController state)
        {
            if (_tankDummyBlueprint != null) return _tankDummyBlueprint;
            // Prefer a preset whose name contains "Tank"; fall back to any
            // Ground-kind preset.
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

        public void DespawnAirDummy()
        {
            if (_airDummyGo != null)
            {
                Destroy(_airDummyGo);
                _airDummyGo = null;
                _airDummyAi = null;
            }
        }

        private void SpawnAirDummy(GameStateController state)
        {
            if (_airDummyGo != null) return; // already alive
            if (state.Library == null) return;

            ChassisBlueprint bp = ResolveAirDummyBlueprint(state);
            if (bp == null)
            {
                Debug.LogWarning(
                    "[Robogame] ArenaController: Air dummy requested but no " +
                    "ChassisBlueprint resolved (assign _airDummyBlueprint in the " +
                    "inspector or ensure GameStateController has a Plane preset).",
                    this);
                return;
            }

            GameObject existing = GameObject.Find(_airDummyName);
            if (existing != null) Destroy(existing);

            _airDummyGo = new GameObject(_airDummyName);
            _airDummyGo.transform.SetPositionAndRotation(_airDummySpawn, Quaternion.identity);
            _airDummyGo.SetActive(false);
            _airDummyAi = _airDummyGo.AddComponent<AirBotInputSource>();
            _airDummyAi.CircleCentre = _airDummyCruiseCentre;
            _airDummyAi.CircleRadius = _airDummyCruiseRadius;
            _airDummyAi.TargetAltitude = _airDummyCruiseAltitude;
            ChassisFactory.Build(
                _airDummyGo, bp, state.Library,
                inputActions: null, addPlayerInputs: false);
            _airDummyGo.SetActive(true);

            ApplyAirDummyFire();
            RegisterChassis(_airDummyGo, MatchSide.Enemy);
            Debug.Log($"[Robogame] Air dummy spawned at {_airDummySpawn} " +
                      $"(blueprint='{bp.name}', cruise r={_airDummyCruiseRadius}m alt={_airDummyCruiseAltitude}m).",
                      _airDummyGo);
        }

        private void ApplyAirDummyFire()
        {
            if (_airDummyAi == null) return;
            bool fire = Tweakables.GetBool(Tweakables.AirDummyFire);
            // Same passive/aggressive split as the tank dummy: target only
            // bound when the fire toggle is on. Bot Cruises peacefully
            // until the user flips the slider.
            _airDummyAi.FireAtTarget = fire;
            _airDummyAi.Target = fire
                ? (Chassis != null ? Chassis.transform : GameObject.Find(_chassisName)?.transform)
                : null;
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

        private void BindFollowCamera(GameObject chassis)
        {
            if (chassis == null) return;
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            FollowCamera follow = mainCam.GetComponent<FollowCamera>();
            if (follow == null) follow = mainCam.gameObject.AddComponent<FollowCamera>();
            follow.Target = chassis.transform;

            if (mainCam.GetComponent<AimReticle>() == null)
                mainCam.gameObject.AddComponent<AimReticle>();
            if (mainCam.GetComponent<HitMarkerOverlay>() == null)
                mainCam.gameObject.AddComponent<HitMarkerOverlay>();
            if (mainCam.GetComponent<VehicleStatsHud>() == null)
                mainCam.gameObject.AddComponent<VehicleStatsHud>();
            if (mainCam.GetComponent<DeathOverlay>() == null)
                mainCam.gameObject.AddComponent<DeathOverlay>();
            // FloatingDamageOverlay subscribes to BlockBehaviour.DamageDealt
            // and renders per-target accumulating damage numbers
            // (session 31's summation behaviour). Re-enable by default
            // — the legacy "destroy if present" gate predated the
            // session-31 rewrite and has been removed.
            if (mainCam.GetComponent<FloatingDamageOverlay>() == null)
                mainCam.gameObject.AddComponent<FloatingDamageOverlay>();

            // Match HUDs (singleplayer game loop). Live in Robogame.Gameplay
            // because they read MatchController state — Robogame.Player sits
            // at a lower asmdef tier and can't reference Gameplay types.
            ObjectiveHud objectiveHud = mainCam.GetComponent<ObjectiveHud>();
            if (objectiveHud == null) objectiveHud = mainCam.gameObject.AddComponent<ObjectiveHud>();
            if (_match != null) objectiveHud.BindMatch(_match);

            MatchEndOverlay endOverlay = mainCam.GetComponent<MatchEndOverlay>();
            if (endOverlay == null) endOverlay = mainCam.gameObject.AddComponent<MatchEndOverlay>();
            if (_match != null) endOverlay.BindMatch(_match);

            StartMatchHud startHud = mainCam.GetComponent<StartMatchHud>();
            if (startHud == null) startHud = mainCam.gameObject.AddComponent<StartMatchHud>();
            if (_match != null) startHud.BindMatch(_match);
            // Friendlier label for keys whose ToString() reads
            // technically (e.g. "BACKQUOTE" → "`").
            string keyLabel = _startMatchKey switch
            {
                Key.Backquote => "`",
                _             => _startMatchKey.ToString().ToUpperInvariant(),
            };
            startHud.SetKeyName(keyLabel);

            // Kill announcer — first-blood + streak banner. Sticks
            // to player kills only.
            KillAnnouncer announcer = mainCam.GetComponent<KillAnnouncer>();
            if (announcer == null) announcer = mainCam.gameObject.AddComponent<KillAnnouncer>();
            if (_match != null) announcer.BindMatch(_match);
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
