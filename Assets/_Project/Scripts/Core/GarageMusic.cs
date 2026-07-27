using System;
using System.IO;
using MidiPlayerTK;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Core
{
    /// <summary>
    /// Plays the garage theme — a public-domain MIDI rendered live
    /// through the GM soundfont (LOG-148). Separate from
    /// <see cref="MusicConductor"/> on purpose: the garage has no beat
    /// grid to defend, no stingers to stay consonant with, and no
    /// intensity to track, so it needs none of the combat stack's
    /// machinery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MIDI is handed to MPTK as raw bytes from
    /// <c>StreamingAssets/Midi/</c> rather than imported into Maestro's
    /// MidiDB, so swapping the theme is a file drop. Playback needs the
    /// soundfont, so this is a silent no-op until
    /// <see cref="MusicSoundFont"/> reports ready — it polls a few
    /// frames rather than blocking the garage load.
    /// </para>
    /// <para>
    /// Cosmetic-only (INV-3) and garage-only: stops itself on scene
    /// unload so a theme never leaks into an arena.
    /// </para>
    /// </remarks>
    public sealed class GarageMusic : MonoBehaviour
    {
        /// <summary>StreamingAssets-relative path of the theme MIDI.</summary>
        public const string StreamingRelativePath = "Midi/bach-invention-08.mid";

        /// <summary>Resources path of the MPTK file-player prefab copy.</summary>
        public const string FilePlayerResourcePath = "Music/MptkFilePlayer";

        /// <summary>Theme gain before the Tweakables music/master/mute chain.</summary>
        private const float ThemeVolume = 0.42f;

        // The bank streams in over several frames; give it a generous
        // window before concluding it isn't coming, then stop polling.
        private const float LoadTimeoutSeconds = 20f;

        private static GarageMusic s_instance;

        private MidiFilePlayer _player;
        private byte[] _midi;
        private float _waited;
        private bool _started;
        private bool _loggedMissing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        /// <summary>
        /// Start (or keep) the garage theme. Safe to call on every
        /// garage entry — a second call while playing is a no-op.
        /// </summary>
        public static void Play()
        {
            if (s_instance != null) return;
            var root = new GameObject("[GarageMusic]");
            DontDestroyOnLoad(root);
            s_instance = root.AddComponent<GarageMusic>();
        }

        /// <summary>Stop the theme and release the synth.</summary>
        public static void Stop()
        {
            if (s_instance == null) return;
            Destroy(s_instance.gameObject);
            s_instance = null;
        }

        private void OnEnable()
        {
            Tweakables.Changed += ApplyVolume;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private void OnDisable()
        {
            Tweakables.Changed -= ApplyVolume;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        private void OnDestroy()
        {
            if (_player != null) _player.MPTK_Stop();
            if (s_instance == this) s_instance = null;
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            // Leaving the garage (arena, menu, quit) ends the theme —
            // same contract MusicConductor holds for the combat track.
            Stop();
        }

        private void Update()
        {
            if (_started) return;

            // The player (and its bank request) is created once; playback
            // waits for that synth's soundfont to finish streaming.
            if (_player == null && !TryCreatePlayer())
            {
                enabled = false;
                return;
            }

            _waited += Time.unscaledDeltaTime;
            if (!MusicSoundFont.IsReady(_player))
            {
                if (_waited > LoadTimeoutSeconds)
                {
                    LogMissing("[GarageMusic] Soundfont not ready after " + LoadTimeoutSeconds +
                               "s — garage stays quiet.");
                    enabled = false;
                }
                return;
            }

            _started = true;
            ApplyVolume();
            _player.MPTK_Play(_midi);
        }

        private bool TryCreatePlayer()
        {
            string path = Path.Combine(Application.streamingAssetsPath, StreamingRelativePath);
            if (!File.Exists(path))
            {
                LogMissing("[GarageMusic] No theme MIDI at StreamingAssets/" + StreamingRelativePath +
                           " — garage stays quiet.");
                return false;
            }

            try
            {
                _midi = File.ReadAllBytes(path);
                // TRACE[LOG-148]: MPTK's own prefab carries the AudioSource +
                // voice-template arrangement the synth needs to reach the
                // audio output; a hand-built GameObject allocates voices but
                // stays silent.
                GameObject prefab = Resources.Load<GameObject>(FilePlayerResourcePath);
                if (prefab == null)
                {
                    LogMissing("[GarageMusic] Missing MPTK player prefab at Resources/" +
                               FilePlayerResourcePath + " — garage stays quiet.");
                    return false;
                }
                GameObject go = Instantiate(prefab, transform);
                go.name = "Player";
                _player = go.GetComponent<MidiFilePlayer>();
                _player.MPTK_CorePlayer = true;
                _player.MPTK_DirectSendToPlayer = true;
                _player.MPTK_MidiAutoRestart = true;   // loop the theme
                _player.MPTK_InitSynth();
                MusicSoundFont.AttachTo(_player);
                return true;
            }
            catch (Exception e)
            {
                LogMissing("[GarageMusic] Theme setup failed (" + e.Message + ").");
                return false;
            }
        }

        private void ApplyVolume()
        {
            if (_player == null) return;
            float music = Tweakables.Get(Tweakables.AudioMusic);
            float master = Tweakables.Get(Tweakables.AudioMaster);
            float mute = Tweakables.GetBool(Tweakables.AudioMute) ? 0f : 1f;
            _player.MPTK_Volume = Mathf.Clamp01(ThemeVolume * music * master * mute);
        }

        private void LogMissing(string message)
        {
            if (_loggedMissing) return;
            _loggedMissing = true;
            Debug.Log(message);
        }
    }
}
