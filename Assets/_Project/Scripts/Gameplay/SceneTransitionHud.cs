using Robogame.Block;
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
        private Button _newButton;
        private Button _saveButton;
        private Button _buildButton;
        private Button _deleteButton;
        private Button _waterButton;
        private Button _planetButton;
        private InputField _nameField;
        private Text _buildLabel;
        private BuildModeController _subscribedBuildMode;
        private GameStateController _subscribedState;
        private bool _catalogDirty = true;
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

            // Late-binding to the singleton: it's created in Bootstrap.
            if (_subscribedState != state)
            {
                if (_subscribedState != null)
                {
                    _subscribedState.BlueprintCatalogChanged -= HandleCatalogChanged;
                    _subscribedState.PresetChanged -= HandlePresetChangedEvent;
                }
                _subscribedState = state;
                _subscribedState.BlueprintCatalogChanged += HandleCatalogChanged;
                _subscribedState.PresetChanged += HandlePresetChangedEvent;
                _catalogDirty = true;
            }

            if (_catalogDirty)
            {
                _catalogDirty = false;
                PopulateDropdown(state);
                RefreshLabel(); // keeps Delete button + name field current
            }

            if (state.State == _lastState) return;
            _lastState = state.State;
            RefreshLabel();
        }

        private void OnDestroy()
        {
            if (_subscribedState != null)
            {
                _subscribedState.BlueprintCatalogChanged -= HandleCatalogChanged;
                _subscribedState.PresetChanged -= HandlePresetChangedEvent;
            }
            if (_subscribedBuildMode != null)
            {
                _subscribedBuildMode.Entered -= HandleBuildEntered;
                _subscribedBuildMode.Exited  -= HandleBuildExited;
            }
        }

        private void HandleCatalogChanged() => _catalogDirty = true;
        private void HandlePresetChangedEvent(int _) => _catalogDirty = true;

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
            _newButton    = BuildSmallButton(canvasGO.transform, "NewRobotButton",    "+ New Robot",  row: 1, HandleNewClicked);
            _saveButton   = BuildSmallButton(canvasGO.transform, "SaveRobotButton",   "Save Robot",   row: 2, HandleSaveClicked);
            _buildButton  = BuildSmallButton(canvasGO.transform, "BuildModeButton",   "Build Mode",   row: 3, HandleBuildClicked);
            _deleteButton = BuildSmallButton(canvasGO.transform, "DeleteRobotButton", "Delete",       row: 4, HandleDeleteClicked);
            TintDestructive(_deleteButton);
            _waterButton  = BuildSmallButton(canvasGO.transform, "WaterArenaButton",  "Water Arena ▶", row: 6, HandleWaterClicked);
            _planetButton = BuildSmallButton(canvasGO.transform, "PlanetArenaButton", "Planet Arena ▶", row: 7, HandlePlanetClicked);
            _nameField    = BuildNameField(canvasGO.transform, row: 5);
            _buildLabel   = _buildButton != null ? _buildButton.GetComponentInChildren<Text>() : null;
        }

        /// <summary>
        /// Build a compact bottom-left button stacked above the dropdown.
        /// <paramref name="row"/> 1 sits just above the dropdown, 2 above
        /// that, etc.
        /// </summary>
        private Button BuildSmallButton(Transform canvas, string objName, string label, int row, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(canvas, worldPositionStays: false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.12f, 0.16f, 0.92f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            ColorBlock cols = btn.colors;
            cols.normalColor = new Color(1f, 1f, 1f, 1f);
            cols.highlightedColor = new Color(0.95f, 0.55f, 0.10f, 1f);
            cols.pressedColor = new Color(0.7f, 0.4f, 0.05f, 1f);
            cols.selectedColor = cols.highlightedColor;
            btn.colors = cols;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = _dropdownSize;
            // Stack above the dropdown with a small gap.
            float yOffset = _margin.y + (_dropdownSize.y + 6f) * row;
            rt.anchoredPosition = new Vector2(_margin.x, yOffset);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, worldPositionStays: false);
            var text = labelGO.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = _fontSize - 2;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            btn.onClick.AddListener(onClick);
            return btn;
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

            // Designer-authored presets first.
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

            // User-saved blueprints (suffix marks them visually).
            for (int i = 0; i < state.UserBlueprints.Count; i++)
            {
                UserBlueprintLibrary.Record rec = state.UserBlueprints[i];
                string display = (rec.Blueprint != null && !string.IsNullOrEmpty(rec.Blueprint.DisplayName))
                    ? rec.Blueprint.DisplayName
                    : System.IO.Path.GetFileNameWithoutExtension(rec.FileName);
                options.Add(new Dropdown.OptionData(display + "  ◆"));
            }

            // Fallback so the user can always tell when their picker is empty.
            if (options.Count == 0) options.Add(new Dropdown.OptionData("(no blueprints)"));

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

            bool inGarage = state.State == GameState.Garage;
            bool inArena  = state.State == GameState.Arena;
            bool inWater  = state.State == GameState.WaterArena;
            bool inPlanet = state.State == GameState.PlanetArena;

            switch (state.State)
            {
                case GameState.Garage:
                    _label.text = "Launch ▶";
                    _button.gameObject.SetActive(true);
                    break;
                case GameState.Arena:
                    _label.text = "◀ Garage";
                    _button.gameObject.SetActive(true);
                    break;
                case GameState.WaterArena:
                    _label.text = "◀ Garage";
                    _button.gameObject.SetActive(true);
                    break;
                case GameState.PlanetArena:
                    _label.text = "◀ Garage";
                    _button.gameObject.SetActive(true);
                    break;
                default:
                    _button.gameObject.SetActive(false);
                    break;
            }

            if (_presetDropdown != null) _presetDropdown.gameObject.SetActive(inGarage);
            if (_newButton   != null)    _newButton.gameObject.SetActive(inGarage);
            if (_saveButton  != null)    _saveButton.gameObject.SetActive(inGarage);
            if (_buildButton != null)    _buildButton.gameObject.SetActive(inGarage);
            if (_waterButton != null)    _waterButton.gameObject.SetActive(inGarage);
            if (_planetButton != null)   _planetButton.gameObject.SetActive(inGarage);
            // Delete is only meaningful for user-saved blueprints.
            bool canDelete = inGarage && !string.IsNullOrEmpty(state.CurrentUserFileName);
            if (_deleteButton != null)   _deleteButton.gameObject.SetActive(canDelete);
            if (_nameField    != null)
            {
                _nameField.gameObject.SetActive(inGarage);
                if (inGarage) SyncNameFieldFromState(state);
            }
            EnsureBuildModeSubscription();
            _ = inArena; // reserved for arena-side HUD growth.
            _ = inWater; // reserved for water-arena HUD growth.
            _ = inPlanet; // reserved for planet-arena HUD growth.
        }

        private void EnsureBuildModeSubscription()
        {
            var garage = FindAnyObjectByType<GarageController>();
            BuildModeController bm = garage != null ? garage.BuildMode : null;
            if (bm == _subscribedBuildMode) return;
            if (_subscribedBuildMode != null)
            {
                _subscribedBuildMode.Entered -= HandleBuildEntered;
                _subscribedBuildMode.Exited  -= HandleBuildExited;
            }
            _subscribedBuildMode = bm;
            if (_subscribedBuildMode != null)
            {
                _subscribedBuildMode.Entered += HandleBuildEntered;
                _subscribedBuildMode.Exited  += HandleBuildExited;
                RefreshBuildLabel();
            }
        }

        private void HandleBuildEntered() => RefreshBuildLabel();
        private void HandleBuildExited()  => RefreshBuildLabel();

        private void RefreshBuildLabel()
        {
            if (_buildLabel == null) return;
            bool active = _subscribedBuildMode != null && _subscribedBuildMode.IsActive;
            _buildLabel.text = active ? "Drive Mode" : "Build Mode";
        }

        private void HandleBuildClicked()
        {
            var garage = FindAnyObjectByType<GarageController>();
            if (garage != null) garage.ToggleBuildMode();
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
                case GameState.WaterArena:
                    var water = FindAnyObjectByType<WaterArenaController>();
                    if (water != null) water.Return();
                    else state.EnterGarage();
                    break;
                case GameState.PlanetArena:
                    var planet = FindAnyObjectByType<PlanetArenaController>();
                    if (planet != null) planet.Return();
                    else state.EnterGarage();
                    break;
            }
        }

        private void HandleWaterClicked()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.EnterWaterArena();
        }

        private void HandlePlanetClicked()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.EnterPlanetArena();
        }

        private void HandleNewClicked()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            state.CreateNewBlueprint();
            // GarageController already listens to PresetChanged and respawns,
            // so the new chassis appears on the podium without further work.
            // Dropdown selection: -1 means "off-list"; clamp to 0 so the
            // visible caption isn't garbage.
            if (_presetDropdown != null && _presetDropdown.options.Count > 0)
            {
                _presetDropdown.SetValueWithoutNotify(0);
                _presetDropdown.RefreshShownValue();
            }
            // Keep dropdown caption honest after the picker is repopulated.
            _catalogDirty = true;
        }

        private void HandleSaveClicked()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null || state.CurrentBlueprint == null) return;
            // Commit any pending name edit before persisting.
            CommitNameField(state);
            string fileName = state.SaveCurrentBlueprint();
            Debug.Log($"[Robogame] Saved blueprint to '{fileName}'.");
            _catalogDirty = true;
        }

        // -----------------------------------------------------------------
        // Name field + delete
        // -----------------------------------------------------------------

        private InputField BuildNameField(Transform canvas, int row)
        {
            var go = new GameObject("NameField");
            go.transform.SetParent(canvas, worldPositionStays: false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.06f, 0.07f, 0.10f, 0.92f);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot     = new Vector2(0f, 0f);
            // Wider than the buttons so long names fit.
            rt.sizeDelta = new Vector2(_dropdownSize.x + 60f, _dropdownSize.y);
            float yOffset = _margin.y + (_dropdownSize.y + 6f) * row;
            rt.anchoredPosition = new Vector2(_margin.x, yOffset);

            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;

            // Text child (active editable).
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, worldPositionStays: false);
            var text = textGO.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.fontSize = _fontSize - 2;
            text.color = Color.white;
            text.supportRichText = false;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 4f);
            trt.offsetMax = new Vector2(-10f, -4f);
            input.textComponent = text;

            // Placeholder.
            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, worldPositionStays: false);
            var ph = phGO.AddComponent<Text>();
            ph.alignment = TextAnchor.MiddleLeft;
            ph.fontSize = _fontSize - 2;
            ph.color = new Color(1f, 1f, 1f, 0.4f);
            ph.fontStyle = FontStyle.Italic;
            ph.text = "Robot name…";
            ph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var prt = phGO.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(10f, 4f);
            prt.offsetMax = new Vector2(-10f, -4f);
            input.placeholder = ph;

            input.characterLimit = 48;
            input.lineType = InputField.LineType.SingleLine;
            input.onEndEdit.AddListener(HandleNameEndEdit);
            return input;
        }

        private void SyncNameFieldFromState(GameStateController state)
        {
            if (_nameField == null || state == null || state.CurrentBlueprint == null) return;
            string current = state.CurrentBlueprint.DisplayName ?? string.Empty;
            if (_nameField.text != current) _nameField.SetTextWithoutNotify(current);
        }

        private void HandleNameEndEdit(string value)
        {
            GameStateController state = GameStateController.Instance;
            if (state == null || state.CurrentBlueprint == null) return;
            string trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) trimmed = "Untitled Chassis";
            state.CurrentBlueprint.DisplayName = trimmed;
            // Repaint the dropdown caption so the rename is visible without a save.
            _catalogDirty = true;
        }

        private void CommitNameField(GameStateController state)
        {
            if (_nameField == null) return;
            HandleNameEndEdit(_nameField.text);
        }

        private void HandleDeleteClicked()
        {
            GameStateController state = GameStateController.Instance;
            if (state == null) return;
            if (string.IsNullOrEmpty(state.CurrentUserFileName))
            {
                Debug.Log("[Robogame] Delete: nothing to delete (current blueprint isn't a user file).");
                return;
            }
            string fileName = state.CurrentUserFileName;
            if (state.DeleteCurrentUserBlueprint())
            {
                Debug.Log($"[Robogame] Deleted user blueprint '{fileName}'.");
                // Drop the freshly-orphaned in-memory blueprint and load whatever
                // is at preset 0 so the garage is in a sane state.
                if (state.PresetCount > 0) state.SelectPreset(0);
                else state.CreateNewBlueprint();
                _catalogDirty = true;
            }
        }

        private static void TintDestructive(Button btn)
        {
            if (btn == null) return;
            ColorBlock cols = btn.colors;
            cols.highlightedColor = new Color(0.95f, 0.25f, 0.20f, 1f); // hazard red on hover
            cols.pressedColor     = new Color(0.65f, 0.15f, 0.12f, 1f);
            cols.selectedColor    = cols.highlightedColor;
            btn.colors = cols;
        }
    }
}
