using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Composite UI voicings that pitch the soundfont-rendered stinger
    /// notes (recorded at the score's root — the game lives in D minor)
    /// instead of shipping new clips. Simple one-shot cues go straight
    /// through <see cref="AudioRouter.PlayUI"/>; these are the few that are
    /// chords, sequences, or layered pairs.
    /// </summary>
    /// <remarks>
    /// Pitch multipliers are equal-temperament intervals over the clip's
    /// recorded root: minor third 2^(3/12), perfect fifth 2^(7/12), octave
    /// down 0.5. Scheduling rides <see cref="AudioRouter.PlayScheduled"/>'s
    /// dsp clock, same path the musical damage stingers use.
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: D-minor cue palette.
    public static class UiCues
    {
        private const float MinorThird = 1.1892071f;   // 2^(3/12)
        private const float PerfectFifth = 1.4983071f; // 2^(7/12)

        /// <summary>
        /// The primary-action flourish (Begin, Save): felt-piano D–F–A,
        /// 45 ms apart, quiet — "the machine starts".
        /// </summary>
        public static void Confirm()
        {
            double t = AudioSettings.dspTime + 0.01;
            AudioRouter.PlayScheduled(AudioCue.StingerPianoNote, t,           1f,           0.55f);
            AudioRouter.PlayScheduled(AudioCue.StingerPianoNote, t + 0.045,  MinorThird,   0.45f);
            AudioRouter.PlayScheduled(AudioCue.StingerPianoNote, t + 0.090,  PerfectFifth, 0.50f);
        }

        /// <summary>Wax seal / splat: the stamp thud with a soft piano root under it.</summary>
        public static void Seal()
        {
            AudioRouter.PlayUI(AudioCue.UiSealStamp);
            AudioRouter.PlayScheduled(AudioCue.StingerPianoNote, AudioSettings.dspTime + 0.02, 1f, 0.30f);
        }

        /// <summary>Page-wipe launch: the brush swish. Call <see cref="PageTurnLand"/> when the cover lands.</summary>
        public static void PageTurn() => AudioRouter.PlayUI(AudioCue.UiPageTurn);

        /// <summary>The timpani touch (octave-down root, pp) as the ink cover reaches full.</summary>
        public static void PageTurnLand()
            => AudioRouter.PlayScheduled(AudioCue.StingerTimpaniNote, AudioSettings.dspTime + 0.01, 0.5f, 0.40f);
    }
}
