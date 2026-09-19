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
    /// Layout, elevation
    /// and interaction language come from the July 2026 Laboratory handoff.
    /// Full-screen overlay built procedurally (same UGUI approach as
    /// <see cref="VariantConfigPanel"/>); opens over the garage, closes on
    /// its own button or when build mode toggles. Per-frame work while open
    /// is a handful of colour lerps + three bubble transforms — zero
    /// allocations; while closed <see cref="Update"/> early-outs on a bool.
    /// </remarks>
    [DisallowMultipleComponent]
    // TRACE[DOC:research/ui-design-handoff-laboratory]: layout + interaction language.
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
        // internal (was private): LabCanvasBuilder.BuildSliderRow constructs
        // these for the switchboard levers it builds (F-053/CHG-035);
        // visibility-only change, same as CHG-033's RowEntry/GroupSection.
        internal sealed class SliderVisual
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
        // internal (was private): LabCanvasBuilder.BuildVial places the
        // bubbles at these same x-fractions when it builds them (F-053);
        // Update (staying here) still owns the per-frame rise/fade.
        internal static readonly float[] s_bubbleX = { 0.32f, 0.62f, 0.47f };   // fraction of tube width
        private static readonly float[] s_bubbleDur = { 2.8f, 3.6f, 2.2f };
        private static readonly float[] s_bubbleOff = { 0f, 0.31f, 0.68f };
        private const float TubeInnerHeight = 198f; // tube 206 minus glass floor
        private Color _liquidTarget = Color.gray;
        private float _fillTarget = 0.5f, _fillCurrent = 0.5f;
        private float _vialPulse;

        // Background fog: three parallax banks rolling on slow sine swells
        // (near = bigger, brighter, wider swing). Sines never pop the way a
        // wrap-around conveyor would on an ultrawide screen.
        private readonly Image[] _fog = new Image[3];
        // internal (was private): LabCanvasBuilder.BuildGround positions the
        // fog banks at these same starting points when it builds them
        // (F-053); Update (staying here) still owns the per-frame sine roll.
        internal static readonly float[] s_fogBaseX = { -700f, 150f, -250f };
        internal static readonly float[] s_fogY = { 120f, -40f, -210f };   // offset from screen centre
        private static readonly float[] s_fogAmp = { 90f, 150f, 240f };   // lateral swing, px
        private static readonly float[] s_fogRate = { 0.15f, 0.11f, 0.08f }; // rad/s

        // Journal-row visuals for the selected recipe: its swatch wears the
        // LIVE mix colour and its label chases the name field, so slider
        // drags / typing restyle it without a list rebuild.
        private Image _selectedSwatch, _selectedSwatchGlow;
        private Text _selectedName;

        // internal (was private): LabCanvasBuilder's moved construction
        // reads the same two fonts (F-053/CHG-035); visibility-only change.
        internal static Font UIFont => InkKit.Display;
        internal static Font AnnoFont => InkKit.Annotation;

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

            // Fog roll: each bank swells laterally on its own slow sine and
            // bobs a little vertically — endless, seamless 2.5D drift.
            for (int i = 0; i < _fog.Length; i++)
            {
                Image f = _fog[i];
                if (f == null) continue;
                f.rectTransform.anchoredPosition = new Vector2(
                    s_fogBaseX[i] + Mathf.Sin(Time.time * s_fogRate[i] + i * 1.7f) * s_fogAmp[i],
                    s_fogY[i] + Mathf.Sin(Time.time * 0.11f + i * 2.1f) * 14f);
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
                Color normalColor = selected ? LabKit.IndigoWash(0.32f) : Color.clear;
                StyleButton(btn, normalColor, selected ? LabKit.IndigoWash(0.38f) : LabKit.IndigoWash(0.18f), LabKit.IndigoWash(0.32f));
                // selectedColor has no independent value at this call site
                // (it mirrors normalColor) -- not part of the shared shape,
                // so it stays a direct assignment here (F-054).
                ColorBlock cols = btn.colors;
                cols.selectedColor = normalColor;
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
            StyleButton(btn, Color.clear, LabKit.IndigoWash(0.18f), LabKit.IndigoWash(0.32f));
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

            LabCanvasBuilder.BuildGround(_root.transform, _fog);

            // The panel: night-workshop wood with brass hardware.
            var panel = NewChild("Panel", _root.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1060f, 560f);
            prt.anchoredPosition = Vector2.zero;
            // The layout is authored at the handoff's 1060×560; a uniform
            // scale grows the whole bench to fill more of the screen.
            prt.localScale = new Vector3(1.22f, 1.22f, 1f);

            // Two-layer drop shadow onto the soot ground (wide ambient +
            // tighter contact, biased downward), then the wood face + 1px
            // border — the face is a CHILD added after the shadows because
            // uGUI children always draw over their parent's own graphic.
            var shadow = AddImage(panel.transform, LabKit.Glow, LabKit.Shade(0.6f), raycast: false);
            Stretch(shadow.rectTransform);
            shadow.rectTransform.offsetMin = new Vector2(-110f, -150f);
            shadow.rectTransform.offsetMax = new Vector2(110f, 70f);
            var contact = AddImage(panel.transform, LabKit.Glow, LabKit.Shade(0.5f), raycast: false);
            Stretch(contact.rectTransform);
            contact.rectTransform.offsetMin = new Vector2(-35f, -70f);
            contact.rectTransform.offsetMax = new Vector2(35f, 25f);
            var face = AddImage(panel.transform, LabKit.Wood, Color.white, raycast: true);
            Stretch(face.rectTransform);
            var border = AddImage(panel.transform, LabKit.Border, LabKit.WoodBorder, raycast: false);
            Stretch(border.rectTransform);
            border.type = Image.Type.Sliced;

            // Top-left sheen + bottom-right pooled dark (panel light overlay),
            // plus a hairline top-edge catchlight so the slab reads raised.
            var sheen = AddImage(panel.transform, LabKit.Glow, LabKit.Bone(0.07f), raycast: false);
            sheen.rectTransform.anchorMin = new Vector2(0.3f, 1f);
            sheen.rectTransform.anchorMax = new Vector2(0.3f, 1f);
            sheen.rectTransform.sizeDelta = new Vector2(760f, 330f);
            var pool = AddImage(panel.transform, LabKit.Glow, LabKit.Shade(0.28f), raycast: false);
            pool.rectTransform.anchorMin = new Vector2(0.9f, 0f);
            pool.rectTransform.anchorMax = new Vector2(0.9f, 0f);
            pool.rectTransform.sizeDelta = new Vector2(640f, 430f);
            var catchlight = AddImage(panel.transform, null, LabKit.Bone(0.10f), raycast: false);
            catchlight.rectTransform.anchorMin = new Vector2(0f, 1f);
            catchlight.rectTransform.anchorMax = new Vector2(1f, 1f);
            catchlight.rectTransform.pivot = new Vector2(0.5f, 1f);
            catchlight.rectTransform.offsetMin = new Vector2(1f, -2f);
            catchlight.rectTransform.offsetMax = new Vector2(-1f, -1f);

            LabCanvasBuilder.BuildScrews(panel.transform);
            LabCanvasBuilder.BuildHeader(panel.transform, () => SetOpen(false));
            _listContent = LabCanvasBuilder.BuildJournal(panel.transform);

            LabCanvasBuilder.SwitchboardResult sw = LabCanvasBuilder.BuildSwitchboard(
                panel.transform, _sliderVisuals, SetActiveSlider,
                OnDmgChanged, OnSizeChanged, OnKbChanged, OnSpeedChanged, OnSpreadChanged,
                OnNameEdited, Save, DeleteCurrent);
            _dmgSlider = sw.DmgSlider;
            _sizeSlider = sw.SizeSlider;
            _kbSlider = sw.KbSlider;
            _speedSlider = sw.SpeedSlider;
            _spreadSlider = sw.SpreadSlider;
            _nameField = sw.NameField;
            _cpuReadout = sw.CpuReadout;
            _deleteLabel = sw.DeleteLabel;
            _deleteStrike = sw.DeleteStrike;

            LabCanvasBuilder.VialResult vial = LabCanvasBuilder.BuildVial(panel.transform, _bubbles);
            _vialRoot = vial.VialRoot;
            _liquid = vial.Liquid;
            _liquidGlowOverlay = vial.LiquidGlowOverlay;
            _surface = vial.Surface;
            _tubeTint = vial.TubeTint;
            _vialOuterGlow = vial.VialOuterGlow;
            _liquidRT = vial.LiquidRT;
            _surfaceRT = vial.SurfaceRT;
        }

        // -----------------------------------------------------------------
        // UGUI primitives
        // -----------------------------------------------------------------
        // internal (was private): LabCanvasBuilder's moved one-shot
        // construction (F-053/CHG-035) shares these with RefreshList /
        // BuildNewRow / BuildRowShell above, which stay here (they rebuild
        // against live editor state); visibility-only change, "one copy"
        // per the CHG-035 spec.

        internal static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        internal static GameObject NewChild(string name, Transform parent)
            => Robogame.Core.UguiKit.NewChild(name, parent);

        // Set a Button's normal/hover/pressed tint. selectedColor and
        // disabledColor are left at the Button's own defaults -- none of
        // the four call sites that share this shape touch them (F-054).
        internal static void StyleButton(Button button, Color normal, Color hover, Color pressed)
        {
            ColorBlock cols = button.colors;
            cols.normalColor = normal;
            cols.highlightedColor = hover;
            cols.pressedColor = pressed;
            button.colors = cols;
        }

        internal static Image AddImage(Transform parent, Sprite sprite, Color color, bool raycast)
        {
            var go = NewChild(sprite != null ? sprite.name : "Fill", parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        internal static Text AddText(Transform parent, string text, Vector2 offsetMin, Vector2 offsetMax,
            Vector2 anchorMin, Vector2 anchorMax, int size, FontStyle style, TextAnchor anchor, Color color)
            => Robogame.Core.UguiKit.AddText(parent, text, UIFont, size, style, color, anchor,
                anchorMin, anchorMax, offsetMin, offsetMax, raycastTarget: false);
    }
}
