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

        [Tooltip("Intensity-layer stems, StreamingAssets-relative, quietest first (bed, +layer2, +layer3). " +
                 "When present and readable, FMOD Core plays these sample-synced instead of the single Clip; " +
                 "each must be rendered at exactly the same length as the Clip. Empty → Unity AudioSource fallback.")]
        public string[] StemFiles;

        [Tooltip("Authored tempo. The beat grid is derived from this + the DSP start time; it is never re-measured from playback.")]
        [Min(20f)] public float Bpm = 100f;

        [Tooltip("Beats per bar — downbeat spacing for bar-quantised stingers.")]
        [Min(1)] public int BeatsPerBar = 4;

        [Tooltip("Track gain before the Music bus / master / mute chain.")]
        [Range(0f, 1f)] public float Volume = 0.55f;
    }
}
