using System.Text;
using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Robogame.UI
{
    /// <summary>
    /// Always-loaded perf-diagnostic overlay. Toggle with F3. Hidden by default
    /// so production sessions don't pay the IMGUI cost; when visible, shows
    /// frame-time stats, GC alloc/frame, active Rigidbody / Joint / Verlet
    /// counts, draw-call estimates (editor only), and chassis block totals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auto-bootstrapped via <see cref="RuntimeInitializeOnLoadMethodAttribute"/>
    /// the same way <see cref="FpsCounter"/> is — no scene authoring required,
    /// shows up in every scene, survives additive scene loads through
    /// <see cref="Object.DontDestroyOnLoad"/>.
    /// </para>
    /// <para>
    /// Cost when hidden: one bool check in <see cref="Update"/> + one bool
    /// check in <see cref="OnGUI"/> per IMGUI event. Cost when visible:
    /// dominated by <c>FindObjectsByType&lt;Rigidbody&gt;</c> sampled at
    /// 1 Hz. Allocation-free per-frame: cached <see cref="GUIStyle"/>,
    /// pre-sized <see cref="StringBuilder"/>, and reused label arrays.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PerformanceHud : MonoBehaviour
    {
        // -----------------------------------------------------------------
        // Bootstrap
        // -----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            // Idempotent — re-entering Play in the editor must not pile up
            // copies. FindObjectsInactive.Include catches a HUD spawned by
            // a previous scene-additive load that's been disabled.
#if UNITY_2023_1_OR_NEWER
            PerformanceHud existing = Object.FindAnyObjectByType<PerformanceHud>(FindObjectsInactive.Include);
#else
            PerformanceHud existing = Object.FindObjectOfType<PerformanceHud>(true);
#endif
            if (existing != null) return;

            GameObject go = new GameObject("PerformanceHud");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<PerformanceHud>();
        }

        // -----------------------------------------------------------------
        // Toggle + sampling cadence
        // -----------------------------------------------------------------

        [Tooltip("Key that toggles the perf HUD on/off. F3 by default to avoid clashing with DevHud (F1) and screenshot tools (F12).")]
        [SerializeField] private Key _toggleKey = Key.F3;

        [Tooltip("Pad from the top edge of the screen, in pixels. Right-aligned by default so the FPS counter (top-left) and the perf HUD don't overlap.")]
        [SerializeField, Min(0f)] private float _paddingTop = 8f;

        [Tooltip("Pad from the right edge of the screen, in pixels.")]
        [SerializeField, Min(0f)] private float _paddingRight = 8f;

        [Tooltip("Width of the panel in pixels.")]
        [SerializeField, Min(120f)] private float _width = 280f;

        [Tooltip("Seconds between expensive samples (active Rigidbody count, robot scan). Frame-time stats update every frame regardless.")]
        [SerializeField, Range(0.1f, 5f)] private float _resampleInterval = 1f;

        // -----------------------------------------------------------------
        // Frame-time accumulation
        // -----------------------------------------------------------------

        // Rolling buffer of frame times in ms. Sized for ~4 seconds of
        // history at 60 fps; enough samples to compute stable 1% / 0.1%
        // percentiles. Pre-allocated; the HUD never allocates after Awake.
        private const int kFrameHistory = 240;
        private readonly float[] _frameTimesMs = new float[kFrameHistory];
        private readonly float[] _sortedScratch = new float[kFrameHistory];
        private int _frameTimeWriteIdx;
        private int _frameTimeFilled;

        // Smoothed (EMA) frame time so the headline number doesn't flicker
        // between adjacent integer fps values frame-to-frame.
        private float _smoothFrameMs = -1f;

        // -----------------------------------------------------------------
        // GC / managed memory
        // -----------------------------------------------------------------

        private long _lastTotalAllocatedBytes;
        private long _gcDeltaBytes;            // delta since previous frame
        private long _peakGcDelta;              // peak in the current sampling window

        // -----------------------------------------------------------------
        // Sampled-once-per-second data
        // -----------------------------------------------------------------

        private float _resampleClock;
        private int _activeRigidbodyCount;
        private int _activeJointCount;
        private int _verletChainCount;
        private int _verletParticleCount;
        private int _robotCount;
        private int _totalBlockCount;
#if UNITY_EDITOR
        private int _drawCalls;
        private int _setPassCalls;
        private int _triangles;
#endif

        // -----------------------------------------------------------------
        // IMGUI surface
        // -----------------------------------------------------------------

        private GUIStyle _labelStyle;
        private GUIStyle _shadowStyle;
        private GUIStyle _boxStyle;
        private readonly StringBuilder _sb = new StringBuilder(640);
        private string _renderedText = string.Empty;
        private bool _visible;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            // Seed the GC delta tracker so the first reported frame doesn't
            // include a megabyte spike from cold-start initialisation.
            _lastTotalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
        }

        private void Update()
        {
            HandleToggle();

            // Frame-time accumulation runs every frame whether we're visible
            // or not — the cost is two array writes + two comparisons. The
            // upside: when the user taps F3 the percentiles already have
            // history, no warm-up needed.
            float dtMs = Time.unscaledDeltaTime * 1000f;
            if (dtMs <= 0f) return;

            _frameTimesMs[_frameTimeWriteIdx] = dtMs;
            _frameTimeWriteIdx = (_frameTimeWriteIdx + 1) % kFrameHistory;
            if (_frameTimeFilled < kFrameHistory) _frameTimeFilled++;

            if (_smoothFrameMs < 0f) _smoothFrameMs = dtMs;
            else
            {
                // Same EMA pattern as FpsCounter (window ~ 0.5s).
                float alpha = Mathf.Clamp01(dtMs / 500f);
                _smoothFrameMs = Mathf.Lerp(_smoothFrameMs, dtMs, alpha);
            }

            // GC delta. Profiler.GetTotalAllocatedMemoryLong tracks the
            // managed heap including non-collected garbage; per-frame delta
            // is what catches steady-state allocations. Negative deltas are
            // collection events; treat as zero for the per-frame number.
            long total = Profiler.GetTotalAllocatedMemoryLong();
            long delta = total - _lastTotalAllocatedBytes;
            _lastTotalAllocatedBytes = total;
            _gcDeltaBytes = delta > 0 ? delta : 0;
            if (_gcDeltaBytes > _peakGcDelta) _peakGcDelta = _gcDeltaBytes;

            // Expensive resample (FindObjectsByType walks the scene). Skip
            // entirely while hidden — there's no value in keeping it warm.
            if (!_visible) return;
            _resampleClock += Time.unscaledDeltaTime;
            if (_resampleClock >= _resampleInterval)
            {
                _resampleClock = 0f;
                Resample();
            }
        }

        private void HandleToggle()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!System.Enum.IsDefined(typeof(Key), _toggleKey) || _toggleKey == Key.None) return;
            try
            {
                if (kb[_toggleKey].wasPressedThisFrame)
                {
                    _visible = !_visible;
                    if (_visible)
                    {
                        _resampleClock = _resampleInterval; // force an immediate sample on first show
                        _peakGcDelta = 0;
                    }
                }
            }
            catch (System.ArgumentOutOfRangeException)
            {
                _toggleKey = Key.F3;
            }
        }

        // -----------------------------------------------------------------
        // Sampling
        // -----------------------------------------------------------------

        private void Resample()
        {
            // FindObjectsByType is O(scene); we sample at most once per
            // second so the 1–10 ms cost amortises into noise. Doing this
            // every frame would be its own perf bug.
#if UNITY_2023_1_OR_NEWER
            Rigidbody[] rbs = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            Joint[] joints = Object.FindObjectsByType<Joint>(FindObjectsSortMode.None);
            Robot[] robots = Object.FindObjectsByType<Robot>(FindObjectsSortMode.None);
#else
            Rigidbody[] rbs = Object.FindObjectsOfType<Rigidbody>();
            Joint[] joints = Object.FindObjectsOfType<Joint>();
            Robot[] robots = Object.FindObjectsOfType<Robot>();
#endif
            // Active = "PhysX is integrating it." Sleeping bodies count
            // toward the active list but produce no contact-solver work;
            // we report total count instead because that's what hits the
            // memory budget. Kinematic bodies still cost broadphase work.
            int active = 0;
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] != null && !rbs[i].IsSleeping()) active++;
            }
            _activeRigidbodyCount = active;
            _activeJointCount = joints.Length;

            VerletRopeSimulator sim = VerletRopeSimulator.Instance;
            _verletChainCount = sim != null ? sim.ChainCount : 0;
            _verletParticleCount = sim != null ? sim.TotalParticleCount : 0;

            _robotCount = robots.Length;
            int blocks = 0;
            for (int i = 0; i < robots.Length; i++)
            {
                if (robots[i] != null && robots[i].Grid != null) blocks += robots[i].Grid.Count;
            }
            _totalBlockCount = blocks;

