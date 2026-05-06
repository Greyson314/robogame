using Robogame.UI;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Menu hooks for the perf-pass workflow. Each item is intentionally
    /// dumb — the goal is one click per common diagnostic action so the
    /// "before / after" capture loop stays cheap.
    /// </summary>
    /// <remarks>
    /// All items live under <c>Robogame &gt; Perf</c> to keep them grouped.
    /// Avoid menu-item proliferation: one entry per coarse action, the same
    /// rule the scaffolders follow. If you need finer control, expose a
    /// SerializeField on the underlying component instead of forking the
    /// menu.
    /// </remarks>
    public static class PerformanceMenu
    {
        private const string kRoot = "Robogame/Perf/";

        // -----------------------------------------------------------------
        // HUD toggle
        // -----------------------------------------------------------------

        [MenuItem(kRoot + "Toggle Perf HUD %#h")]
        private static void TogglePerfHud()
        {
            // Only meaningful while playing — the HUD auto-bootstraps via
            // RuntimeInitializeOnLoad and doesn't exist outside Play.
            if (!Application.isPlaying)
            {
                Debug.Log("[Perf] HUD only exists in Play mode. Hit Play first, then F3 (or this menu).");
                return;
            }

            PerformanceHud hud = Object.FindAnyObjectByType<PerformanceHud>();
            if (hud == null)
            {
                Debug.LogWarning("[Perf] No PerformanceHud in the scene — auto-bootstrap should have spawned one. Reloading the scene usually fixes this.");
                return;
            }
            hud.Toggle();
        }

        // -----------------------------------------------------------------
        // Render stats
        // -----------------------------------------------------------------

        [MenuItem(kRoot + "Log Render Stats")]
        private static void LogRenderStats()
        {
            // UnityStats numbers are valid only during a Play-mode frame.
            // Outside Play they hold the last cached editor draw, which is
            // misleading.
            if (!Application.isPlaying)
            {
                Debug.Log("[Perf] Hit Play first — UnityStats only reports during Play-mode frames.");
                return;
            }
            Debug.Log(
                $"[Perf] Draw {UnityStats.drawCalls}  SetPass {UnityStats.setPassCalls}  " +
                $"Tris {UnityStats.triangles:N0}  Verts {UnityStats.vertices:N0}  " +
                $"Shadow casters {UnityStats.shadowCasters}  " +
                $"Frame time {UnityStats.frameTime * 1000f:F2} ms  " +
                $"Render time {UnityStats.renderTime * 1000f:F2} ms");
        }

        // -----------------------------------------------------------------
        // V-Sync
        // -----------------------------------------------------------------

        [MenuItem(kRoot + "Toggle V-Sync")]
        private static void ToggleVSync()
        {
            int next = QualitySettings.vSyncCount == 0 ? 1 : 0;
            QualitySettings.vSyncCount = next;
            Debug.Log($"[Perf] vSyncCount = {next} ({(next == 0 ? "uncapped" : "synced")}). " +
                      "Quality setting reverts on Play-mode exit.");
        }

        // -----------------------------------------------------------------
        // Profiler frame capture
        // -----------------------------------------------------------------

        [MenuItem(kRoot + "Capture Profiler Frame")]
        private static void CaptureProfilerFrame()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[Perf] Hit Play first.");
                return;
            }
            // EnableBinaryLog is the modern path; the synchronous capture
            // hangs the editor for a moment but produces the most accurate
            // single-frame breakdown short of native tooling.
            UnityEngine.Profiling.Profiler.maxUsedMemory = 256 * 1024 * 1024;
            UnityEngine.Profiling.Profiler.enabled = true;
            Debug.Log("[Perf] Profiler enabled. Open Window > Analysis > Profiler and grab the next frame.");
        }
    }
}
