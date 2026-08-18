using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Robogame.Core
{
    /// <summary>
    /// The ink-wipe scene transition, painted serpentine: horizontal brush
    /// bands sweep left→right, right→left, alternating down the page —
    /// painting over the current scene stroke by stroke. The next scene
    /// loads under the full cover, then the strokes continue off the other
    /// side. One call — <see cref="To"/> — replaces a hard
    /// <see cref="SceneManager.LoadScene(string)"/> cut anywhere a screen
    /// is a "sheet" (menu → garage, garage → arena).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Self-hosting overlay: builds its own top-sorted canvas, survives the
    /// load via DontDestroyOnLoad, destroys itself after the out-sweep. A
    /// full-screen invisible Image raycast-blocks input for the whole ride.
    /// Reduced motion swaps the strokes for a plain quick fade.
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

        private const int Bands = 5;
        private const float BandStagger = 0.085f;   // top stroke leads, each next follows
        private const float BandDur = UiMotion.Page * 0.5f;
        private const float HoldDur = 0.55f;
        private const float LoadAt = 0.16f;         // into Hold, after the label has a frame to show

        private RectTransform[] _bands;
        private float[] _bandDir;                   // +1 = entered from the left, exits right
        private CanvasGroup _coverGroup;            // reduced-motion path only
        private GameObject _coverRoot;
        private CanvasGroup _label;
        private string _scene;
        private Phase _phase;
        private float _t;
        private float _travel;
        private bool _loaded;
        private bool _reduced;

        private float InDur => _reduced ? 0.16f : BandDur + (Bands - 1) * BandStagger;
        private float OutDur => _reduced ? 0.20f : BandDur + (Bands - 1) * BandStagger;

        /// <summary>
        /// Paint over the screen in alternating strokes, load
        /// <paramref name="sceneName"/> under the ink, paint off. The sheet
        /// stamp ("no. 02 — The Garage") flashes while covered.
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

            // Input blocker — invisible, full-screen, alive for the whole ride.
            var blocker = UguiKit.NewChild("Blocker", transform);
            Stretch((RectTransform)blocker.transform, 0f, 0f);
            var blockImg = blocker.AddComponent<Image>();
            blockImg.color = Color.clear;
            blockImg.raycastTarget = true;

            // Cover root holds the strokes (or the fade rect when reduced).
            _coverRoot = UguiKit.NewChild("Cover", transform);
            Stretch((RectTransform)_coverRoot.transform, 0f, 0f);

            Canvas.ForceUpdateCanvases();
            _travel = canvasRt.rect.width + 520f + 200f;

            if (_reduced)
            {
                var fade = UguiKit.NewChild("Fade", _coverRoot.transform);
                Stretch((RectTransform)fade.transform, 260f, 20f);
                var fadeImg = fade.AddComponent<Image>();
                fadeImg.color = UguiPalette.Ink;
                fadeImg.raycastTarget = false;
                _coverGroup = _coverRoot.AddComponent<CanvasGroup>();
                _coverGroup.alpha = 0f;
                UiTween.Alpha(_coverGroup, 1f, 0.15f, UiMotion.Ease.Settle);
            }
            else
            {
                // Serpentine strokes: band 0 at the top enters from the
                // left; each band below alternates, staggered like a brush
                // reloading between passes.
                _bands = new RectTransform[Bands];
                _bandDir = new float[Bands];
                for (int i = 0; i < Bands; i++)
                {
                    float dir = (i % 2 == 0) ? 1f : -1f; // +1: enters left → exits right
                    _bandDir[i] = dir;

                    var band = UguiKit.NewChild($"Stroke{i}", _coverRoot.transform);
                    var rt = (RectTransform)band.transform;
                    rt.anchorMin = new Vector2(0f, 1f - (i + 1) / (float)Bands);
                    rt.anchorMax = new Vector2(1f, 1f - i / (float)Bands);
                    // Side bleed for the brush edges; 1px vertical overlap
                    // so band seams never show a hairline of the scene.
                    rt.offsetMin = new Vector2(-260f, -1f);
                    rt.offsetMax = new Vector2(260f, 1f);
                    var img = band.AddComponent<Image>();
                    img.color = UguiPalette.Ink;
                    img.raycastTarget = false;

                    AddBrushEdge(rt, leading: true, dir);
                    AddBrushEdge(rt, leading: false, dir);

                    rt.anchoredPosition = new Vector2(-dir * _travel, 0f);
                    UiTween.Move(rt, Vector2.zero, BandDur, UiMotion.Ease.Page, i * BandStagger);
                    _bands[i] = rt;
                }
            }

            // Sheet stamp — canvas center, fades in only while fully inked.
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

            UiCues.PageTurn();
            _phase = Phase.In;
            _t = 0f;
        }

        /// <summary>The jagged dry-brush edge on a stroke band. Leading = the moving front.</summary>
        private void AddBrushEdge(RectTransform band, bool leading, float dir)
        {
            // A rightward stroke's front is its right edge with the jag
            // pointing right; its back edge mirrors. Leftward strokes swap.
            bool onRight = leading ? dir > 0f : dir < 0f;
            var go = UguiKit.NewChild(leading ? "EdgeLead" : "EdgeTrail", band);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(onRight ? 1f : 0f, 0f);
            rt.anchorMax = new Vector2(onRight ? 1f : 0f, 1f);
            rt.pivot = new Vector2(onRight ? 0f : 1f, 0.5f);
            rt.sizeDelta = new Vector2(96f, 0f);
            rt.anchoredPosition = new Vector2(onRight ? -1f : 1f, 0f);
            // WipeBrush jags on its right; mirror it for left-facing edges.
            rt.localScale = new Vector3(onRight ? 1f : -1f, 1f, 1f);
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.WipeBrush;
            img.color = UguiPalette.Ink;
            img.raycastTarget = false;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            switch (_phase)
            {
                case Phase.In:
                    if (_t >= InDur)
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
                        if (_reduced)
                        {
                            UiTween.Alpha(_coverGroup, 0f, 0.18f, UiMotion.Ease.Settle);
                        }
                        else
                        {
                            // Each stroke keeps painting the way it was
                            // going, top to bottom again.
                            for (int i = 0; i < Bands; i++)
                                UiTween.Move(_bands[i], new Vector2(_bandDir[i] * _travel, 0f),
                                    BandDur, UiMotion.Ease.Page, i * BandStagger);
                        }
                        _phase = Phase.Out;
                        _t = 0f;
                    }
                    break;

                case Phase.Out:
                    if (_t >= OutDur)
                    {
                        s_active = false;
                        Destroy(gameObject);
                    }
                    break;
            }
        }

        private static void Stretch(RectTransform rt, float bleedX, float bleedY)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-bleedX, -bleedY);
            rt.offsetMax = new Vector2(bleedX, bleedY);
        }
    }
}
