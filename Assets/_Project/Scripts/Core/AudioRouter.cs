using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Robogame.Core
{
    /// <summary>
    /// Scene-root singleton that owns the project's audio plumbing —
    /// volume routing from <see cref="Tweakables"/>, an
    /// <see cref="AudioMixer"/> reference (when wired), the pooled
    /// one-shot voice path, and the persistent loop-voice API used by
    /// engine / rotor / wheel cues.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Plumbing model.</b> Two voice pools:
    /// </para>
    /// <list type="bullet">
    /// <item><b>One-shot pool</b> — fixed-size array of
    /// <see cref="AudioSource"/> components on child GameObjects of the
    /// router. <see cref="PlayOneShot(AudioCue, Vector3)"/> claims a
    /// free voice, configures it from the cue entry, plays, and
    /// auto-releases when the clip ends. Cap:
    /// <see cref="MaxConcurrentVoices"/>. Pool exhaustion = drop the
    /// oldest non-solo voice (matches the VFX spawner's eviction rule).</item>
    /// <item><b>Loop voices</b> — allocated on demand via
    /// <see cref="PlayLoop(AudioCue, Transform)"/>, parented to the
    /// caller's transform, returned as an <see cref="AudioLoopHandle"/>
    /// the caller stops explicitly. Engine + rotor sounds live here
    /// because they outlive any reasonable pool turnover.</item>
    /// </list>
    /// <para>
    /// <b>Volume routing without a mixer.</b> v1 ships without an
    /// <see cref="AudioMixer"/> asset (Unity authors mixers only via
    /// the editor; we don't ship a wizard for it yet). Per-bus volumes
    /// are applied on each <see cref="AudioSource"/> directly: live
    /// volume = cueBaseVolume × busMultiplier × masterMultiplier ×
    /// muteGate. <see cref="Tweakables.Changed"/> re-applies to every
    /// live voice so a slider drag is heard immediately.
    /// </para>
    /// <para>
    /// <b>Performance contract.</b>
    /// </para>
    /// <list type="bullet">
    /// <item>Steady-state <see cref="PlayOneShot"/>: zero allocations
    /// once warm. Hot path is one library lookup (linear over &lt;50
    /// entries), one free-list pop, one transform write, one AudioSource
    /// configure + Play.</item>
    /// <item>Solo cues stop any same-cue voice in flight before
    /// claiming a new voice — prevents a held SMG fire from stacking
    /// 24 simultaneous "fire" voices during a 2-second burst.</item>
    /// <item>Loops own their own AudioSource (one allocation per
    /// loop start, one destroy per stop). Per-block enable/disable is
    /// the only churn surface; rotor blocks aren't built per-frame.</item>
    /// </list>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AudioRouter : MonoBehaviour
    {
        public const int MaxConcurrentVoices = 24;
        public const string MixerResourcePath = "AudioMixer";

        // AudioMixer parameter names. Declared once so a future audio
        // pass can rename / re-route by changing this constant rather
        // than spelunking call sites.
        public const string MixerParamMaster = "MasterVol";
        public const string MixerParamSfx    = "SfxVol";
        public const string MixerParamMusic  = "MusicVol";
        public const string MixerParamUI     = "UIVol";

        private static AudioRouter s_instance;
        private static GameObject s_root;

        private AudioMixer _mixer;
        private bool _mixerProbed;

        private AudioCueLibrary _library;
        private bool _libraryProbed;

        // One-shot pool. Each voice is a child GameObject with an
        // AudioSource. Pre-sized to MaxConcurrentVoices; idle voices
        // sit on _freeIndices (Stack of int).
        private AudioSource[] _voices;
        private VoiceState[] _voiceStates;
        private Stack<int> _freeIndices;

        private struct VoiceState
        {
            public bool InUse;
            public AudioCue Cue;
            public AudioBus Bus;
            public float BaseVolume;       // cue.Volume — pre-bus-pre-master
            public float ExpireAt;         // Time.unscaledTime when the source's clip will have finished
        }

        // Loop voices. Each is a separately-owned AudioSource on a
        // child of the caller's transform. Tracked here so volume
        // changes propagate; the handle is what the caller holds.
        private readonly List<AudioLoopHandle> _loops = new(8);

        // De-duped missing-cue logger. Statics survive domain reload —
        // explicit reset.
        private static readonly HashSet<AudioCue> s_loggedMissing = new();

        // -----------------------------------------------------------------
        // Bootstrap
        // -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_root = null;
            s_loggedMissing.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_instance != null) return;
            s_root = new GameObject("[AudioRouter]");
            DontDestroyOnLoad(s_root);
            s_instance = s_root.AddComponent<AudioRouter>();
        }

        private void Awake()
        {
            BuildVoicePool();
        }

        private void OnEnable()
        {
            Tweakables.Changed += ApplyVolumesFromTweakables;
            ApplyVolumesFromTweakables();
        }

        private void OnDisable()
        {
            Tweakables.Changed -= ApplyVolumesFromTweakables;
        }

        private void BuildVoicePool()
        {
            _voices = new AudioSource[MaxConcurrentVoices];
            _voiceStates = new VoiceState[MaxConcurrentVoices];
            _freeIndices = new Stack<int>(MaxConcurrentVoices);
            for (int i = MaxConcurrentVoices - 1; i >= 0; i--)
            {
                var go = new GameObject($"Voice_{i:D2}");
                go.transform.SetParent(transform, worldPositionStays: false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 1f;       // overridden per cue
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 4f;
                src.maxDistance = 80f;
                src.dopplerLevel = 0f;       // arcade — no Doppler
                _voices[i] = src;
                _freeIndices.Push(i);
            }
        }

        // -----------------------------------------------------------------
        // Public API — one-shots
        // -----------------------------------------------------------------

        /// <summary>
        /// Play a one-shot cue at a world position. 3D cues are
        /// spatialised by the cue's <c>SpatialBlend</c>; 2D cues
        /// ignore the position entirely.
        /// </summary>
        public static void PlayOneShot(AudioCue cue, Vector3 worldPosition)
        {
            EnsureBootstrap();
            if (s_instance == null) return;
            s_instance.PlayOneShotInternal(cue, worldPosition);
        }

        /// <summary>UI overload — always 2D, routed through the UI bus.</summary>
        public static void PlayUI(AudioCue cue)
        {
            EnsureBootstrap();
            if (s_instance == null) return;
            // 2D cues don't care about position; the cue's SpatialBlend
            // already stamps it. Pass Vector3.zero.
            s_instance.PlayOneShotInternal(cue, Vector3.zero);
        }

        private void PlayOneShotInternal(AudioCue cue, Vector3 worldPosition)
        {
            AudioCueLibrary.Entry entry = ResolveEntry(cue);
            if (entry == null || entry.Clip == null) { LogMissingOnce(cue); return; }

            // Solo: stop any in-flight voice playing the same cue
            // before claiming a new one. Prevents a held SMG fire from
            // stacking 24 simultaneous fire voices during a burst.
            if (entry.Solo) StopMatchingOneShots(cue);

            int idx = AcquireVoice();
            AudioSource src = _voices[idx];
            ConfigureSource(src, entry);
            src.transform.position = worldPosition;
            src.Play();

            float life = entry.Clip.length / Mathf.Max(0.01f, src.pitch);
            _voiceStates[idx] = new VoiceState
            {
                InUse = true,
                Cue = cue,
                Bus = entry.Bus,
                BaseVolume = entry.Volume,
                ExpireAt = Time.unscaledTime + life + 0.05f,
            };
        }

        private int AcquireVoice()
        {
            if (_freeIndices.Count > 0) return _freeIndices.Pop();

            // Pool exhausted: evict the voice that will expire first
            // (least audible-time-remaining). This is O(MaxConcurrentVoices)
            // worst case, fine at N=24.
            int evict = 0;
            float earliest = float.MaxValue;
            for (int i = 0; i < _voiceStates.Length; i++)
            {
                if (!_voiceStates[i].InUse) return i; // safety net
                if (_voiceStates[i].ExpireAt < earliest)
                {
                    earliest = _voiceStates[i].ExpireAt;
                    evict = i;
                }
            }
            ReleaseVoice(evict, returnToFree: false);
            return evict;
        }

        private void StopMatchingOneShots(AudioCue cue)
        {
            for (int i = 0; i < _voiceStates.Length; i++)
            {
                if (_voiceStates[i].InUse && _voiceStates[i].Cue == cue)
                {
                    _voices[i].Stop();
                    ReleaseVoice(i, returnToFree: true);
                }
            }
        }

        private void ReleaseVoice(int idx, bool returnToFree)
        {
            _voiceStates[idx].InUse = false;
            if (returnToFree) _freeIndices.Push(idx);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _voiceStates.Length; i++)
            {
                if (!_voiceStates[i].InUse) continue;
                if (now >= _voiceStates[i].ExpireAt)
                {
                    if (_voices[i].isPlaying) _voices[i].Stop();
                    ReleaseVoice(i, returnToFree: true);
                }
            }
        }

        // -----------------------------------------------------------------
        // Public API — loops
        // -----------------------------------------------------------------

        /// <summary>
        /// Start a looped voice parented to <paramref name="parent"/>.
        /// The returned handle owns the AudioSource until
        /// <see cref="AudioLoopHandle.Stop"/> is called — the caller
        /// is responsible for stopping the loop on disable / despawn.
        /// </summary>
        /// <remarks>
        /// Loops own their AudioSource separately from the one-shot
        /// pool — they outlive any pool turnover and would otherwise
        /// pin a slot indefinitely. One alloc on start, one destroy on
        /// stop; loops aren't started per frame.
        /// </remarks>
        public static AudioLoopHandle PlayLoop(AudioCue cue, Transform parent)
        {
            EnsureBootstrap();
            if (s_instance == null || parent == null) return AudioLoopHandle.Invalid;
            return s_instance.PlayLoopInternal(cue, parent);
        }

        private AudioLoopHandle PlayLoopInternal(AudioCue cue, Transform parent)
        {
            AudioCueLibrary.Entry entry = ResolveEntry(cue);
            if (entry == null || entry.Clip == null) { LogMissingOnce(cue); return AudioLoopHandle.Invalid; }

            var go = new GameObject($"Loop_{cue}");
            go.transform.SetParent(parent, worldPositionStays: false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = entry.SpatialBlend;
            src.clip = entry.Clip;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 4f;
            src.maxDistance = 80f;
            src.dopplerLevel = 0f;
            src.pitch = 1f;

            var handle = new AudioLoopHandle(this, src, cue, entry.Bus, entry.Volume);
            _loops.Add(handle);
            handle.ReapplyVolume();
            src.Play();
            return handle;
        }

        internal void NotifyLoopStopped(AudioLoopHandle handle)
        {
            _loops.Remove(handle);
        }

        // -----------------------------------------------------------------
        // Library lookup
        // -----------------------------------------------------------------

        private AudioCueLibrary ResolveLibrary()
        {
            if (_libraryProbed) return _library;
            _libraryProbed = true;
            _library = AudioCueLibrary.Load();
            if (_library == null)
            {
                Debug.Log("[AudioRouter] No AudioCueLibrary at " +
                          "Resources/AudioCueLibrary. Run Robogame → Scaffold → " +
                          "Audio → Build Cue Library to wire USFX clips.");
            }
            return _library;
        }

        private AudioCueLibrary.Entry ResolveEntry(AudioCue cue)
        {
            AudioCueLibrary lib = ResolveLibrary();
            return lib?.Find(cue);
        }

        private void ConfigureSource(AudioSource src, AudioCueLibrary.Entry entry)
        {
            src.clip = entry.Clip;
            src.loop = false;
            src.spatialBlend = entry.SpatialBlend;
            src.pitch = 1f + Random.Range(-entry.PitchJitter, entry.PitchJitter);
            src.volume = ComputeVolume(entry.Bus, entry.Volume);
        }

        // -----------------------------------------------------------------
        // Volume routing
        // -----------------------------------------------------------------

        private float _busMaster = 1f, _busSfx = 1f, _busMusic = 1f, _busUi = 1f;
        private float _muteGate = 1f;

        private void ApplyVolumesFromTweakables()
        {
            _busMaster = Tweakables.Get(Tweakables.AudioMaster);
            _busSfx    = Tweakables.Get(Tweakables.AudioSfx);
            _busMusic  = Tweakables.Get(Tweakables.AudioMusic);
            _busUi     = Tweakables.Get(Tweakables.AudioUI);
            _muteGate  = Tweakables.GetBool(Tweakables.AudioMute) ? 0f : 1f;

            // If a mixer is wired (future), prefer the proper SetFloat
            // path. Otherwise fall through to per-source volume math.
            AudioMixer m = Mixer;
            if (m != null)
            {
                m.SetFloat(MixerParamMaster, LinearToDb(_busMaster * _muteGate));
                m.SetFloat(MixerParamSfx,    LinearToDb(_busSfx));
                m.SetFloat(MixerParamMusic,  LinearToDb(_busMusic));
                m.SetFloat(MixerParamUI,     LinearToDb(_busUi));
            }
            else
            {
                // Per-source application. AudioListener.volume isn't
                // sufficient because we want per-bus control without
                // a mixer asset.
                for (int i = 0; i < _voiceStates.Length; i++)
                {
                    if (!_voiceStates[i].InUse) continue;
                    _voices[i].volume = ComputeVolume(_voiceStates[i].Bus, _voiceStates[i].BaseVolume);
                }
                for (int i = 0; i < _loops.Count; i++)
                {
                    _loops[i].ReapplyVolume();
                }
            }
        }

        internal float ComputeVolume(AudioBus bus, float baseVol)
        {
            float busMult = bus switch
            {
                AudioBus.Sfx   => _busSfx,
                AudioBus.Music => _busMusic,
                AudioBus.UI    => _busUi,
                _              => 1f,
            };
            return Mathf.Clamp01(baseVol * busMult * _busMaster * _muteGate);
        }

        public AudioMixer Mixer
        {
            get
            {
                if (_mixerProbed) return _mixer;
                _mixerProbed = true;
                _mixer = Resources.Load<AudioMixer>(MixerResourcePath);
                return _mixer;
            }
        }

        public static float LinearToDb(float linear01)
        {
            if (linear01 <= 0.0001f) return -80f;
            return Mathf.Log10(Mathf.Clamp01(linear01)) * 20f;
        }

        // -----------------------------------------------------------------
        // Diagnostics
        // -----------------------------------------------------------------

        private void LogMissingOnce(AudioCue cue)
        {
            if (s_loggedMissing.Contains(cue)) return;
            s_loggedMissing.Add(cue);
            Debug.Log($"[AudioRouter] AudioCue.{cue} has no clip. " +
                      "Run Robogame → Scaffold → Audio → Build Cue Library, " +
                      "or check the entry in Resources/AudioCueLibrary.asset.");
        }
    }

    /// <summary>
    /// Owner-held handle to a looped audio voice. The owner is
    /// responsible for calling <see cref="Stop"/> when the underlying
    /// gameplay state ends (block destroyed, drive disabled, etc.).
    /// </summary>
    /// <remarks>
    /// The handle is a reference type (not a struct) because callers
    /// store it in fields across frames; struct-with-version-check
    /// patterns get error-prone fast and the alloc cost is one-per-loop-
    /// start, not a hot path.
    /// </remarks>
    public sealed class AudioLoopHandle
    {
        public static readonly AudioLoopHandle Invalid = new AudioLoopHandle();

        private AudioRouter _router;
        private AudioSource _source;
        private AudioCue _cue;
        private AudioBus _bus;
        private float _baseVolume;

        public bool IsValid => _source != null;
        public AudioCue Cue => _cue;

        private AudioLoopHandle() { /* Invalid sentinel only. */ }

        internal AudioLoopHandle(AudioRouter router, AudioSource source, AudioCue cue, AudioBus bus, float baseVolume)
        {
            _router = router;
            _source = source;
            _cue = cue;
            _bus = bus;
            _baseVolume = baseVolume;
        }

        /// <summary>Adjust the per-cue base volume (stacks on bus + master + mute).</summary>
        public void SetBaseVolume(float v)
        {
            _baseVolume = Mathf.Max(0f, v);
            ReapplyVolume();
        }

        /// <summary>Adjust playback pitch live. Used to scale rotor whine with RPM.</summary>
        public void SetPitch(float pitch)
        {
            if (_source == null) return;
            _source.pitch = Mathf.Clamp(pitch, 0.25f, 3.0f);
        }

        /// <summary>Reapply mix-bus volume — called by the router on Tweakables.Changed.</summary>
        internal void ReapplyVolume()
        {
            if (_source == null || _router == null) return;
            _source.volume = _router.ComputeVolume(_bus, _baseVolume);
        }

        /// <summary>Stop the loop and destroy the underlying voice GameObject.</summary>
        public void Stop()
        {
            if (_source == null) return;
            AudioRouter router = _router;
            AudioSource src = _source;
            _source = null;
            _router = null;
            if (src.isPlaying) src.Stop();
            // Destroy the AudioSource's GameObject (the child the
            // router parented to the caller's transform).
            if (src.gameObject != null) Object.Destroy(src.gameObject);
            router?.NotifyLoopStopped(this);
        }
    }
}
