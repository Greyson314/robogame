using Robogame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Procedural main-menu HUD. Lives in MainMenu.unity, the first scene
    /// loaded after Bootstrap. Three primary actions:
    /// <list type="bullet">
    ///   <item><b>Begin</b> — load Garage.unity (entry into the build / loadout flow).</item>
    ///   <item><b>Settings</b> — defer to the persistent <see cref="SettingsHud"/> panel
    ///     that already lives on the Bootstrap GameObject.</item>
    ///   <item><b>Take Leave</b> — Application.Quit. No-op in editor (logs instead).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built procedurally in UGUI to match <see cref="SettingsHud"/> /
    /// <see cref="SceneTransitionHud"/>. Fade-in on enable, version readout
    /// bottom-right.
    /// </para>
    /// <para>
    /// TRACE[DOC:research/ui-design-handoff]: layout and treatments follow
    /// the unified-menu reference — paper ground with drafting grid,
    /// registration marks, ink brush title underline with vermilion splats,
    /// ink-blob primary button, wash-underline secondary buttons, and a
    /// mirror-written flavor line (the da Vinci easter egg).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("Scene name to load when Begin is pressed. Must be in Build Settings.")]
        [SerializeField] private string _startScene = "Garage";

        [Tooltip("Game title shown at the top of the menu.")]
        [SerializeField] private string _title = "Robogame";

        [Tooltip("Optional tagline shown beneath the title.")]
        [SerializeField] private string _tagline = "A Bestiary of Contraptions";

        private CanvasGroup _fadeGroup;
        private float _fadeT;
        private const float FadeDuration = 0.45f;

        private void Awake()
        {
            EnsureEventSystem();
            BuildPanel();
        }

        private void Update()
        {
            // Fade-in. Cheap and high-impact; uses unscaled time so a paused
            // game (Time.timeScale=0) still animates the menu.
            if (_fadeGroup != null && _fadeT < 1f)
            {
                _fadeT = Mathf.Min(1f, _fadeT + Time.unscaledDeltaTime / FadeDuration);
                _fadeGroup.alpha = Mathf.SmoothStep(0f, 1f, _fadeT);
            }
        }

        // -----------------------------------------------------------------
        // EventSystem (shared with other HUDs)
        // -----------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            EventSystem es = Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Destroy(legacy);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            HudEventSystem.DisableKeyboardNavigation(es);
        }

        // -----------------------------------------------------------------
        // Panel construction
        // -----------------------------------------------------------------

        private void BuildPanel()
        {
            // Top-level canvas — sits below SettingsHud's order so the
            // Settings panel layers cleanly on top when invoked.
            var canvasGO = new GameObject("MainMenuCanvas");
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            _fadeGroup = canvasGO.AddComponent<CanvasGroup>();
            _fadeGroup.alpha = 0f;
            _fadeT = 0f;

            // Paper ground — baked radial falloff, full-bleed.
            var bg = NewChild("Paper", canvasGO.transform);
            FillParent(bg);
            var paperImg = bg.AddComponent<Image>();
            paperImg.sprite = InkKit.Paper;
            paperImg.color = Color.white;

            // Faint drafting grid over the paper.
            var grid = NewChild("Grid", canvasGO.transform);
            FillParent(grid);
            var gridImg = grid.AddComponent<Image>();
            gridImg.sprite = InkKit.GridTile;
            gridImg.type = Image.Type.Tiled;
            gridImg.color = UguiPalette.GridLine;
            gridImg.raycastTarget = false;

            // Registration marks at the four corners — the "printed off the
            // drafting table" signature.
            AddRegMark(canvasGO.transform, new Vector2(0f, 0f), new Vector2(30f, 30f));
            AddRegMark(canvasGO.transform, new Vector2(1f, 0f), new Vector2(-30f, 30f));
            AddRegMark(canvasGO.transform, new Vector2(0f, 1f), new Vector2(30f, -30f));
            AddRegMark(canvasGO.transform, new Vector2(1f, 1f), new Vector2(-30f, -30f));

            // Centered content column.
            var column = NewChild("Column", canvasGO.transform);
            var colRT = column.GetComponent<RectTransform>();
            colRT.anchorMin = new Vector2(0.5f, 0.5f);
            colRT.anchorMax = new Vector2(0.5f, 0.5f);
            colRT.pivot = new Vector2(0.5f, 0.5f);
            colRT.sizeDelta = new Vector2(560f, 620f);
            colRT.anchoredPosition = Vector2.zero;

            // Title — Title Case, never all caps.
            var title = AddText(column.transform, _title, 84, InkKit.Display, FontStyle.Normal, TextAnchor.MiddleCenter,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -150f), offsetMax: Vector2.zero,
                color: HudStyles.TextPrimary);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            // Never wrap/clip the wordmark — render it full-width on one line.
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;

            // Full-column ink brush underline beneath the title…
            var underline = NewChild("TitleUnderline", column.transform);
            var tuRT = underline.GetComponent<RectTransform>();
            tuRT.anchorMin = new Vector2(0.5f, 1f);
            tuRT.anchorMax = new Vector2(0.5f, 1f);
            tuRT.pivot = new Vector2(0.5f, 1f);
            tuRT.sizeDelta = new Vector2(560f, 14f);
            // Sits clear below the wordmark — some faces render glyphs low
            // in their line box, so the geometric text bottom is not the
            // visual bottom.
            tuRT.anchoredPosition = new Vector2(0f, -200f);
            tuRT.localRotation = Quaternion.Euler(0f, 0f, -0.5f);
            var ulImg = underline.AddComponent<Image>();
            ulImg.sprite = InkKit.Underline;
            ulImg.color = UguiPalette.Ink;
            ulImg.raycastTarget = false;

            // …with two small vermilion splats off its right end.
            AddSplat(column.transform, new Vector2(296f, -198f), 13f);
            AddSplat(column.transform, new Vector2(316f, -206f), 8f);

            // Tagline — annotation voice, Cardo italic.
            if (!string.IsNullOrEmpty(_tagline))
            {
                var tag = AddText(column.transform, _tagline, 24, InkKit.Annotation, FontStyle.Italic, TextAnchor.MiddleCenter,
                    anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                    offsetMin: new Vector2(0f, -272f), offsetMax: new Vector2(0f, -228f),
                    color: HudStyles.TextMuted);
                tag.rectTransform.pivot = new Vector2(0.5f, 1f);
            }

            // Button stack: one ink-blob primary, two wash-underline secondaries.
            float stackTopY = -300f;
            BuildPrimaryButton(column.transform, "Begin",
                anchoredPos: new Vector2(0f, stackTopY),
                onClick: HandleStart);
            BuildSecondaryButton(column.transform, "Settings",
                anchoredPos: new Vector2(0f, stackTopY - 96f),
                washHover: UguiPalette.Accent, onClick: HandleSettings);
            BuildSecondaryButton(column.transform, "Take Leave",
                anchoredPos: new Vector2(0f, stackTopY - 156f),
                washHover: new Color(UguiPalette.Vermilion.r, UguiPalette.Vermilion.g, UguiPalette.Vermilion.b, 0.35f),
                onClick: HandleExit);

            // Bottom-left: control hint + mirror-written flavor line.
            // 56px inset keeps them clear of the corner registration marks.
            AddText(canvasGO.transform, "Esc — Open Settings",
                18, InkKit.Display, FontStyle.Normal, TextAnchor.LowerLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f),
                offsetMin: new Vector2(56f, 44f), offsetMax: new Vector2(400f, 68f),
                color: HudStyles.TextMuted);
            var flavor = AddText(canvasGO.transform, "the frame remembers every fall",
                17, InkKit.Annotation, FontStyle.Italic, TextAnchor.LowerLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f),
                offsetMin: new Vector2(56f, 18f), offsetMax: new Vector2(400f, 40f),
                color: HudStyles.TextMuted);
            // Mirror writing — flavor lines only. TRACE[DOC:research/ui-design-handoff]
            flavor.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            var flavorCol = flavor.color; flavorCol.a = 0.6f; flavor.color = flavorCol;

            // Bottom-right: version + build info, Cardo italic.
            string version = string.IsNullOrEmpty(Application.version) ? "dev" : Application.version;
            string platform = Application.platform.ToString();
            AddText(canvasGO.transform, $"v{version}  ·  {platform}",
                17, InkKit.Annotation, FontStyle.Italic, TextAnchor.LowerRight,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f),
                offsetMin: new Vector2(-400f, 18f), offsetMax: new Vector2(-56f, 40f),
                color: HudStyles.TextMuted);
        }

        // -----------------------------------------------------------------
        // Button handlers
        // -----------------------------------------------------------------

        private void HandleStart()
        {
            if (string.IsNullOrEmpty(_startScene))
            {
                Debug.LogWarning("[Robogame] MainMenu: _startScene is empty.");
                return;
            }
            SceneManager.LoadScene(_startScene, LoadSceneMode.Single);
        }

        private static void HandleSettings()
        {
            // SettingsHud lives on the persistent Bootstrap; reach it via
            // FindAnyObjectByType so the MainMenu controller doesn't need a
            // serialised reference (Bootstrap is in a different scene).
            SettingsHud hud = Object.FindAnyObjectByType<SettingsHud>();
            if (hud == null)
            {
                Debug.LogWarning("[Robogame] MainMenu: no SettingsHud in scene. Did Bootstrap.unity load before MainMenu?");
                return;
            }
            hud.Open();
        }

        private static void HandleExit()
        {
#if UNITY_EDITOR
            // In editor, Application.Quit doesn't exit the editor — flip
            // playmode off instead so the button has visible effect.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // -----------------------------------------------------------------
        // UGUI primitives
        // -----------------------------------------------------------------

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static void FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddRegMark(Transform parent, Vector2 anchor, Vector2 inset)
        {
            var go = NewChild("RegMark", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(16f, 16f);
            rt.anchoredPosition = inset;
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.RegMark;
            img.color = new Color(UguiPalette.Ink.r, UguiPalette.Ink.g, UguiPalette.Ink.b, 0.45f);
            img.raycastTarget = false;
        }

        private static void AddSplat(Transform parent, Vector2 pos, float size)
        {
            var go = NewChild("Splat", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.Splat;
            img.color = UguiPalette.Vermilion;
            img.raycastTarget = false;
        }

        private static Text AddText(Transform parent, string content, int size,
            Font font, FontStyle style, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax,
            Color color)
        {
            var go = NewChild("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var t = go.AddComponent<Text>();
            t.text = content;
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            // Legacy Text truncates the whole line when a font's line box
            // overruns a small rect (bit us hard with Yuji Syuku's CJK
            // metrics) — every menu string is short static copy, so
            // overflow is always safe here.
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Primary action: solid ink brush blob, cream label, slight rotation.</summary>
        private void BuildPrimaryButton(Transform parent, string label, Vector2 anchoredPos, System.Action onClick)
        {
            var go = NewChild($"Btn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(360f, 72f);
            rt.anchoredPosition = anchoredPos;
            rt.localRotation = Quaternion.Euler(0f, 0f, -0.7f);
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.BrushBlob;
            img.color = UguiPalette.Ink;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.normalColor      = UguiPalette.Ink;
            cols.highlightedColor = UguiPalette.InkHover;
            cols.pressedColor     = Color.black;
            cols.selectedColor    = UguiPalette.Ink;
            cols.colorMultiplier  = 1f;
            cols.fadeDuration     = 0.10f;
            btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());
            btn.onClick.AddListener(PlayUiClick);

            var t = AddText(go.transform, label, 30, InkKit.Display, FontStyle.Normal, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, UguiPalette.CreamText);
            t.raycastTarget = false;
        }

        /// <summary>Secondary action: plain ink text over an indigo wash underline; hover deepens the wash.</summary>
        private void BuildSecondaryButton(Transform parent, string label, Vector2 anchoredPos, Color washHover, System.Action onClick)
        {
            var go = NewChild($"Btn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(280f, 48f);
            rt.anchoredPosition = anchoredPos;
            // Invisible raycast face so the whole rect is clickable.
            var face = go.AddComponent<Image>();
            face.color = Color.clear;

            // Wash underline sits behind the label's lower half.
            var wash = NewChild("Wash", go.transform);
            var wrt = wash.GetComponent<RectTransform>();
            wrt.anchorMin = new Vector2(0.5f, 0f);
            wrt.anchorMax = new Vector2(0.5f, 0f);
            wrt.pivot = new Vector2(0.5f, 0f);
            wrt.sizeDelta = new Vector2(210f, 14f);
            wrt.anchoredPosition = new Vector2(0f, 4f);
            var washImg = wash.AddComponent<Image>();
            // BarFill (not Underline): the wash behind a secondary button is
            // a bold swipe, and the thin Underline sprite all but vanished
            // at this size against the paper.
            washImg.sprite = InkKit.BarFill;
            Color washIdle = UguiPalette.Accent; washIdle.a = 0.65f;
            washImg.color = washIdle;
            washImg.raycastTarget = false;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = washImg;
            ColorBlock cols = btn.colors;
            cols.normalColor      = washIdle;
            cols.highlightedColor = washHover;
            cols.pressedColor     = UguiPalette.AccentPressed;
            cols.selectedColor    = washIdle;
            cols.colorMultiplier  = 1f;
            cols.fadeDuration     = 0.10f;
            btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());
            btn.onClick.AddListener(PlayUiClick);

            var t = AddText(go.transform, label, 26, InkKit.Display, FontStyle.Normal, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, HudStyles.TextPrimary);
            t.raycastTarget = false;
        }

        // Method-group hook so per-button AddListener doesn't allocate a
        // closure. Static for the same reason it lives in SettingsHud /
        // SceneTransitionHud.
        private static void PlayUiClick()
            => Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiClick);
    }
}
