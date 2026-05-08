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
    /// Tab-style layout: a single "Tweaks" tab today, auto-built one row per
    /// <see cref="Tweakables.Spec"/>. Float specs render as a labelled
    /// slider; bool specs render as a checkbox. Rows are grouped by
    /// <see cref="Tweakables.Spec.Group"/> with a collapsible foldout per
    /// section, a per-group reset, a top-level search filter, and a
    /// keybinds reference at the bottom.
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
        private static readonly Color s_panelColor   = new Color(0.06f, 0.07f, 0.10f, 0.93f);
        private static readonly Color s_groupColor   = new Color(0.95f, 0.55f, 0.10f, 1f);
        private static readonly Color s_textColor    = Color.white;
        private static readonly Color s_textDimColor = new Color(0.78f, 0.80f, 0.84f, 1f);

        private InputField _searchField;
        private string _searchFilter = string.Empty;

        // Row registry per group → all rows belonging to that group, plus
        // the header rect so we can collapse/expand and apply the search
        // filter. Group expanded-state is held in _groupExpanded; defaults
        // to true (open) so first-time users see all rows.
        private sealed class RowEntry
        {
            public Tweakables.Spec Spec;
            public GameObject Row;
            public Slider Slider;            // float
            public Toggle Toggle;            // bool
            public Text ValueText;           // float
        }
        private sealed class GroupSection
        {
            public string Name;
            public GameObject Header;
            public RectTransform ChevronRT;
            public List<RowEntry> Rows = new();
            public bool Expanded = true;
        }
        private readonly Dictionary<string, GroupSection> _groups = new();
        private readonly List<RowEntry> _allRows = new();

        private void Awake()
        {
            EnsureEventSystem();
            BuildPanel();
            SetOpen(false);
            // Re-apply the time-scale gate when the user toggles the
            // QoL.PauseOnSettings flag while the panel is open.
            Tweakables.Changed += ApplyPause;
        }

        private void OnDestroy()
        {
            Tweakables.Changed -= ApplyPause;
            // Restore time scale on teardown so a scene reload while
            // the panel was open doesn't leave the next scene paused.
            Time.timeScale = 1f;
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
                if (_searchField != null) _searchField.text = string.Empty;
            }
            ApplyPause();
        }

        // Time-scale gate driven by the QoL.PauseOnSettings tweakable.
        // Disambiguates "settings is open AND user wants pause" from
        // "settings is open AND user wants live tuning". When the tween
        // tweakable flips while the panel is open (rare — only by
        // navigating to it, toggling, and looking at gameplay live),
        // ApplyPause re-fires via Tweakables.Changed.
        private void ApplyPause()
        {
            bool pauseOn = Tweakables.GetBool(Tweakables.SettingsPause);
            // Use unscaledTime everywhere we set timeScale so paused-
            // ness doesn't break the audio expire-sweep, the kill-
            // banner fade, or the floating-damage animations. Project
            // already migrated those paths.
            Time.timeScale = (_open && pauseOn) ? 0f : 1f;
        }

        /// <summary>True while the settings panel is currently visible.</summary>
        public bool IsOpen => _open;

        /// <summary>Open the settings panel from another HUD (e.g. Main Menu's Settings button).</summary>
        public void Open() => SetOpen(true);

        /// <summary>Close the settings panel.</summary>
        public void Close() => SetOpen(false);

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
                offset: new Vector2(16f, 0f));

            // Tab strip (only one tab for now).
            AddTabPill(header.transform, "Tweaks", new Vector2(-200f, 0f));

            // Close (X) button.
            var closeBtn = AddButton(header.transform, "✕", new Vector2(-12f, 0f), new Vector2(48f, 48f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            closeBtn.onClick.AddListener(() => SetOpen(false));

            // Reset-all button. Re-baselines every Tweakable to its inline default.
            // No confirmation prompt — values are durable on disk anyway, and
            // "Reset" already implies the action.
            var resetBtn = AddButton(header.transform, "Reset All", new Vector2(-72f, 0f), new Vector2(150f, 40f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            resetBtn.onClick.AddListener(() =>
            {
                Tweakables.ResetAll();
                RefreshAllRows();
            });

            // Search bar — sits below the header, above the scrolling rows.
            // Filters by label/group substring (case-insensitive). Empty
            // string shows everything.
            var search = NewChild("Search", panel.transform);
            var searchRT = search.GetComponent<RectTransform>();
            searchRT.anchorMin = new Vector2(0f, 1f);
            searchRT.anchorMax = new Vector2(1f, 1f);
            searchRT.pivot = new Vector2(0.5f, 1f);
            searchRT.sizeDelta = new Vector2(-32f, 36f);   // 16px margin per side
            searchRT.anchoredPosition = new Vector2(0f, -76f);
            search.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
            _searchField = BuildSearchField(search);

            // Scrollable body. Reserve a vertical strip on the right for
            // the scrollbar so handle visuals don't overlap slider rows.
            const float scrollbarWidth = 14f;
            var scrollGO = NewChild("Scroll", panel.transform);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0f, 0f);
            scrollRT.anchorMax = new Vector2(1f, 1f);
            scrollRT.offsetMin = new Vector2(16f, 16f);
            scrollRT.offsetMax = new Vector2(-16f, -120f);  // header (64) + search (36) + gaps (~20)
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
            BuildKeybindsSection();
        }

        // -----------------------------------------------------------------
        // Search filter
        // -----------------------------------------------------------------

        private InputField BuildSearchField(GameObject host)
        {
            // Placeholder + text components for InputField.
            var placeholderGO = NewChild("Placeholder", host.transform);
            var placeholderRT = placeholderGO.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.offsetMin = new Vector2(12f, 0f);
            placeholderRT.offsetMax = new Vector2(-12f, 0f);
            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.text = "Search tweaks…  (e.g. recoil, water, tank)";
            placeholder.font = UIFont;
            placeholder.fontSize = 16;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = new Color(0.55f, 0.58f, 0.63f, 1f);
            placeholder.alignment = TextAnchor.MiddleLeft;

            var textGO = NewChild("Text", host.transform);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(12f, 0f);
            textRT.offsetMax = new Vector2(-12f, 0f);
            var text = textGO.AddComponent<Text>();
            text.font = UIFont;
            text.fontSize = 16;
            text.color = s_textColor;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            var input = host.AddComponent<InputField>();
            input.targetGraphic = host.GetComponent<Image>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            input.onValueChanged.AddListener(OnSearchChanged);
            return input;
        }

        private void OnSearchChanged(string s)
        {
            _searchFilter = s == null ? string.Empty : s.Trim().ToLowerInvariant();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            bool searching = !string.IsNullOrEmpty(_searchFilter);
            foreach (GroupSection g in _groups.Values)
            {
                int visibleRows = 0;
                foreach (RowEntry r in g.Rows)
                {
                    bool match = !searching
                        || r.Spec.Label.ToLowerInvariant().Contains(_searchFilter)
                        || r.Spec.Group.ToLowerInvariant().Contains(_searchFilter)
                        || r.Spec.Key.ToLowerInvariant().Contains(_searchFilter);
                    // While searching, ignore the foldout state; the user
                    // wants to see every match regardless of group expansion.
                    bool show = match && (searching || g.Expanded);
                    r.Row.SetActive(show);
                    if (match) visibleRows++;
                }
                // Group header is hidden when no row matches the search.
                if (g.Header != null) g.Header.SetActive(visibleRows > 0);
            }
        }

        // -----------------------------------------------------------------
        // Action rows (one-shot buttons — respawn, etc.)
        // -----------------------------------------------------------------

        private void BuildActionRows()
        {
            // Actions group is always expanded and not searchable; it's a
            // small fixed section at the top of the body.
            AddPlainGroupHeader("Actions");
            AddActionRow("Respawn Combat Dummy", "Respawn", () =>
            {
                ArenaController arena = Object.FindAnyObjectByType<ArenaController>();
                if (arena == null)
                {
                    Debug.LogWarning("[Robogame] Respawn Dummy: no ArenaController in the active scene. Are you in the arena?");
                    return;
                }
                arena.RespawnDummy();
            });
            AddActionRow("Respawn Player (or press K)", "Respawn", () =>
            {
                ArenaController arena = Object.FindAnyObjectByType<ArenaController>();
                if (arena == null)
                {
                    Debug.LogWarning("[Robogame] Respawn Player: no ArenaController in the active scene. Are you in the arena?");
                    return;
                }
                arena.RespawnPlayer();
            });
            // Esc → Main Menu route. Useful from any scene; SettingsHud
            // lives on the persistent Bootstrap so closing back to menu
            // doesn't tear down the persistent services.
            AddActionRow("Return to Main Menu", "Main Menu", () =>
            {
                Close();
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu",
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            });
        }

        private void AddActionRow(string label, string buttonLabel, System.Action onClick)
        {
            var rowGO = NewChild($"Action_{label}", _content.transform);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            rowGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

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

            var btn = AddButton(rowGO.transform, buttonLabel, new Vector2(-12f, 0f), new Vector2(180f, 32f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            btn.onClick.AddListener(() => onClick?.Invoke());
        }

        // -----------------------------------------------------------------
        // Tweak rows (auto-built from Tweakables.All)
        // -----------------------------------------------------------------

        private void BuildTweakRows()
        {
            string lastGroup = null;
            GroupSection currentSection = null;
            foreach (Tweakables.Spec spec in Tweakables.All)
            {
                if (spec.Group != lastGroup)
                {
                    currentSection = AddFoldoutGroupHeader(spec.Group);
                    lastGroup = spec.Group;
                }
                RowEntry row = spec.Kind == Tweakables.SpecKind.Bool
                    ? AddBoolRow(spec)
                    : AddSliderRow(spec);
                if (currentSection != null) currentSection.Rows.Add(row);
                _allRows.Add(row);
            }
        }

        private void AddPlainGroupHeader(string group)
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

        private GroupSection AddFoldoutGroupHeader(string group)
        {
            var go = NewChild($"Group_{group}", _content.transform);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);

            // Chevron indicator on the left, rotated by ApplyGroupExpansion.
            var chevronGO = NewChild("Chevron", go.transform);
            var chevronRT = chevronGO.GetComponent<RectTransform>();
            chevronRT.anchorMin = new Vector2(0f, 0.5f);
            chevronRT.anchorMax = new Vector2(0f, 0.5f);
            chevronRT.pivot = new Vector2(0.5f, 0.5f);
            chevronRT.sizeDelta = new Vector2(14f, 14f);
            chevronRT.anchoredPosition = new Vector2(16f, 0f);
            var chevText = chevronGO.AddComponent<Text>();
            chevText.text = "▶";
            chevText.font = UIFont;
            chevText.fontSize = 14;
            chevText.fontStyle = FontStyle.Bold;
            chevText.color = s_groupColor;
            chevText.alignment = TextAnchor.MiddleCenter;

            // Group-name label.
            var labelGO = NewChild("Label", go.transform);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(1f, 1f);
            labelRT.offsetMin = new Vector2(36f, 0f);
            labelRT.offsetMax = new Vector2(-180f, 0f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = group.ToUpperInvariant();
            labelText.font = UIFont;
            labelText.fontSize = 18;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = s_groupColor;
            labelText.alignment = TextAnchor.MiddleLeft;

            // Section reset button — resets every Tweakable in this group.
            var resetBtn = AddButton(go.transform, "Reset", new Vector2(-12f, 0f), new Vector2(120f, 28f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            string capturedGroup = group;
            resetBtn.onClick.AddListener(() =>
            {
                foreach (Tweakables.Spec sp in Tweakables.All)
                {
                    if (sp.Group == capturedGroup) Tweakables.Reset(sp.Key);
                }
                RefreshAllRows();
            });

            // Foldout toggle button covers the row's left/centre area
            // (excluding the reset button) so clicking the label or chevron
            // collapses/expands.
            var foldBtnGO = NewChild("FoldClick", go.transform);
            var foldRT = foldBtnGO.GetComponent<RectTransform>();
            foldRT.anchorMin = Vector2.zero;
            foldRT.anchorMax = Vector2.one;
            foldRT.offsetMin = Vector2.zero;
            foldRT.offsetMax = new Vector2(-140f, 0f); // leave room for reset button
            var foldImg = foldBtnGO.AddComponent<Image>();
            foldImg.color = new Color(1f, 1f, 1f, 0f); // invisible click target
            var foldBtn = foldBtnGO.AddComponent<Button>();
            foldBtn.targetGraphic = foldImg;
            string capturedKey = group;
            foldBtn.onClick.AddListener(() => ToggleGroupExpanded(capturedKey));

            var section = new GroupSection
            {
                Name = group,
                Header = go,
                ChevronRT = chevronRT,
                Expanded = true,
            };
            _groups[group] = section;
            ApplyGroupExpansion(section);
            return section;
        }

        private void ToggleGroupExpanded(string groupKey)
        {
            if (!_groups.TryGetValue(groupKey, out GroupSection g)) return;
            g.Expanded = !g.Expanded;
            ApplyGroupExpansion(g);
            ApplyFilter();
        }

        private static void ApplyGroupExpansion(GroupSection g)
        {
            // Chevron points right when collapsed, down when expanded.
            if (g.ChevronRT != null)
            {
                g.ChevronRT.localRotation = Quaternion.Euler(0f, 0f, g.Expanded ? -90f : 0f);
            }
            foreach (RowEntry r in g.Rows)
            {
                r.Row.SetActive(g.Expanded);
            }
        }

        private RowEntry AddSliderRow(Tweakables.Spec spec)
        {
            var rowGO = NewChild($"Row_{spec.Key}", _content.transform);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            rowGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

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

            var sliderGO = NewChild("Slider", rowGO.transform);
            var sliderRT = sliderGO.GetComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0f, 0.5f);
            sliderRT.anchorMax = new Vector2(1f, 0.5f);
            sliderRT.pivot = new Vector2(0.5f, 0.5f);
            sliderRT.offsetMin = new Vector2(240f, -10f);
            sliderRT.offsetMax = new Vector2(-260f,  10f);
            Slider slider = BuildSlider(sliderGO, spec);

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

            var resetBtn = AddButton(rowGO.transform, "↺", new Vector2(-8f, 0f), new Vector2(40f, 32f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            string capturedKey = spec.Key;
            resetBtn.onClick.AddListener(() =>
            {
                Tweakables.Reset(capturedKey);
                RefreshAllRows();
            });

            slider.onValueChanged.AddListener(v =>
            {
                Tweakables.Set(spec.Key, v);
                valueText.text = FormatValue(v);
            });
            return new RowEntry { Spec = spec, Row = rowGO, Slider = slider, ValueText = valueText };
        }

        private RowEntry AddBoolRow(Tweakables.Spec spec)
        {
            var rowGO = NewChild($"Row_{spec.Key}", _content.transform);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            rowGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            // Label on the left.
            var labelGO = NewChild("Label", rowGO.transform);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.sizeDelta = new Vector2(560f, 0f);
            labelRT.anchoredPosition = new Vector2(12f, 0f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = spec.Label;
            labelText.font = UIFont;
            labelText.fontSize = 18;
            labelText.color = s_textColor;
            labelText.alignment = TextAnchor.MiddleLeft;

            // Toggle on the right (with reset button further right).
            var toggleGO = NewChild("Toggle", rowGO.transform);
            var toggleRT = toggleGO.GetComponent<RectTransform>();
            toggleRT.anchorMin = new Vector2(1f, 0.5f);
            toggleRT.anchorMax = new Vector2(1f, 0.5f);
            toggleRT.pivot = new Vector2(1f, 0.5f);
            toggleRT.sizeDelta = new Vector2(180f, 32f);
            toggleRT.anchoredPosition = new Vector2(-60f, 0f);
            Toggle toggle = BuildToggle(toggleGO, spec);

            var resetBtn = AddButton(rowGO.transform, "↺", new Vector2(-8f, 0f), new Vector2(40f, 32f),
                anchor: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f));
            string capturedKey = spec.Key;
            resetBtn.onClick.AddListener(() =>
            {
                Tweakables.Reset(capturedKey);
                RefreshAllRows();
            });

            return new RowEntry { Spec = spec, Row = rowGO, Toggle = toggle };
        }

        private static Slider BuildSlider(GameObject host, Tweakables.Spec spec)
        {
            var bg = NewChild("Background", host.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f);
            bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.18f);

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

        private static Toggle BuildToggle(GameObject host, Tweakables.Spec spec)
        {
            // Invisible click target covering the whole toggle area so the
            // Toggle component picks up clicks anywhere across the State
            // label + track, not just the small pill graphic.
            var clickImg = host.AddComponent<Image>();
            clickImg.color = new Color(1f, 1f, 1f, 0f);

            // Track (background pill).
            var track = NewChild("Track", host.transform);
            var trackRT = track.GetComponent<RectTransform>();
            trackRT.anchorMin = new Vector2(1f, 0.5f);
            trackRT.anchorMax = new Vector2(1f, 0.5f);
            trackRT.pivot = new Vector2(1f, 0.5f);
            trackRT.sizeDelta = new Vector2(56f, 26f);
            trackRT.anchoredPosition = Vector2.zero;
            var trackImg = track.AddComponent<Image>();
            trackImg.color = new Color(1f, 1f, 1f, 0.22f);

            // Knob — moves left/right based on value.
            var knob = NewChild("Knob", track.transform);
            var knobRT = knob.GetComponent<RectTransform>();
            knobRT.anchorMin = new Vector2(0f, 0.5f);
            knobRT.anchorMax = new Vector2(0f, 0.5f);
            knobRT.pivot = new Vector2(0f, 0.5f);
            knobRT.sizeDelta = new Vector2(22f, 22f);
            knobRT.anchoredPosition = new Vector2(2f, 0f);
            var knobImg = knob.AddComponent<Image>();
            knobImg.color = Color.white;

            // ON/OFF label sits to the left of the track.
            var stateGO = NewChild("State", host.transform);
            var stateRT = stateGO.GetComponent<RectTransform>();
            stateRT.anchorMin = new Vector2(0f, 0f);
            stateRT.anchorMax = new Vector2(1f, 1f);
            stateRT.offsetMin = new Vector2(0f, 0f);
            stateRT.offsetMax = new Vector2(-66f, 0f);
            var stateText = stateGO.AddComponent<Text>();
            stateText.text = "OFF";
            stateText.font = UIFont;
            stateText.fontSize = 16;
            stateText.fontStyle = FontStyle.Bold;
            stateText.color = s_textDimColor;
            stateText.alignment = TextAnchor.MiddleRight;

            // Toggle component drives the visual state via onValueChanged.
            // targetGraphic = the host's invisible click image, so the
            // entire toggle region (State label + track) acts as one button.
            var toggle = host.AddComponent<Toggle>();
            toggle.targetGraphic = clickImg;
            toggle.transition = Selectable.Transition.None;
            toggle.isOn = Tweakables.GetBool(spec.Key);
            ApplyToggleVisuals(toggle.isOn, trackImg, knobRT, stateText);
            string capturedKey = spec.Key;
            toggle.onValueChanged.AddListener(on =>
            {
                Tweakables.SetBool(capturedKey, on);
                ApplyToggleVisuals(on, trackImg, knobRT, stateText);
            });
            return toggle;
        }

        private static void ApplyToggleVisuals(bool on, Image track, RectTransform knob, Text stateLabel)
        {
            if (track != null)
                track.color = on ? new Color(0.95f, 0.55f, 0.10f, 0.85f) : new Color(1f, 1f, 1f, 0.22f);
            if (knob != null)
                knob.anchoredPosition = new Vector2(on ? 32f : 2f, 0f);
            if (stateLabel != null)
            {
                stateLabel.text = on ? "ON" : "OFF";
                stateLabel.color = on ? s_textColor : s_textDimColor;
            }
        }

        // -----------------------------------------------------------------
        // Keybinds reference panel
        // -----------------------------------------------------------------

        private void BuildKeybindsSection()
        {
            // Use a foldout group so keybinds nest into the same UI grammar
            // as Tweakables groups. We mark it collapsed-by-default AFTER
            // adding rows: ApplyGroupExpansion only walks Rows, so an
            // empty Rows list at construction time wouldn't hide newly
            // appended rows.
            GroupSection g = AddFoldoutGroupHeader("Keybinds");

            AddKeybindRow(g, "Pitch / throttle",         "W / S");
            AddKeybindRow(g, "Steer / roll",             "A / D");
            AddKeybindRow(g, "Vertical (jump / climb)",  "Space");
            AddKeybindRow(g, "Fire primary",             "Mouse 1");
            AddKeybindRow(g, "Aim down sights",          "Mouse 2 (hold)");
            AddKeybindRow(g, "Camera zoom (orbit)",      "Mouse wheel");
            AddKeybindRow(g, "Release grapples",         "R");
            AddKeybindRow(g, "Respawn player",           "K");
            AddKeybindRow(g, "Begin combat (warmup → live)", "`  (backtick)");
            AddKeybindRow(g, "Toggle settings",          "Esc");
            AddKeybindRow(g, "Toggle dev HUD",           "F1");

            g.Expanded = false;
            ApplyGroupExpansion(g);
        }

        private void AddKeybindRow(GroupSection g, string action, string keys)
        {
            var rowGO = NewChild($"Key_{action}", _content.transform);
            var le = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 32f;
            rowGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);

            var actionGO = NewChild("Action", rowGO.transform);
            var actionRT = actionGO.GetComponent<RectTransform>();
            actionRT.anchorMin = new Vector2(0f, 0f);
            actionRT.anchorMax = new Vector2(0.6f, 1f);
            actionRT.offsetMin = new Vector2(20f, 0f);
            actionRT.offsetMax = new Vector2(-8f, 0f);
            var actionText = actionGO.AddComponent<Text>();
            actionText.text = action;
            actionText.font = UIFont;
            actionText.fontSize = 16;
            actionText.color = s_textColor;
            actionText.alignment = TextAnchor.MiddleLeft;

            var keyGO = NewChild("Keys", rowGO.transform);
            var keyRT = keyGO.GetComponent<RectTransform>();
            keyRT.anchorMin = new Vector2(0.6f, 0f);
            keyRT.anchorMax = new Vector2(1f, 1f);
            keyRT.offsetMin = new Vector2(8f, 0f);
            keyRT.offsetMax = new Vector2(-12f, 0f);
            var keyText = keyGO.AddComponent<Text>();
            keyText.text = keys;
            keyText.font = UIFont;
            keyText.fontSize = 16;
            keyText.fontStyle = FontStyle.Bold;
            keyText.color = s_groupColor;
            keyText.alignment = TextAnchor.MiddleRight;

            // Stub spec so the row shares the visibility / search pipeline
            // even though there's nothing to tweak. Keys field carries the
            // bound shortcut so the search filter matches it.
            var stubSpec = new Tweakables.Spec("__keybind__" + action, "Keybinds", action + " — " + keys, 0f, 0f, 1f, Tweakables.SpecKind.Bool);
            var entry = new RowEntry { Spec = stubSpec, Row = rowGO };
            g.Rows.Add(entry);
            _allRows.Add(entry);
        }

        // -----------------------------------------------------------------
        // Refresh
        // -----------------------------------------------------------------

        private void RefreshAllRows()
        {
            foreach (RowEntry r in _allRows)
            {
                if (r.Slider != null)
                {
                    float v = Tweakables.Get(r.Spec.Key);
                    r.Slider.SetValueWithoutNotify(v);
                    if (r.ValueText != null) r.ValueText.text = FormatValue(v);
                }
                else if (r.Toggle != null)
                {
                    bool on = Tweakables.GetBool(r.Spec.Key);
                    r.Toggle.SetIsOnWithoutNotify(on);
                    // Visual sync: walk the toggle's children to find the
                    // pill + knob + state label since they're all
                    // siblings/children of the host (created in BuildToggle).
                    Transform host = r.Toggle.transform;
                    Image track = host.Find("Track")?.GetComponent<Image>();
                    RectTransform knobRT = host.Find("Track/Knob") as RectTransform;
                    Text stateLabel = host.Find("State")?.GetComponent<Text>();
                    ApplyToggleVisuals(on, track, knobRT, stateLabel);
                }
            }
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
            sb.value = 1f;
            return sb;
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
            // Audio cue on every Settings button. Method-group listener
            // so we don't capture a closure per button. Caller adds
            // their gameplay listener separately.
            btn.onClick.AddListener(PlayUiClick);
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

        // Method-group hook so AddListener doesn't allocate a closure
        // per button. Static so the AddButton helper (also static) can
        // bind it without a captured `this`.
        private static void PlayUiClick()
            => Robogame.Core.AudioRouter.PlayUI(Robogame.Core.AudioCue.UiClick);

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
