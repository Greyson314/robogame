using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Robogame.Core
{
    /// <summary>
    /// Authored mapping from <see cref="AudioCue"/> → clip + mix bus +
    /// spatialisation. The single instance lives at
    /// <c>Resources/AudioCueLibrary.asset</c> so any runtime code can
    /// fetch it via <see cref="Load"/> without scene-time wiring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bones, not music.</b> The asset is shipped empty by design —
    /// every cue starts with a null clip. Authoring real clips happens
    /// in the audio pass; until then <see cref="AudioRouter.PlayOneShot"/>
    /// hits the missing-cue logger and falls through silently.
    /// </para>
    /// <para>
    /// Why not <see cref="UnityEngine.Audio.AudioMixerSnapshot"/> per
    /// cue? Snapshots are for whole-scene mood transitions ("dive into
    /// menu music, duck SFX 6 dB"), not per-shot routing. Each cue
    /// gets a fixed bus and the mixer handles snapshots independently
    /// when those land.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Audio Cue Library", fileName = "AudioCueLibrary")]
    public sealed class AudioCueLibrary : ScriptableObject
    {
        public const string ResourcePath = "AudioCueLibrary";

        [Serializable]
        public sealed class Entry
        {
            public AudioCue Cue;
            public AudioClip Clip;
            public AudioBus Bus = AudioBus.Sfx;
            [Tooltip("0 = pure 2D, 1 = full 3D positional. UI / music = 0; combat = 1.")]
            [Range(0f, 1f)] public float SpatialBlend = 1f;
            [Tooltip("Per-cue volume multiplier (linear). Stacks on top of the mix bus.")]
            [Range(0f, 2f)] public float Volume = 1f;
            [Tooltip("Random ±pitch jitter applied at play time. 0 = no jitter; 0.1 = ±10%.")]
            [Range(0f, 0.5f)] public float PitchJitter = 0.05f;
            [Tooltip("Only one instance of this cue can be alive at a time (prevents stuttering on rapid-fire SMG hits).")]
            public bool Solo;
        }

        [Tooltip("One row per AudioCue value. Empty rows are valid — the cue will be a silent no-op until a clip is wired.")]
        [SerializeField] private List<Entry> _entries = new();

        [Tooltip("Optional: AudioMixerGroups for the four buses. AudioRouter pulls these via Mixer parameters; this is here for editor inspection only.")]
        [SerializeField] private AudioMixerGroup _masterGroup;
        [SerializeField] private AudioMixerGroup _sfxGroup;
        [SerializeField] private AudioMixerGroup _musicGroup;
        [SerializeField] private AudioMixerGroup _uiGroup;

        public IReadOnlyList<Entry> Entries => _entries;
        public AudioMixerGroup GetGroup(AudioBus bus) => bus switch
        {
            AudioBus.Master => _masterGroup,
            AudioBus.Sfx    => _sfxGroup,
            AudioBus.Music  => _musicGroup,
            AudioBus.UI     => _uiGroup,
            _               => _sfxGroup,
        };

        /// <summary>
        /// First-match lookup. Returns null when no entry exists for
        /// the cue — the caller's missing-cue logging path handles it.
        /// </summary>
        public Entry Find(AudioCue cue)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i] != null && _entries[i].Cue == cue) return _entries[i];
            }
            return null;
        }

        // -----------------------------------------------------------------

        private static AudioCueLibrary s_cached;

        public static AudioCueLibrary Load()
        {
            if (s_cached != null) return s_cached;
            s_cached = Resources.Load<AudioCueLibrary>(ResourcePath);
            // Don't warn here — AudioRouter handles "library missing"
            // by falling through to the no-op stub. A missing library
            // pre-audio-pass is the expected state.
            return s_cached;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => s_cached = null;
    }
}