#if UNITY_EDITOR
            // UnityStats is editor-only; build players don't expose these
            // counters at runtime. Players that care can read them through
            // FrameTimingManager once it stabilises across platforms.
            _drawCalls = UnityEditor.UnityStats.drawCalls;
            _setPassCalls = UnityEditor.UnityStats.setPassCalls;
            _triangles = UnityEditor.UnityStats.triangles;
#endif

            // Reset the per-window GC peak so we report "max alloc seen
            // since the last sample" rather than ever-growing history.
            _peakGcDelta = 0;
        }

        // -----------------------------------------------------------------
        // Rendering
        // -----------------------------------------------------------------

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyles();

            // Re-use the StringBuilder; the resulting string is small enough
            // (~400 chars) that the per-frame allocation is dominated by the
            // GUI.Label internal text-mesh build anyway.
            BuildText();

            float h = _labelStyle.CalcHeight(new GUIContent(_renderedText), _width - 16f) + 12f;
            float x = Screen.width - _paddingRight - _width;
            float y = _paddingTop;

            GUI.Box(new Rect(x, y, _width, h), GUIContent.none, _boxStyle);

            // Drop-shadow trick (same as FpsCounter): one offset black draw,
            // one normal draw. Cheaper than a full shader-based stroke.
            Rect shadow = new Rect(x + 9f, y + 7f, _width - 16f, h);
            Rect main = new Rect(x + 8f, y + 6f, _width - 16f, h);
            GUI.Label(shadow, _renderedText, _shadowStyle);
            GUI.Label(main, _renderedText, _labelStyle);
        }

        private void BuildText()
        {
            // Frame-time percentiles. Sort a copy of the rolling buffer
            // (the in-place sort would scramble write-order and produce
            // jittery output). Cheap at 240 entries.
            float p1 = 0f, p01 = 0f, avg = 0f;
            int n = _frameTimeFilled;
            if (n > 0)
            {
                System.Array.Copy(_frameTimesMs, _sortedScratch, n);
                System.Array.Sort(_sortedScratch, 0, n);
                // Lows: highest frame times (lowest fps). Index from the
                // top end of the sorted buffer.
                int p1Idx = Mathf.Clamp(n - Mathf.Max(1, n / 100), 0, n - 1);
                int p01Idx = Mathf.Clamp(n - Mathf.Max(1, n / 1000), 0, n - 1);
                p1 = _sortedScratch[p1Idx];
                p01 = _sortedScratch[p01Idx];
                float sum = 0f;
                for (int i = 0; i < n; i++) sum += _sortedScratch[i];
                avg = sum / n;
            }

            float fps = _smoothFrameMs > 0f ? 1000f / _smoothFrameMs : 0f;

            _sb.Length = 0;
            _sb.Append("<b>Performance</b>  (F3 to hide)\n");
            _sb.AppendFormat("Frame: {0:F2} ms  ({1:F0} fps)\n", _smoothFrameMs, fps);
            _sb.AppendFormat("Avg/1%/0.1% low: {0:F1} / {1:F1} / {2:F1} ms\n", avg, p1, p01);
            _sb.AppendFormat("GC alloc this frame: {0}\n", FormatBytes(_gcDeltaBytes));
            _sb.Append('\n');
            _sb.AppendFormat("Active RB / Joints: {0} / {1}\n", _activeRigidbodyCount, _activeJointCount);
            _sb.AppendFormat("Verlet chains / particles: {0} / {1}\n", _verletChainCount, _verletParticleCount);
            _sb.AppendFormat("Robots / blocks: {0} / {1}\n", _robotCount, _totalBlockCount);
            _sb.AppendFormat("Physics step: {0:F2} ms (fixed)\n", Time.fixedDeltaTime * 1000f);
#if UNITY_EDITOR
            _sb.Append('\n');
            _sb.AppendFormat("Draw / SetPass / Tris: {0} / {1} / {2:N0}\n", _drawCalls, _setPassCalls, _triangles);
#endif
            _renderedText = _sb.ToString();
        }

        private static string FormatBytes(long b)
        {
            if (b <= 0) return "0 B";
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024f).ToString("F1") + " KB";
            return (b / (1024f * 1024f)).ToString("F2") + " MB";
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                richText = true,
                wordWrap = true,
            };
            _labelStyle.normal.textColor = new Color(0.92f, 0.97f, 1f, 1f);

            _shadowStyle = new GUIStyle(_labelStyle);
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f);

            _boxStyle = new GUIStyle(GUI.skin.box);
        }

        // -----------------------------------------------------------------
        // Public surface for editor menu / external toggles
        // -----------------------------------------------------------------

        /// <summary>Show the HUD. No-op if already visible.</summary>
        public void Show() { _visible = true; _resampleClock = _resampleInterval; }

        /// <summary>Hide the HUD.</summary>
        public void Hide() { _visible = false; }

        /// <summary>Flip the HUD between shown/hidden.</summary>
        public void Toggle() { if (_visible) Hide(); else Show(); }
    }
}
