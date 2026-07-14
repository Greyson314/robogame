using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Note-selection policy for an instrument-voiced cue. Stored per
    /// entry in <see cref="AudioCueLibrary"/>; <see cref="MusicalSfx"/>
    /// turns the policy into a pitch multiplier at play time.
    /// </summary>
    public enum MusicalPhrase : byte
    {
        /// <summary>Not musical — the cue keeps its random pitch jitter.</summary>
        None = 0,
        /// <summary>Each shot lands on a random pentatonic degree.</summary>
        ScaleRandom = 1,
        /// <summary>Rapid repeats climb the scale — a volley becomes an
        /// ascending run; a quiet gap resets to the root.</summary>
        ArpeggioUp = 2,
        /// <summary>Root / fifth / octave only — consonant against
        /// anything else that's sounding. Impacts and landings.</summary>
        ChordTone = 3,
    }

    /// <summary>
    /// Pitch selection for instrument-voiced cues — the inventor-
    /// aesthetic audio direction (docs/research/inventor-aesthetic.md):
    /// a mortar volley reads as a piano flourish on shot + landing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is note <i>selection</i>, not synthesis (audio.md forbids
    /// procedural synthesis; pitch-shifting an authored clip is already
    /// sanctioned for engine loops). The authored clip is recorded at
    /// the scale root; multipliers stay within one octave, inside the
    /// range where repitching doesn't audibly chipmunk an instrument
    /// sample.
    /// </para>
    /// <para>
    /// Everything is pinned to one global major-pentatonic scale, so
    /// any two musical cues sounding together are consonant by
    /// construction — the cheap trick that makes 16 chassis firing at
    /// once read as texture instead of tone soup. Phrase state is
    /// client-local and cosmetic; clients may hear different runs for
    /// the same volley and that's fine (INV-3 untouched — nothing here
    /// feeds back into gameplay).
    /// </para>
    /// <para>
    /// Zero allocations at steady state: fixed arrays indexed by cue.
    /// </para>
    /// </remarks>
    public static class MusicalSfx
    {
        /// <summary>Repeats inside this window advance an ArpeggioUp
        /// run; a longer gap resets the run to the root.</summary>
        public const float VolleyWindowSeconds = 1.2f;

        // Major pentatonic degrees as semitone offsets from the root,
        // capped at the octave. Precomputed to pitch multipliers.
        private static readonly float[] s_scale =
        {
            1f,          // 0  root
            1.12246f,    // 2  major second
            1.25992f,    // 4  major third
            1.49831f,    // 7  fifth
            1.68179f,    // 9  major sixth
            2f,          // 12 octave
        };

        private static readonly float[] s_chordTones = { 1f, 1.49831f, 2f };

        private static readonly int s_cueCount =
            System.Enum.GetValues(typeof(AudioCue)).Length;

        // TRACE[DOC:audio.md]: statics survive domain reload — reset below.
        private static int[] s_step = new int[s_cueCount];
        private static float[] s_lastPlay = new float[s_cueCount];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_step = new int[s_cueCount];
            s_lastPlay = new float[s_cueCount];
        }

        /// <summary>Number of degrees on the global scale (including the octave).</summary>
        public static int ScaleSteps => s_scale.Length;

        /// <summary>
        /// Pitch multiplier for a specific scale degree — for callers
        /// (e.g. the stinger director, ADR-0006) that walk the
        /// pentatonic deliberately instead of via a per-cue policy.
        /// Steps clamp to the octave.
        /// </summary>
        public static float ScalePitch(int step)
        {
            if (step < 0) step = 0;
            if (step >= s_scale.Length) step = s_scale.Length - 1;
            return s_scale[step];
        }

        /// <summary>
        /// Pitch multiplier for the next play of a musical cue. Callers
        /// with <see cref="MusicalPhrase.None"/> should not be here —
        /// they keep the jitter path.
        /// </summary>
        public static float NextPitch(AudioCue cue, MusicalPhrase phrase)
        {
            switch (phrase)
            {
                case MusicalPhrase.ScaleRandom:
                    return s_scale[Random.Range(0, s_scale.Length)];

                case MusicalPhrase.ChordTone:
                    return s_chordTones[Random.Range(0, s_chordTones.Length)];

                case MusicalPhrase.ArpeggioUp:
                {
                    int i = (int)cue;
                    float now = Time.unscaledTime;
                    if (now - s_lastPlay[i] > VolleyWindowSeconds) s_step[i] = 0;
                    s_lastPlay[i] = now;
                    float pitch = s_scale[s_step[i]];
                    s_step[i] = (s_step[i] + 1) % s_scale.Length;
                    return pitch;
                }

                default:
                    return 1f;
            }
        }
    }
}
