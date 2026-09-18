using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Builds a Windows Standalone player from the CLI (<c>-executeMethod</c>)
    /// or the Editor menu. LAUNCH-READINESS B1 ("a Windows player build
    /// succeeds from the CLI or manage_build, reproducibly") and B2 (size +
    /// load time recorded with a band).
    /// </summary>
    /// <remarks>
    /// Scenes come from <see cref="EditorBuildSettings"/> (enabled only, in
    /// list order) — never hand-listed here, so a scene toggled in the
    /// Editor's Build Settings dialog is the only place the shipped scene
    /// list changes. Guarded by
    /// <c>PlayerBuildTests.BuildScenes_MatchEditorBuildSettings</c>.
    /// </remarks>
    public static class PlayerBuild
    {
        private const string OutputRelativePath = "Builds/Windows/Robogame.exe";

        [MenuItem("Robogame/Build/Windows Player", priority = 400)]
        public static void BuildWindows()
        {
            string[] scenes = EnabledScenePaths();
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outputPath = Path.Combine(projectRoot, OutputRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            string line = "[PLAYER-BUILD] result=" + summary.result +
                          " totalSize=" + summary.totalSize +
                          " totalTime=" + summary.totalTime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) +
                          " errors=" + summary.totalErrors +
                          " warnings=" + summary.totalWarnings +
                          " scenes=" + scenes.Length;

            Debug.Log(line);
            AppendToLog(line);

            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// The enabled scenes of <see cref="EditorBuildSettings"/>, in list
        /// order. Public (not internal: PlayerBuildTests lives in the
        /// Robogame.Tests.EditMode assembly, with no InternalsVisibleTo
        /// between the two) so the guard can run without invoking a real
        /// build.
        /// </summary>
        public static string[] EnabledScenePaths() =>
            EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

        // Same log path convention as PerfBaselineHarness.AppendToLog
        // (Assets/_Project/Tests/PlayMode/Perf/PerfBaselineHarness.cs), duplicated
        // here because an Editor tool cannot reference the test assembly.
        private static void AppendToLog(string line)
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "..", "docs", "perf-captures");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "harness-log.txt");
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(path, $"{stamp}  {line}\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PLAYER-BUILD] could not write log file: {e.Message}");
            }
        }
    }
}
