using UnityEngine;

namespace Robogame.UI
{
    /// <summary>
    /// Always-on, top-left FPS readout. Lightweight IMGUI label so it
    /// sidesteps the UGUI canvas entirely and survives scaffold rebuilds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Independent from <see cref="DevHud"/> on purpose — DevHud is opt-in
    /// behind F1 and a designer toggling it shouldn't lose the perf number.
    /// We also draw a one-frame instantaneous FPS plus a smoothed average
    /// so a single hitch doesn't make the readout dance: the smoothed
    /// number is the headline value, the instantaneous is parenthetical
    /// for spotting spikes during physics-heavy frames (rotor + ropes,
    /// stress tower, etc — see <c>docs/PHYSICS_PLAN.md</c>).
    /// </para>
    /// <para>
    /// Cost is one <see cref="OnGUI"/> draw call per frame with no
    /// allocations on the hot path (cached label string updated only
    /// when the displayed values change by &gt;= 1 fps).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FpsCounter : MonoBehaviour
    {
        /// <summary>
        /// Runtime auto-bootstrap. Spawns a single hidden GameObject
        /// hosting the FPS counter on first scene load so the readout
        /// shows up in every scene without depending on the editor
        /// scaffolder having been re-run. Idempotent — guarded by a
        /// FindFirstObjectByType lookup so re-entering Play doesn't
        /// pile up duplicates, and DontDestroyOnLoad keeps it alive
        /// across additive scene loads (Garage ⇄ Arena).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
#if UNITY_2023_1_OR_NEWER
            FpsCounter existing = Object.FindAnyObjectByType<FpsCounter>(FindObjectsInactive.Include);
#else
            FpsCounter existing = Object.FindObjectOfType<FpsCounter>(true);
#endif
            if (existing != null) return;

            GameObject go = new GameObject("FpsCounter");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<FpsCounter>();
        }

        [Tooltip("Seconds the smoothed average integrates over. Higher = steadier number, slower to react.")]
        [SerializeField, Min(0.05f)] private float _smoothingWindow = 0.5f;

        [Tooltip("Pixel padding from the top-left corner.")]
        [SerializeField, Min(0f)] private float _padding = 8f;

        // Exponential-moving-average frame time in seconds. Seeded on the
        // first Update so we don't render "Inf FPS" for one frame.
        private float _smoothDt = -1f;
        private int _lastSmoothFps = -1;
        private int _lastInstFps = -1;
        private string _label = "FPS: --";
        private GUIStyle _style;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            if (_smoothDt < 0f)
            {
                _smoothDt = dt;
            }
            else
            {
                // EMA: alpha = dt / window, clamped so a single huge frame
                // can't fully overwrite history.
                float alpha = Mathf.Clamp01(dt / Mathf.Max(_smoothingWindow, 0.05f));
                _smoothDt = Mathf.Lerp(_smoothDt, dt, alpha);
            }

            int smoothFps = Mathf.RoundToInt(1f / Mathf.Max(_smoothDt, 1e-5f));
            int instFps = Mathf.RoundToInt(1f / Mathf.Max(dt, 1e-5f));

            // Only rebuild the string when the displayed value actually
            // changes — avoids per-frame GC churn from string.Format.
            if (smoothFps != _lastSmoothFps || instFps != _lastInstFps)
            {
                _lastSmoothFps = smoothFps;
                _lastInstFps = instFps;
                _label = $"FPS: {smoothFps}  ({instFps})";
            }
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                };
                _style.normal.textColor = Color.white;
            }

            // Cheap drop-shadow: draw the label twice, once offset by 1px
            // in black so it stays legible against bright sky / grass.
            Rect shadow = new Rect(_padding + 1f, _padding + 1f, 240f, 24f);
            Rect main = new Rect(_padding, _padding, 240f, 24f);

            Color prev = _style.normal.textColor;
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(shadow, _label, _style);
            _style.normal.textColor = prev;
            GUI.Label(main, _label, _style);
        }
    }
}
