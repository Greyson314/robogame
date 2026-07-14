using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Core
{
    /// <summary>
    /// Owns the combat backing track and its beat grid (ADR-0006).
    /// The track is started with <see cref="AudioSource.PlayScheduled"/>
    /// so its first sample lands on a known DSP time; every beat
    /// position after that is arithmetic on
    /// <see cref="AudioSettings.dspTime"/> — never polled from
    /// playback position, never touched by <c>Time.time</c>.
    /// <see cref="MusicalHitDirector"/>-style consumers ask
    /// <see cref="NextSlotDsp"/> for quantised stinger slots.
    /// </summary>
    /// <remarks>
    /// Client-side cosmetic only — quantisation delays *presentation*
    /// of a hit, never its gameplay state (INV-3 untouched). Bootstrap,
    /// domain-reload adoption and Tweakables-driven volume all mirror
    /// <see cref="AudioRouter"/>.
    /// </remarks>
    public sealed class MusicConductor : MonoBehaviour
    {
        private static MusicConductor s_instance;
        private static bool s_loggedMissingTrack;

        // TRACE[ADR-0006]: grid facts survive a mid-play domain reload via
        // serialization — the surviving AudioSource keeps playing, and a
        // re-derived _startDsp would guess; these fields don't.
        [SerializeField] private double _startDsp;
        [SerializeField] private float _secondsPerBeat;
        [SerializeField] private int _beatsPerBar = 4;
        [SerializeField] private float _trackVolume = 0.55f;
        [SerializeField] private bool _playing;

        private AudioSource _source;

        // -----------------------------------------------------------------
        // Bootstrap
        // -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_loggedMissingTrack = false;
        }

        private static void EnsureBootstrap()
        {
            if (s_instance != null) return;
            s_instance = FindFirstObjectByType<MusicConductor>();
            if (s_instance != null) return;
            var root = new GameObject("[MusicConductor]");
            DontDestroyOnLoad(root);
            s_instance = root.AddComponent<MusicConductor>();
        }

        private void OnEnable()
        {
            EnsureSource();
            Tweakables.Changed += ApplyVolume;
            ApplyVolume();
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private void OnDisable()
        {
            Tweakables.Changed -= ApplyVolume;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        private void EnsureSource()
        {
            if (_source != null) return;
            Transform existing = transform.Find("Track");
            GameObject go = existing != null ? existing.gameObject : new GameObject("Track");
            if (existing == null) go.transform.SetParent(transform, worldPositionStays: false);
            _source = go.GetComponent<AudioSource>();
            if (_source == null) _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;   // music is 2D
            _source.dopplerLevel = 0f;
            AudioCueLibrary lib = AudioCueLibrary.Load();
            if (lib != null) _source.outputAudioMixerGroup = lib.GetGroup(AudioBus.Music);
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            // Arena scenes start the track via ArenaController's bind;
            // anything that leaves the arena (back to garage, next map)
            // stops it rather than leaking combat drums into the menu.
            if (_playing) StopTrackInternal();
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>Track playing and grid queries valid.</summary>
        public static bool IsPlaying => s_instance != null && s_instance._playing;

        /// <summary>Beats per bar of the running track (downbeat spacing).</summary>
        public static int BeatsPerBar => s_instance != null ? s_instance._beatsPerBar : 4;

        /// <summary>
        /// Load the combat track definition from
        /// <c>Resources/Music/CombatTrack</c> and start it. Missing
        /// asset is a silent no-op (logged once) — pre-audio-pass
        /// behaviour identical to a missing <see cref="AudioCue"/> clip.
        /// </summary>
        public static void StartCombatTrack()
        {
            var def = Resources.Load<MusicTrackDefinition>(MusicTrackDefinition.CombatTrackResourcePath);
            if (def == null || def.Clip == null)
            {
                if (!s_loggedMissingTrack)
                {
                    s_loggedMissingTrack = true;
                    Debug.Log("[MusicConductor] No combat track at Resources/" +
                              MusicTrackDefinition.CombatTrackResourcePath +
                              ". Run Robogame → Scaffold → Music → Build Combat Music.");
                }
                return;
            }
            StartTrack(def);
        }

        /// <summary>Start (or restart) the backing track from its first sample.</summary>
        public static void StartTrack(MusicTrackDefinition def)
        {
            EnsureBootstrap();
            s_instance.StartTrackInternal(def);
        }

        public static void StopTrack()
        {
            if (s_instance != null) s_instance.StopTrackInternal();
        }

        /// <summary>
        /// DSP time of the next quantised slot —
        /// <c>subdivisionBeats 1, offsetBeats 0.5</c> is the off-beat
        /// 8th the design defaults to. Returns a negative value when no
        /// track is playing (callers should skip the stinger, not play
        /// it unquantised — silence beats off-grid noise).
        /// </summary>
        public static double NextSlotDsp(double subdivisionBeats, double offsetBeats, double minLeadSeconds)
        {
            if (!IsPlaying) return -1.0;
            MusicConductor c = s_instance;
            return MusicMath.NextSlot(
                c._startDsp, AudioSettings.dspTime, c._secondsPerBeat,
                subdivisionBeats, offsetBeats, minLeadSeconds);
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private void StartTrackInternal(MusicTrackDefinition def)
        {
            EnsureSource();
            if (_playing && _source.clip == def.Clip) return;   // rebind on same track — keep the grid

            _source.Stop();
            _source.clip = def.Clip;
            _secondsPerBeat = 60f / def.Bpm;
            _beatsPerBar = def.BeatsPerBar;
            _trackVolume = def.Volume;
            ApplyVolume();

            // One buffer of lead would do; a fixed 0.25 s absorbs the
            // first-play clip load without an audible gap at scene start.
            _startDsp = AudioSettings.dspTime + 0.25;
            _source.PlayScheduled(_startDsp);
            _playing = true;
        }

        private void StopTrackInternal()
        {
            if (_source != null) _source.Stop();
            _playing = false;
        }

        private void ApplyVolume()
        {
            if (_source == null) return;
            float music = Tweakables.Get(Tweakables.AudioMusic);
            float master = Tweakables.Get(Tweakables.AudioMaster);
            float mute = Tweakables.GetBool(Tweakables.AudioMute) ? 0f : 1f;
            _source.volume = Mathf.Clamp01(_trackVolume * music * master * mute);
        }
    }
}
