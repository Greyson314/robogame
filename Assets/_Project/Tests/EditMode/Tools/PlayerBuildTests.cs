using System.Linq;
using NUnit.Framework;
using Robogame.Tools.Editor;
using UnityEditor;

namespace Robogame.Tests.EditMode.Tools
{
    /// <summary>
    /// PlayerBuild ships exactly the scenes EditorBuildSettings has enabled,
    /// in order — never a hand-written list that can drift from what a Build
    /// Settings dialog would ship (LAUNCH-READINESS B1).
    /// </summary>
    public sealed class PlayerBuildTests
    {
        [Test]
        public void BuildScenes_MatchEditorBuildSettings()
        {
            string[] expected = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            string[] actual = PlayerBuild.EnabledScenePaths();

            Assert.That(actual, Is.EqualTo(expected),
                "PlayerBuild.EnabledScenePaths() must mirror EditorBuildSettings' " +
                "enabled scenes, in order.");

            // The six scenes shipped today (ProjectSettings/EditorBuildSettings.asset,
            // 2026-09-18), Bootstrap first. A scene toggled off (e.g. a disabled
            // DigZone_Test) must not appear here; a real drift in the shipped
            // set should fail this line loudly rather than pass silently.
            Assert.That(actual.Length, Is.EqualTo(6),
                "expected six enabled scenes (see ProjectSettings/EditorBuildSettings.asset)");
            Assert.That(actual[0], Does.EndWith("/Bootstrap.unity"),
                "Bootstrap must build first");
        }
    }
}
