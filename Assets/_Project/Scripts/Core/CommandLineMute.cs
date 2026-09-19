using System;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// A command-line flag mutes every first-party audio path (factory
    /// finding F-062, docs/loop/FINDINGS.md)
    /// so a headless player run (LAUNCH-READINESS B3) never plays the
    /// game's music or SFX on the operator's speakers. Deliberately not a
    /// Tweakable (INV-1): no player passes this flag, so unlike
    /// <see cref="Tweakables.AudioMute"/> it never reaches the player's
    /// saved tweakables.json.
    /// </summary>
    public static class CommandLineMute
    {
        private const string Flag = "-factory-mute";

        private static bool? s_active;

        // Test seam only: CommandLineMuteAudioTests sets this via
        // reflection to prove the flag reaches MusicConductor/AudioRouter's
        // volume math without needing a real command line. Shipped code
        // never touches it.
        internal static bool? s_forcedForTests;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
            s_forcedForTests = null;
        }

        /// <summary>
        /// True for the life of the process once the real command line has
        /// been read. Parsed once and cached — the flag can't change
        /// mid-run, so callers never pay a per-call array scan.
        /// </summary>
        public static bool Active
        {
            get
            {
                if (s_forcedForTests.HasValue) return s_forcedForTests.Value;
                if (!s_active.HasValue)
                {
                    s_active = Parse(Environment.GetCommandLineArgs());
                    // One line in Player.log is the run's own proof the flag was seen.
                    if (s_active.Value) Debug.Log("[CommandLineMute] " + Flag + " active: first-party audio is muted for this run.");
                }
                return s_active.Value;
            }
        }

        /// <summary>
        /// Testable seam: true when <paramref name="args"/> carries the
        /// flag as a whole token (case-insensitive; a longer or shorter
        /// token such as "-factory-muted" does not match).
        /// </summary>
        public static bool Parse(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
