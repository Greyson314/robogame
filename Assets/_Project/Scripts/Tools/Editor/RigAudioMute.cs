using System;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Mutes the Editor's audio output when this Editor is a rig, not a
    /// person: a batch-mode test run (<see cref="Application.isBatchMode"/>)
    /// or an Editor the factory launcher started with ROBOGAME_RIG_MUTE=1.
    /// Grey's steer, 2026-09-17 (docs/loop/INBOX.md): the headless PlayMode
    /// runs on the factory rig were playing the garage music through the
    /// desktop speakers every two minutes. <see cref="EditorUtility.audioMasterMute"/>
    /// is the Game view's "Mute Audio" toggle; the FMOD integration mirrors it (Assets/Plugins/FMOD/src/RuntimeManager.cs:1513)
    /// onto its master bus, and MusicConductor mirrors it onto the bank-less music group (MusicConductor.cs:256), so one switch covers MPTK and FMOD.
    /// A human-started Editor (no env var, not batch) is never touched, and a
    /// player build never runs this file.
    /// </summary>
    [InitializeOnLoad]
    internal static class RigAudioMute
    {
        internal const string EnvVar = "ROBOGAME_RIG_MUTE";

        static RigAudioMute()
        {
            if (!ShouldMute()) return;
            if (!EditorUtility.audioMasterMute) EditorUtility.audioMasterMute = true;
        }

        /// <summary>True when this Editor process is a rig: batch mode, or the launcher's env var.</summary>
        internal static bool ShouldMute()
        {
            if (Application.isBatchMode) return true;
            return Environment.GetEnvironmentVariable(EnvVar) == "1";
        }
    }
}
