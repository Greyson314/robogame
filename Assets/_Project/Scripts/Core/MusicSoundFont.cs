using System;
using System.IO;
using MidiPlayerTK;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Binds the project's General MIDI bank
    /// (<c>StreamingAssets/SoundFont/</c>) to MPTK synths (LOG-148).
    /// Until a synth is attached and ready, <see cref="MusicMidi"/> and
    /// <see cref="GarageMusic"/> stay inert and their callers keep the
    /// baked-WAV fallbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loading is <b>per synth</b> (<c>synth.MPTK_SoundFont.Load</c>),
    /// not global. The global <c>MPTK_LoadLiveSF</c> binds only to
    /// synths that already exist when it runs, so a lazily-created
    /// player — which is exactly how both of ours are built — would
    /// come up bankless and silently voice nothing. Per-synth loading
    /// has no such ordering trap.
    /// </para>
    /// <para>
    /// The bank stays a single file on disk rather than a Maestro
    /// editor import, so swapping it is a file drop and nothing in
    /// <c>Resources/</c> grows by 30 MB. All cosmetic (INV-3): a
    /// missing or unreadable bank logs once and changes nothing else.
    /// </para>
    /// </remarks>
    public static class MusicSoundFont
    {
        /// <summary>StreamingAssets-relative path of the GM bank (GeneralUser GS).</summary>
        public const string StreamingRelativePath = "SoundFont/GeneralUser-GS.sf2";

        private static bool s_logged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_logged = false;

        /// <summary>Absolute path of the bank on disk.</summary>
        public static string Path =>
            System.IO.Path.Combine(Application.streamingAssetsPath, StreamingRelativePath);

        /// <summary>Bank present on disk (says nothing about any synth).</summary>
        public static bool Exists => File.Exists(Path);

        /// <summary>That synth has its bank loaded and can voice notes.</summary>
        public static bool IsReady(MidiSynth synth) =>
            synth != null && synth.MPTK_SoundFont != null && synth.MPTK_SoundFont.IsReady;

        /// <summary>
        /// Start loading the bank into <paramref name="synth"/>. Returns
        /// false when the bank is missing or MPTK refuses the request —
        /// callers keep their fallback. True means loading started;
        /// poll <see cref="IsReady"/> before expecting sound.
        /// </summary>
        public static bool AttachTo(MidiSynth synth)
        {
            if (synth == null) return false;
            if (IsReady(synth)) return true;

            // Batch Unity (headless test rig, CI) runs with audio disabled;
            // MPTK will still schedule voices against sample data that never
            // gets built and NREs per note (the Tuba-D1 GEN_FILTERQ failure
            // that broke Garage_Idle_Baseline — deterministic whenever the
            // async bank load beat the perf capture window). No audio device
            // means MIDI has no job here; callers keep their WAV fallbacks.
            // TRACE[LOG-164]: root fix, preferred over LogAssert.Expect.
            if (Application.isBatchMode)
            {
                LogOnce("[MusicSoundFont] Batch mode — MIDI voices stay off.");
                return false;
            }

            if (!Exists)
            {
                LogOnce("[MusicSoundFont] No soundfont at StreamingAssets/" + StreamingRelativePath +
                        " — MIDI voices stay off, WAV stingers keep playing.");
                return false;
            }

            try
            {
                // MPTK needs a file:// URI, and Windows absolute paths need
                // forward slashes to survive its scheme parse.
                string uri = "file://" + Path.Replace('\\', '/');
                if (!synth.MPTK_SoundFont.Load(uri))
                {
                    LogOnce("[MusicSoundFont] MPTK refused the soundfont URI (" + uri +
                            ") — staying on the WAV path.");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                LogOnce("[MusicSoundFont] Soundfont load failed (" + e.Message +
                        ") — staying on the WAV path.");
                return false;
            }
        }

        private static void LogOnce(string message)
        {
            if (s_logged) return;
            s_logged = true;
            Debug.Log(message);
        }
    }
}
