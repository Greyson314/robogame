using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>
    /// The ink-wipe scene transition: a brush stroke crosses the frame,
    /// the next scene loads behind the ink, the stroke continues off.
    /// One call — <see cref="To"/> — replaces a hard
    /// <see cref="SceneManager.LoadScene(string)"/> cut anywhere a screen
    /// is a "sheet" (menu → garage, garage → arena).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Self-hosting overlay: builds its own top-sorted canvas, survives the
    /// load via DontDestroyOnLoad, destroys itself after the out-sweep. The
    /// cover Image raycast-blocks input for the whole ride. Reduced motion
    /// swaps the sweep for a plain quick fade (same cover, no travel).
    /// </para>
    /// <para>
    /// The load itself is the same synchronous
    /// <see cref="SceneManager.LoadScene(string)"/> the project already
    /// uses — the wipe hides the hitch rather than pretending to stream.
    /// </para>
    /// </remarks>
    // TRACE[DOC:research/ui-design-handoff-motion]: Page verb / ink wipe.
    [DisallowMultipleComponent]
    public sealed class PageWipe : MonoBehaviour
    {
        private enum Phase { In, Hold, Out }

        private static bool s_active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_active = false;

        private RectTransform _cover;
        private CanvasGroup _coverGroup;
        private CanvasGroup _label;
        private string _scene;
        private Phase _phase;
        private float _t;
        private float _travel;      // reference px from off-left to centered
        private bool _loaded;
        private bool _reduced;

        private const float InDur = UiMotion.Page * 0.62f;
        private const float HoldDur = 0.55f;
        private const float OutDur = UiMotion.Page * 0.62f;
        private const float LoadAt = 0.16f;  // into Hold, after the label has a frame to show

        /// <summary>
        /// Sweep ink across the screen, load <paramref name="sceneName"/>
        /// under it, sweep off. The sheet stamp ("no. 02 — The Garage")
        /// flashes while covered.
        /// </summary>
        public static void To(string sceneName, string sheetNo, string sheetTitle)
        {
            if (s_active) return;
            if (string.IsNullOrEmpty(sceneName)) return;
            s_active = true;

            var root = new GameObject("[PageWipe]");
            DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var wipe = root.AddComponent<PageWipe>();
            wipe._scene = sceneName;
            wipe.BuildOverlay(sheetNo, sheetTitle);
        }

        private void BuildOverlay(string sheetNo, string sheetTitle)
        {
            _reduced = UiMotion.Reduced;
            var canvasRt = (RectTransform)transform;

            // Cover: full-stretch ink with side bleed; we slide the whole
            // rect by anchoredPosition, so it needs to overhang both edges.
            var coverGo = UguiKit.NewChild("Cover", transform);
            _cover = (RectTransform)coverGo.transform;
            _cover.anchorMin = Vector2.zero;
            _cover.anchorMax = Vector2.one;
            _cover.offsetMin = new Vector2(-260f, -20f);
            _cover.offsetMax = new Vector2(260f, 20f);
            var coverImg = coverGo.AddComponent<Image>();
            coverImg.color = UguiPalette.Ink;
            coverImg.raycastTarget = true; // input blocked during the ride

            // Brush edges: leading (right) and trailing (left, mirrored).
            var lead = UguiKit.NewChild("BrushLead", _cover);
            var leadRt = (RectTransform)lead.transform;
            leadRt.anchorMin = new Vector2(1f, 0f);
            leadRt.anchorMax = new Vector2(1f, 1f);
            leadRt.pivot = new Vector2(0f, 0.5f);
            leadRt.sizeDelta = new Vector2(96f, 0f);
            leadRt.anchoredPosition = new Vector2(-1f, 0f);
            var leadImg = lead.AddComponent<Image>();
            leadImg.sprite = InkKit.WipeBrush;
            leadImg.color = UguiPalette.Ink;
            leadImg.raycastTarget = false;

            var trail = UguiKit.NewChild("BrushTrail", _cover);
            var trailRt = (RectTransform)trail.transform;
            trailRt.anchorMin = new Vector2(0f, 0f);
            trailRt.anchorMax = new Vector2(0f, 1f);
            trailRt.pivot = new Vector2(1f, 0.5f);
            trailRt.sizeDelta = new Vector2(96f, 0f);
            trailRt.anchoredPosition = new Vector2(1f, 0f);
            trailRt.localScale = new Vector3(-1f, 1f, 1f); // mirror the jag
            var trailImg = trail.AddComponent<Image>();
            trailImg.sprite = InkKit.WipeBrush;
            trailImg.color = UguiPalette.Ink;
            trailImg.raycastTarget = false;

            // Sheet stamp — sits at canvas center (not on the cover), fades
            // in only while the frame is fully inked.
            var labelGo = UguiKit.NewChild("Sheet", transform);
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.sizeDelta = new Vector2(900f, 220f);
            labelRt.anchoredPosition = Vector2.zero;
            _label = labelGo.AddComponent<CanvasGroup>();
            _label.alpha = 0f;
            _label.blocksRaycasts = false;

            UguiKit.AddText(labelGo.transform, $"sheet {sheetNo}", InkKit.Annotation, 15, FontStyle.Italic,
                UguiPalette.TextDim, TextAnchor.MiddleCenter,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -60f), offsetMax: new Vector2(0f, -10f),
                raycastTarget: false);
            UguiKit.AddText(labelGo.transform, sheetTitle, InkKit.Display, 58, FontStyle.Normal,
                UguiPalette.CreamText, TextAnchor.MiddleCenter,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                raycastTarget: false, horizontalOverflow: true);

            // Travel distance: canvas width + bleed + brush overhang.
            Canvas.ForceUpdateCanvases();
            _travel = canvasRt.rect.width + 520f + 200f;

            if (_reduced)
            {
                // No sweep: quick fade over, load, fade off.
                _coverGroup = coverGo.AddComponent<CanvasGroup>();
                _coverGroup.alpha = 0f;
                UiTween.Alpha(_coverGroup, 1f, 0.15f, UiMotion.Ease.Settle);
            }
            else
            {
                _cover.anchoredPosition = new Vector2(-_travel, 0f);
                UiTween.Move(_cover, Vector2.zero, InDur, UiMotion.Ease.Page);
            }
            UiCues.PageTurn();
            _phase = Phase.In;
            _t = 0f;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            switch (_phase)
            {
                case Phase.In:
                    if (_t >= (_reduced ? 0.16f : InDur))
                    {
                        UiCues.PageTurnLand();
                        UiTween.Alpha(_label, 1f, 0.12f, UiMotion.Ease.Settle);
                        _phase = Phase.Hold;
                        _t = 0f;
                    }
                    break;

                case Phase.Hold:
                    if (!_loaded && _t >= LoadAt)
                    {
                        _loaded = true;
                        SceneManager.LoadScene(_scene, LoadSceneMode.Single);
                    }
                    if (_t >= HoldDur)
                    {
                        UiTween.Alpha(_label, 0f, 0.10f, UiMotion.Ease.Settle);
                        if (_reduced) UiTween.Alpha(_coverGroup, 0f, 0.18f, UiMotion.Ease.Settle);
                        else UiTween.Move(_cover, new Vector2(_travel, 0f), OutDur, UiMotion.Ease.Page);
                        _phase = Phase.Out;
                        _t = 0f;
                    }
                    break;

                case Phase.Out:
                    if (_t >= (_reduced ? 0.20f : OutDur))
                    {
                        s_active = false;
                        Destroy(gameObject);
                    }
                    break;
            }
        }
    }
}
