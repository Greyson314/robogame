using System.Collections.Generic;
using Robogame.Block;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode block picker. The bottom-center HUD shows a row of
    /// <see cref="BlockCategory"/> tabs and, beneath them, a slot strip
    /// listing every <see cref="BlockDefinition"/> in the active category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selection is by mouse click, by number keys (1..N selects the Nth
    /// slot in the current category), or by category cycling
    /// (<c>Q</c> / <c>E</c>). The picker enumerates
    /// <see cref="GameStateController.Library"/> at startup and rebuilds
    /// when the library changes (reusing the same root canvas), so
    /// authoring a new <see cref="BlockDefinition"/> automatically
    /// surfaces it in the picker without code edits here.
    /// </para>
    /// <para>
    /// Visible only while build mode is active — subscribes to the
    /// <see cref="BuildModeController"/> Entered/Exited events to toggle
    /// the canvas.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildHotbar : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildMode;
        [SerializeField] private BlockEditor _editor;
        [SerializeField] private Vector2 _slotSize = new Vector2(72f, 72f);
        [SerializeField] private float _slotGap = 8f;
        [SerializeField] private float _bottomMargin = 28f;
        [SerializeField] private Vector2 _tabSize = new Vector2(100f, 28f);
        [SerializeField] private float _tabGap = 6f;

        public BlockEditor Editor { get => _editor; set => _editor = value; }

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode == value) return;
                Unsubscribe();
                _buildMode = value;
                Subscribe();
                SyncVisibility();
            }
        }

        /// <summary>Currently selected block id; <c>BlockIds.Cube</c> if nothing's loaded.</summary>
        public string SelectedBlockId
        {
            get
            {
                List<BlockDefinition> list = ActiveDefs;
                if (list == null || list.Count == 0) return BlockIds.Cube;
                int i = Mathf.Clamp(_selectedSlotIndex, 0, list.Count - 1);
                return list[i].Id;
            }
        }

        // -----------------------------------------------------------------
        // Categories
        // -----------------------------------------------------------------

        // Order matters — this is the visual left-to-right tab order.
        private static readonly BlockCategory[] s_categoryOrder =
        {
            BlockCategory.Structure,
            BlockCategory.Cpu,
            BlockCategory.Movement,
            BlockCategory.Weapon,
            BlockCategory.Module,
            BlockCategory.Cosmetic,
        };

        private static readonly Dictionary<BlockCategory, string> s_categoryLabel = new()
        {
            { BlockCategory.Structure, "Structure" },
            { BlockCategory.Cpu,       "CPU" },
            { BlockCategory.Movement,  "Movement" },
            { BlockCategory.Weapon,    "Weapons" },
            { BlockCategory.Module,    "Modules" },
            { BlockCategory.Cosmetic,  "Cosmetic" },
        };

        private struct Tab
        {
            public BlockCategory Category;
            public Image Background;
        }

        private struct Slot
        {
            public BlockDefinition Def;
            public Image Background;
            public Text  LabelText;
            public Text  NumberText;
        }

        // Categorised view of the live library.
        private readonly Dictionary<BlockCategory, List<BlockDefinition>> _byCategory = new();
        private readonly List<BlockCategory> _activeCategories = new();
        private int _activeCategoryIndex = 0;
        private int _selectedSlotIndex = 0;

        private List<BlockDefinition> ActiveDefs
        {
            get
            {
                if (_activeCategories.Count == 0) return null;
                BlockCategory c = _activeCategories[Mathf.Clamp(_activeCategoryIndex, 0, _activeCategories.Count - 1)];
                return _byCategory.TryGetValue(c, out var list) ? list : null;
            }
        }

        // UI roots ---------------------------------------------------------
        private GameObject _root;
        private RectTransform _tabRow;
        private RectTransform _slotRow;
        private readonly List<Tab> _tabs = new();
        private readonly List<Slot> _slots = new();
        private Text _cpuReadout;
        private Text _detailText;
        private bool _subscribed;
        private bool _libraryDirty = true;
        private int _lastLibraryVersion = -1;

        private void Awake()
        {
            BuildCanvas();
            SetVisible(false);
        }

        private void OnEnable()
        {
            Subscribe();
            SyncVisibility();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || _buildMode == null) return;
            _buildMode.Entered += HandleEntered;
            _buildMode.Exited  += HandleExited;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _buildMode == null) return;
            _buildMode.Entered -= HandleEntered;
            _buildMode.Exited  -= HandleExited;
            _subscribed = false;
        }

        private void HandleEntered()
        {
            // Re-resolve the library each time we enter — picks up any
            // newly-authored definitions without forcing a play-mode
            // restart.
            _libraryDirty = true;
            SetVisible(true);
        }

        private void HandleExited() => SetVisible(false);

        private void SyncVisibility()
        {
            SetVisible(_buildMode != null && _buildMode.IsActive);
        }

        private void Update()
        {
            // Lazy-subscribe in case BuildMode was assigned after our OnEnable ran.
            if (!_subscribed && _buildMode != null) { Subscribe(); SyncVisibility(); }
            if (_buildMode == null || !_buildMode.IsActive) return;

            EnsureLibraryLoaded();

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                // Q / E cycles category tabs.
                if (kb.qKey.wasPressedThisFrame) CycleCategory(-1);
                if (kb.eKey.wasPressedThisFrame) CycleCategory(+1);

                // Number keys 1..N select Nth slot in the active category.
                List<BlockDefinition> defs = ActiveDefs;
                int n = defs != null ? Mathf.Min(defs.Count, 9) : 0;
                if (n > 0)
                {
                    if (kb.digit1Key.wasPressedThisFrame) SetSelectedSlot(0);
                    if (n >= 2 && kb.digit2Key.wasPressedThisFrame) SetSelectedSlot(1);
                    if (n >= 3 && kb.digit3Key.wasPressedThisFrame) SetSelectedSlot(2);
                    if (n >= 4 && kb.digit4Key.wasPressedThisFrame) SetSelectedSlot(3);
                    if (n >= 5 && kb.digit5Key.wasPressedThisFrame) SetSelectedSlot(4);
                    if (n >= 6 && kb.digit6Key.wasPressedThisFrame) SetSelectedSlot(5);
                    if (n >= 7 && kb.digit7Key.wasPressedThisFrame) SetSelectedSlot(6);
                    if (n >= 8 && kb.digit8Key.wasPressedThisFrame) SetSelectedSlot(7);
                    if (n >= 9 && kb.digit9Key.wasPressedThisFrame) SetSelectedSlot(8);
                }
            }

            RefreshCpuReadout();
            RefreshDetailText();
        }

        // -----------------------------------------------------------------
        // Library load / category build
        // -----------------------------------------------------------------

        private void EnsureLibraryLoaded()
        {
            GameStateController state = GameStateController.Instance;
            BlockDefinitionLibrary lib = state != null ? state.Library : null;
            if (lib == null) return;

            // Cheap dirty check: re-enumerate when the count changes or we
            // were asked to refresh on Enter. The library is small so this
            // is fine to redo on every Enter.
            int version = lib.Definitions != null ? lib.Definitions.Count : 0;
            if (!_libraryDirty && version == _lastLibraryVersion) return;

            RebuildCategoryIndex(lib);
            _lastLibraryVersion = version;
            _libraryDirty = false;
            RebuildTabs();
            RebuildSlots();
            ApplySelectionVisuals();
        }

        private void RebuildCategoryIndex(BlockDefinitionLibrary lib)
        {
            _byCategory.Clear();
            _activeCategories.Clear();
            if (lib == null || lib.Definitions == null) return;

            foreach (BlockDefinition def in lib.Definitions)
            {
                if (def == null) continue;
                if (!_byCategory.TryGetValue(def.Category, out var list))
                {
                    list = new List<BlockDefinition>();
                    _byCategory[def.Category] = list;
                }
                list.Add(def);
            }

            // Preserve the canonical category order for visible tabs.
            for (int i = 0; i < s_categoryOrder.Length; i++)
            {
                BlockCategory c = s_categoryOrder[i];
                if (_byCategory.TryGetValue(c, out var list) && list.Count > 0)
                    _activeCategories.Add(c);
            }
            // Pick up any unexpected categories at the tail.
            foreach (var kvp in _byCategory)
            {
                if (!_activeCategories.Contains(kvp.Key)) _activeCategories.Add(kvp.Key);
            }

            _activeCategoryIndex = Mathf.Clamp(_activeCategoryIndex, 0, Mathf.Max(0, _activeCategories.Count - 1));
            _selectedSlotIndex = 0;
        }

        private void CycleCategory(int delta)
        {
            if (_activeCategories.Count == 0) return;
            int n = _activeCategories.Count;
            _activeCategoryIndex = ((_activeCategoryIndex + delta) % n + n) % n;
            _selectedSlotIndex = 0;
            RebuildSlots();
            ApplySelectionVisuals();
        }

        private void SetSelectedSlot(int index)
        {
            List<BlockDefinition> defs = ActiveDefs;
            if (defs == null || defs.Count == 0) return;
            _selectedSlotIndex = Mathf.Clamp(index, 0, defs.Count - 1);
            ApplySelectionVisuals();
        }

        private void SetActiveCategory(int index)
        {
            if (index < 0 || index >= _activeCategories.Count) return;
            _activeCategoryIndex = index;
            _selectedSlotIndex = 0;
            RebuildSlots();
            ApplySelectionVisuals();
        }

        private void RefreshCpuReadout()
        {
            if (_cpuReadout == null || _editor == null) return;
            BlockEditor.CpuUsage u = _editor.GetCpuUsage();
            _cpuReadout.text = $"CPU  {u.Used} / {u.Cap}";
            bool hot = u.Cap == 0 || u.OverBudget || u.Used >= u.Cap;
            _cpuReadout.color = hot
                ? new Color(0.95f, 0.30f, 0.25f, 1f)
                : new Color(1f, 1f, 1f, 0.9f);
        }

        private void RefreshDetailText()
        {
            if (_detailText == null) return;
            List<BlockDefinition> defs = ActiveDefs;
            if (defs == null || defs.Count == 0) { _detailText.text = string.Empty; return; }
            BlockDefinition def = defs[Mathf.Clamp(_selectedSlotIndex, 0, defs.Count - 1)];
            if (def == null) { _detailText.text = string.Empty; return; }
            _detailText.text =
                $"{def.DisplayName}   ·   mass {def.Mass:0.##}kg   ·   CPU {def.CpuCost}   ·   HP {def.MaxHealth:0}";
        }

        // -----------------------------------------------------------------
        // UI build
        // -----------------------------------------------------------------

        private void BuildCanvas()
        {
            _root = new GameObject("BuildHotbarCanvas");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95; // sit just below the SceneTransitionHud.
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _root.AddComponent<GraphicRaycaster>();

            // Slot row container (centered, just above _bottomMargin).
            var slotRowGO = new GameObject("SlotRow");
            slotRowGO.transform.SetParent(_root.transform, worldPositionStays: false);
            _slotRow = slotRowGO.AddComponent<RectTransform>();
            _slotRow.anchorMin = new Vector2(0.5f, 0f);
            _slotRow.anchorMax = new Vector2(0.5f, 0f);
            _slotRow.pivot     = new Vector2(0.5f, 0f);
            _slotRow.sizeDelta = new Vector2(0f, _slotSize.y);
            _slotRow.anchoredPosition = new Vector2(0f, _bottomMargin);

            // Tab row container, sits above the slot row.
            var tabRowGO = new GameObject("TabRow");
            tabRowGO.transform.SetParent(_root.transform, worldPositionStays: false);
            _tabRow = tabRowGO.AddComponent<RectTransform>();
            _tabRow.anchorMin = new Vector2(0.5f, 0f);
            _tabRow.anchorMax = new Vector2(0.5f, 0f);
            _tabRow.pivot     = new Vector2(0.5f, 0f);
            _tabRow.sizeDelta = new Vector2(0f, _tabSize.y);
            _tabRow.anchoredPosition = new Vector2(0f, _bottomMargin + _slotSize.y + 22f);

            // Detail line (block name + stats) sits between slots and tabs.
            var detailGO = new GameObject("Detail");
            detailGO.transform.SetParent(_root.transform, worldPositionStays: false);
            _detailText = detailGO.AddComponent<Text>();
            _detailText.alignment = TextAnchor.MiddleCenter;
            _detailText.fontSize = 14;
            _detailText.color = new Color(1f, 1f, 1f, 0.7f);
            _detailText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _detailText.text = string.Empty;
            var drt = detailGO.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 0f);
            drt.anchorMax = new Vector2(0.5f, 0f);
            drt.pivot     = new Vector2(0.5f, 0f);
            drt.sizeDelta = new Vector2(560f, 18f);
            drt.anchoredPosition = new Vector2(0f, _bottomMargin + _slotSize.y + 4f);

            // CPU readout sits centered above the tab row.
            var cpuGO = new GameObject("CpuReadout");
            cpuGO.transform.SetParent(_root.transform, worldPositionStays: false);
            _cpuReadout = cpuGO.AddComponent<Text>();
            _cpuReadout.alignment = TextAnchor.MiddleCenter;
            _cpuReadout.fontSize = 18;
            _cpuReadout.color = new Color(1f, 1f, 1f, 0.9f);
            _cpuReadout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _cpuReadout.text = "CPU  0 / 0";
            var crt = cpuGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0f);
            crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot     = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(260f, 24f);
            crt.anchoredPosition = new Vector2(0f, _bottomMargin + _slotSize.y + _tabSize.y + 30f);
        }

        private void RebuildTabs()
        {
            // Clear previous tabs.
            foreach (var t in _tabs)
            {
                if (t.Background != null) Destroy(t.Background.gameObject);
            }
            _tabs.Clear();

            int n = _activeCategories.Count;
            if (n == 0) return;
            float totalW = n * _tabSize.x + (n - 1) * _tabGap;
            float startX = -totalW * 0.5f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                BlockCategory cat = _activeCategories[i];

                var tabGO = new GameObject($"Tab_{cat}");
                tabGO.transform.SetParent(_tabRow, worldPositionStays: false);
                var img = tabGO.AddComponent<Image>();
                img.color = TabColor(false);

                var btn = tabGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetActiveCategory(idx));

                var rt = tabGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot     = new Vector2(0f, 0.5f);
                rt.sizeDelta = _tabSize;
                rt.anchoredPosition = new Vector2(startX + i * (_tabSize.x + _tabGap), 0f);

                var labelGO = new GameObject("Label");
                labelGO.transform.SetParent(tabGO.transform, worldPositionStays: false);
                var label = labelGO.AddComponent<Text>();
                label.text = s_categoryLabel.TryGetValue(cat, out var s) ? s : cat.ToString();
                label.fontSize = 14;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.font = font;
                var lrt = labelGO.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;

                _tabs.Add(new Tab { Category = cat, Background = img });
            }
        }

        private void RebuildSlots()
        {
            // Clear previous slots.
            foreach (var s in _slots)
            {
                if (s.Background != null) Destroy(s.Background.gameObject);
            }
            _slots.Clear();

            List<BlockDefinition> defs = ActiveDefs;
            if (defs == null || defs.Count == 0) return;

            int n = defs.Count;
            float totalW = n * _slotSize.x + (n - 1) * _slotGap;
            float startX = -totalW * 0.5f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                BlockDefinition def = defs[i];

                var slotGO = new GameObject($"Slot_{i + 1}_{def.Id}");
                slotGO.transform.SetParent(_slotRow, worldPositionStays: false);
                var img = slotGO.AddComponent<Image>();
                img.color = SlotColor(false);

                var btn = slotGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetSelectedSlot(idx));

                var rt = slotGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot     = new Vector2(0f, 0.5f);
                rt.sizeDelta = _slotSize;
                rt.anchoredPosition = new Vector2(startX + i * (_slotSize.x + _slotGap), 0f);

                // Number ribbon (only shown for the first 9 slots).
                Text num = null;
                if (i < 9)
                {
                    var numGO = new GameObject("Num");
                    numGO.transform.SetParent(slotGO.transform, worldPositionStays: false);
                    num = numGO.AddComponent<Text>();
                    num.text = (i + 1).ToString();
                    num.fontSize = 16;
                    num.alignment = TextAnchor.UpperLeft;
                    num.color = new Color(1f, 1f, 1f, 0.75f);
                    num.font = font;
                    var numRT = numGO.GetComponent<RectTransform>();
                    numRT.anchorMin = Vector2.zero;
                    numRT.anchorMax = Vector2.one;
                    numRT.offsetMin = new Vector2(6f, 4f);
                    numRT.offsetMax = new Vector2(-4f, -4f);
                }

                // Label (centered).
                var labelGO = new GameObject("Label");
                labelGO.transform.SetParent(slotGO.transform, worldPositionStays: false);
                var label = labelGO.AddComponent<Text>();
                label.text = ShortLabel(def);
                label.fontSize = 14;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.font = font;
                var lrt = labelGO.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;

                _slots.Add(new Slot { Def = def, Background = img, LabelText = label, NumberText = num });
            }
        }

        /// <summary>Compact label for the slot square.</summary>
        private static string ShortLabel(BlockDefinition def)
        {
            if (def == null) return "?";
            string n = def.DisplayName;
            if (string.IsNullOrEmpty(n)) return def.Id;
            // Strip any trailing "Block" suffix and clamp to ~8 chars
            // so the slot square stays legible.
            if (n.EndsWith(" Block")) n = n.Substring(0, n.Length - 6);
            return n.Length <= 8 ? n : n.Substring(0, 8);
        }

        private void ApplySelectionVisuals()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Background != null)
                    _tabs[i].Background.color = TabColor(i == _activeCategoryIndex);
            }
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Background != null)
                    _slots[i].Background.color = SlotColor(i == _selectedSlotIndex);
            }
        }

        private static Color SlotColor(bool selected) => selected
            ? new Color(0.95f, 0.55f, 0.10f, 0.95f)   // hazard orange when active
            : new Color(0.10f, 0.12f, 0.16f, 0.92f);  // dark panel otherwise

        private static Color TabColor(bool active) => active
            ? new Color(0.95f, 0.55f, 0.10f, 0.95f)
            : new Color(0.06f, 0.08f, 0.11f, 0.85f);

        private void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }
    }
}
