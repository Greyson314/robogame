using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Drives the Pass A garage ⇄ arena scene transitions via a real UGUI
    /// canvas (so Input System UI integration suppresses player actions
    /// like fire while clicking the button).
    /// </summary>
    /// <remarks>
    /// On <c>Awake</c> this component lazily builds an <see cref="EventSystem"/>
    /// (with <see cref="InputSystemUIInputModule"/>), a <see cref="Canvas"/>
    /// in <c>ScreenSpaceOverlay</c>, and a single <see cref="Button"/> labelled
    /// for the current <see cref="GameStateController.State"/>. Pass B will
    /// replace this with a proper authored Canvas prefab.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SceneTransitionHud : MonoBehaviour
    {
        [SerializeField] private Vector2 _buttonSize = new Vector2(220f, 64f);
        [SerializeField] private Vector2 _margin = new Vector2(28f, 28f);
        [SerializeField] private int _fontSize = 22;
        [SerializeField] private Vector2 _dropdownSize = new Vector2(220f, 44f);

        private Button _button;
        private Text _label;
        private Dropdown _presetDropdown;
        private int _lastPresetCount = -1;
        private GameState _lastState = GameState.Bootstrap;

        private void Awake()
        {
            EnsureEventSystem();
            BuildCanvas();
            RefreshLabel();
        }

        private void Update()
        {
            // Cheap: only restyle when the state changes.
            GameStateController state = GameStateController.Instance;
            if (state == null) return;

            int presetCount = state.PresetBlueprints != null ? state.PresetBlueprints.Count : 0;
            if (presetCount != _lastPresetCount)
            {
                _lastPresetCount = presetCount;
                PopulateDropdown(state);
            }

            if (state.State == _lastState) return;
            _lastState = state.State;
            RefreshLabel();
        }

        // -----------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            EventSystem es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }
            // The legacy StandaloneInputModule conflicts with the new Input
            // System's UI module; replace if present.
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Destroy(legacy);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private void BuildCanvas()
        {
            // Canvas root.
            var canvasGO = new GameObject("SceneTransitionCanvas");
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            // Button.
            var btnGO = new GameObject("LaunchButton");
            btnGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);
            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.16f, 0.92f);
            _button = btnGO.AddComponent<Button>();
            _button.targetGraphic = img;

            ColorBlock cols = _button.colors;
            cols.normalColor = new Color(1f, 1f, 1f, 1f);
            cols.highlightedColor = new Color(0.95f, 0.55f, 0.10f, 1f); // hazard orange tint
            cols.pressedColor = new Color(0.7f, 0.4f, 0.05f, 1f);
            cols.selectedColor = cols.highlightedColor;
            _button.colors = cols;

            var rt = btnGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = _buttonSize;
            rt.anchoredPosition = new Vector2(-_margin.x, _margin.y);

            // Label child.
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(btnGO.transform, worldPositionStays: false);
            _label = labelGO.AddComponent<Text>();
            _label.alignment = TextAnchor.MiddleCenter;
            _label.fontSize = _fontSize;
            _label.color = Color.white;
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _button.onClick.AddListener(HandleClick);

            BuildPresetDropdown(canvasGO.transform);
        }

        private void BuildPresetDropdown(Transform canvas)
        {
            // Bottom-left chassis selector, shown only in the Garage.
            var ddGO = new GameObject("PresetDropdown");
            ddGO.transform.SetParent(canvas, worldPositionStays: false);
            var ddImg = ddGO.AddComponent<Image>();
            ddImg.color = new Color(0.10f, 0.12f, 0.16f, 0.92f);
            _presetDropdown = ddGO.AddComponent<Dropdown>();
            _presetDropdown.targetGraphic = ddImg;

            var ddRT = ddGO.GetComponent<RectTransform>();
            ddRT.anchorMin = new Vector2(0f, 0f);
            ddRT.anchorMax = new Vector2(0f, 0f);
            ddRT.pivot = new Vector2(0f, 0f);
            ddRT.sizeDelta = _dropdownSize;
            ddRT.anchoredPosition = new Vector2(_margin.x, _margin.y);

            // Caption (the label that shows the currently selected option).
            var captionGO = new GameObject("Label");
            captionGO.transform.SetParent(ddGO.transform, worldPositionStays: false);
            var caption = captionGO.AddComponent<Text>();
            caption.alignment = TextAnchor.MiddleLeft;
            caption.fontSize = _fontSize;
            caption.color = Color.white;
            caption.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var captionRT = captionGO.GetComponent<RectTransform>();
            captionRT.anchorMin = Vector2.zero;
            captionRT.anchorMax = Vector2.one;
            captionRT.offsetMin = new Vector2(12f, 2f);
            captionRT.offsetMax = new Vector2(-28f, -2f);
            _presetDropdown.captionText = caption;

            // Drop-down chevron arrow (simple right-aligned glyph).
            var arrowGO = new GameObject("Arrow");
            arrowGO.transform.SetParent(ddGO.transform, worldPositionStays: false);
            var arrow = arrowGO.AddComponent<Text>();
            arrow.alignment = TextAnchor.MiddleRight;
            arrow.fontSize = _fontSize;
            arrow.color = Color.white;
            arrow.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            arrow.text = "▾";
            var arrowRT = arrowGO.GetComponent<RectTransform>();
            arrowRT.anchorMin = Vector2.zero;
            arrowRT.anchorMax = Vector2.one;
            arrowRT.offsetMin = Vector2.zero;
            arrowRT.offsetMax = new Vector2(-10f, 0f);

            // Template the dropdown uses to spawn the open list.
            var template = new GameObject("Template");
            template.transform.SetParent(ddGO.transform, worldPositionStays: false);
            template.SetActive(false);
            var templateRT = template.AddComponent<RectTransform>();
            templateRT.anchorMin = new Vector2(0f, 1f);
            templateRT.anchorMax = new Vector2(1f, 1f);
            templateRT.pivot = new Vector2(0.5f, 1f);
            templateRT.sizeDelta = new Vector2(0f, 150f);
            var templateImg = template.AddComponent<Image>();
            templateImg.color = new Color(0.08f, 0.10f, 0.13f, 0.97f);
            var templateScroll = template.AddComponent<ScrollRect>();
            template.AddComponent<Mask>().showMaskGraphic = true;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, worldPositionStays: false);
            var viewportRT = viewport.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, worldPositionStays: false);
            var contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0f, 28f);

            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, worldPositionStays: false);
            var itemRT = item.AddComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0f, 0.5f);
            itemRT.anchorMax = new Vector2(1f, 0.5f);
            itemRT.sizeDelta = new Vector2(0f, 28f);
            var itemToggle = item.AddComponent<Toggle>();

            var itemBg = new GameObject("Item Background");
            itemBg.transform.SetParent(item.transform, worldPositionStays: false);
            var itemBgImg = itemBg.AddComponent<Image>();
            itemBgImg.color = new Color(0.18f, 0.22f, 0.28f, 1f);
            var itemBgRT = itemBg.GetComponent<RectTransform>();
            itemBgRT.anchorMin = Vector2.zero;
            itemBgRT.anchorMax = Vector2.one;
            itemBgRT.offsetMin = Vector2.zero;
            itemBgRT.offsetMax = Vector2.zero;

            var itemChecks = new GameObject("Item Checkmark");
            itemChecks.transform.SetParent(item.transform, worldPositionStays: false);
            var checkImg = itemChecks.AddComponent<Image>();
            checkImg.color = new Color(0.95f, 0.55f, 0.10f, 1f);
            var checkRT = itemChecks.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0f, 0.5f);
            checkRT.anchorMax = new Vector2(0f, 0.5f);
            checkRT.sizeDelta = new Vector2(20f, 20f);
            checkRT.anchoredPosition = new Vector2(14f, 0f);

            var itemLabel = new GameObject("Item Label");
            itemLabel.transform.SetParent(item.transform, worldPositionStays: false);
            var itemText = itemLabel.AddComponent<Text>();
            itemText.alignment = TextAnchor.MiddleLeft;
            itemText.fontSize = _fontSize - 2;
            itemText.color = Color.white;
            itemText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var itemTextRT = itemLabel.GetComponent<RectTransform>();
            itemTextRT.anchorMin = Vector2.zero;
            itemTextRT.anchorMax = Vector2.one;
            itemTextRT.offsetMin = new Vector2(36f, 2f);
            itemTextRT.offsetMax = new Vector2(-10f, -2f);

            itemToggle.targetGraphic = itemBgImg;
            itemToggle.graphic = checkImg;
            itemToggle.isOn = true;

            templateScroll.content = contentRT;
            templateScroll.viewport = viewportRT;
            templateScroll.horizontal = false;
            templateScroll.vertical = true;

            _presetDropdown.template = templateRT;
            _presetDropdown.itemText = itemText;

            _presetDropdown.onValueChanged.AddListener(HandlePresetChanged);
        }

        private void PopulateDropdown(GameStateController state)
        {
            if (_presetDropdown == null) return;
            _presetDropdown.onValueChanged.RemoveListener(HandlePresetChanged);
            _presetDropdown.ClearOptions();
            var options = new System.Collections.Generic.List<Dropdown.OptionData>();
            if (state.PresetBlueprints != null)
            {
                for (int i = 0; i < state.PresetBlueprints.Count; i++)
                {
                    var bp = state.PresetBlueprints[i];
                    string label = (bp != null && !string.IsNullOrEmpty(bp.DisplayName))
                        ? bp.DisplayName
                        : $"Preset {i}";
                    options.Add(new Dropdown.OptionData(label));
                }
            }
            _presetDropdown.AddOptions(options);
            int sel = Mathf.Clamp(state.CurrentPresetIndex, 0, Mathf.Max(0, options.Count - 1));
            _presetDropdown.SetValueWithoutNotify(sel);
            _presetDropdown.RefreshShownValue();
            _presetDropdown.onValueChanged.AddListener(HandlePresetChanged);
        }

        private void HandlePresetChanged(int index)
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.SelectPreset(index);
        }

        private void RefreshLabel()
        {
            if (_label == null || _button == null) return;
            GameStateController state = GameStateController.Instance;
            if (state == null)
            {
                _button.gameObject.SetActive(false);
                if (_presetDropdown != null) _presetDropdown.gameObject.SetActive(false);
                return;
            }

            switch (state.State)
            {
                case GameState.Garage:
                    _label.text = "Launch ▶";
                    _button.gameObject.SetActive(true);
                    if (_presetDropdown != null) _presetDropdown.gameObject.SetActive(true);
                    break;
                case GameState.Arena:
                    _label.text = "◀ Garage";
                    _button.gameObject.SetActive(true);
                    if (_presetDropdown != null) _presetDropdown.gameObject.SetActive(false);
                    break;
                default:
                    _button.gameObject.SetActive(false);
                    if (_presetDropdown != null) _presetDropdown.gameObject.SetActive(false);
                    break;
            }
        }

        private void HandleClick()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;

            switch (state.State)
            {
                case GameState.Garage:
                    var garage = FindAnyObjectByType<GarageController>();
                    if (garage != null) garage.Launch();
                    else state.EnterArena();
                    break;
                case GameState.Arena:
                    var arena = FindAnyObjectByType<ArenaController>();
                    if (arena != null) arena.Return();
                    else state.EnterGarage();
                    break;
            }
        }
    }
}
