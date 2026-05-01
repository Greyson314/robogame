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

        public const string GroundAccel        = "Ground.Acceleration";
        public const string GroundMaxSpeed     = "Ground.MaxSpeed";
        public const string GroundTurnRate     = "Ground.TurnRate";

        public const string ChassisLinDamp     = "Chassis.LinearDamping";
        public const string ChassisAngDamp     = "Chassis.AngularDamping";

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
            Register(ThrusterMaxThrust, "Thruster", "Max Thrust",       155.0f, 50f, 400f);
            Register(ThrusterIdle,      "Thruster", "Idle Throttle",      0.4f,  0f,   1f);
            Register(ThrusterResponse,  "Thruster", "Throttle Response",  2.6f,  0.5f, 10f);

            // Ground drive.
            Register(GroundAccel,       "Ground",   "Acceleration",      26.25f, 5f, 80f);
            Register(GroundMaxSpeed,    "Ground",   "Max Speed",         13.5f,  3f, 40f);
            Register(GroundTurnRate,    "Ground",   "Turn Rate",          7.5f,  1f, 20f);

            // Chassis-level rigidbody damping (applied live in RobotDrive).
            Register(ChassisLinDamp,    "Chassis",  "Linear Damping",     0.2f,  0f,  4f);
            Register(ChassisAngDamp,    "Chassis",  "Angular Damping",    2.0f,  0f, 10f);

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
