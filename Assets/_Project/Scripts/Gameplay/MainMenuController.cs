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
    ///   <item><b>Start</b> — load Garage.unity (entry into the build / loadout flow).</item>
    ///   <item><b>Settings</b> — defer to the persistent <see cref="SettingsHud"/> panel
    ///     that already lives on the Bootstrap GameObject.</item>
    ///   <item><b>Exit</b> — Application.Quit. No-op in editor (logs instead).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built procedurally in UGUI to match <see cref="SettingsHud"/> /
    /// <see cref="SceneTransitionHud"/>. Fade-in on enable, hover lift on
    /// the buttons, version readout in the bottom-right corner.
    /// </para>
    /// <para>
    /// The persistent SettingsHud (instantiated by Bootstrap.unity) survives
    /// scene transitions so MainMenu's "Settings" button just calls into
    /// the existing instance — no duplicate UI to maintain.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("Scene name to load when Start is pressed. Must be in Build Settings.")]
        [SerializeField] private string _startScene = "Garage";

        [Tooltip("Game title shown at the top of the menu.")]
        [SerializeField] private string _title = "ROBOGAME";

        [Tooltip("Optional tagline shown beneath the title.")]
        [SerializeField] private string _tagline = "voxel combat sandbox";

        // -----------------------------------------------------------------
        // Palette (kept consistent with SettingsHud)
        // -----------------------------------------------------------------
        private static readonly Color s_bgColor       = new Color(0.04f, 0.05f, 0.08f, 1f);
        private static readonly Color s_panelColor    = new Color(0.06f, 0.07f, 0.10f, 0.93f);
        private static readonly Color s_accentOrange  = new Color(0.95f, 0.55f, 0.10f, 1f);
        private static readonly Color s_accentDim     = new Color(0.85f, 0.50f, 0.10f, 0.55f);
        private static readonly Color s_textColor     = Color.white;
        private static readonly Color s_textDim       = new Color(0.78f, 0.80f, 0.84f, 1f);
        private static readonly Color s_buttonBase    = new Color(0.10f, 0.13f, 0.18f, 1f);

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
        }

        // -----------------------------------------------------------------
        // Panel construction
        // -----------------------------------------------------------------

        private static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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

            // Solid full-bleed background.
            var bg = NewChild("Background", canvasGO.transform);
            FillParent(bg);
            bg.AddComponent<Image>().color = s_bgColor;

            // Top accent strip — ties visually to the in-game palette.
            var accent = NewChild("AccentTop", canvasGO.transform);
            var accRT = accent.GetComponent<RectTransform>();
            accRT.anchorMin = new Vector2(0f, 1f);
            accRT.anchorMax = new Vector2(1f, 1f);
            accRT.pivot = new Vector2(0.5f, 1f);
            accRT.sizeDelta = new Vector2(0f, 6f);
            accRT.anchoredPosition = Vector2.zero;
            accent.AddComponent<Image>().color = s_accentOrange;

            // Bottom accent strip (thinner, dimmer).
            var accentBottom = NewChild("AccentBottom", canvasGO.transform);
            var accBRT = accentBottom.GetComponent<RectTransform>();
            accBRT.anchorMin = new Vector2(0f, 0f);
            accBRT.anchorMax = new Vector2(1f, 0f);
            accBRT.pivot = new Vector2(0.5f, 0f);
            accBRT.sizeDelta = new Vector2(0f, 2f);
            accBRT.anchoredPosition = Vector2.zero;
            accentBottom.AddComponent<Image>().color = s_accentDim;

            // Centered content column.
            var column = NewChild("Column", canvasGO.transform);
            var colRT = column.GetComponent<RectTransform>();
            colRT.anchorMin = new Vector2(0.5f, 0.5f);
            colRT.anchorMax = new Vector2(0.5f, 0.5f);
            colRT.pivot = new Vector2(0.5f, 0.5f);
            colRT.sizeDelta = new Vector2(560f, 600f);
            colRT.anchoredPosition = Vector2.zero;

            // Title.
            var title = AddText(column.transform, _title, 110, FontStyle.Bold, TextAnchor.MiddleCenter,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -150f), offsetMax: Vector2.zero,
                color: s_textColor);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);

            // Title underline accent.
            var titleUnderline = NewChild("TitleUnderline", column.transform);
            var tuRT = titleUnderline.GetComponent<RectTransform>();
            tuRT.anchorMin = new Vector2(0.5f, 1f);
            tuRT.anchorMax = new Vector2(0.5f, 1f);
            tuRT.pivot = new Vector2(0.5f, 1f);
            tuRT.sizeDelta = new Vector2(220f, 4f);
            tuRT.anchoredPosition = new Vector2(0f, -158f);
            titleUnderline.AddComponent<Image>().color = s_accentOrange;

            // Tagline.
            if (!string.IsNullOrEmpty(_tagline))
            {
                var tag = AddText(column.transform, _tagline, 26, FontStyle.Italic, TextAnchor.MiddleCenter,
                    anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                    offsetMin: new Vector2(0f, -210f), offsetMax: new Vector2(0f, -170f),
                    color: s_textDim);
                tag.rectTransform.pivot = new Vector2(0.5f, 1f);
            }

            // Button stack.
            const float btnW = 320f;
            const float btnH = 64f;
            const float btnGap = 14f;
            float stackTopY = -260f;
            BuildButton(column.transform, "Start",    new Vector2(btnW, btnH),
                anchoredPos: new Vector2(0f, stackTopY - 0 * (btnH + btnGap)),
                onClick: HandleStart);
            BuildButton(column.transform, "Settings", new Vector2(btnW, btnH),
                anchoredPos: new Vector2(0f, stackTopY - 1 * (btnH + btnGap)),
                onClick: HandleSettings);
            BuildButton(column.transform, "Exit",     new Vector2(btnW, btnH),
                anchoredPos: new Vector2(0f, stackTopY - 2 * (btnH + btnGap)),
                onClick: HandleExit);

            // Bottom-left: control hint.
            AddText(canvasGO.transform, "Esc — open settings",
                14, FontStyle.Normal, TextAnchor.LowerLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f),
                offsetMin: new Vector2(20f, 16f), offsetMax: new Vector2(280f, 36f),
                color: s_textDim);

            // Bottom-right: version + build info.
            string version = string.IsNullOrEmpty(Application.version) ? "dev" : Application.version;
            string platform = Application.platform.ToString();
            AddText(canvasGO.transform, $"v{version}  ·  {platform}",
                14, FontStyle.Normal, TextAnchor.LowerRight,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f),
                offsetMin: new Vector2(-360f, 16f), offsetMax: new Vector2(-20f, 36f),
                color: s_textDim);
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

        private static Text AddText(Transform parent, string content, int size,
            FontStyle style, TextAnchor anchor,
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
            t.font = UIFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            return t;
        }

        private void BuildButton(Transform parent, string label, Vector2 size, Vector2 anchoredPos, System.Action onClick)
        {
            var go = NewChild($"Btn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = s_buttonBase;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.normalColor      = s_buttonBase;
            cols.highlightedColor = s_accentOrange;
            cols.pressedColor     = new Color(0.7f, 0.4f, 0.05f, 1f);
            cols.selectedColor    = s_buttonBase;
            cols.colorMultiplier  = 1f;
            cols.fadeDuration     = 0.10f;
            btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());
            btn.onClick.AddListener(PlayUiClick);

            // Left side accent bar — shows orange on hover via a sibling
            // image whose alpha fades with selection state. Cheap polish.
            var bar = NewChild("Bar", go.transform);
            var brt = bar.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.sizeDelta = new Vector2(6f, 0f);
            brt.anchoredPosition = Vector2.zero;
            bar.AddComponent<Image>().color = s_accentOrange;

            // Label.
            var labelGO = NewChild("Label", go.transform);
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = UIFont;
            t.fontSize = 26;
            t.fontStyle = FontStyle.Bold;
            t.color = s_textColor;
            t.alignment = TextAnchor.MiddleCenter;
        }

        // Method-group hook so per-button AddListener doesn't allocate a
        // closure. Static for the same reason it lives in SettingsHud /
        // SceneTransitionHud.
        private static void PlayUiClick()
            => Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiClick);
    }
}
