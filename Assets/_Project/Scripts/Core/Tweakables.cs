using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Front-end-accessible tweak registry for runtime physics / mechanical
    /// knobs. Subsystems read values via <see cref="Get(string)"/>; the
    /// settings UI writes them via <see cref="Set(string, float)"/>; values
    /// persist as JSON in <see cref="Application.persistentDataPath"/> so
    /// tweaks survive across play sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anything tunable from the settings menu lives here. Each tweak is
    /// declared once with a key, label, default, and range — the UI builds
    /// itself from <see cref="All"/>, and code reads the live value via
    /// <see cref="Get(string)"/>.
    /// </para>
    /// <para>
    /// Subsystems that cache values once (e.g. a rigidbody's
    /// <c>linearDamping</c>) should subscribe to <see cref="Changed"/> and
    /// re-push their cached field. Subsystems that read every frame don't
    /// need to do anything special.
    /// </para>
    /// </remarks>
    public static class Tweakables
    {
        // -----------------------------------------------------------------
        // Keys (compile-time-checked, no string typos at call sites)
        // -----------------------------------------------------------------

        // Plane.* / Ground.* / Chassis.* MIGRATED to server-authoritative
        // chassis-level blueprint config (ChassisBlueprint.PlaneTuning /
        // GroundTuning / ChassisDamping) — gameplay-observable movement
        // forces must not vary per-machine. PHYSICS_PLAN §1.5 / §5.

        // Thruster.* / Rudder.* MIGRATED: per-thruster max thrust + per-rudder
        // authority ride ChassisBlueprint.Entry.BlockConfig; thruster
        // idle/response are chassis-level ChassisBlueprint.ThrusterTuning
        // (Option A). No longer per-machine Tweakables. PHYSICS_PLAN §1.5 / §5.

        public const string WaterDensity      = "Water.Density";
        public const string WaterDisplacement = "Water.Displacement";
        public const string WaterLinearDrag   = "Water.LinearDrag";
        public const string WaterAngularDrag  = "Water.AngularDrag";
        public const string WaterGravity      = "Water.Gravity";
        public const string WaveAmplitude     = "Water.WaveAmplitude";
        public const string WaveLength        = "Water.WaveLength";
        public const string WaveSpeed         = "Water.WaveSpeed";
        public const string WaveSteepness     = "Water.WaveSteepness";

        // Combat.Smg* and Combat.Bomb* MIGRATED to per-block authoring
        // (this PR). ProjectileGun reads from
        // BlockDefinition.GetComponentData<WeaponDefinition>() and
        // falls back to inline SerializeField defaults; BombBayBlock
        // reads from BombDefinition the same way. Live tuning happens
        // in the editor via the asset (Assets/_Project/ScriptableObjects/
        // WeaponDefinitions/Weapon_Smg.asset), not via the in-game
        // Settings UI. PHYSICS_PLAN § 5.

        // Rope (free-body link block — see RopeBlock). Length / radius /
        // mass / damping / joint-limit stay as Tweakables intentionally:
        // rope feel is hard to dial in without live mid-match tuning, and
        // the user explicitly carved out an exception here. Rope LENGTH
        // (in chassis cells) moved to per-block blueprint config
        // (ChassisBlueprint.Entry.Dims.x) so a long-rope grappling hook
        // and a short-rope mace can coexist on the same chassis. The
        // segment-length Tweakable below now only controls the verlet
        // sub-segment density, not the rope's reach — placement-relevant
        // tip cell stays purely a function of blueprint data. See
        // RopeGeometry.ChainCellCount and RopeBlock.LiveSegmentCount.
        public const string RopeSegmentLength  = "Rope.SegmentLength";  // metres per link
        public const string RopeSegmentRadius  = "Rope.SegmentRadius";  // capsule radius (m)
        public const string RopeSegmentMass    = "Rope.SegmentMass";    // kg per link
        public const string RopeLinearDamping  = "Rope.LinearDamping";  // per-segment linear damping

        // Rotor.RPM MIGRATED to per-rotor blueprint config
        // (ChassisBlueprint.Entry.BlockConfig → BlockBehaviour.ConfigValue,
        // read by RotorBlock.LiveRpm) so a slow main rotor and a fast tail
        // rotor coexist on one chassis. PHYSICS_PLAN §1.5 / §5.

        // ---------------------------------------------------------------
        // MP DEBT AUDIT (PHYSICS_PLAN § 1.5)
        // ---------------------------------------------------------------
        // Every gameplay-observable knob has now been migrated off the
        // per-machine Tweakables (hard invariant #1 satisfied). What
        // remains is world-physics / arena state the server makes
        // canonical, or presentation / rope-feel knobs that never affect
        // cross-machine state:
        //
        //   • Water.*                         — arena property; server
        //                                       pushes the seed in MP.
        //   • Rope.SegmentLength/Radius/Mass  — rope feel; stay tunable.
        //   • Rope.AngularLimit/Damping       — rope feel; stay tunable.
        //
        // Already migrated:
        //   • Aero.WingSpan / Chord / Thickness → BlockBehaviour.Dims (per-foil).
        //   • Rope.SegmentCount                  → BlockBehaviour.Dims.x (per-rope).
        //   • Combat.Smg*                        → WeaponDefinition SO (per-weapon-block).
        //   • Combat.Bomb*                       → BombDefinition SO (per-bomb-block).
        //   • Combat.Rope* (tip damage)          → TipBlock SerializeFields (per-tip-block).
        //   • Impact.* (ramming damage)          → ImpactConfig SO (server/world-canonical).
        //   • Plane.* / Ground.* / Chassis.*     → ChassisBlueprint chassis-level config (per-chassis).
        //   • Thruster idle/response             → ChassisBlueprint.ThrusterTuning (per-chassis).
        //   • Thruster MaxThrust / Rudder / Rotor.RPM → Entry.BlockConfig (per-block).

        // Rope-tip contact damage MIGRATED to per-tip-block SerializeFields
        // on TipBlock (this PR). HookBlock and MaceBlock both inherit those
        // fields; mass differential between Hook and Mace continues to drive
        // the kinetic-energy gameplay differential. PHYSICS_PLAN § 3 / § 5.

        // Momentum / ramming impact MIGRATED to the server/world-canonical
        // ImpactConfig SO (Resources/ImpactConfig.asset) — ramming damage is
        // a gameplay outcome and must not vary per-machine. See
        // MomentumImpactHandler + PHYSICS_PLAN § 1.5 / § 5.

        // Stress / debug. These are dev-only knobs surfaced in the
        // settings panel + dev HUD so a session can stress-test the
        // physics pipeline without code changes. Slider values >= 0.5
        // are treated as "on" — the bool gate trick avoids adding a
        // separate boolean storage type to the tweakables registry.
        public const string StressRotorTower    = "Stress.RotorTower";    // 0/1 toggle: spawn the spinning-rotor tower in the arena
        public const string StressRotorTowerRpm = "Stress.RotorTowerRpm"; // RPM applied to every rotor in the stress tower (independent of Rotor.RPM)

        // Tank dummy bot: drives in a circle, optional fire-at-player.
        // Singleplayer training affordance — see GroundBotInputSource for
        // the AI behaviour. 0/1 toggles per the existing Stress.* convention.
        public const string TankDummySpawn = "Stress.TankDummy";       // 0/1 toggle: spawn / despawn the patrolling tank
        public const string TankDummyFire  = "Stress.TankDummyFire";   // 0/1 toggle: tank fires at player chassis when in arc + range

        // Audio mix bus volumes, 0–1 (linear). AudioRouter converts these
        // to dB at the mixer level so the slider reads as "perceived
        // loudness" and a value of 0 produces silence. The Mute toggle
        // is a hard global cut applied on top — used to gate every group
        // without rewriting individual slider values.
        // Presentation-only; the "no Tweakable affects gameplay" rule
        // (PHYSICS_PLAN § 1.5) is satisfied by construction.
        public const string AudioMaster = "Audio.MasterVolume";
        public const string AudioSfx    = "Audio.SfxVolume";
        public const string AudioMusic  = "Audio.MusicVolume";
        public const string AudioUI     = "Audio.UIVolume";
        public const string AudioMute   = "Audio.Mute";

        // QoL toggle: freeze gameplay while the settings panel is
        // open. Defaults to true — players reaching for Esc usually
        // want the world to stop, not to die mid-tweak. Disable for
        // tuning sessions where you want to see the slider's effect
        // on a moving chassis live.
        // Pure presentation flag (no gameplay-canonical impact);
        // satisfies the "no Tweakable affects MP outcomes" rule by
        // construction since pausing is a singleplayer-only concept.
        public const string SettingsPause = "QoL.PauseOnSettings";

        // Air dummy bot: same idea, plane chassis. Spawn/Fire toggles.
        // Like the tank: spawn flag drops the bot into the arena passively
        // (Patrol-only, no target); fire flag binds the player as the target
        // and switches the bot into Pursue/Engage with live fire.
        public const string AirDummySpawn  = "Stress.AirDummy";        // 0/1 toggle: spawn / despawn the patrolling air bot
        public const string AirDummyFire   = "Stress.AirDummyFire";    // 0/1 toggle: air bot engages + fires at player

        // -----------------------------------------------------------------
        // Spec
        // -----------------------------------------------------------------

        public enum SpecKind
        {
            /// <summary>Continuous slider value in [Min, Max].</summary>
            Float,
            /// <summary>On/off toggle. Stored as 0 (off) or 1 (on); UI renders as a checkbox.</summary>
            Bool,
        }

        public sealed class Spec
        {
            public readonly string Key;
            public readonly string Group;
            public readonly string Label;
            public readonly float Default;
            public readonly float Min;
            public readonly float Max;
            public readonly SpecKind Kind;
            public Spec(string key, string group, string label, float def, float min, float max, SpecKind kind = SpecKind.Float)
            {
                Key = key; Group = group; Label = label;
                Default = def; Min = min; Max = max; Kind = kind;
            }
        }

        private static readonly List<Spec> _specs = new List<Spec>();
        private static readonly Dictionary<string, Spec> _byKey = new Dictionary<string, Spec>();
        private static readonly Dictionary<string, float> _values = new Dictionary<string, float>();
        private static bool _initialized;

        /// <summary>Raised any time a value changes (or after a bulk reload from disk).</summary>
        public static event Action Changed;

        public static IReadOnlyList<Spec> All
        {
            get { EnsureInitialized(); return _specs; }
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        public static float Get(string key)
        {
            EnsureInitialized();
            if (_values.TryGetValue(key, out float v)) return v;
            if (_byKey.TryGetValue(key, out Spec s)) return s.Default;
            Debug.LogWarning($"[Tweakables] Unknown key '{key}' — returning 0.");
            return 0f;
        }

        public static void Set(string key, float value)
        {
            EnsureInitialized();
            if (!_byKey.TryGetValue(key, out Spec s))
            {
                Debug.LogWarning($"[Tweakables] Set: unknown key '{key}'.");
                return;
            }
            float clamped = Mathf.Clamp(value, s.Min, s.Max);
            if (s.Kind == SpecKind.Bool) clamped = clamped >= 0.5f ? 1f : 0f;
            if (_values.TryGetValue(key, out float current) && Mathf.Approximately(current, clamped)) return;
            _values[key] = clamped;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Convenience: read a Bool-kind tweakable as a real bool.</summary>
        public static bool GetBool(string key) => Get(key) >= 0.5f;

        /// <summary>Convenience: write a Bool-kind tweakable from a real bool.</summary>
        public static void SetBool(string key, bool value) => Set(key, value ? 1f : 0f);

        public static void Reset(string key)
        {
            EnsureInitialized();
            if (!_byKey.TryGetValue(key, out Spec s)) return;
            Set(key, s.Default);
        }

        public static void ResetAll()
        {
            EnsureInitialized();
            foreach (Spec s in _specs) _values[s.Key] = s.Default;
            Save();
            Changed?.Invoke();
        }

        // -----------------------------------------------------------------
        // Registration / persistence
        // -----------------------------------------------------------------

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // Plane / Ground / Chassis-damping + Thruster + Rudder + Rotor
            // RPM tuning all migrated to server-authoritative blueprint
            // config (ChassisBlueprint chassis-level configs + per-block
            // Entry.BlockConfig). No longer per-machine Tweakables; every
            // gameplay-observable knob is now invariant-#1 clean.
            // PHYSICS_PLAN §1.5 / §5.

            // Water-arena buoyancy (read live by WaterVolume / BuoyancyController).
            // Density is in "buoyancy units", not kg/m³ — empirically ~4 is
            // enough to float a tank-sized chassis, so we keep the range
            // tight (0–20) to give the slider real resolution.
            Register(WaterDensity,      "Water",    "Density",            4.0f,  0f,   20f);
            // Effective fraction of each block's bounding cube that
            // actually displaces water. Blocks are modelled as hollow
            // shells (~0.3) so a chassis must be "shaped buoyantly"
            // (wider footprint, more wet blocks) to stay up rather than
            // every cube being a solid float.
            Register(WaterDisplacement, "Water",    "Displacement",       0.30f, 0f,    1f);
            Register(WaterLinearDrag,   "Water",    "Linear Drag",        1.4f,  0f,   10f);
            Register(WaterAngularDrag,  "Water",    "Angular Drag",       2.5f,  0f,   10f);
            Register(WaterGravity,      "Water",    "Gravity",            9.81f, 0f,   30f);

            // Gerstner wave parameters consumed by WaterSurface.SampleHeight
            // and WaterMeshAnimator. Defaults are tuned big-and-slow so the
            // arena reads as a stylised swell rather than a fizzy ripple
            // pond — the dev HUD exposes sliders for live tuning.
            // Speed note: phase speed in m/s. Period for wave i = λ_i / speed.
            // At λ=30, speed=2.0 gives a 15 s dominant period (~slow swell);
            // smaller-wavelength components (×0.625, ×0.385) ride at ~9 s / 6 s.
            // Drop below ~1.0 and the surface visibly stops moving.
            Register(WaveAmplitude,     "Water",    "Wave Amplitude",     1.20f, 0f,    4f);
            Register(WaveLength,        "Water",    "Wave Length",        30.0f, 1f,   80f);
            Register(WaveSpeed,         "Water",    "Wave Speed",          2.0f, 0f,   10f);
            Register(WaveSteepness,     "Water",    "Wave Steepness",     0.45f, 0f,    1f);

            // SMG / Bomb stats migrated to per-block ScriptableObjects
            // (WeaponDefinition / BombDefinition wired via
            // BlockDefinition.ComponentData). Edit the assets in the
            // editor; live in-game tuning is gone for these by design.

            // Rope physics. Rope LENGTH (in cells) moved to per-block
            // blueprint config — see RopeGeometry.ChainCellCount. The
            // SegmentLength knob below is now a sub-segment density
            // tuning knob (smaller = smoother chain over the same
            // total length); it no longer affects the rope's reach.
            // Length / radius / mass / linear-damping stay hot-tunable
            // per the user's "ropes need in-match tuning" call. Angular
            // limit + angular damping were retired with the joint-chain
            // rope; the Verlet sim derives bending from positional
            // constraints instead (see VerletRopeChain.BendingStiffness).
            Register(RopeSegmentLength,  "Rope", "Segment Length (m)", 0.50f, 0.10f, 1.50f);
            Register(RopeSegmentRadius,  "Rope", "Segment Radius (m)", 0.08f, 0.02f, 0.40f);
            Register(RopeSegmentMass,    "Rope", "Segment Mass (kg)",  0.04f, 0.005f, 1.0f);
            Register(RopeLinearDamping,  "Rope", "Linear Damping",     0.10f, 0f,    4f);

            // Rotor.RPM migrated to per-rotor blueprint config
            // (Entry.BlockConfig → BlockBehaviour.ConfigValue, read by
            // RotorBlock.LiveRpm; historical default 60 rpm). No longer a
            // per-machine Tweakable. PHYSICS_PLAN §1.5 / §5.

            // Aerofoil visual dims migrated to per-block blueprint config
            // (ChassisBlueprint.Entry.Dims) in the variable-parts pass.
            // Authored in the build mode VariantConfigPanel; AeroSurface-
            // Block reads BlockBehaviour.Dims at place-time. No tweakables.

            // Rope-tip damage migrated to per-tip-block SerializeFields on
            // TipBlock (this PR). HookBlock and MaceBlock inherit them.

            // Impact ramming-damage curve migrated to ImpactConfig SO
            // (Resources/ImpactConfig.asset) — server/world-canonical,
            // not a per-machine Tweakable. PHYSICS_PLAN § 1.5 / § 5.

            // Stress / debug. The arena controller subscribes to Changed
            // and (de)spawns the rotor tower live as the slider crosses
            // 0.5 — drag the tower in for a Profiler capture, drag it
            // back out, no scene reload required.
            RegisterBool(StressRotorTower, "Stress", "Spawn Rotor Tower", false);
            Register(StressRotorTowerRpm,  "Stress", "Tower RPM",       300.0f, 0f, 600f);
            RegisterBool(TankDummySpawn,   "Stress", "Spawn Tank Dummy",   false);
            RegisterBool(TankDummyFire,    "Stress", "Tank Fires Player",  false);
            RegisterBool(AirDummySpawn,    "Stress", "Spawn Air Dummy",    false);
            RegisterBool(AirDummyFire,     "Stress", "Air Bot Fires",      false);

            // Audio mix. Defaults: Master 1.0, SFX/Music/UI 0.8, no
            // mute. AudioRouter subscribes to Changed and re-applies
            // every value when any one moves. The unit is linear gain
            // 0–1; the dB conversion happens in AudioRouter so the
            // slider stays interpretable from the inspector.
            Register(AudioMaster,    "Audio", "Master Volume", 1.00f, 0f, 1f);
            Register(AudioSfx,       "Audio", "SFX Volume",    0.80f, 0f, 1f);
            Register(AudioMusic,     "Audio", "Music Volume",  0.80f, 0f, 1f);
            Register(AudioUI,        "Audio", "UI Volume",     0.80f, 0f, 1f);
            RegisterBool(AudioMute,  "Audio", "Mute All",      false);

            // QoL.
            RegisterBool(SettingsPause, "QoL", "Pause When Settings Open", true);

            Load();
        }

        private static void Register(string key, string group, string label, float def, float min, float max)
        {
            var s = new Spec(key, group, label, def, min, max, SpecKind.Float);
            _specs.Add(s);
            _byKey[key] = s;
            _values[key] = def;
        }

        private static void RegisterBool(string key, string group, string label, bool def)
        {
            float v = def ? 1f : 0f;
            var s = new Spec(key, group, label, v, 0f, 1f, SpecKind.Bool);
            _specs.Add(s);
            _byKey[key] = s;
            _values[key] = v;
        }

        private static string SavePath
            => Path.Combine(Application.persistentDataPath, "tweakables.json");

        [Serializable]
        private sealed class SaveDoc
        {
            public string[] keys;
            public float[] values;
        }

        private static void Save()
        {
            try
            {
                var doc = new SaveDoc { keys = new string[_values.Count], values = new float[_values.Count] };
                int i = 0;
                foreach (var kv in _values)
                {
                    doc.keys[i] = kv.Key;
                    doc.values[i] = kv.Value;
                    i++;
                }
                File.WriteAllText(SavePath, JsonUtility.ToJson(doc, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tweakables] Save failed: {e.Message}");
            }
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                var doc = JsonUtility.FromJson<SaveDoc>(File.ReadAllText(SavePath));
                if (doc == null || doc.keys == null || doc.values == null) return;
                int n = Mathf.Min(doc.keys.Length, doc.values.Length);
                for (int i = 0; i < n; i++)
                {
                    string k = doc.keys[i];
                    if (!_byKey.TryGetValue(k, out Spec s)) continue;
                    float v = Mathf.Clamp(doc.values[i], s.Min, s.Max);
                    if (s.Kind == SpecKind.Bool) v = v >= 0.5f ? 1f : 0f;
                    _values[k] = v;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tweakables] Load failed: {e.Message}");
            }
        }
    }
}
