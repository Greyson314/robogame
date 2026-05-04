using System.Collections.Generic;
using Robogame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Esc-toggled settings panel. Lives on the persistent Bootstrap object
    /// so a single instance survives scene transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pass-A scope: a single "Tweaks" tab that auto-builds one slider row
    /// per <see cref="Tweakables.Spec"/>. Sliders write live; values
    /// persist via <c>Tweakables</c> so changes carry across runs. Future
    /// tabs (Audio, Graphics, Bindings) plug in next to "Tweaks" without
    /// changing this layout.
    /// </para>
    /// <para>
    /// Built procedurally in UGUI to match <see cref="SceneTransitionHud"/>'s
    /// approach — no authored Canvas prefab needed for Pass A. Pass B can
    /// replace the whole thing with a designed UI Toolkit document.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SettingsHud : MonoBehaviour
    {
        [Tooltip("Key that toggles the settings panel. Defaults to Escape.")]
        [SerializeField] private Key _toggleKey = Key.Escape;

        private GameObject _root;
        private GameObject _content;
        private bool _open;
        private static readonly Color s_panelColor = new Color(0.06f, 0.07f, 0.10f, 0.93f);
        private static readonly Color s_groupColor = new Color(0.95f, 0.55f, 0.10f, 1f);
        private static readonly Color s_textColor  = Color.white;

        private void Awake()
        {
            EnsureEventSystem();
            BuildPanel();
            SetOpen(false);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[_toggleKey].wasPressedThisFrame)
            {
                SetOpen(!_open);
            }
        }

        // -----------------------------------------------------------------
        // EventSystem (shared with SceneTransitionHud)
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
        // Open/close
        // -----------------------------------------------------------------

        private void SetOpen(bool open)
        {
            _open = open;
            if (_root != null) _root.SetActive(open);
            if (open)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // -----------------------------------------------------------------
        // Panel construction
        // -----------------------------------------------------------------

        private static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private void BuildPanel()
        {
            // Top-level canvas — sits above SceneTransitionHud's order.
            var canvasGO = new GameObject("SettingsCanvas");
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            _root = canvasGO;

            // Dim background.
            var dimGO = NewChild("Dim", canvasGO.transform);
            FillParent(dimGO);
            dimGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            // Centered panel.
            var panel = NewChild("Panel", canvasGO.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(900f, 720f);
            panel.AddComponent<Image>().color = s_panelColor;

            // Header strip.
            var header = NewChild("Header", panel.transform);
            var headerRT = header.GetComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.sizeDelta = new Vector2(0f, 64f);
            header.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 1f);

            // Title text.
            AddText(header.transform, "SETTINGS", 32, FontStyle.Bold, TextAnchor.MiddleLeft,
                offset: new Vector2(24f, 0f));

            // Tab strip (only one tab for now).
            AddTabPill(header.transform, "Tweaks", new Vector2(-200f, 0f));

            // Close (X) button.
            var closeBtn = AddButton(header.transform, "✕", new Vector2(-12f, 0f), new Vector2(48f, 48f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            closeBtn.onClick.AddListener(() => SetOpen(false));

            // Reset-all button.
            var resetBtn = AddButton(header.transform, "Reset All", new Vector2(-72f, 0f), new Vector2(150f, 40f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            resetBtn.onClick.AddListener(() =>
            {
                Tweakables.ResetAll();
                RefreshAllSliders();
            });

            // Scrollable body. Reserve a vertical strip on the right for
            // the scrollbar so handle visuals don't overlap slider rows.
            const float scrollbarWidth = 14f;
            var scrollGO = NewChild("Scroll", panel.transform);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0f, 0f);
            scrollRT.anchorMax = new Vector2(1f, 1f);
            scrollRT.offsetMin = new Vector2(16f, 16f);
            scrollRT.offsetMax = new Vector2(-16f, -80f);
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewportGO = NewChild("Viewport", scrollGO.transform);
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = new Vector2(-(scrollbarWidth + 4f), 0f);
            viewportGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = viewportRT;

            // Vertical scrollbar pinned to the right edge of the scroll area.
            Scrollbar vbar = BuildVerticalScrollbar(scrollGO.transform, scrollbarWidth);
            scroll.verticalScrollbar = vbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 4f;

            var contentGO = NewChild("Content", viewportGO.transform);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0f, 0f);
            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRT;

            _content = contentGO;
            BuildActionRows();
            BuildTweakRows();
        }

        // -----------------------------------------------------------------
        // Action rows (one-shot buttons — respawn dummy, etc.)
        // -----------------------------------------------------------------

        private void BuildActionRows()
        {
            AddGroupHeader("Actions");
            AddActionRow("Respawn Combat Dummy", "Respawn", () =>
            {
                // Settings HUD lives on persistent Bootstrap; the ArenaController
                // only exists in Arena.unity. Look it up at click-time so this
                // works no matter which scene is active.
                ArenaController arena = Object.FindAnyObjectByType<ArenaController>();
                if (arena == null)
                {
                    Debug.LogWarning("[Robogame] Respawn Dummy: no ArenaController in the active scene. Are you in the arena?");
                    return;
                }
                arena.RespawnDummy();
            });
        }

        private void AddActionRow(string label, string buttonLabel, System.Action onClick)
        {
            var rowGO = NewChild($"Action_{label}", _content.transform);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            rowGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            // Label (left).
            var labelGO = NewChild("Label", rowGO.transform);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.sizeDelta = new Vector2(360f, 0f);
            labelRT.anchoredPosition = new Vector2(12f, 0f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = label;
            labelText.font = UIFont;
            labelText.fontSize = 18;
            labelText.color = s_textColor;
            labelText.alignment = TextAnchor.MiddleLeft;

            // Button (right).
            var btn = AddButton(rowGO.transform, buttonLabel, new Vector2(-12f, 0f), new Vector2(180f, 32f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        private readonly List<(Tweakables.Spec spec, Slider slider, Text valueText)> _rows = new();

        private void BuildTweakRows()
        {
            string lastGroup = null;
            foreach (Tweakables.Spec spec in Tweakables.All)
            {
                if (spec.Group != lastGroup)
                {
                    AddGroupHeader(spec.Group);
                    lastGroup = spec.Group;
                }
                AddTweakRow(spec);
            }
        }

        private void AddGroupHeader(string group)
        {
            var go = NewChild($"Group_{group}", _content.transform);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            var label = AddText(go.transform, group.ToUpperInvariant(), 18, FontStyle.Bold, TextAnchor.MiddleLeft,
                offset: new Vector2(8f, 0f));
            label.color = s_groupColor;
        }

        private void AddTweakRow(Tweakables.Spec spec)
        {
            var rowGO = NewChild($"Row_{spec.Key}", _content.transform);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            rowGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            // Label (left, fixed width).
            var labelGO = NewChild("Label", rowGO.transform);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.sizeDelta = new Vector2(220f, 0f);
            labelRT.anchoredPosition = new Vector2(12f, 0f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = spec.Label;
            labelText.font = UIFont;
            labelText.fontSize = 18;
            labelText.color = s_textColor;
            labelText.alignment = TextAnchor.MiddleLeft;

            // Slider (middle, stretches).
            var sliderGO = NewChild("Slider", rowGO.transform);
            var sliderRT = sliderGO.GetComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0f, 0.5f);
            sliderRT.anchorMax = new Vector2(1f, 0.5f);
            sliderRT.pivot = new Vector2(0.5f, 0.5f);
            sliderRT.offsetMin = new Vector2(240f, -10f);
            sliderRT.offsetMax = new Vector2(-260f,  10f);
            Slider slider = BuildSlider(sliderGO, spec);

            // Value display (right of slider).
            var valueGO = NewChild("Value", rowGO.transform);
            var valueRT = valueGO.GetComponent<RectTransform>();
            valueRT.anchorMin = new Vector2(1f, 0f);
            valueRT.anchorMax = new Vector2(1f, 1f);
            valueRT.pivot = new Vector2(1f, 0.5f);
            valueRT.sizeDelta = new Vector2(120f, 0f);
            valueRT.anchoredPosition = new Vector2(-130f, 0f);
            var valueText = valueGO.AddComponent<Text>();
            valueText.font = UIFont;
            valueText.fontSize = 18;
            valueText.color = s_textColor;
            valueText.alignment = TextAnchor.MiddleRight;
            valueText.text = FormatValue(slider.value);

            // Reset button (far right).
            var resetBtn = AddButton(rowGO.transform, "↺", new Vector2(-8f, 0f), new Vector2(40f, 32f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            string capturedKey = spec.Key;
            resetBtn.onClick.AddListener(() =>
            {
                Tweakables.Reset(capturedKey);
                RefreshAllSliders();
            });

            slider.onValueChanged.AddListener(v =>
            {
                Tweakables.Set(spec.Key, v);
                valueText.text = FormatValue(v);
            });
            _rows.Add((spec, slider, valueText));
        }

        private static Slider BuildSlider(GameObject host, Tweakables.Spec spec)
        {
            // Background bar.
            var bg = NewChild("Background", host.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f);
            bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.18f);

            // Fill area + fill.
            var fillArea = NewChild("Fill Area", host.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.4f);
            faRT.anchorMax = new Vector2(1f, 0.6f);
            faRT.offsetMin = new Vector2(8f, 0f);
            faRT.offsetMax = new Vector2(-8f, 0f);
            var fill = NewChild("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.95f, 0.55f, 0.10f, 1f);

            // Handle slide area + handle.
            var handleArea = NewChild("Handle Slide Area", host.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = new Vector2(0f, 0f);
            haRT.anchorMax = new Vector2(1f, 1f);
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);
            var handle = NewChild("Handle", handleArea.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(20f, 24f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            var slider = host.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = spec.Min;
            slider.maxValue = spec.Max;
            slider.value = Tweakables.Get(spec.Key);
            slider.wholeNumbers = false;
            return slider;
        }

        // Vertical scrollbar built to the same dark/orange palette as the
        // sliders. Anchored to the right edge of <paramref name="parent"/>
        // so the ScrollRect's AutoHideAndExpandViewport mode can show /
        // hide it without leaving an awkward gap when content fits.
        private static Scrollbar BuildVerticalScrollbar(Transform parent, float width)
        {
            var bar = NewChild("ScrollbarV", parent);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            rt.anchoredPosition = Vector2.zero;
            var bg = bar.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);

            var slidingArea = NewChild("Sliding Area", bar.transform);
            var saRT = slidingArea.GetComponent<RectTransform>();
            saRT.anchorMin = Vector2.zero;
            saRT.anchorMax = Vector2.one;
            saRT.offsetMin = new Vector2(2f, 2f);
            saRT.offsetMax = new Vector2(-2f, -2f);

            var handle = NewChild("Handle", slidingArea.transform);
            var hRT = handle.GetComponent<RectTransform>();
            hRT.anchorMin = Vector2.zero;
            hRT.anchorMax = Vector2.one;
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;
            var hImg = handle.AddComponent<Image>();
            hImg.color = new Color(0.95f, 0.55f, 0.10f, 0.90f);

            var sb = bar.AddComponent<Scrollbar>();
            sb.targetGraphic = hImg;
            sb.handleRect = hRT;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.value = 1f; // start scrolled to top (content anchored top-left)
            return sb;
        }

        private void RefreshAllSliders()
        {
            foreach (var row in _rows)
            {
                float v = Tweakables.Get(row.spec.Key);
                row.slider.SetValueWithoutNotify(v);
                row.valueText.text = FormatValue(v);
            }
        }

        private static string FormatValue(float v)
        {
            float abs = Mathf.Abs(v);
            if (abs >= 100f) return v.ToString("F0");
            if (abs >= 10f)  return v.ToString("F1");
            return v.ToString("F2");
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

        private static Text AddText(Transform parent, string text, int size, FontStyle style, TextAnchor anchor,
            Vector2 offset = default)
        {
            var go = NewChild("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offset;
            rt.offsetMax = offset;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = UIFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = s_textColor;
            t.alignment = anchor;
            return t;
        }

        private static Button AddButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
            Vector2 anchor, Vector2 pivot)
        {
            var go = NewChild("Button", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.22f, 0.28f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.highlightedColor = new Color(0.95f, 0.55f, 0.10f, 1f);
            cols.pressedColor = new Color(0.7f, 0.4f, 0.05f, 1f);
            btn.colors = cols;

            var labelGO = NewChild("Label", go.transform);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = UIFont;
            t.fontSize = 18;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        private static void AddTabPill(Transform parent, string label, Vector2 anchoredPos)
        {
            var go = NewChild($"Tab_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(160f, 40f);
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.95f, 0.55f, 0.10f, 1f);
            var t = AddText(go.transform, label, 18, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.color = Color.white;
        }
    }
}
