using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Robogame.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Robogame.Tests.PlayMode.Perf
{
    /// <summary>
    /// Render-cost attribution probe. The in-game bisect showed
    /// "camera away from the bots/dummies = +40–60 fps" — a render
    /// cost, not AI. This headlessly quantifies *which* render cost by
    /// framing the camera on the chassis cluster and measuring
    /// frame-time across shadow configurations.
    /// </summary>
    /// <remarks>
    /// Batchmode (no <c>-nographics</c>) runs the full render loop
    /// including shadow passes — only the on-screen present and IMGUI
    /// repaint are skipped — so shadow / draw cost IS measurable here
    /// even though OnGUI is not.
    ///
    /// Configs, each a 300-frame window with the camera framing the
    /// chassis:
    /// <list type="bullet">
    ///   <item><description><b>away</b> — camera at empty sky (the
    ///   "look away" reference; chassis frustum-culled).</description></item>
    ///   <item><description><b>baseline</b> — camera on the chassis,
    ///   everything as-shipped.</description></item>
    ///   <item><description><b>noChassisShadowCast</b> — chassis block
    ///   renderers' <c>shadowCastingMode = Off</c> (sun still shadows
    ///   terrain).</description></item>
    ///   <item><description><b>noSunShadows</b> — directional light
    ///   shadows off entirely.</description></item>
    /// </list>
    /// (baseline − away) = total chassis-in-view cost.
    /// (baseline − noChassisShadowCast) = chassis shadow-caster cost.
    /// (baseline − noSunShadows) = all-shadow cost.
    /// </remarks>
    [Category("Perf")]
    public sealed class PerfRenderProbe
    {
        private const int WarmupFrames = 200;
        private const int SampleFrames = 300;

        [UnityTest]
        public IEnumerator Arena_ChassisRenderCost_Attribution()
        {
            // --- bootstrap the real player path (see PerfBaselineHarness) ---
            LogAssert.ignoreFailingMessages = true;
            int pv = QualitySettings.vSyncCount; int pt = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;

            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
            float g = 0f;
            while (GameStateController.Instance == null && g < 10f) { g += Time.unscaledDeltaTime; yield return null; }
            Assert.IsNotNull(GameStateController.Instance, "GameStateController never came up.");
            yield return new WaitForSecondsRealtime(1.5f);
            GameStateController.Instance.EnterArena();
            g = 0f; int stable = 0;
            while (stable < 30 && g < 20f)
            {
                g += Time.unscaledDeltaTime;
                stable = SceneManager.GetActiveScene().name == "Arena" ? stable + 1 : 0;
                yield return null;
            }
            Assert.AreEqual("Arena", SceneManager.GetActiveScene().name, "Arena never became active.");
            for (int i = 0; i < WarmupFrames; i++) yield return null;
            yield return new WaitForSecondsRealtime(2f);

            // --- gather chassis block renderers + their world bounds ---
            Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var blockRenderers = new System.Collections.Generic.List<Renderer>();
            bool hasBounds = false;
            Bounds b = default;
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled) continue;
                if (!r.gameObject.name.StartsWith("Block_")) continue;
                blockRenderers.Add(r);
                if (!hasBounds) { b = r.bounds; hasBounds = true; }
                else b.Encapsulate(r.bounds);
            }
            Assert.IsTrue(hasBounds && blockRenderers.Count > 0,
                $"No Block_* renderers found in the Arena (found {all.Length} renderers total).");
            Debug.Log($"[RENDER-PROBE] chassis block renderers in scene: {blockRenderers.Count}, " +
                      $"bounds center={b.center} size={b.size}");

            // --- dedicated measurement camera (no follow script to fight) ---
            Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++) if (cams[i] != null) cams[i].enabled = false;
            var camGo = new GameObject("PerfProbeCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = true;
            float dist = Mathf.Max(b.size.magnitude * 1.4f, 12f);
            Vector3 onPos = b.center + new Vector3(0.35f, 0.5f, -1f).normalized * dist;
            Quaternion onRot = Quaternion.LookRotation(b.center - onPos);
            Vector3 awayPos = b.center + Vector3.up * 200f;
            Quaternion awayRot = Quaternion.LookRotation(Vector3.up);

            Light sun = null;
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null && lights[i].type == LightType.Directional) { sun = lights[i]; break; }

            // --- configs ---
            float away = 0f, baseMs = 0f, noCast = 0f, noSun = 0f;

            cam.transform.SetPositionAndRotation(awayPos, awayRot);
            yield return Settle();
            yield return MeasureWindow(); away = _lastMs;

            cam.transform.SetPositionAndRotation(onPos, onRot);
            yield return Settle();
            yield return MeasureWindow(); baseMs = _lastMs;

            // chassis shadow casters off
            var prevModes = new ShadowCastingMode[blockRenderers.Count];
            for (int i = 0; i < blockRenderers.Count; i++)
            {
                prevModes[i] = blockRenderers[i].shadowCastingMode;
                blockRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }
            yield return Settle();
            yield return MeasureWindow(); noCast = _lastMs;
            for (int i = 0; i < blockRenderers.Count; i++)
                if (blockRenderers[i] != null) blockRenderers[i].shadowCastingMode = prevModes[i];

            // sun shadows off
            LightShadows prevShadows = sun != null ? sun.shadows : LightShadows.None;
            if (sun != null) sun.shadows = LightShadows.None;
            yield return Settle();
            yield return MeasureWindow(); noSun = _lastMs;
            if (sun != null) sun.shadows = prevShadows;

            string line = string.Format(CultureInfo.InvariantCulture,
                "[RENDER-PROBE] blocks={0} | away={1:F3}ms({2:F0}fps) baseline={3:F3}ms({4:F0}fps) " +
                "noChassisShadowCast={5:F3}ms({6:F0}fps) noSunShadows={7:F3}ms({8:F0}fps) | " +
                "chassis-in-view cost={9:F3}ms, chassis-shadowcast cost={10:F3}ms, all-shadow cost={11:F3}ms",
                blockRenderers.Count,
                away, 1000f / away, baseMs, 1000f / baseMs,
                noCast, 1000f / noCast, noSun, 1000f / noSun,
                baseMs - away, baseMs - noCast, baseMs - noSun);
            Debug.Log(line);
            AppendLog(line);

            QualitySettings.vSyncCount = pv; Application.targetFrameRate = pt;
            Assert.Pass(line);
        }

        private float _lastMs;

        private static IEnumerator Settle()
        {
            for (int i = 0; i < 30; i++) yield return null;
        }

        private IEnumerator MeasureWindow()
        {
            var sw = new Stopwatch();
            double sum = 0;
            for (int i = 0; i < SampleFrames; i++)
            {
                sw.Restart();
                yield return null;
                sw.Stop();
                sum += sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
            }
            _lastMs = (float)(sum / SampleFrames);
        }

        private static void AppendLog(string line)
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "..", "docs", "perf-captures");
                Directory.CreateDirectory(dir);
                string stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(Path.Combine(dir, "harness-log.txt"), $"{stamp}  {line}\n");
            }
            catch (System.Exception e) { Debug.LogWarning($"[RENDER-PROBE] log write failed: {e.Message}"); }
        }
    }
}
