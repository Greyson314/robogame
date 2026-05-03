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

        public const string PlanePitchPower    = "Plane.PitchPower";
        public const string PlaneRollPower     = "Plane.RollPower";
        public const string PlaneYawFromBank   = "Plane.YawFromBank";
        public const string PlanePitchDamping  = "Plane.PitchDamping";
        public const string PlaneRollDamping   = "Plane.RollDamping";
        public const string PlaneYawDamping    = "Plane.YawDamping";

        public const string ThrusterMaxThrust  = "Thruster.MaxThrust";
        public const string ThrusterIdle       = "Thruster.IdleThrottle";
        public const string ThrusterResponse   = "Thruster.ThrottleResponse";

        public const string RudderAuthority    = "Rudder.Authority";

        public const string GroundAccel        = "Ground.Acceleration";
        public const string GroundMaxSpeed     = "Ground.MaxSpeed";
        public const string GroundTurnRate     = "Ground.TurnRate";

        public const string ChassisLinDamp     = "Chassis.LinearDamping";
        public const string ChassisAngDamp     = "Chassis.AngularDamping";

        public const string WaterDensity      = "Water.Density";
        public const string WaterDisplacement = "Water.Displacement";
        public const string WaterLinearDrag   = "Water.LinearDrag";
        public const string WaterAngularDrag  = "Water.AngularDrag";
        public const string WaterGravity      = "Water.Gravity";
        public const string WaveAmplitude     = "Water.WaveAmplitude";
        public const string WaveLength        = "Water.WaveLength";
        public const string WaveSpeed         = "Water.WaveSpeed";
        public const string WaveSteepness     = "Water.WaveSteepness";

        // Combat — SMG pellet weapon. One stat block today; will move
        // to per-WeaponDefinition ScriptableObjects when a second
        // weapon type ships (Plasma / Rail / etc.).
        public const string SmgFireRate       = "Combat.SmgFireRate";       // shots per second
        public const string SmgMuzzleSpeed    = "Combat.SmgMuzzleSpeed";    // m/s
        public const string SmgSpread         = "Combat.SmgSpread";         // half-cone deg
        public const string SmgDamage         = "Combat.SmgDamage";         // direct-hit dmg

        // Bomb bay (gravity bomb dropped from chassis -Y).
        public const string BombDropInterval  = "Combat.BombDropInterval";  // seconds between drops while held
        public const string BombDamage        = "Combat.BombDamage";        // direct-hit dmg at impact cell
        public const string BombRadius        = "Combat.BombRadius";        // explosion radius (m)
        public const string BombInitialSpeed  = "Combat.BombInitialSpeed";  // initial downward speed (m/s)

        // -----------------------------------------------------------------
        // Spec
        // -----------------------------------------------------------------

        public sealed class Spec
        {
            public readonly string Key;
            public readonly string Group;
            public readonly string Label;
            public readonly float Default;
            public readonly float Min;
            public readonly float Max;
            public Spec(string key, string group, string label, float def, float min, float max)
            {
                Key = key; Group = group; Label = label;
                Default = def; Min = min; Max = max;
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
            if (_values.TryGetValue(key, out float current) && Mathf.Approximately(current, clamped)) return;
            _values[key] = clamped;
            Save();
            Changed?.Invoke();
        }

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

            // Plane control authority + damping.
            Register(PlanePitchPower,   "Plane",    "Pitch Power",        7.5f, 1f, 20f);
            Register(PlaneRollPower,    "Plane",    "Roll Power",         9.0f, 1f, 25f);
            Register(PlaneYawFromBank,  "Plane",    "Yaw From Bank",      2.0f, 0f,  8f);
            Register(PlanePitchDamping, "Plane",    "Pitch Damping",      3.5f, 0f, 10f);
            Register(PlaneRollDamping,  "Plane",    "Roll Damping",       2.8f, 0f, 10f);
            Register(PlaneYawDamping,   "Plane",    "Yaw Damping",        1.6f, 0f,  6f);

            // Thruster.
            Register(ThrusterMaxThrust, "Thruster", "Max Thrust",       310.0f, 50f, 800f);
            Register(ThrusterIdle,      "Thruster", "Idle Throttle",      0.4f,  0f,   1f);
            Register(ThrusterResponse,  "Thruster", "Throttle Response",  2.6f,  0.5f, 10f);

            // Rudder — yaw force per (m/s of forward speed) per (1.0 of
            // steer input). At authority=3 a 5 m/s boat with full A/D
            // gets a stern-side force of ~15 N which yaws a 40 kg hull
            // about 25°/s — boaty without being twitchy.
            Register(RudderAuthority,   "Rudder",   "Rudder Authority",   3.0f,  0f,  15f);

            // Ground drive.
            Register(GroundAccel,       "Ground",   "Acceleration",      26.25f, 5f, 80f);
            Register(GroundMaxSpeed,    "Ground",   "Max Speed",         13.5f,  3f, 40f);
            Register(GroundTurnRate,    "Ground",   "Turn Rate",          7.5f,  1f, 20f);

            // Chassis-level rigidbody damping (applied live in RobotDrive).
            Register(ChassisLinDamp,    "Chassis",  "Linear Damping",     0.2f,  0f,  4f);
            Register(ChassisAngDamp,    "Chassis",  "Angular Damping",    2.0f,  0f, 10f);

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

            // Combat — SMG-style pellet rifle. Defaults tuned for a
            // 220 m arena: 80 m/s pellets are visibly trackable
            // (~2.75 s flight time edge-to-edge), 12 rps reads as a
            // light SMG burst, 1.2° spread gives bullet drift over a
            // 50 m engagement without making close range cone-of-fail.
            // 25 dmg direct hit kills a 100 HP cube in 4 shots.
            Register(SmgFireRate,       "Combat",   "Fire Rate (rps)",   12.0f,  4f,  25f);
            Register(SmgMuzzleSpeed,    "Combat",   "Muzzle Speed (m/s)",80.0f, 30f, 200f);
            Register(SmgSpread,         "Combat",   "Spread (deg)",       1.2f,  0f,   6f);
            Register(SmgDamage,         "Combat",   "Direct Damage",     25.0f,  1f, 100f);

            // Bomb bay. One bomb every 1.2 s reads as a heavy ordnance
            // cadence (vs the SMG's 12 rps). 18 m radius covers ~1.5
            // chassis lengths so it kills lightly-armoured ground targets
            // in one hit while still letting tanks survive a near-miss.
            // 80 dmg at the centre cell drops a 100 HP cube, falloff in
            // splash rings handles edge damage.
            Register(BombDropInterval,  "Combat",   "Bomb Drop Interval", 1.2f,  0.3f, 5f);
            Register(BombDamage,        "Combat",   "Bomb Damage",       80.0f,  10f, 300f);
            Register(BombRadius,        "Combat",   "Bomb Radius (m)",   18.0f,   3f,  60f);
            Register(BombInitialSpeed,  "Combat",   "Bomb Initial Speed", 2.0f,   0f,  20f);

            Load();
        }

        private static void Register(string key, string group, string label, float def, float min, float max)
        {
            var s = new Spec(key, group, label, def, min, max);
            _specs.Add(s);
            _byKey[key] = s;
            _values[key] = def;
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
                    _values[k] = Mathf.Clamp(doc.values[i], s.Min, s.Max);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Tweakables] Load failed: {e.Message}");
            }
        }
    }
}
