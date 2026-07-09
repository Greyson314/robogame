using System;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// The garage "Laboratory" screen: author player <see cref="Concoction"/>s —
    /// custom explosive payloads. Three sliders (damage / size / knockback,
    /// 0–100%, default 50%) raise the recipe's CPU surcharge as you raise power.
    /// Saved concoctions persist via <see cref="ConcoctionLibrary"/> and are
    /// chosen per explosive block in the variant panel's dropdown. See ADR-0004.
    /// </summary>
    /// <remarks>
    /// Full-screen overlay panel built procedurally (same IMGUI-free UGUI
    /// approach as <see cref="VariantConfigPanel"/>). Opens over the garage;
    /// closes on its own button or when build mode toggles. Loading + saving
    /// hit <see cref="ConcoctionLibrary"/> (tiny directory, cheap). The runtime
    /// <see cref="ConcoctionRegistry"/> is refreshed on every save so the
    /// variant dropdown and CPU bar see new recipes immediately.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LabController : MonoBehaviour
    {
        // Surcharge expressed as a fraction of the weapon's base CPU: the Lab
        // is block-agnostic (a recipe can go on a bomb or a mortar), so it
        // shows "+X% of weapon CPU" rather than an absolute number. Mirrors
        // Concoction.CpuSurcharge's (sliderSum × 0.5) factor.
        private const float SurchargeFactorPerSliderSum = 0.5f;

        private BuildModeController _buildMode;
        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode != null) { _buildMode.Entered -= HandleBuildModeChanged; _buildMode.Exited -= HandleBuildModeChanged; }
                _buildMode = value;
                if (_buildMode != null) { _buildMode.Entered += HandleBuildModeChanged; _buildMode.Exited += HandleBuildModeChanged; }
            }
        }

        public bool IsOpen => _root != null && _root.activeSelf;

        // Editor state.
        private string _editingId = string.Empty;   // "" = authoring a new recipe
        private float _dmg = Concoction.DefaultPct;
        private float _size = Concoction.DefaultPct;
        private float _kb = Concoction.DefaultPct;

        // ----- UGUI -----
        private GameObject _root;
        private InputField _nameField;
        private Slider _dmgSlider, _sizeSlider, _kbSlider;
        private Text _dmgValue, _sizeValue, _kbValue;
        private Text _cpuReadout;
        private GameObject _listContent;
        private bool _suppress;

        // Panel chrome flows from the shared UGUI theme so every menu / build
        // panel reskins from one place. See docs/subsystems/ui-direction.md.
        private static readonly Color s_accent  = UguiPalette.Accent;
        private static readonly Color s_dim     = UguiPalette.TextDim;
        private static readonly Color s_backdrop = UguiPalette.Backdrop;
        private static readonly Color s_panelBg = UguiPalette.PanelBg;
        private static readonly Color s_btnIdle  = UguiPalette.ButtonIdle;
        private static readonly Color s_btnHi    = UguiPalette.Accent;
        private static readonly Color s_btnPress = UguiPalette.AccentPressed;

        private static Font UIFont => Robogame.Core.InkKit.Display;

        private void Awake()
        {
            BuildCanvas();
            SetOpen(false);
        }

        private void OnEnable() => ConcoctionLibrary.Changed += RefreshList;
        private void OnDisable() => ConcoctionLibrary.Changed -= RefreshList;

        private void OnDestroy()
        {
            if (_buildMode != null) { _buildMode.Entered -= HandleBuildModeChanged; _buildMode.Exited -= HandleBuildModeChanged; }
        }

        // Entering / exiting build mode closes the Lab (it's a garage-root sub-screen).
        private void HandleBuildModeChanged() => SetOpen(false);

        public void Toggle()
        {
            if (IsOpen) SetOpen(false);
            else Open();
        }

        public void Open()
        {
            ConcoctionRegistry.ReloadFromLibrary();
            NewConcoction();          // start on a fresh recipe
            RefreshList();
            SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            if (_root != null) _root.SetActive(open);
            // Free the cursor while the Lab is up so the player can click
            // sliders / type a name (the garage normally locks it for orbit).
            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // -----------------------------------------------------------------
        // Editor actions
        // -----------------------------------------------------------------

        private void NewConcoction()
        {
            _editingId = string.Empty;
            _dmg = _size = _kb = Concoction.DefaultPct;
            SyncEditorToFields(NextFreeMixName());
        }

        // Default a fresh recipe to the lowest unused "Mix N" instead of a fixed
        // "New Mix" (playtest, session 120 — every recipe defaulted to the same
        // name). The player can still rename in the field before saving.
        private static string NextFreeMixName()
        {
            var taken = new HashSet<string>();
            foreach (Concoction c in ConcoctionRegistry.GetAll())
                if (c != null && !string.IsNullOrEmpty(c.DisplayName)) taken.Add(c.DisplayName);
            for (int n = 1; ; n++)
            {
                string candidate = "Mix " + n;
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        private void LoadIntoEditor(Concoction c)
        {
            if (c == null) return;
            _editingId = c.Id;
            _dmg = c.DamagePct; _size = c.SizePct; _kb = c.KnockbackPct;
            SyncEditorToFields(c.DisplayName);
        }

        private void SyncEditorToFields(string name)
        {
            _suppress = true;
            if (_nameField != null) _nameField.text = string.IsNullOrEmpty(name) ? "" : name;
            if (_dmgSlider != null) _dmgSlider.value = _dmg;
            if (_sizeSlider != null) _sizeSlider.value = _size;
            if (_kbSlider != null) _kbSlider.value = _kb;
            _suppress = false;
            UpdateValues();
        }

        private void Save()
        {
            string name = _nameField != null && !string.IsNullOrWhiteSpace(_nameField.text)
                ? _nameField.text.Trim() : "Concoction";
            // Reuse the id when editing an existing recipe so Save overwrites
            // its file; otherwise mint a fresh stable id.
            string id = string.IsNullOrEmpty(_editingId)
                ? "cx-" + Guid.NewGuid().ToString("N").Substring(0, 12)
                : _editingId;

            var c = new Concoction(id, name, _dmg, _size, _kb);
            c.Validate();
            // Stable per-id filename → editing the same recipe overwrites it.
            ConcoctionLibrary.Save(c, id + ConcoctionLibrary.Extension);
            ConcoctionRegistry.ReloadFromLibrary();   // make it pickable immediately
            _editingId = id;
            AudioRouter.PlayOneShot(AudioCue.LabSave, transform.position);
            RefreshList();
        }

        private void DeleteCurrent()
        {
            if (string.IsNullOrEmpty(_editingId)) return;
            ConcoctionLibrary.Delete(_editingId + ConcoctionLibrary.Extension);
            ConcoctionRegistry.ReloadFromLibrary();
            NewConcoction();
            RefreshList();
        }

        private void OnDmgChanged(float v)  { if (_suppress) return; _dmg  = v; UpdateValues(); }
        private void OnSizeChanged(float v) { if (_suppress) return; _size = v; UpdateValues(); }
        private void OnKbChanged(float v)   { if (_suppress) return; _kb   = v; UpdateValues(); }

        private void UpdateValues()
        {
            if (_dmgValue  != null) _dmgValue.text  = $"{Mathf.RoundToInt(_dmg * 100f)}%";
            if (_sizeValue != null) _sizeValue.text = $"{Mathf.RoundToInt(_size * 100f)}%";
            if (_kbValue   != null) _kbValue.text   = $"{Mathf.RoundToInt(_kb * 100f)}%";
            if (_cpuReadout != null)
            {
                float factor = (_dmg + _size + _kb) * SurchargeFactorPerSliderSum; // 0..1.5
                _cpuReadout.text =
                    $"dmg ×{Concoction.Multiplier(_dmg):0.0}   size ×{Concoction.Multiplier(_size):0.0}   kb ×{Concoction.Multiplier(_kb):0.0}" +
                    $"     •     CPU surcharge +{Mathf.RoundToInt(factor * 100f)}% of weapon base";
                _cpuReadout.color = factor > 1.0f ? s_accent : s_dim;
            }
        }

        private void RefreshList()
        {
            if (_listContent == null) return;
            for (int i = _listContent.transform.childCount - 1; i >= 0; i--)
                Destroy(_listContent.transform.GetChild(i).gameObject);

            List<ConcoctionLibrary.Record> records = ConcoctionLibrary.LoadAll();
            const float rowH = 34f;
            var contentRT = _listContent.GetComponent<RectTransform>();
            contentRT.sizeDelta = new Vector2(0f, Mathf.Max(1, records.Count) * rowH);
            for (int i = 0; i < records.Count; i++)
            {
                Concoction c = records[i].Concoction;
                var go = NewChild($"Row_{i}", _listContent.transform);
                var img = go.AddComponent<Image>();
                img.color = c.Id == _editingId ? s_btnPress : s_btnIdle;
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                ColorBlock cols = btn.colors; cols.highlightedColor = s_btnHi; cols.pressedColor = s_btnPress; btn.colors = cols;
                Concoction captured = c;
                btn.onClick.AddListener(() => { LoadIntoEditor(captured); RefreshList(); });
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-6f, rowH - 4f);
                rt.anchoredPosition = new Vector2(0f, -i * rowH - 2f);
                AddText(go.transform, c.DisplayName, new Vector2(8f, 0f), new Vector2(-8f, 0f),
                    Vector2.zero, Vector2.one, 13, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            }
        }

        // -----------------------------------------------------------------
        // UGUI build
        // -----------------------------------------------------------------

        private void BuildCanvas()
        {
            _root = new GameObject("LabCanvas");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110; // above the variant panel (96)
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _root.AddComponent<GraphicRaycaster>();

            // Full-screen dim backdrop (also eats clicks behind the panel).
            var backdrop = NewChild("Backdrop", _root.transform);
            Stretch(backdrop.GetComponent<RectTransform>());
            backdrop.AddComponent<Image>().color = s_backdrop;

            // Centered panel.
            var panel = NewChild("Panel", _root.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(720f, 460f);
            prt.anchoredPosition = Vector2.zero;
            panel.AddComponent<Image>().color = s_panelBg;

            AddText(panel.transform, "LABORATORY  —  EXPLOSIVES", new Vector2(20f, -16f), new Vector2(-20f, -44f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 22, FontStyle.Bold, TextAnchor.MiddleLeft, s_accent);

            // Close button (top-right).
            BuildButton(panel.transform, "CLOSE", new Vector2(1f, 1f), new Vector2(-16f, -14f),
                new Vector2(90f, 30f), () => SetOpen(false));

            BuildLeftList(panel.transform);
            BuildEditor(panel.transform);
        }

        // Left column: saved-concoction list inside a clipping viewport.
        private void BuildLeftList(Transform panel)
        {
            var viewport = NewChild("ListViewport", panel);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0f, 0f); vrt.anchorMax = new Vector2(0f, 1f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.offsetMin = new Vector2(20f, 60f);
            vrt.offsetMax = new Vector2(0f, -56f);
            vrt.sizeDelta = new Vector2(240f, vrt.sizeDelta.y);
            viewport.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 1f);
            var mask = viewport.AddComponent<RectMask2D>();

            _listContent = NewChild("Content", viewport.transform);
            var crt = _listContent.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 1f);

            AddText(panel, "SAVED MIXES", new Vector2(20f, 0f), new Vector2(260f, 22f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), 12, FontStyle.Bold, TextAnchor.MiddleLeft, s_dim)
                .rectTransform.anchoredPosition = new Vector2(20f, 36f);
        }

        // Right column: name field, three sliders, readout, action buttons.
        private void BuildEditor(Transform panel)
        {
            var col = NewChild("Editor", panel);
            var rt = col.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(276f, 60f);
            rt.offsetMax = new Vector2(-20f, -56f);

            // Name input.
            AddText(col.transform, "NAME", new Vector2(0f, -2f), new Vector2(80f, -28f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, s_dim);
            _nameField = BuildInputField(col.transform, new Vector2(0f, -30f), new Vector2(360f, 30f));

            _dmgSlider  = BuildSliderRow(col.transform, "DAMAGE",    slot: 1, OnDmgChanged,  out _dmgValue);
            _sizeSlider = BuildSliderRow(col.transform, "SIZE",      slot: 2, OnSizeChanged, out _sizeValue);
            _kbSlider   = BuildSliderRow(col.transform, "KNOCKBACK", slot: 3, OnKbChanged,   out _kbValue);

            _cpuReadout = AddText(col.transform, "", new Vector2(0f, 0f), new Vector2(0f, 22f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 13, FontStyle.Italic, TextAnchor.MiddleLeft, s_dim);
            _cpuReadout.rectTransform.pivot = new Vector2(0f, 1f);
            _cpuReadout.rectTransform.anchoredPosition = new Vector2(0f, -30f - 4 * 56f);

            // Action row.
            BuildButton(col.transform, "SAVE",   new Vector2(0f, 0f), new Vector2(0f, 8f),   new Vector2(120f, 34f), Save);
            BuildButton(col.transform, "NEW",    new Vector2(0f, 0f), new Vector2(132f, 8f), new Vector2(110f, 34f), NewConcoction);
            BuildButton(col.transform, "DELETE", new Vector2(0f, 0f), new Vector2(254f, 8f), new Vector2(110f, 34f), DeleteCurrent);
        }

        private Slider BuildSliderRow(Transform parent, string label, int slot,
            UnityEngine.Events.UnityAction<float> onChanged, out Text valueText)
        {
            var row = NewChild($"Row_{label}", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 50f);
            rt.anchoredPosition = new Vector2(0f, -slot * 56f);

            AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(140f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), 14, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            valueText = AddText(row.transform, "50%", new Vector2(-8f, 0f), new Vector2(-8f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 1f), 14, FontStyle.Bold, TextAnchor.MiddleRight, s_accent);
            valueText.rectTransform.sizeDelta = new Vector2(60f, 0f);

            var host = NewChild("Slider", row.transform);
            var srt = host.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0.5f); srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(120f, -10f);
            srt.offsetMax = new Vector2(-72f, 10f);

            var bg = NewChild("Background", host.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f); bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

            var fillArea = NewChild("Fill Area", host.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.4f); faRT.anchorMax = new Vector2(1f, 0.6f);
            faRT.offsetMin = new Vector2(8f, 0f); faRT.offsetMax = new Vector2(-8f, 0f);
            var fill = NewChild("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            fill.AddComponent<Image>().color = s_accent;

            var handleArea = NewChild("Handle Slide Area", host.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(10f, 0f); haRT.offsetMax = new Vector2(-10f, 0f);
            var handle = NewChild("Handle", handleArea.transform);
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(16f, 22f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            var slider = host.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.fillRect = fillRT;
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = Concoction.DefaultPct;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private InputField BuildInputField(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var go = NewChild("NameField", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);

            var textGo = NewChild("Text", go.transform);
            Stretch(textGo.GetComponent<RectTransform>(), 8f);
            var text = textGo.AddComponent<Text>();
            text.font = UIFont; text.fontSize = 15; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;

            var placeholderGo = NewChild("Placeholder", go.transform);
            Stretch(placeholderGo.GetComponent<RectTransform>(), 8f);
            var placeholder = placeholderGo.AddComponent<Text>();
            placeholder.font = UIFont; placeholder.fontSize = 15; placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = s_dim; placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "name your mix…";

            var field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 24;
            field.onValueChanged.AddListener(_ => { });
            return field;
        }

        private void BuildButton(Transform parent, string label, Vector2 anchor, Vector2 anchoredPos, Vector2 size, Action onClick)
        {
            var go = NewChild($"Btn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = s_btnIdle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors; cols.highlightedColor = s_btnHi; cols.pressedColor = s_btnPress; btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());
            AddText(go.transform, label, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                13, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        }

        // -----------------------------------------------------------------
        // UGUI primitives
        // -----------------------------------------------------------------

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static Text AddText(Transform parent, string text, Vector2 offsetMin, Vector2 offsetMax,
            Vector2 anchorMin, Vector2 anchorMax, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var go = NewChild("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var t = go.AddComponent<Text>();
            t.text = text; t.font = UIFont; t.fontSize = size; t.fontStyle = style;
            t.color = color; t.alignment = anchor;
            return t;
        }
    }
}
