using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Core
{
    /// <summary>
    /// Owns the combat backing track and its beat grid (ADR-0006,
    /// ADR-0007). Two backends:
    /// <list type="bullet">
    /// <item><b>FMOD Core</b> (preferred): the track's intensity-layer
    /// stems play as sample-synced FMOD channels. FMOD mixes on its own
    /// DSP clock, so the grid anchor is mapped into
    /// <see cref="AudioSettings.dspTime"/> through a
    /// <see cref="MusicClock"/> offset estimator fed once per frame —
    /// the "two-clocks" bridge. <see cref="SetIntensity"/> fades layers
    /// in and out.</item>
    /// <item><b>Unity fallback</b>: the single authored clip via
    /// <see cref="AudioSource.PlayScheduled"/>, grid anchored directly
    /// on the scheduled start — v1 behaviour, used when stems or the
    /// FMOD runtime are unavailable.</item>
    /// </list>
    /// Consumers only ever see <see cref="NextSlotDsp"/>, in dsp-time,
    /// regardless of backend.
    /// </summary>
    /// <remarks>
    /// Client-side cosmetic only — quantisation delays *presentation*
    /// of a hit, never its gameplay state (INV-3 untouched). The FMOD
    /// path bypasses the Music AudioMixer bus, so Tweakables volume is
    /// applied straight onto the FMOD channel group instead.
    /// </remarks>
    public sealed class MusicConductor : MonoBehaviour
    {
        private static MusicConductor s_instance;
        private static bool s_loggedMissingTrack;
        private static bool s_loggedFmodFallback;

        // TRACE[ADR-0006]: grid facts survive a mid-play domain reload via
        // serialization on the Unity path. FMOD handles cannot survive a
        // reload, so the FMOD path restarts the track instead (see OnEnable).
        [SerializeField] private double _startDsp;
        [SerializeField] private float _secondsPerBeat;
        [SerializeField] private int _beatsPerBar = 4;
        [SerializeField] private float _trackVolume = 0.55f;
        [SerializeField] private bool _playing;
        [SerializeField] private bool _fmodMode;

        private AudioSource _source;

        // TRACE[ADR-0007]: FMOD Core owns stem playback; native handles,
        // never serialized. All stems start paused on the same channel
        // group and are released by a shared setDelay tick, so they are
        // sample-locked to each other by construction.
        [NonSerialized] private FMOD.ChannelGroup _fmodGroup;
        [NonSerialized] private FMOD.ChannelGroup _fmodMaster;
        [NonSerialized] private FMOD.Sound[] _fmodSounds;
        [NonSerialized] private FMOD.Channel[] _fmodChannels;
        [NonSerialized] private bool _fmodHandlesLive;
        private ulong _fmodStartClock;      // master-clock samples of the first stem sample
        private int _fmodRate;
        private readonly MusicClock _clock = new MusicClock();

        // Per-stem intensity fade windows, copied from the track
        // definition at start (MusicMath.LayerGain semantics). Native-
        // handle lifetime — rebuilt with the channels, never serialized.
        [NonSerialized] private float[] _fadeStart;
        [NonSerialized] private float[] _fadeEnd;
        [SerializeField] private float _maxIntensity = 2f;

        private float _intensity;           // smoothed, 0..2
        private float _intensityTarget;
#if UNITY_EDITOR
        private bool _editorMuted;
#endif
        private const float IntensityRiseSpeed = 1.5f;   // /s — escalate fast
        private const float IntensityFallSpeed = 0.35f;  // /s — cool down slow

        // -----------------------------------------------------------------
        // Bootstrap
        // -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_loggedMissingTrack = false;
            s_loggedFmodFallback = false;
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

            // Domain reload while FMOD was playing: the native channels
            // are orphaned (their managed handles died with the domain).
            // Restart cleanly rather than pretending the old grid holds.
            if (_playing && _fmodMode && !_fmodHandlesLive)
            {
                _playing = false;
                _fmodMode = false;
                StartCombatTrack();
            }
        }

        private void OnDisable()
        {
            Tweakables.Changed -= ApplyVolume;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        private void OnDestroy()
        {
            ReleaseFmod();
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

        /// <summary>Track playing and grid queries valid (FMOD mode: once the clock bridge has warmed up).</summary>
        public static bool IsPlaying => s_instance != null && s_instance._playing;

        /// <summary>Beats per bar of the running track (downbeat spacing).</summary>
        public static int BeatsPerBar => s_instance != null ? s_instance._beatsPerBar : 4;

        /// <summary>True when the FMOD stem backend is driving the track.</summary>
        public static bool FmodActive => s_instance != null && s_instance._playing && s_instance._fmodMode;

        /// <summary>
        /// Combat heat → per-stem layer fades, clamped to
        /// [0, <see cref="MusicTrackDefinition.MaxIntensity"/>] of the
        /// running track. Each stem's authored fade window maps
        /// intensity to gain (<see cref="MusicMath.LayerGain"/> —
        /// risers fade in, calm layers fade out). Smoothed internally
        /// (fast rise, slow fall). No-op on the Unity fallback backend.
        /// </summary>
        public static void SetIntensity(float intensity)
        {
            if (s_instance != null)
                s_instance._intensityTarget = Mathf.Clamp(intensity, 0f, s_instance._maxIntensity);
        }

        /// <summary>
        /// Load the combat track definition from
        /// <c>Resources/Music/CombatTrack</c> and start it. Missing
        /// asset is a silent no-op (logged once) — pre-audio-pass
        /// behaviour identical to a missing <see cref="AudioCue"/> clip.
        /// </summary>
        public static void StartCombatTrack()
        {
            var def = Resources.Load<MusicTrackDefinition>(MusicTrackDefinition.CombatTrackResourcePath);
            if (def == null || (def.Clip == null && (def.Stems == null || def.Stems.Length == 0)))
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
        /// track is playing, or while the FMOD clock bridge is still
        /// warming up (callers should skip the stinger, not play it
        /// unquantised — silence beats off-grid noise).
        /// </summary>
        public static double NextSlotDsp(double subdivisionBeats, double offsetBeats, double minLeadSeconds)
        {
            if (!IsPlaying) return -1.0;
            MusicConductor c = s_instance;
            double startDsp;
            if (c._fmodMode)
            {
                if (!c._clock.Ready) return -1.0;
                startDsp = c._clock.ToTarget((double)c._fmodStartClock / c._fmodRate);
            }
            else
            {
                startDsp = c._startDsp;
            }
            return MusicMath.NextSlot(
                startDsp, AudioSettings.dspTime, c._secondsPerBeat,
                subdivisionBeats, offsetBeats, minLeadSeconds);
        }

        // -----------------------------------------------------------------
        // Per-frame: clock bridge + intensity fades (FMOD mode only)
        // -----------------------------------------------------------------

        private void Update()
        {
            if (!_playing || !_fmodMode || !_fmodHandlesLive) return;

#if UNITY_EDITOR
            // Mirror the Game view's Mute Audio button onto the FMOD group.
            // FMOD's native output bypasses Unity's mute entirely, and the
            // integration's own mirror (RuntimeManager) needs a Studio master
            // bank we don't load (ADR-0007 runs bank-less Core channels).
            bool muted = UnityEditor.EditorUtility.audioMasterMute;
            if (muted != _editorMuted)
            {
                _editorMuted = muted;
                _fmodGroup.setMute(muted);
            }
#endif

            ulong clock, parent;
            if (_fmodMaster.getDSPClock(out clock, out parent) == FMOD.RESULT.OK)
                _clock.AddSample((double)clock / _fmodRate, AudioSettings.dspTime);

            float speed = _intensityTarget > _intensity ? IntensityRiseSpeed : IntensityFallSpeed;
            _intensity = Mathf.MoveTowards(_intensity, _intensityTarget, speed * Time.unscaledDeltaTime);
            for (int i = 0; i < _fmodChannels.Length; i++)
                _fmodChannels[i].setVolume(MusicMath.LayerGain(_intensity, _fadeStart[i], _fadeEnd[i]));
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private void StartTrackInternal(MusicTrackDefinition def)
        {
            EnsureSource();
            if (_playing && _fmodMode && _fmodHandlesLive) return;          // rebind — keep the grid
            if (_playing && !_fmodMode && _source.clip == def.Clip && def.Clip != null) return;

            StopTrackInternal();

            _secondsPerBeat = 60f / def.Bpm;
            _beatsPerBar = def.BeatsPerBar;
            _trackVolume = def.Volume;
            _maxIntensity = def.MaxIntensity;

            if (def.Stems != null && def.Stems.Length > 0 && TryStartFmod(def))
            {
                _fmodMode = true;
                _playing = true;
                ApplyVolume();
                return;
            }

            if (def.Clip == null) return;   // stems failed and no fallback clip

            _fmodMode = false;
            _source.clip = def.Clip;
            ApplyVolume();

            // One buffer of lead would do; a fixed 0.25 s absorbs the
            // first-play clip load without an audible gap at scene start.
            _startDsp = AudioSettings.dspTime + 0.25;
            _source.PlayScheduled(_startDsp);
            _playing = true;
        }

        private bool TryStartFmod(MusicTrackDefinition def)
        {
            try
            {
                string root = Application.streamingAssetsPath;
                for (int i = 0; i < def.Stems.Length; i++)
                    if (!File.Exists(Path.Combine(root, def.Stems[i].File)))
                        return FmodFallback("stem missing: " + def.Stems[i].File);

                FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;
                FMOD.SPEAKERMODE mode; int raw;
                core.getSoftwareFormat(out _fmodRate, out mode, out raw);
                if (core.getMasterChannelGroup(out _fmodMaster) != FMOD.RESULT.OK)
                    return FmodFallback("no master channel group");
                if (core.createChannelGroup("RobogameMusic", out _fmodGroup) != FMOD.RESULT.OK)
                    return FmodFallback("createChannelGroup failed");
                _fmodMaster.addGroup(_fmodGroup);

                int n = def.Stems.Length;
                _fmodSounds = new FMOD.Sound[n];
                _fmodChannels = new FMOD.Channel[n];
                _fadeStart = new float[n];
                _fadeEnd = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _fadeStart[i] = def.Stems[i].FadeStart;
                    _fadeEnd[i] = def.Stems[i].FadeEnd;
                    string path = Path.Combine(root, def.Stems[i].File);
                    FMOD.RESULT r = core.createSound(path,
                        FMOD.MODE.LOOP_NORMAL | FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D,
                        out _fmodSounds[i]);
                    if (r != FMOD.RESULT.OK) { ReleaseFmod(); return FmodFallback("createSound " + r); }
                    r = core.playSound(_fmodSounds[i], _fmodGroup, true, out _fmodChannels[i]);
                    if (r != FMOD.RESULT.OK) { ReleaseFmod(); return FmodFallback("playSound " + r); }
                }

                // Sample-synced start: every stem is released by the same
                // master-clock tick, a fixed lead ahead of "now".
                ulong clock, parent;
                _fmodGroup.getDSPClock(out clock, out parent);
                _fmodStartClock = parent + (ulong)(0.25 * _fmodRate);
                for (int i = 0; i < n; i++)
                {
                    _fmodChannels[i].setDelay(_fmodStartClock, 0, false);
                    _fmodChannels[i].setVolume(MusicMath.LayerGain(0f, _fadeStart[i], _fadeEnd[i]));
                    _fmodChannels[i].setPaused(false);
                }

                _clock.Reset();
                _intensity = 0f;
                _intensityTarget = 0f;
                _fmodHandlesLive = true;
                return true;
            }
            catch (Exception e)
            {
                ReleaseFmod();
                return FmodFallback(e.Message);
            }
        }

        private static bool FmodFallback(string reason)
        {
            if (!s_loggedFmodFallback)
            {
                s_loggedFmodFallback = true;
                Debug.LogWarning("[MusicConductor] FMOD stem backend unavailable (" + reason +
                                 ") — falling back to the Unity AudioSource track.");
            }
            return false;
        }

        private void StopTrackInternal()
        {
            if (_source != null) _source.Stop();
            ReleaseFmod();
            _playing = false;
            _fmodMode = false;
        }

        private void ReleaseFmod()
        {
            if (!_fmodHandlesLive) return;
            _fmodHandlesLive = false;
            try
            {
                if (_fmodChannels != null)
                    for (int i = 0; i < _fmodChannels.Length; i++)
                        _fmodChannels[i].stop();
                if (_fmodSounds != null)
                    for (int i = 0; i < _fmodSounds.Length; i++)
                        _fmodSounds[i].release();
                _fmodGroup.release();
            }
            catch (Exception)
            {
                // Editor shutdown / FMOD already torn down — nothing to release.
            }
            _fmodChannels = null;
            _fmodSounds = null;
        }

        private void ApplyVolume()
        {
            float music = Tweakables.Get(Tweakables.AudioMusic);
            float master = Tweakables.Get(Tweakables.AudioMaster);
            float mute = Tweakables.GetBool(Tweakables.AudioMute) ? 0f : 1f;
            float v = Mathf.Clamp01(_trackVolume * music * master * mute);
            // TRACE[ADR-0007]: FMOD bypasses the Music AudioMixer bus, so the
            // Tweakables chain lands on the channel group instead.
            if (_fmodHandlesLive) _fmodGroup.setVolume(v);
            if (_source != null) _source.volume = v;
        }
    }
}
