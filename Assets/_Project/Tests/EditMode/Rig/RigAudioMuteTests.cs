using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tests.EditMode.Rig
{
    /// <summary>
    /// The factory's batch test runs must not play the game's audio through
    /// the desktop speakers (Grey, 2026-09-17, INBOX). The rule that matters:
    /// an Editor that is a rig (batch mode) has the master mute on before any
    /// test plays a sound. This test is the observable for that rule on the rig
    /// itself: it runs in every batch suite run, and it is ignored in a human's
    /// Editor, where muting would be wrong.
    /// </summary>
    public sealed class RigAudioMuteTests
    {
        [Test]
        public void BatchMode_HasEditorAudioMuted()
        {
            if (!Application.isBatchMode)
                Assert.Ignore("Not a batch (rig) session: a human's Editor is never muted by the rig rule.");
            Assert.IsTrue(EditorUtility.audioMasterMute,
                "This is a batch-mode Editor and its audio is not muted: the rig would play the game's music through the speakers. The [InitializeOnLoad] rig mute (Tools/Editor/RigAudioMute.cs) did not run or was undone.");
        }
    }
}
