using System;
using MidiPlayerTK;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// MPTK-backed stinger voice (ADR-0007): plays the musical
    /// damage-feedback stingers as real soundfont notes instead of the
    /// baked placeholder WAVs. One synth channel per instrument, General
    /// MIDI patches, runs authored as note tables walking the same D
    /// pentatonic the WAV path uses.
    /// </summary>
    /// <remarks>
    /// Strictly optional: <see cref="IsAvailable"/> is false until a
    /// soundfont has been imported through Maestro's setup window
    /// (MPTK ships without one), and callers fall back to the
    /// <see cref="AudioRouter"/> WAV path. MPTK synthesises through
    /// Unity's audio pipeline, so scheduling here converts a dsp-time
    /// slot into the millisecond delay MPTK understands — accurate to
    /// roughly one synth buffer rather than sample-exact. That trade
    /// (timbre for a few ms of tightness) is ADR-0007's call.
    /// </remarks>
    public static class MusicMidi
    {
        private static MidiStreamPlayer s_player;
        private static bool s_initFailed;

        // Indexed by MusicalHitDirector's instrument index.
        // GM patches: 45 pizzicato strings, 61 brass section, 0 grand piano, 47 timpani.
        private static readonly int[] s_patches = { 45, 61, 0, 47 };
        // Root notes at the project root D (MusicalSfx contract): D4, D3, D4, D2.
        private static readonly int[] s_roots = { 62, 50, 62, 38 };
        // D major pentatonic, semitones from root — mirror of MusicalSfx.ScalePitch.
        private static readonly int[] s_pentSemis = { 0, 2, 4, 7, 9, 12 };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_player = null;
            s_initFailed = false;
        }

        /// <summary>Soundfont imported and synth bootable — WAV fallback otherwise.</summary>
        public static bool IsAvailable =>
            !s_initFailed && MidiPlayerGlobal.MPTK_SoundFontLoaded && Ensure() != null;

        /// <summary>
        /// Schedule one stinger at <paramref name="slotDsp"/> (dsp-time
        /// domain). Mirrors the WAV path's tiering: note walks the
        /// pentatonic from <paramref name="pentStep"/>, flourish is a
        /// fast run, phrase is a run into a held root+fifth. Incoming
        /// drops an octave at reduced velocity.
        /// </summary>
        public static void PlayStinger(int instrument, MusicMath.StingerTier tier,
            int pentStep, bool incoming, double slotDsp, float volumeScale)
        {
            MidiStreamPlayer player = Ensure();
            if (player == null) return;

            float music = Tweakables.Get(Tweakables.AudioMusic);
            float master = Tweakables.Get(Tweakables.AudioMaster);
            float mute = Tweakables.GetBool(Tweakables.AudioMute) ? 0f : 1f;
            player.MPTK_Volume = Mathf.Clamp01(music * master * mute);

            long baseDelay = (long)Math.Max(0.0, (slotDsp - AudioSettings.dspTime) * 1000.0);
            int root = s_roots[instrument] + (incoming ? -12 : 0);
            int vel = (int)(105 * volumeScale);

            switch (tier)
            {
                case MusicMath.StingerTier.Note:
                    Play(player, instrument, root + s_pentSemis[pentStep % s_pentSemis.Length],
                         vel, baseDelay, 450);
                    break;

                case MusicMath.StingerTier.Flourish:
                    // Fast 16th run up the chord, landing on the octave.
                    Play(player, instrument, root, (int)(vel * 0.85f), baseDelay, 200);
                    Play(player, instrument, root + 4, (int)(vel * 0.9f), baseDelay + 110, 200);
                    Play(player, instrument, root + 7, (int)(vel * 0.95f), baseDelay + 220, 200);
                    Play(player, instrument, root + 12, vel, baseDelay + 330, 550);
                    break;

                case MusicMath.StingerTier.Phrase:
                    // Kill phrase: run in 16ths, then root+fifth held on the beat.
                    Play(player, instrument, root, (int)(vel * 0.8f), baseDelay, 180);
                    Play(player, instrument, root + 4, (int)(vel * 0.85f), baseDelay + 150, 180);
                    Play(player, instrument, root + 7, (int)(vel * 0.9f), baseDelay + 300, 180);
                    Play(player, instrument, root + 9, (int)(vel * 0.9f), baseDelay + 450, 180);
                    Play(player, instrument, root + 12, vel, baseDelay + 600, 1200);
                    Play(player, instrument, root + 19, (int)(vel * 0.7f), baseDelay + 600, 1200);
                    break;
            }
        }

        private static void Play(MidiStreamPlayer player, int channel, int note,
            int velocity, long delayMs, long durationMs)
        {
            player.MPTK_PlayEvent(new MPTKEvent
            {
                Command = MPTKCommand.NoteOn,
                Value = note,
                Channel = channel,
                Velocity = Mathf.Clamp(velocity, 1, 127),
                Duration = durationMs,
                Delay = delayMs,
            });
        }

        private static MidiStreamPlayer Ensure()
        {
            if (s_player != null || s_initFailed) return s_player;
            try
            {
                var go = new GameObject("[MusicMidi]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_player = go.AddComponent<MidiStreamPlayer>();
                s_player.MPTK_CorePlayer = true;
                s_player.MPTK_InitSynth();
                for (int i = 0; i < s_patches.Length; i++)
                {
                    s_player.MPTK_PlayEvent(new MPTKEvent
                    {
                        Command = MPTKCommand.PatchChange,
                        Value = s_patches[i],
                        Channel = i,
                    });
                }
            }
            catch (Exception e)
            {
                s_initFailed = true;
                s_player = null;
                Debug.LogWarning("[MusicMidi] MPTK synth init failed (" + e.Message +
                                 ") — stingers stay on the WAV path.");
            }
            return s_player;
        }
    }
}
