using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Authored metadata for one combat backing track (ADR-0006): the
    /// clip plus the beat-grid facts <see cref="MusicConductor"/> needs
    /// to quantise stingers against it. Lives in <c>Resources/Music/</c>
    /// so the conductor can load it by path without scene wiring —
    /// same pattern as <see cref="AudioCueLibrary"/>.
    /// </summary>
    /// <remarks>
    /// Authoring contract: the clip must be rendered at exactly
    /// <c>Bars × BeatsPerBar × (60/Bpm)</c> seconds so the loop seam
    /// preserves the beat grid, and its tonal material must sit on the
    /// project's global scale root (see <see cref="MusicalSfx"/> —
    /// stinger clips are recorded at the same root, so consonance is
    /// by construction, the Sea-of-Thieves trick).
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Music Track", fileName = "MusicTrack")]
    public sealed class MusicTrackDefinition : ScriptableObject
    {
        public const string CombatTrackResourcePath = "Music/CombatTrack";

        public AudioClip Clip;

        /// <summary>
        /// One intensity-driven FMOD stem. The fade window maps combat
        /// intensity to layer gain via <see cref="MusicMath.LayerGain"/>:
        /// equal endpoints → always full (the bed), start &lt; end →
        /// fades in across the window, start &gt; end → fades OUT
        /// (calm layers that duck when a fight starts).
        /// </summary>
        [System.Serializable]
        public struct Stem
        {
            [Tooltip("StreamingAssets-relative WAV, rendered at exactly the Clip's length.")]
            public string File;

            [Tooltip("Intensity where this layer's fade begins. Greater than Fade End → the layer fades OUT.")]
            public float FadeStart;

            [Tooltip("Intensity where the fade completes. Equal to Fade Start → always at full gain (the bed).")]
            public float FadeEnd;
        }

        [Tooltip("Intensity-layer stems for the FMOD Core backend. When present and readable, " +
                 "these play sample-synced instead of the single Clip. Empty → Unity AudioSource fallback.")]
        public Stem[] Stems;

        /// <summary>
        /// Top of this track's intensity range — the largest fade
        /// endpoint over all stems (0 when the track has no stems).
        /// <see cref="MusicConductor.SetIntensity"/> clamps here so the
        /// range is authored per-track, not hard-coded.
        /// </summary>
        public float MaxIntensity
        {
            get
            {
                float max = 0f;
                if (Stems != null)
                    for (int i = 0; i < Stems.Length; i++)
                        max = Mathf.Max(max, Mathf.Max(Stems[i].FadeStart, Stems[i].FadeEnd));
                return max;
            }
        }

        [Tooltip("Authored tempo. The beat grid is derived from this + the DSP start time; it is never re-measured from playback.")]
        [Min(20f)] public float Bpm = 100f;

        [Tooltip("Beats per bar — downbeat spacing for bar-quantised stingers.")]
        [Min(1)] public int BeatsPerBar = 4;

        [Tooltip("Track gain before the Music bus / master / mute chain.")]
        [Range(0f, 1f)] public float Volume = 0.55f;
    }
}
