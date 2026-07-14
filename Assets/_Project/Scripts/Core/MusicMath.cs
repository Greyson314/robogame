using System;

namespace Robogame.Core
{
    /// <summary>
    /// Pure beat-grid math for the combat music system (ADR-0006).
    /// Static and Unity-free so the quantisation logic is unit-testable
    /// without an <c>AudioSource</c> in sight. All times are DSP-clock
    /// seconds (<c>AudioSettings.dspTime</c> domain) — never mix with
    /// <c>Time.time</c>, which is frame-driven and drifts against the
    /// audio thread.
    /// </summary>
    public static class MusicMath
    {
        /// <summary>
        /// Next quantised slot on a beat grid. The grid starts at
        /// <paramref name="startDsp"/> (the track's scheduled first
        /// sample), repeats every <paramref name="subdivisionBeats"/>,
        /// and is shifted by <paramref name="offsetBeats"/> — an
        /// off-beat 8th is <c>subdivision 1, offset 0.5</c>.
        /// Returns the earliest slot at or after
        /// <paramref name="nowDsp"/> + <paramref name="minLeadSeconds"/>;
        /// the lead exists because scheduling "now" on the audio thread
        /// is already too late (needs ≥ one DSP buffer of headroom).
        /// </summary>
        public static double NextSlot(
            double startDsp, double nowDsp, double secondsPerBeat,
            double subdivisionBeats, double offsetBeats, double minLeadSeconds)
        {
            if (secondsPerBeat <= 0 || subdivisionBeats <= 0)
                throw new ArgumentOutOfRangeException(nameof(secondsPerBeat));

            double period = subdivisionBeats * secondsPerBeat;
            double gridOrigin = startDsp + offsetBeats * secondsPerBeat;
            double earliest = nowDsp + minLeadSeconds;

            // Epsilon guards the exactly-on-slot case: seconds-per-beat
            // values like 0.6 aren't exact in binary, so the division
            // can land 1 ulp above an integer and Ceiling would skip a
            // whole slot. Being 1 ns "late" is inaudible; being a full
            // subdivision late is a missed beat.
            const double epsilon = 1e-9;
            double k = Math.Ceiling((earliest - gridOrigin) / period - epsilon);
            if (k < 0) k = 0;   // track scheduled in the future — first slot is the origin
            return gridOrigin + k * period;
        }

        /// <summary>Stinger intensity tiers, ordered by weight.</summary>
        public enum StingerTier { Note = 0, Flourish = 1, Phrase = 2 }

        /// <summary>
        /// Accumulated damage at or above this within one quantise
        /// window upgrades a Note to a Flourish. Nominal-damage scale
        /// (SMG pellet ≈ 4–8, cannonball ≈ 60).
        /// </summary>
        public const float FlourishDamageThreshold = 30f;

        /// <summary>
        /// Tier for an accumulated hit window. A kill always lands the
        /// full Phrase; otherwise the window's total nominal damage
        /// decides Note vs Flourish.
        /// </summary>
        public static StingerTier TierFor(float windowDamage, bool killed)
        {
            if (killed) return StingerTier.Phrase;
            return windowDamage >= FlourishDamageThreshold
                ? StingerTier.Flourish
                : StingerTier.Note;
        }
    }
}
