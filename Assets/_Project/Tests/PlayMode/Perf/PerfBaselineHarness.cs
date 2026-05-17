using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Robogame.Tests.PlayMode.Perf
{
    /// <summary>
    /// Automated idle-baseline perf harness (PERFORMANCE_PASS_PLAN Phase 0/1,
    /// editor-playmode variant). Loads a gameplay scene, lets it settle, then
    /// samples a fixed window of frames and records frame-time percentiles +
    /// managed GC bytes/frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this is and is not.</b> This captures the <i>idle</i> state
    /// only (camera stationary, no player input, no combat) — the one state
    /// reproducible without a human at the controls. Active/combat rows in
    /// <c>docs/PERFORMANCE_BASELINES.md</c> still need a manual build run.
    /// </para>
    /// <para>
    /// <b>Editor numbers are 5–10× off absolute</b> (PERFORMANCE.md §3.2) —
    /// the editor itself ticks. These numbers are valid for <i>deltas</i>
    /// ("did this change help?"), which is exactly what a before/after pass
    /// needs. Run the same harness on the same machine before and after a
    /// change; the ratio is meaningful even though the absolute ms is not.
    /// </para>
    /// <para>
    /// <b>Headless CLI does NOT exercise OnGUI — verified.</b> A
    /// <c>-batchmode</c> run (even <i>with</i> a gfx device, no
    /// <c>-nographics</c>) has no window and no IMGUI repaint loop, so
    /// <c>OnGUI</c> never ticks and the HUD-allocation delta reads 0 B
    /// both before and after. The session-84 fixes (ObjectiveHud /
    /// ScrapCarriedIndicator) live in <c>OnGUI</c>; their numeric
    /// before/after is only obtainable from the <b>editor Test Runner</b>
    /// (Game-view repaints OnGUI) or a <b>windowed Development Build</b>.
    /// Run headless, this harness validates the non-OnGUI idle frame
    /// (Update / physics / render-setup) — a real forward regression
    /// guard, but not a check on the OnGUI fixes specifically.
    /// </para>
    /// <para>
    /// Results are appended to
    /// <c>docs/perf-captures/harness-log.txt</c> and echoed to the console
    /// with a <c>[PERF-BASELINE]</c> prefix so a CLI <c>-runTests</c> log can
    /// be grepped.
    /// </para>
    /// </remarks>
    [Category("Perf")]
    public sealed class PerfBaselineHarness
    {
        // Frames discarded after scene load before sampling. Scene load
        // triggers ChassisFactory builds, terrain bake, shader warmup,
        // first-GC — none of that is steady state.
        private const int WarmupFrames = 240;

        // Frames measured. 600 ≈ 5–10 s of editor frames; enough for a
        // stable median and a meaningful 1%/0.1% tail.
        private const int SampleFrames = 600;

        private int _prevVSync;
        private int _prevTarget;

        [SetUp]
        public void SetUp()
        {
            // Uncap the framerate so measured frame time reflects work done,
            // not a vsync/target wait. Restored in TearDown.
            _prevVSync = QualitySettings.vSyncCount;
            _prevTarget = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            // This is a perf-capture harness, not a correctness test. A
            // benign scene-load warning/error from an unrelated subsystem
            // must not nuke the measurement — we assert on numbers, not
            // logs. (Without this, the Unity Test Framework fails the run
            // on any [Error] line during a [UnityTest].)
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.vSyncCount = _prevVSync;
            Application.targetFrameRate = _prevTarget;
        }

        [UnityTest]
        public IEnumerator Arena_Idle_Baseline()
        {
            yield return Measure("Arena");
        }

        [UnityTest]
        public IEnumerator Garage_Idle_Baseline()
        {
            yield return Measure("Garage");
        }

        private static IEnumerator Measure(string sceneName)
        {
            // The gameplay scenes are NOT loadable in isolation —
            // ArenaController/GarageController require the persistent
            // GameStateController that only Bootstrap.unity authors
            // (DontDestroyOnLoad). Loading them directly logs an [Error]
            // and the scene half-initialises (meaningless numbers). So
            // bootstrap the real way: load Bootstrap, let GameBootstrap
            // flow to MainMenu, then transition via GameStateController —
            // exactly the path a player takes.
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);

            // GameStateController.Awake (DontDestroyOnLoad) seeds
            // CurrentBlueprint from its serialised _defaultBlueprint, so
            // once Instance exists the state is fully hydrated.
            float guard = 0f;
            while (GameStateController.Instance == null && guard < 10f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.IsNotNull(GameStateController.Instance,
                "GameStateController never came up — Bootstrap.unity wiring changed?");

            // GameBootstrap.Start races a LoadScene("MainMenu"); let that
            // settle so our transition isn't clobbered by it.
            yield return new WaitForSecondsRealtime(1.5f);

            if (sceneName == "Arena") GameStateController.Instance.EnterArena();
            else if (sceneName == "Garage") GameStateController.Instance.EnterGarage();
            else Assert.Fail($"Unsupported scene '{sceneName}'.");

            // Wait until the requested scene is the active one and holds
            // (guards against a late MainMenu load winning the race).
            guard = 0f;
            int stable = 0;
            while (stable < 30 && guard < 20f)
            {
                guard += Time.unscaledDeltaTime;
                stable = SceneManager.GetActiveScene().name == sceneName ? stable + 1 : 0;
                yield return null;
            }
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name,
                $"Scene '{sceneName}' never became stably active.");

            // Settle: discard warmup frames. Real-time wait too so async
            // bakes / Awaitable-staggered work finish.
            for (int i = 0; i < WarmupFrames; i++) yield return null;
            yield return new WaitForSecondsRealtime(2f);

            // Force a GC so the sampling window starts from a clean slate;
            // we want steady-state churn, not leftover warmup garbage.
            System.GC.Collect();
            yield return null;

            var frameMs = new float[SampleFrames];
            long gcBefore = System.GC.GetAllocatedBytesForCurrentThread();
            var sw = new Stopwatch();

            for (int i = 0; i < SampleFrames; i++)
            {
                sw.Restart();
                yield return null;
                sw.Stop();
                frameMs[i] = (float)(sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
            }

            long gcAfter = System.GC.GetAllocatedBytesForCurrentThread();
            long gcDelta = gcAfter - gcBefore;

            System.Array.Sort(frameMs);
            float min = frameMs[0];
            float median = frameMs[SampleFrames / 2];
            float p99 = frameMs[Mathf.Clamp((int)(SampleFrames * 0.99f), 0, SampleFrames - 1)];
            float p999 = frameMs[Mathf.Clamp((int)(SampleFrames * 0.999f), 0, SampleFrames - 1)];
            float sum = 0f;
            for (int i = 0; i < SampleFrames; i++) sum += frameMs[i];
            float avg = sum / SampleFrames;
            float fps = avg > 0f ? 1000f / avg : 0f;
            double gcPerFrame = (double)gcDelta / SampleFrames;

            string line = string.Format(CultureInfo.InvariantCulture,
                "[PERF-BASELINE] scene={0} state=idle frames={1} " +
                "avg={2:F3}ms median={3:F3}ms min={4:F3}ms p99={5:F3}ms p99.9={6:F3}ms " +
                "fps={7:F1} gcTotal={8}B gcPerFrame={9:F1}B",
                sceneName, SampleFrames, avg, median, min, p99, p999, fps,
                gcDelta, gcPerFrame);

            Debug.Log(line);
            AppendToLog(line);

            // Hard gate: idle steady state must not allocate. This will fail
            // loudly if a regression reintroduces a per-frame allocation —
            // which is the whole point of keeping the harness in CI reach.
            // Threshold (not 0) tolerates Unity-internal editor-only churn
            // the build wouldn't have; tighten if a build run shows lower.
            Assert.That(gcPerFrame, Is.LessThan(2048.0),
                $"Idle steady-state GC regression in '{sceneName}': " +
                $"{gcPerFrame:F1} B/frame (target ~0; editor tolerance 2048). " +
                "Open the Memory profiler allocation calltree at the idle row.");
        }

        private static void AppendToLog(string line)
        {
            try
            {
                // Application.dataPath = <project>/Assets
                string dir = Path.Combine(Application.dataPath, "..", "docs", "perf-captures");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "harness-log.txt");
                string stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture);
                File.AppendAllText(path, $"{stamp}  {line}\n");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PERF-BASELINE] could not write log file: {e.Message}");
            }
        }
    }
}
