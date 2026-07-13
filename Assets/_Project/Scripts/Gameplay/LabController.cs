using System;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// The garage "Laboratory" screen: author player <see cref="Concoction"/>s —
    /// custom ammunition chemistry. Five reagent sliders (damage / size /
    /// knockback / speed / spread, 0–100%, default 50%) mix a live pigment
    /// colour (<see cref="ConcoctionColor"/>) that names the recipe and dyes
    /// its shots in combat; raising any lever raises the CPU surcharge.
    /// Saved concoctions persist via <see cref="ConcoctionLibrary"/> and are
    /// chosen per weapon block in the variant panel's dropdown. See ADR-0004
    /// + docs/decisions/0005 (session 141 full pass).
    /// </summary>
    /// <remarks>
    /// Visual direction: the "night workshop" — the one sanctioned dark
    /// departure from the parchment UI. Soot ground, wood panel with brass
    /// hardware, a recessed concoctions well (left), a raised switchboard
    /// plate (centre) and a live specimen vial (right) whose liquid wears
    /// the mix's <see cref="Concoction.MixedColor"/> and whose fill level
    /// tracks Size + Spread. Tokens/sprites in <see cref="LabKit"/>.
    /// TRACE[DOC:research/ui-design-handoff-laboratory]: layout, elevation
    /// and interaction language come from the July 2026 Laboratory handoff.
    /// Full-screen overlay built procedurally (same UGUI approach as
    /// <see cref="VariantConfigPanel"/>); opens over the garage, closes on
    /// its own button or when build mode toggles. Per-frame work while open
    /// is a handful of colour lerps + three bubble transforms — zero
    /// allocations; while closed <see cref="Update"/> early-outs on a bool.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LabController : MonoBehaviour
    {
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
        private float _speed = Concoction.DefaultPct;
        private float _spread = Concoction.DefaultPct;
        // Scratch recipe reused for colour/name previews so slider drags
        // don't allocate one Concoction per event.
        private readonly Concoction _probe = new Concoction("probe", "");
        // True once the player types in the name field — the auto-generated
        // name stops chasing the sliders and their words win.
        private bool _nameCustomised;
        private bool _deleteArmed;

        // ----- UGUI -----
        private GameObject _root;
        private InputField _nameField;
        private Slider _dmgSlider, _sizeSlider, _kbSlider, _speedSlider, _spreadSlider;
        private Text _cpuReadout;
        private Text _deleteLabel;
        private GameObject _deleteStrike;
        private GameObject _listContent;
        private bool _suppress;

        // Per-slider visuals for the "active while dragging" accent state.
        private sealed class SliderVisual
        {
            public Image Fill, Pip;
            public GameObject PipGlow;
            public Text Readout;
        }
        private readonly Dictionary<Slider, SliderVisual> _sliderVisuals = new();
        private Slider _activeSlider;

        // The specimen vial (right column) — live liquid colour + fill.
        private RectTransform _vialRoot;
        private Image _liquid, _liquidGlowOverlay, _surface, _tubeTint, _vialOuterGlow;
        private RectTransform _liquidRT, _surfaceRT;
        private readonly Image[] _bubbles = new Image[3];
        private static readonly float[] s_bubbleX = { 0.32f, 0.62f, 0.47f };   // fraction of tube width
        private static readonly float[] s_bubbleDur = { 2.8f, 3.6f, 2.2f };
        private static readonly float[] s_bubbleOff = { 0f, 0.31f, 0.68f };
        private const float TubeInnerHeight = 198f; // tube 206 minus glass floor
        private Color _liquidTarget = Color.gray;
        private float _fillTarget = 0.5f, _fillCurrent = 0.5f;
        private float _vialPulse;

        // Journal-row visuals for the selected recipe: its swatch wears the
        // LIVE mix colour and its label chases the name field, so slider
        // drags / typing restyle it without a list rebuild.
        private Image _selectedSwatch, _selectedSwatchGlow;
        private Text _selectedName;

        private static Font UIFont => InkKit.Display;
        private static Font AnnoFont => InkKit.Annotation;

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

        // The vial is the live readout: liquid eases toward the mix colour
        // ("ink wetting"), the fill level chases Size + Spread, bubbles rise
        // on fixed loops and the whole brew flickers gently. All cached refs,
        // no allocations.
        private void Update()
        {
            if (!IsOpen) return;
            float dt = Time.deltaTime;

            _fillCurrent = Mathf.Lerp(_fillCurrent, _fillTarget, dt * 8f);
            float fillH = _fillCurrent * TubeInnerHeight;
            if (_liquidRT != null) _liquidRT.sizeDelta = new Vector2(0f, fillH);
            if (_surfaceRT != null) _surfaceRT.anchoredPosition = new Vector2(0f, fillH);

            if (_liquid != null)
            {
                Color solid = Color.Lerp(_liquid.color, _liquidTarget, dt * 8f);
                // labFlicker: 0.85 ↔ 1 over 3.2s.
                solid.a = 0.925f + 0.075f * Mathf.Sin(Time.time * (2f * Mathf.PI / 3.2f));
                _liquid.color = solid;
                Color glow = LiquidGlow(solid);
                if (_liquidGlowOverlay != null) _liquidGlowOverlay.color = glow;
                if (_surface != null) _surface.color = glow;
                if (_tubeTint != null) _tubeTint.color = LiquidDim(solid);
                if (_vialOuterGlow != null) { Color og = glow; og.a = 0.30f; _vialOuterGlow.color = og; }
            }

            // Save pulse — the brew takes with a visible beat.
            _vialPulse = Mathf.Max(0f, _vialPulse - dt * 3f);
            if (_vialRoot != null)
            {
                float s = 1f + 0.10f * _vialPulse;
                _vialRoot.localScale = new Vector3(s, s, 1f);
            }

            // Bubbles: rise 64px from the tube floor and fade (labBubble).
            for (int i = 0; i < _bubbles.Length; i++)
            {
                Image b = _bubbles[i];
                if (b == null) continue;
                float phase = (Time.time / s_bubbleDur[i] + s_bubbleOff[i]) % 1f;
                float a = phase < 0.15f ? phase / 0.15f * 0.8f : 0.8f * (1f - (phase - 0.15f) / 0.85f);
                Color c = b.color; c.a = a * 0.7f;
                b.color = c;
                var rt = b.rectTransform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 10f + phase * 64f);
            }
        }

        // Liquid colour family derived from the mix (handoff: solid / glow / dim).
        private static Color LiquidGlow(Color solid)
        {
            Color g = Color.Lerp(solid, Color.white, 0.35f);
            g.a = 0.55f;
            return g;
        }

        private static Color LiquidDim(Color solid)
        {
            solid.a = 0.22f;
            return solid;
        }

        // -----------------------------------------------------------------
        // Editor actions
        // -----------------------------------------------------------------

        private void NewConcoction()
        {
            _editingId = string.Empty;
            _dmg = _size = _kb = _speed = _spread = Concoction.DefaultPct;
            _nameCustomised = false;
            DisarmDelete();
            SyncEditorToFields(GenerateDefaultName());
        }

        private void LoadIntoEditor(Concoction c)
        {
            if (c == null) return;
            _editingId = c.Id;
            _dmg = c.DamagePct; _size = c.SizePct; _kb = c.KnockbackPct;
            _speed = c.SpeedPct; _spread = c.SpreadPct;
            // A saved recipe's name is the player's (or a settled default) —
            // don't let slider nudges rename it out from under them.
            _nameCustomised = true;
            DisarmDelete();
            SyncEditorToFields(c.DisplayName);
        }

        private void SyncEditorToFields(string name)
        {
            _suppress = true;
            if (_nameField != null) _nameField.text = string.IsNullOrEmpty(name) ? "" : name;
            if (_dmgSlider != null) _dmgSlider.value = _dmg;
            if (_sizeSlider != null) _sizeSlider.value = _size;
            if (_kbSlider != null) _kbSlider.value = _kb;
            if (_speedSlider != null) _speedSlider.value = _speed;
            if (_spreadSlider != null) _spreadSlider.value = _spread;
            _suppress = false;
            UpdateValues();
            SnapVial();
        }

        // Fill the scratch recipe from the live sliders.
        private Concoction Probe()
        {
            _probe.DamagePct = _dmg; _probe.SizePct = _size; _probe.KnockbackPct = _kb;
            _probe.SpeedPct = _speed; _probe.SpreadPct = _spread;
            return _probe;
        }

        // Colour-derived default name with a numeral suffix against the
        // saved list ("Dark Madder Concoction (2)"). The recipe being edited
        // keeps its own name out of the collision set.
        private string GenerateDefaultName()
        {
            string baseName = ConcoctionColor.DefaultName(Probe());
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Concoction c in ConcoctionRegistry.GetAll())
                if (c != null && c.Id != _editingId && !string.IsNullOrEmpty(c.DisplayName))
                    taken.Add(c.DisplayName);
            if (!taken.Contains(baseName)) return baseName;
            for (int n = 2; ; n++)
            {
                string candidate = $"{baseName} ({n})";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        private void Save()
        {
            string name = _nameField != null && !string.IsNullOrWhiteSpace(_nameField.text)
                ? _nameField.text.Trim() : GenerateDefaultName();
            // Reuse the id when editing an existing recipe so Save overwrites
            // its file; otherwise mint a fresh stable id.
            string id = string.IsNullOrEmpty(_editingId)
                ? "cx-" + Guid.NewGuid().ToString("N").Substring(0, 12)
                : _editingId;

            var c = new Concoction(id, name, _dmg, _size, _kb, _speed, _spread);
            c.Validate();
            // Stable per-id filename → editing the same recipe overwrites it.
            ConcoctionLibrary.Save(c, id + ConcoctionLibrary.Extension);
            ConcoctionRegistry.ReloadFromLibrary();   // make it pickable immediately
            _editingId = id;
            _nameCustomised = true;                   // the saved name is theirs now
            _vialPulse = 1f;                          // the brew takes — visual beat
            AudioRouter.PlayOneShot(AudioCue.LabSave, transform.position);
            RefreshList();
        }

        // Two-click delete: first click arms (vermilion strike-through +
        // "Sure?"), second commits. Any selection change / New disarms.
        private void DeleteCurrent()
        {
            if (string.IsNullOrEmpty(_editingId)) return;
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                if (_deleteLabel != null) { _deleteLabel.text = "Sure?"; _deleteLabel.color = LabKit.Bone(); }
                if (_deleteStrike != null) _deleteStrike.SetActive(true);
                return;
            }
            ConcoctionLibrary.Delete(_editingId + ConcoctionLibrary.Extension);
            ConcoctionRegistry.ReloadFromLibrary();
            NewConcoction();
            RefreshList();
        }

        private void DisarmDelete()
        {
            _deleteArmed = false;
            if (_deleteLabel != null) { _deleteLabel.text = "Delete"; _deleteLabel.color = LabKit.Bone(0.6f); }
            if (_deleteStrike != null) _deleteStrike.SetActive(false);
        }

        private void OnDmgChanged(float v)    { if (_suppress) return; _dmg    = v; OnLeverMoved(); }
        private void OnSizeChanged(float v)   { if (_suppress) return; _size   = v; OnLeverMoved(); }
        private void OnKbChanged(float v)     { if (_suppress) return; _kb     = v; OnLeverMoved(); }
        private void OnSpeedChanged(float v)  { if (_suppress) return; _speed  = v; OnLeverMoved(); }
        private void OnSpreadChanged(float v) { if (_suppress) return; _spread = v; OnLeverMoved(); }

        private void OnLeverMoved()
        {
            DisarmDelete();
            UpdateValues();
            // Auto-name chases the mix until the player takes over the field.
            if (!_nameCustomised && _nameField != null)
            {
                _suppress = true;
                _nameField.text = GenerateDefaultName();
                _suppress = false;
            }
        }

        private void OnNameEdited(string value)
        {
            if (_suppress) return;
            _nameCustomised = true;
            // The selected journal row's label follows the field live.
            if (_selectedName != null) _selectedName.text = value;
        }

        private void UpdateValues()
        {
            Concoction probe = Probe();
            _liquidTarget = probe.MixedColor;
            // Fill level tracks payload volume: 30% + (Size + Spread) / 5 → 30–70%.
            _fillTarget = (30f + (_size * 100f + _spread * 100f) / 5f) / 100f;

            foreach (KeyValuePair<Slider, SliderVisual> kv in _sliderVisuals)
                StyleSlider(kv.Key, kv.Value, kv.Key == _activeSlider);

            // The selected journal row's swatch wears the live mix.
            if (_selectedSwatch != null) _selectedSwatch.color = _liquidTarget;
            if (_selectedSwatchGlow != null) _selectedSwatchGlow.color = LiquidGlow(_liquidTarget);

            if (_cpuReadout != null)
            {
                float sliderSum = _dmg + _size + _kb + _speed + _spread;
                float factor = sliderSum * Concoction.SurchargeFactorPerSliderSum; // 0..1.5
                // Single spaces + "of base" so the line fits the column unwrapped.
                _cpuReadout.text =
                    $"dmg ×{Concoction.Multiplier(_dmg):0.0} size ×{Concoction.Multiplier(_size):0.0} kb ×{Concoction.Multiplier(_kb):0.0} spd ×{Concoction.Multiplier(_speed):0.0} spr ×{Concoction.Multiplier(_spread):0.0}" +
                    $" · cpu +{Mathf.RoundToInt(factor * 100f)}% of base";
                _cpuReadout.color = factor > 1.0f ? LabKit.Accent : LabKit.Bone(0.5f);
            }
        }

        private void StyleSlider(Slider s, SliderVisual v, bool active)
        {
            if (v.Fill != null) v.Fill.color = active ? LabKit.Accent : LabKit.Bone(0.75f);
            if (v.Pip != null) v.Pip.color = active ? LabKit.Accent : LabKit.Bone();
            if (v.PipGlow != null) v.PipGlow.SetActive(active);
            if (v.Readout != null)
            {
                v.Readout.text = $"{Mathf.RoundToInt(s.value * 100f)}%";
                v.Readout.color = active ? LabKit.Accent : LabKit.Bone(0.88f);
            }
        }

        private void SetActiveSlider(Slider s)
        {
            _activeSlider = s;
            foreach (KeyValuePair<Slider, SliderVisual> kv in _sliderVisuals)
                StyleSlider(kv.Key, kv.Value, kv.Key == _activeSlider);
        }

        // Selection / open shouldn't visibly re-mix from the previous brew.
        private void SnapVial()
        {
            if (_liquid != null) _liquid.color = _liquidTarget;
            _fillCurrent = _fillTarget;
        }

        // "no. N" batch annotation for journal rows — a stable lab-notebook
        // number derived from the recipe id (no persistence change needed).
        private static int BatchNumber(string id)
        {
            if (string.IsNullOrEmpty(id)) return 1;
            int h = 17;
            for (int i = 0; i < id.Length; i++) h = h * 31 + id[i];
            return Mathf.Abs(h) % 99 + 1;
        }

        private void RefreshList()
        {
            if (_listContent == null) return;
            for (int i = _listContent.transform.childCount - 1; i >= 0; i--)
                Destroy(_listContent.transform.GetChild(i).gameObject);
            _selectedSwatch = null;
            _selectedSwatchGlow = null;
            _selectedName = null;

            const float rowH = 44f;
            int shown = 0;

            // First row: New Concoction (per the handoff, it lives in the well).
            BuildNewRow(rowH, ref shown);

            List<ConcoctionLibrary.Record> records = ConcoctionLibrary.LoadAll();
            foreach (ConcoctionLibrary.Record record in records)
            {
                Concoction c = record.Concoction;
                if (c == null) continue;
                bool selected = c.Id == _editingId;

                GameObject go = BuildRowShell($"Row_{shown}", rowH, shown, out Image bg);
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = bg;
                ColorBlock cols = btn.colors;
                cols.normalColor = selected ? LabKit.IndigoWash(0.32f) : Color.clear;
                cols.highlightedColor = selected ? LabKit.IndigoWash(0.38f) : LabKit.IndigoWash(0.18f);
                cols.pressedColor = LabKit.IndigoWash(0.32f);
                cols.selectedColor = cols.normalColor;
                btn.colors = cols;
                Concoction captured = c;
                btn.onClick.AddListener(() => { LoadIntoEditor(captured); RefreshList(); });

                // Vial-shaped swatch — the recipe IS its colour; the shelf
                // reads like a row of labelled specimen jars.
                var swatch = NewChild("Swatch", go.transform);
                var swRT = swatch.GetComponent<RectTransform>();
                swRT.anchorMin = new Vector2(0f, 0.5f); swRT.anchorMax = new Vector2(0f, 0.5f);
                swRT.pivot = new Vector2(0f, 0.5f);
                swRT.sizeDelta = new Vector2(15f, 19f);
                swRT.anchoredPosition = new Vector2(12f, -2f);
                var swGlow = AddImage(swatch.transform, LabKit.Glow, LiquidGlow(c.MixedColor), raycast: false);
                var glowRT = swGlow.rectTransform;
                glowRT.anchorMin = Vector2.zero; glowRT.anchorMax = Vector2.one;
                glowRT.offsetMin = new Vector2(-8f, -8f); glowRT.offsetMax = new Vector2(8f, 8f);
                swGlow.gameObject.SetActive(selected);
                var swImg = swatch.AddComponent<Image>();
                swImg.sprite = LabKit.MiniVial;
                swImg.color = c.MixedColor;
                swImg.raycastTarget = false;
                var swBorder = AddImage(swatch.transform, LabKit.Border, LabKit.Bone(0.35f), raycast: false);
                Stretch(swBorder.rectTransform);
                swBorder.type = Image.Type.Sliced;
                // Brass cork nub above the swatch mouth.
                var nub = AddImage(swatch.transform, null, LabKit.Brass(0.85f), raycast: false);
                var nubRT = nub.rectTransform;
                nubRT.anchorMin = new Vector2(0.5f, 1f); nubRT.anchorMax = new Vector2(0.5f, 1f);
                nubRT.sizeDelta = new Vector2(9f, 4f);
                nubRT.anchoredPosition = new Vector2(0f, 2f);
                if (selected)
                {
                    _selectedSwatch = swImg;
                    _selectedSwatchGlow = swGlow;
                }

                Text nameText = AddText(go.transform, c.DisplayName, new Vector2(36f, 0f), new Vector2(-52f, 0f),
                    Vector2.zero, Vector2.one, 15, FontStyle.Normal, TextAnchor.MiddleLeft,
                    selected ? LabKit.Bone() : LabKit.Bone(0.65f));
                if (selected) _selectedName = nameText;

                // Batch annotation, right-aligned (Xanh Mono italic in the
                // handoff → project annotation face).
                Text batch = AddText(go.transform, $"no. {BatchNumber(c.Id)}", new Vector2(-60f, 0f), new Vector2(-12f, 0f),
                    new Vector2(1f, 0f), new Vector2(1f, 1f), 12, FontStyle.Italic, TextAnchor.MiddleRight, LabKit.Bone(0.4f));
                batch.font = AnnoFont;
                shown++;
            }
            var contentRT = _listContent.GetComponent<RectTransform>();
            contentRT.sizeDelta = new Vector2(0f, Mathf.Max(1, shown) * rowH);
        }

        private void BuildNewRow(float rowH, ref int shown)
        {
            GameObject go = BuildRowShell("Row_New", rowH, shown, out Image bg);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock cols = btn.colors;
            cols.normalColor = Color.clear;
            cols.highlightedColor = LabKit.IndigoWash(0.18f);
            cols.pressedColor = LabKit.IndigoWash(0.32f);
            btn.colors = cols;
            btn.onClick.AddListener(() => { NewConcoction(); RefreshList(); });

            // Plus glyph: two 2px bone bars.
            var glyph = NewChild("Plus", go.transform);
            var gRT = glyph.GetComponent<RectTransform>();
            gRT.anchorMin = new Vector2(0f, 0.5f); gRT.anchorMax = new Vector2(0f, 0.5f);
            gRT.pivot = new Vector2(0f, 0.5f);
            gRT.sizeDelta = new Vector2(15f, 15f);
            gRT.anchoredPosition = new Vector2(12f, 0f);
            var barH = AddImage(glyph.transform, null, LabKit.Bone(), raycast: false);
            barH.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            barH.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            barH.rectTransform.sizeDelta = new Vector2(0f, 2f);
            var barV = AddImage(glyph.transform, null, LabKit.Bone(), raycast: false);
            barV.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            barV.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            barV.rectTransform.sizeDelta = new Vector2(2f, 0f);

            AddText(go.transform, "New Concoction", new Vector2(36f, 0f), new Vector2(-12f, 0f),
                Vector2.zero, Vector2.one, 15, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());
            shown++;
        }

        private GameObject BuildRowShell(string name, float rowH, int index, out Image bg)
        {
            var go = NewChild(name, _listContent.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, rowH);
            rt.anchoredPosition = new Vector2(0f, -index * rowH);
            bg = go.AddComponent<Image>();
            // White base: Button ColorTint MULTIPLIES the block colour by
            // the graphic's own — a clear base would erase every state.
            bg.color = Color.white;

            // 1px bone divider under every row.
            var divider = AddImage(go.transform, null, LabKit.Bone(0.12f), raycast: false);
            var dRT = divider.rectTransform;
            dRT.anchorMin = new Vector2(0f, 0f); dRT.anchorMax = new Vector2(1f, 0f);
            dRT.sizeDelta = new Vector2(0f, 1f);
            dRT.anchoredPosition = Vector2.zero;
            return go;
        }

        // -----------------------------------------------------------------
        // UGUI build — the night workshop
        // -----------------------------------------------------------------

        private void BuildCanvas()
        {
            _root = new GameObject("LabCanvas");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110; // above the variant panel (96)
            // Match the Settings/Pause scaling so the HUD isn't tiny above 1080p.
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            BuildGround();

            // The panel: night-workshop wood with brass hardware.
            var panel = NewChild("Panel", _root.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1060f, 560f);
            prt.anchoredPosition = Vector2.zero;

            // Drop shadow onto the soot ground, then the wood face + 1px
            // border — the face is a CHILD added after the shadow because
            // uGUI children always draw over their parent's own graphic.
            var shadow = AddImage(panel.transform, LabKit.Glow, LabKit.Shade(0.55f), raycast: false);
            Stretch(shadow.rectTransform);
            shadow.rectTransform.offsetMin = new Vector2(-90f, -110f);
            shadow.rectTransform.offsetMax = new Vector2(90f, 70f);
            var face = AddImage(panel.transform, LabKit.Wood, Color.white, raycast: true);
            Stretch(face.rectTransform);
            var border = AddImage(panel.transform, LabKit.Border, LabKit.WoodBorder, raycast: false);
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;

            // Top-left sheen + bottom-right pooled dark (panel light overlay).
            var sheen = AddImage(panel.transform, LabKit.Glow, LabKit.Bone(0.05f), raycast: false);
            sheen.rectTransform.anchorMin = new Vector2(0.3f, 1f);
            sheen.rectTransform.anchorMax = new Vector2(0.3f, 1f);
            sheen.rectTransform.sizeDelta = new Vector2(760f, 330f);
            var pool = AddImage(panel.transform, LabKit.Glow, LabKit.Shade(0.30f), raycast: false);
            pool.rectTransform.anchorMin = new Vector2(0.9f, 0f);
            pool.rectTransform.anchorMax = new Vector2(0.9f, 0f);
            pool.rectTransform.sizeDelta = new Vector2(640f, 430f);

            BuildScrews(panel.transform);
            BuildHeader(panel.transform);
            BuildJournal(panel.transform);
            BuildSwitchboard(panel.transform);
            BuildVial(panel.transform);
        }

        // Soot ground, chalk drafting grid, corner registration ticks.
        private void BuildGround()
        {
            var ground = NewChild("Ground", _root.transform);
            Stretch(ground.GetComponent<RectTransform>());
            var g = ground.AddComponent<Image>();
            g.sprite = LabKit.Ground;
            g.color = Color.white;      // eats clicks behind the panel

            // Uneven soot blotches (indigo cool / brass warm / pooled black).
            PlaceBlotch(ground.transform, 0.18f, 0.22f, 500f, 340f, LabKit.IndigoWash(0.07f));
            PlaceBlotch(ground.transform, 0.84f, 0.84f, 620f, 420f, LabKit.Brass(0.05f));
            PlaceBlotch(ground.transform, 0.70f, 0.10f, 400f, 300f, LabKit.Shade(0.35f));

            // Chalk drafting grid — same 28px cell as the paper screens.
            var grid = AddImage(ground.transform, InkKit.GridTile, LabKit.Bone(0.045f), raycast: false);
            Stretch(grid.rectTransform);
            grid.type = Image.Type.Tiled;

            // Registration ticks: L-shaped chalk marks at the four corners.
            BuildCornerTick(ground.transform, 0f, 1f);
            BuildCornerTick(ground.transform, 1f, 1f);
            BuildCornerTick(ground.transform, 0f, 0f);
            BuildCornerTick(ground.transform, 1f, 0f);
        }

        private void PlaceBlotch(Transform parent, float ax, float ay, float w, float h, Color color)
        {
            var img = AddImage(parent, LabKit.Glow, color, raycast: false);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void BuildCornerTick(Transform parent, float ax, float ay)
        {
            float sx = ax > 0.5f ? -1f : 1f;
            float sy = ay > 0.5f ? -1f : 1f;
            var corner = NewChild("Tick", parent);
            var rt = corner.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(18f, 18f);
            rt.anchoredPosition = new Vector2(14f * sx, 14f * sy);
            var barH = AddImage(corner.transform, null, LabKit.Bone(0.4f), raycast: false);
            barH.rectTransform.anchorMin = new Vector2(0f, ay); barH.rectTransform.anchorMax = new Vector2(1f, ay);
            barH.rectTransform.pivot = new Vector2(0.5f, ay);
            barH.rectTransform.sizeDelta = new Vector2(0f, 1f);
            var barV = AddImage(corner.transform, null, LabKit.Bone(0.4f), raycast: false);
            barV.rectTransform.anchorMin = new Vector2(ax, 0f); barV.rectTransform.anchorMax = new Vector2(ax, 1f);
            barV.rectTransform.pivot = new Vector2(ax, 0.5f);
            barV.rectTransform.sizeDelta = new Vector2(1f, 0f);
        }

        // Four brass screw heads, each slot at its own lazy angle.
        private void BuildScrews(Transform panel)
        {
            BuildScrew(panel, 0f, 1f, 40f);
            BuildScrew(panel, 1f, 1f, -15f);
            BuildScrew(panel, 0f, 0f, 75f);
            BuildScrew(panel, 1f, 0f, 10f);
        }

        private void BuildScrew(Transform panel, float ax, float ay, float slotAngle)
        {
            float sx = ax > 0.5f ? -1f : 1f;
            float sy = ay > 0.5f ? -1f : 1f;
            var screw = AddImage(panel, LabKit.BrassKnob, Color.white, raycast: false);
            var rt = screw.rectTransform;
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(10f, 10f);
            rt.anchoredPosition = new Vector2(10f * sx, 10f * sy);
            var slot = AddImage(screw.transform, null, LabKit.BrassSlot, raycast: false);
            var sRT = slot.rectTransform;
            sRT.anchorMin = new Vector2(0.5f, 0.5f); sRT.anchorMax = new Vector2(0.5f, 0.5f);
            sRT.sizeDelta = new Vector2(8f, 1.5f);
            sRT.localRotation = Quaternion.Euler(0f, 0f, slotAngle);
        }

        private void BuildHeader(Transform panel)
        {
            AddText(panel, "The Laboratory", new Vector2(36f, -76f), new Vector2(-160f, -26f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 34, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());

            // Header rule.
            var rule = AddImage(panel, null, LabKit.Bone(0.18f), raycast: false);
            var rRT = rule.rectTransform;
            rRT.anchorMin = new Vector2(0f, 1f); rRT.anchorMax = new Vector2(1f, 1f);
            rRT.pivot = new Vector2(0.5f, 1f);
            rRT.offsetMin = new Vector2(36f, -84f);
            rRT.offsetMax = new Vector2(-36f, -83f);

            // Close: transparent with a hairline bone border; hover → accent.
            var close = NewChild("Btn_Close", panel);
            var cRT = close.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(1f, 1f); cRT.anchorMax = new Vector2(1f, 1f);
            cRT.pivot = new Vector2(1f, 1f);
            cRT.sizeDelta = new Vector2(88f, 32f);
            cRT.anchoredPosition = new Vector2(-36f, -38f);
            var cBorder = close.AddComponent<Image>();
            cBorder.sprite = LabKit.Border;
            cBorder.type = Image.Type.Sliced;
            var cBtn = close.AddComponent<Button>();
            cBtn.targetGraphic = cBorder;
            ColorBlock cc = cBtn.colors;
            cc.normalColor = LabKit.Bone(0.35f);
            cc.highlightedColor = LabKit.Accent;
            cc.pressedColor = LabKit.AccentGlow;
            cBtn.colors = cc;
            cBtn.onClick.AddListener(() => SetOpen(false));
            AddText(close.transform, "Close", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                15, FontStyle.Normal, TextAnchor.MiddleCenter, LabKit.Bone());
        }

        // Left column: the concoctions well — a recessed shelf of saved jars.
        private void BuildJournal(Transform panel)
        {
            AddText(panel, "Concoctions", new Vector2(36f, -128f), new Vector2(276f, -104f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 16, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());

            var well = NewChild("Well", panel);
            var wRT = well.GetComponent<RectTransform>();
            wRT.anchorMin = new Vector2(0f, 0f); wRT.anchorMax = new Vector2(0f, 1f);
            wRT.pivot = new Vector2(0f, 1f);
            wRT.offsetMin = new Vector2(36f, 40f);
            wRT.offsetMax = new Vector2(276f, -136f);
            var wellBg = well.AddComponent<Image>();
            wellBg.color = LabKit.Shade(0.28f);
            var wellBorder = AddImage(well.transform, LabKit.Border, LabKit.Shade(0.55f), raycast: false);
            Stretch(wellBorder.rectTransform);
            wellBorder.type = Image.Type.Sliced;
            // Lighter bottom lip + inset top shade = recessed read.
            var lip = AddImage(well.transform, null, LabKit.Bone(0.14f), raycast: false);
            lip.rectTransform.anchorMin = new Vector2(0f, 0f); lip.rectTransform.anchorMax = new Vector2(1f, 0f);
            lip.rectTransform.sizeDelta = new Vector2(0f, 1f);
            var inset = AddImage(well.transform, LabKit.FadeV, LabKit.Shade(0.40f), raycast: false);
            inset.rectTransform.anchorMin = new Vector2(0f, 1f); inset.rectTransform.anchorMax = new Vector2(1f, 1f);
            inset.rectTransform.pivot = new Vector2(0.5f, 1f);
            inset.rectTransform.sizeDelta = new Vector2(0f, 8f);
            // FadeV is opaque-top; flip so the shade hugs the well's top edge.
            inset.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var viewport = NewChild("ListViewport", well.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();
            _listContent = NewChild("Content", viewport.transform);
            var crt = _listContent.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 1f);
        }

        // Centre column: the switchboard — raised plate with five levers,
        // the label field and the Save / Delete actions.
        private void BuildSwitchboard(Transform panel)
        {
            var col = NewChild("Switchboard", panel);
            var rt = col.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(308f, 26f);
            rt.offsetMax = new Vector2(772f, -106f);

            // Raised plate: drop shadow, top-lit face, top-lighter border.
            var plate = NewChild("Plate", col.transform);
            var pRT = plate.GetComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0f, 1f); pRT.anchorMax = new Vector2(1f, 1f);
            pRT.pivot = new Vector2(0.5f, 1f);
            pRT.sizeDelta = new Vector2(0f, 286f);
            pRT.anchoredPosition = Vector2.zero;
            var plateShadow = AddImage(plate.transform, LabKit.Glow, LabKit.Shade(0.45f), raycast: false);
            Stretch(plateShadow.rectTransform);
            plateShadow.rectTransform.offsetMin = new Vector2(-30f, -46f);
            plateShadow.rectTransform.offsetMax = new Vector2(30f, 18f);
            var plateFace = AddImage(plate.transform, LabKit.Plate, Color.white, raycast: false);
            Stretch(plateFace.rectTransform);
            var plateBorder = AddImage(plate.transform, LabKit.Border, LabKit.Bone(0.12f), raycast: false);
            Stretch(plateBorder.rectTransform);
            plateBorder.type = Image.Type.Sliced;
            var plateTopLight = AddImage(plate.transform, null, LabKit.Bone(0.22f), raycast: false);
            plateTopLight.rectTransform.anchorMin = new Vector2(0f, 1f);
            plateTopLight.rectTransform.anchorMax = new Vector2(1f, 1f);
            plateTopLight.rectTransform.pivot = new Vector2(0.5f, 1f);
            plateTopLight.rectTransform.sizeDelta = new Vector2(0f, 1f);

            _dmgSlider    = BuildSliderRow(plate.transform, "Damage",    0, OnDmgChanged);
            _sizeSlider   = BuildSliderRow(plate.transform, "Size",      1, OnSizeChanged);
            _kbSlider     = BuildSliderRow(plate.transform, "Knockback", 2, OnKbChanged);
            _speedSlider  = BuildSliderRow(plate.transform, "Speed",     3, OnSpeedChanged);
            _spreadSlider = BuildSliderRow(plate.transform, "Spread",    4, OnSpreadChanged);

            // Sunken name field.
            _nameField = BuildNameField(col.transform, new Vector2(0f, -306f), new Vector2(300f, 38f));
            _nameField.onValueChanged.AddListener(OnNameEdited);

            // Multiplier / CPU-surcharge annotation (kept from 141 — the
            // handoff computes this string and invites surfacing it).
            _cpuReadout = AddText(col.transform, "", new Vector2(0f, -372f), new Vector2(0f, -352f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 11, FontStyle.Italic, TextAnchor.MiddleLeft, LabKit.Bone(0.5f));
            _cpuReadout.font = AnnoFont;

            BuildActions(col.transform);
        }

        private Slider BuildSliderRow(Transform plate, string label, int index,
            UnityEngine.Events.UnityAction<float> onChanged)
        {
            var row = NewChild($"Row_{label}", plate);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(20f, -22f - index * 52f - 34f);
            rt.offsetMax = new Vector2(-20f, -22f - index * 52f);

            AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(96f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), 16, FontStyle.Normal, TextAnchor.MiddleLeft, LabKit.Bone());

            // Readout, right-aligned tabular (annotation face is monospaced).
            Text readout = AddText(row.transform, "50%", new Vector2(-58f, 0f), new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 1f), 16, FontStyle.Normal, TextAnchor.MiddleRight, LabKit.Bone(0.88f));
            readout.font = AnnoFont;

            // Track host: full-height hit area between label and readout.
            var host = NewChild("Slider", row.transform);
            var hRT = host.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0f, 0f); hRT.anchorMax = new Vector2(1f, 1f);
            hRT.offsetMin = new Vector2(112f, 0f);
            hRT.offsetMax = new Vector2(-74f, 0f);
            // An invisible full-size graphic so the whole 34px band drags.
            var hitArea = host.AddComponent<Image>();
            hitArea.color = Color.clear;

            // Brass ruled bar (with its own dark rim) at the vertical centre.
            var barRim = AddImage(host.transform, LabKit.Border, LabKit.Shade(0.55f), raycast: false);
            barRim.type = Image.Type.Sliced;
            barRim.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            barRim.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            barRim.rectTransform.sizeDelta = new Vector2(0f, 6f);
            var bar = AddImage(host.transform, LabKit.BrassBar, Color.white, raycast: false);
            bar.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bar.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(-2f, 4f);

            // Faint ink tick marks every 10%, riding above the bar.
            var ticks = AddImage(host.transform, LabKit.Ticks, LabKit.Bone(0.35f), raycast: false);
            ticks.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            ticks.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            ticks.rectTransform.sizeDelta = new Vector2(0f, 6f);
            ticks.rectTransform.anchoredPosition = new Vector2(0f, 9f);

            // Fill strip (slider-driven) over the brass bar.
            var fillArea = NewChild("Fill Area", host.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.5f); faRT.anchorMax = new Vector2(1f, 0.5f);
            faRT.sizeDelta = new Vector2(0f, 4f);
            var fill = NewChild("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = LabKit.Bone(0.75f);
            fillImg.raycastTarget = false;

            // Pip handle with a hidden accent glow for the drag state.
            // Zero-height slide area: the Slider re-stretches the handle to
            // the area's full cross-axis every frame, so the only way to a
            // 9px round pip is a 0px-tall area + the handle's own sizeDelta.
            var handleArea = NewChild("Handle Slide Area", host.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = new Vector2(0f, 0.5f); haRT.anchorMax = new Vector2(1f, 0.5f);
            haRT.offsetMin = new Vector2(5f, 0f); haRT.offsetMax = new Vector2(-5f, 0f);
            var handle = NewChild("Handle", handleArea.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            // Pin to the vertical centre — the Slider only drives the X
            // anchors, and a stretched Y turns the pip into a lozenge.
            handleRT.anchorMin = new Vector2(0.5f, 0.5f);
            handleRT.anchorMax = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(9f, 9f);
            var pipGlow = AddImage(handle.transform, LabKit.Glow, LabKit.AccentGlow, raycast: false);
            pipGlow.rectTransform.sizeDelta = new Vector2(26f, 26f);
            pipGlow.gameObject.SetActive(false);
            var pip = handle.AddComponent<Image>();
            pip.sprite = LabKit.Circle;
            pip.color = LabKit.Bone();

            var slider = host.AddComponent<Slider>();
            slider.targetGraphic = hitArea;
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fillRT;
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = Concoction.DefaultPct;
            slider.onValueChanged.AddListener(onChanged);

            _sliderVisuals[slider] = new SliderVisual { Fill = fillImg, Pip = pip, PipGlow = pipGlow.gameObject, Readout = readout };

            // Press-and-hold accent: the dragged lever, its fill and its
            // readout go galvanic until release.
            var trigger = host.AddComponent<EventTrigger>();
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => SetActiveSlider(slider));
            trigger.triggers.Add(down);
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => SetActiveSlider(null));
            trigger.triggers.Add(up);
            return slider;
        }

        private InputField BuildNameField(Transform parent, Vector2 anchoredPos, Vector2 size)
        {
            var go = NewChild("NameField", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = LabKit.Shade(0.35f);
            var fBorder = AddImage(go.transform, LabKit.Border, LabKit.Shade(0.55f), raycast: false);
            Stretch(fBorder.rectTransform);
            fBorder.type = Image.Type.Sliced;
            var inset = AddImage(go.transform, LabKit.FadeV, LabKit.Shade(0.45f), raycast: false);
            inset.rectTransform.anchorMin = new Vector2(0f, 1f);
            inset.rectTransform.anchorMax = new Vector2(1f, 1f);
            inset.rectTransform.pivot = new Vector2(0.5f, 1f);
            inset.rectTransform.sizeDelta = new Vector2(0f, 6f);
            inset.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var textGo = NewChild("Text", go.transform);
            Stretch(textGo.GetComponent<RectTransform>(), 12f);
            var text = textGo.AddComponent<Text>();
            text.font = UIFont; text.fontSize = 19; text.color = LabKit.Bone();
            text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var placeholderGo = NewChild("Placeholder", go.transform);
            Stretch(placeholderGo.GetComponent<RectTransform>(), 12f);
            var placeholder = placeholderGo.AddComponent<Text>();
            placeholder.font = UIFont; placeholder.fontSize = 19; placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = LabKit.Bone(0.4f); placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.verticalOverflow = VerticalWrapMode.Overflow;
            placeholder.text = "the mix names itself…";

            var field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 32;
            return field;
        }

        private void BuildActions(Transform col)
        {
            // Save: a bone brushstroke blob with ink text — the one light
            // shape on the dark bench (physical, per the elevation language).
            var save = NewChild("Btn_Save", col);
            var sRT = save.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0f, 1f); sRT.anchorMax = new Vector2(0f, 1f);
            sRT.pivot = new Vector2(0f, 1f);
            sRT.sizeDelta = new Vector2(118f, 42f);
            sRT.anchoredPosition = new Vector2(0f, -388f);
            var saveShadow = AddImage(save.transform, LabKit.Glow, LabKit.Shade(0.5f), raycast: false);
            Stretch(saveShadow.rectTransform);
            saveShadow.rectTransform.offsetMin = new Vector2(-14f, -20f);
            saveShadow.rectTransform.offsetMax = new Vector2(14f, 6f);
            var saveImg = AddImage(save.transform, InkKit.BrushBlob, Color.white, raycast: true);
            Stretch(saveImg.rectTransform);
            var saveBtn = save.AddComponent<Button>();
            saveBtn.targetGraphic = saveImg;
            ColorBlock sc = saveBtn.colors;
            sc.normalColor = LabKit.Bone();
            sc.highlightedColor = Color.white;
            sc.pressedColor = LabKit.Bone(0.82f);
            saveBtn.colors = sc;
            saveBtn.onClick.AddListener(Save);
            AddText(save.transform, "Save", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                17, FontStyle.Normal, TextAnchor.MiddleCenter, UguiPalette.Ink);

            // Delete: text-only, destructive; arming strikes it through in
            // the rationed vermilion.
            var del = NewChild("Btn_Delete", col);
            var dRT = del.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(0f, 1f); dRT.anchorMax = new Vector2(0f, 1f);
            dRT.pivot = new Vector2(0f, 1f);
            dRT.sizeDelta = new Vector2(80f, 42f);
            dRT.anchoredPosition = new Vector2(136f, -388f);
            // Invisible hit area — the label itself is raycast-off like all
            // AddText output.
            var delHit = del.AddComponent<Image>();
            delHit.color = Color.clear;
            _deleteLabel = AddText(del.transform, "Delete", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
                16, FontStyle.Normal, TextAnchor.MiddleCenter, LabKit.Bone(0.6f));
            var strike = AddImage(del.transform, null, UguiPalette.Vermilion, raycast: false);
            strike.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            strike.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            strike.rectTransform.sizeDelta = new Vector2(64f, 2.5f);
            strike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
            _deleteStrike = strike.gameObject;
            _deleteStrike.SetActive(false);
            var delBtn = del.AddComponent<Button>();
            delBtn.targetGraphic = _deleteLabel;
            delBtn.transition = Selectable.Transition.None;
            delBtn.onClick.AddListener(DeleteCurrent);
        }

        // Right column: the specimen vial — cork, rolled lip, glass tube,
        // live liquid, bubbles, ground shadow, wax seal, fig. 1 caption.
        private void BuildVial(Transform panel)
        {
            var colCenterX = 804f + 110f; // right column 804..1024

            var vial = NewChild("Vial", panel);
            _vialRoot = vial.GetComponent<RectTransform>();
            _vialRoot.anchorMin = new Vector2(0f, 1f); _vialRoot.anchorMax = new Vector2(0f, 1f);
            _vialRoot.pivot = new Vector2(0.5f, 0.5f);
            _vialRoot.sizeDelta = new Vector2(70f, 230f);
            _vialRoot.anchoredPosition = new Vector2(colCenterX, -128f - 115f);

            // Outer glow (liquid-coloured) behind everything.
            _vialOuterGlow = AddImage(vial.transform, LabKit.Glow, LabKit.Shade(0f), raycast: false);
            Stretch(_vialOuterGlow.rectTransform);
            _vialOuterGlow.rectTransform.offsetMin = new Vector2(-45f, -35f);
            _vialOuterGlow.rectTransform.offsetMax = new Vector2(45f, 15f);

            // Ground shadow ellipse.
            var shadow = AddImage(vial.transform, LabKit.Glow, LabKit.Shade(0.55f), raycast: false);
            shadow.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            shadow.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            shadow.rectTransform.sizeDelta = new Vector2(64f, 14f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -2f);

            // Tube region: 36 wide, from below the lip to the bottom.
            var tube = NewChild("Tube", vial.transform);
            var tRT = tube.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0.5f, 0f); tRT.anchorMax = new Vector2(0.5f, 1f);
            tRT.pivot = new Vector2(0.5f, 0f);
            tRT.sizeDelta = new Vector2(36f, 0f);
            tRT.offsetMin = new Vector2(-18f, 4f);
            tRT.offsetMax = new Vector2(18f, -24f);

            // Inner colour tint (the liquid haze inside the glass).
            _tubeTint = AddImage(tube.transform, LabKit.TubeFill, LabKit.Shade(0f), raycast: false);
            Stretch(_tubeTint.rectTransform);

            // Masked liquid stack.
            var maskGo = NewChild("LiquidMask", tube.transform);
            Stretch(maskGo.GetComponent<RectTransform>());
            var maskImg = maskGo.AddComponent<Image>();
            maskImg.sprite = LabKit.TubeFill;
            maskImg.raycastTarget = false;
            var mask = maskGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var liquid = NewChild("Liquid", maskGo.transform);
            _liquidRT = liquid.GetComponent<RectTransform>();
            _liquidRT.anchorMin = new Vector2(0f, 0f); _liquidRT.anchorMax = new Vector2(1f, 0f);
            _liquidRT.pivot = new Vector2(0.5f, 0f);
            _liquidRT.sizeDelta = new Vector2(0f, 100f);
            _liquid = liquid.AddComponent<Image>();
            _liquid.color = Color.gray;
            _liquid.raycastTarget = false;
            // Glow-toned top: FadeV is opaque-top, exactly the handoff's
            // glow→solid vertical gradient when laid over the solid fill.
            _liquidGlowOverlay = AddImage(liquid.transform, LabKit.FadeV, LabKit.Shade(0f), raycast: false);
            Stretch(_liquidGlowOverlay.rectTransform);

            // Bright surface ellipse riding the fill line.
            var surface = NewChild("Surface", maskGo.transform);
            _surfaceRT = surface.GetComponent<RectTransform>();
            _surfaceRT.anchorMin = new Vector2(0.5f, 0f); _surfaceRT.anchorMax = new Vector2(0.5f, 0f);
            _surfaceRT.pivot = new Vector2(0.5f, 0.5f);
            _surfaceRT.sizeDelta = new Vector2(32f, 8f);
            _surface = surface.AddComponent<Image>();
            _surface.sprite = LabKit.Glow;
            _surface.raycastTarget = false;

            // Bubbles (animated in Update).
            for (int i = 0; i < _bubbles.Length; i++)
            {
                float size = i == 0 ? 6f : 4f;
                var b = AddImage(maskGo.transform, LabKit.Ring, LabKit.Bone(0.7f), raycast: false);
                b.rectTransform.anchorMin = new Vector2(s_bubbleX[i], 0f);
                b.rectTransform.anchorMax = new Vector2(s_bubbleX[i], 0f);
                b.rectTransform.sizeDelta = new Vector2(size, size);
                b.rectTransform.anchoredPosition = new Vector2(0f, 10f);
                _bubbles[i] = b;
            }

            // Vertical glass highlight streak.
            var streak = AddImage(tube.transform, LabKit.Glow, LabKit.Bone(0.22f), raycast: false);
            streak.rectTransform.anchorMin = new Vector2(0f, 0f);
            streak.rectTransform.anchorMax = new Vector2(0f, 1f);
            streak.rectTransform.pivot = new Vector2(0f, 0.5f);
            streak.rectTransform.offsetMin = new Vector2(5f, 12f);
            streak.rectTransform.offsetMax = new Vector2(13f, -8f);

            // Glass wall on top of the liquid.
            var wall = AddImage(tube.transform, LabKit.TubeOutline, LabKit.Bone(0.5f), raycast: false);
            Stretch(wall.rectTransform);

            // Rolled lip.
            var lip = NewChild("Lip", vial.transform);
            var lipRT = lip.GetComponent<RectTransform>();
            lipRT.anchorMin = new Vector2(0.5f, 1f); lipRT.anchorMax = new Vector2(0.5f, 1f);
            lipRT.pivot = new Vector2(0.5f, 1f);
            lipRT.sizeDelta = new Vector2(44f, 7f);
            lipRT.anchoredPosition = new Vector2(0f, -18f);
            var lipBg = lip.AddComponent<Image>();
            lipBg.color = LabKit.Bone(0.14f);
            lipBg.raycastTarget = false;
            var lipEdge = AddImage(lip.transform, LabKit.Border, LabKit.Bone(0.5f), raycast: false);
            Stretch(lipEdge.rectTransform);
            lipEdge.type = Image.Type.Sliced;

            // Cork.
            var cork = AddImage(vial.transform, LabKit.Cork, Color.white, raycast: false);
            cork.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            cork.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            cork.rectTransform.pivot = new Vector2(0.5f, 1f);
            cork.rectTransform.sizeDelta = new Vector2(36f, 20f);
            cork.rectTransform.anchoredPosition = new Vector2(0f, 0f);

            // Wax seal — the screen's one vermilion mark.
            var seal = AddImage(vial.transform, InkKit.WaxSeal, Color.white, raycast: false);
            seal.rectTransform.anchorMin = new Vector2(1f, 0f);
            seal.rectTransform.anchorMax = new Vector2(1f, 0f);
            seal.rectTransform.sizeDelta = new Vector2(22f, 22f);
            seal.rectTransform.anchoredPosition = new Vector2(-5f, 62f); // kissing the tube's right wall
            seal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);

            // fig. 1 caption.
            Text fig = AddText(panel, "fig. 1", new Vector2(804f, -394f), new Vector2(1024f, -372f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 13, FontStyle.Italic, TextAnchor.MiddleCenter, LabKit.Bone(0.5f));
            fig.font = AnnoFont;
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

        private static Image AddImage(Transform parent, Sprite sprite, Color color, bool raycast)
        {
            var go = NewChild(sprite != null ? sprite.name : "Fill", parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
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
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
