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
    /// custom ammunition chemistry. Five reagent sliders (damage / size /
    /// knockback / speed / spread, 0–100%, default 50%) mix a live pigment
    /// colour (<see cref="ConcoctionColor"/>) that names the recipe and dyes
    /// its shots in combat; raising any lever raises the CPU surcharge.
    /// Saved concoctions persist via <see cref="ConcoctionLibrary"/> and are
    /// chosen per weapon block in the variant panel's dropdown. See ADR-0004
    /// + docs/decisions/0005 (session 141 full pass).
    /// </summary>
    /// <remarks>
    /// Two panes: the JOURNAL (left — searchable database of saved recipes,
    /// pigment chips, two-click delete) and the BENCH (right — vial sliders
    /// tinted with their own reagent pigment, a cauldron swatch that eases
    /// toward the live mix, and an auto-generated name the player can
    /// overtype). Full-screen overlay panel built procedurally (same UGUI
    /// approach as <see cref="VariantConfigPanel"/>); opens over the garage,
    /// closes on its own button or when build mode toggles. The per-frame
    /// work while open is one colour lerp + two RectTransform rotations —
    /// zero allocations; while closed <see cref="Update"/> early-outs on a
    /// bool.
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
        private string _filter = string.Empty;

        // ----- UGUI -----
        private GameObject _root;
        private InputField _nameField;
        private InputField _searchField;
        private Slider _dmgSlider, _sizeSlider, _kbSlider, _speedSlider, _spreadSlider;
        private Text _dmgValue, _sizeValue, _kbValue, _speedValue, _spreadValue;
        private Text _cpuReadout;
        private Text _deleteLabel;
        private GameObject _listContent;
        private Image _cauldron, _swirlA, _swirlB, _nameChip;
        private bool _suppress;

        // Cauldron colour easing + save pulse (per-frame while open only).
        private Color _cauldronTarget = Color.gray;
        private float _cauldronPulse;

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

        // The cauldron wash eases toward the live mix ("ink wetting", per
        // the UI handoff's state-change language) and the swirl blobs turn
        // slowly at offset speeds so the mix reads as liquid, not paint chip.
        private void Update()
        {
            if (!IsOpen) return;
            if (_cauldron != null)
            {
                _cauldron.color = Color.Lerp(_cauldron.color, _cauldronTarget, Time.deltaTime * 8f);
                _cauldronPulse = Mathf.Max(0f, _cauldronPulse - Time.deltaTime * 3f);
                float s = 1f + 0.18f * _cauldronPulse;
                _cauldron.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            if (_swirlA != null)
            {
                _swirlA.rectTransform.Rotate(0f, 0f, 9f * Time.deltaTime);
                Color a = _cauldronTarget; a.a = 0.35f;
                _swirlA.color = Color.Lerp(_swirlA.color, a, Time.deltaTime * 5f);
            }
            if (_swirlB != null)
            {
                _swirlB.rectTransform.Rotate(0f, 0f, -14f * Time.deltaTime);
                Color b = Color.Lerp(_cauldronTarget, Color.white, 0.3f); b.a = 0.22f;
                _swirlB.color = Color.Lerp(_swirlB.color, b, Time.deltaTime * 5f);
            }
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
            SnapCauldron();
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
            _cauldronPulse = 1f;                      // the brew takes — visual beat
            AudioRouter.PlayOneShot(AudioCue.LabSave, transform.position);
            RefreshList();
        }

        // Two-click delete: first click arms ("Sure?"), second commits.
        // Any selection change / New disarms.
        private void DeleteCurrent()
        {
            if (string.IsNullOrEmpty(_editingId)) return;
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                if (_deleteLabel != null) { _deleteLabel.text = "Sure?"; _deleteLabel.color = UguiPalette.Danger; }
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
            if (_deleteLabel != null) { _deleteLabel.text = "Delete"; _deleteLabel.color = Color.white; }
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

        private void OnNameEdited(string _)
        {
            if (_suppress) return;
            _nameCustomised = true;
        }

        private void UpdateValues()
        {
            if (_dmgValue    != null) _dmgValue.text    = $"{Mathf.RoundToInt(_dmg * 100f)}%";
            if (_sizeValue   != null) _sizeValue.text   = $"{Mathf.RoundToInt(_size * 100f)}%";
            if (_kbValue     != null) _kbValue.text     = $"{Mathf.RoundToInt(_kb * 100f)}%";
            if (_speedValue  != null) _speedValue.text  = $"{Mathf.RoundToInt(_speed * 100f)}%";
            if (_spreadValue != null) _spreadValue.text = $"{Mathf.RoundToInt(_spread * 100f)}%";

            Concoction probe = Probe();
            _cauldronTarget = probe.MixedColor;
            if (_nameChip != null) _nameChip.color = _cauldronTarget;

            if (_cpuReadout != null)
            {
                float sliderSum = _dmg + _size + _kb + _speed + _spread;
                float factor = sliderSum * Concoction.SurchargeFactorPerSliderSum; // 0..1.5
                _cpuReadout.text =
                    $"dmg ×{Concoction.Multiplier(_dmg):0.0}  size ×{Concoction.Multiplier(_size):0.0}  kb ×{Concoction.Multiplier(_kb):0.0}  spd ×{Concoction.Multiplier(_speed):0.0}  spr ×{Concoction.Multiplier(_spread):0.0}" +
                    $"   •   CPU +{Mathf.RoundToInt(factor * 100f)}% of weapon base";
                _cpuReadout.color = factor > 1.0f ? s_accent : s_dim;
            }
        }

        // Selection / open shouldn't visibly re-mix from the previous colour.
        private void SnapCauldron()
        {
            if (_cauldron != null) _cauldron.color = _cauldronTarget;
        }

        private void OnSearchChanged(string s)
        {
            _filter = s ?? string.Empty;
            RefreshList();
        }

        private void RefreshList()
        {
            if (_listContent == null) return;
            for (int i = _listContent.transform.childCount - 1; i >= 0; i--)
                Destroy(_listContent.transform.GetChild(i).gameObject);

            List<ConcoctionLibrary.Record> records = ConcoctionLibrary.LoadAll();
            const float rowH = 36f;
            int shown = 0;
            foreach (ConcoctionLibrary.Record record in records)
            {
                Concoction c = record.Concoction;
                if (c == null) continue;
                if (_filter.Length > 0 &&
                    (c.DisplayName == null ||
                     c.DisplayName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                var go = NewChild($"Row_{shown}", _listContent.transform);
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
                rt.anchoredPosition = new Vector2(0f, -shown * rowH - 2f);

                // Pigment chip — the recipe IS its colour; the shelf reads
                // like a row of labelled specimen jars.
                var chip = NewChild("Chip", go.transform);
                var chipRT = chip.GetComponent<RectTransform>();
                chipRT.anchorMin = new Vector2(0f, 0.5f); chipRT.anchorMax = new Vector2(0f, 0.5f);
                chipRT.pivot = new Vector2(0f, 0.5f);
                chipRT.sizeDelta = new Vector2(20f, 20f);
                chipRT.anchoredPosition = new Vector2(7f, 0f);
                var chipImg = chip.AddComponent<Image>();
                chipImg.sprite = InkKit.BrushBlob;
                chipImg.color = c.MixedColor;

                AddText(go.transform, c.DisplayName, new Vector2(34f, 0f), new Vector2(-8f, 0f),
                    Vector2.zero, Vector2.one, 13, FontStyle.Bold, TextAnchor.MiddleLeft, UguiPalette.Ink);
                shown++;
            }
            var contentRT = _listContent.GetComponent<RectTransform>();
            contentRT.sizeDelta = new Vector2(0f, Mathf.Max(1, shown) * rowH);
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
            // Match the Settings/Pause scaling so the HUD isn't tiny above 1080p.
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Full-screen dim backdrop (also eats clicks behind the panel).
            var backdrop = NewChild("Backdrop", _root.transform);
            Stretch(backdrop.GetComponent<RectTransform>());
            backdrop.AddComponent<Image>().color = s_backdrop;

            // Centered panel — paper ground, workshop-journal reading.
            var panel = NewChild("Panel", _root.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(900f, 560f);
            prt.anchoredPosition = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.sprite = InkKit.Paper;
            panelImg.type = Image.Type.Simple;
            panelImg.color = s_panelBg;

            AddText(panel.transform, "The Laboratory", new Vector2(24f, -46f), new Vector2(-24f, -12f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 24, FontStyle.Bold, TextAnchor.MiddleLeft, s_accent);
            AddText(panel.transform, "payload chemistry — mix, label, bottle", new Vector2(230f, -44f), new Vector2(-120f, -16f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 13, FontStyle.Italic, TextAnchor.MiddleLeft, s_dim);

            // Close button (top-right).
            BuildButton(panel.transform, "Close", new Vector2(1f, 1f), new Vector2(-16f, -14f),
                new Vector2(90f, 30f), () => SetOpen(false));

            BuildJournal(panel.transform);
            BuildBench(panel.transform);
        }

        // Left pane: the experiment journal — search + saved-recipe shelf.
        private void BuildJournal(Transform panel)
        {
            AddText(panel, "Experiment journal", new Vector2(24f, -72f), new Vector2(296f, -50f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 12, FontStyle.Bold, TextAnchor.MiddleLeft, s_dim);

            _searchField = BuildInputField(panel, new Vector2(24f, -76f), new Vector2(272f, 28f), "filter the shelf…");
            _searchField.onValueChanged.AddListener(OnSearchChanged);

            var viewport = NewChild("ListViewport", panel);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0f, 0f); vrt.anchorMax = new Vector2(0f, 1f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.offsetMin = new Vector2(24f, 64f);
            vrt.offsetMax = new Vector2(0f, -108f);
            vrt.sizeDelta = new Vector2(272f, vrt.sizeDelta.y);
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.10f); // recessed shelf
            viewport.AddComponent<RectMask2D>();

            _listContent = NewChild("Content", viewport.transform);
            var crt = _listContent.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, 1f);

            AddText(panel, "pick a jar to re-mix it", new Vector2(24f, 38f), new Vector2(296f, 58f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), 11, FontStyle.Italic, TextAnchor.MiddleLeft, s_dim);
        }

        // Right pane: the mixing bench — cauldron, vials, label, bottling.
        private void BuildBench(Transform panel)
        {
            var col = NewChild("Bench", panel);
            var rt = col.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(324f, 60f);
            rt.offsetMax = new Vector2(-24f, -56f);

            // Cauldron: the live mix, top-right of the bench. A big brush
            // blob eased toward the mixed colour + two slow counter-rotating
            // translucent blobs for the marbling read.
            var pot = NewChild("Cauldron", col.transform);
            var potRT = pot.GetComponent<RectTransform>();
            potRT.anchorMin = new Vector2(1f, 1f); potRT.anchorMax = new Vector2(1f, 1f);
            potRT.pivot = new Vector2(1f, 1f);
            potRT.sizeDelta = new Vector2(128f, 128f);
            potRT.anchoredPosition = new Vector2(-8f, -4f);
            _cauldron = pot.AddComponent<Image>();
            _cauldron.sprite = InkKit.BrushBlob;
            _cauldron.color = Color.gray;
            _swirlA = BuildSwirl(pot.transform, 96f, 25f);
            _swirlB = BuildSwirl(pot.transform, 64f, -40f);

            // Label (name) row.
            AddText(col.transform, "Label", new Vector2(0f, -2f), new Vector2(80f, -28f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), 13, FontStyle.Bold, TextAnchor.MiddleLeft, s_dim);
            _nameField = BuildInputField(col.transform, new Vector2(0f, -30f), new Vector2(330f, 30f), "the mix names itself…");
            _nameField.onValueChanged.AddListener(OnNameEdited);
            // Wax-seal chip next to the label: the exact bottled swatch.
            var seal = NewChild("NameSeal", col.transform);
            var sealRT = seal.GetComponent<RectTransform>();
            sealRT.anchorMin = new Vector2(0f, 1f); sealRT.anchorMax = new Vector2(0f, 1f);
            sealRT.pivot = new Vector2(0f, 1f);
            sealRT.sizeDelta = new Vector2(26f, 26f);
            sealRT.anchoredPosition = new Vector2(340f, -32f);
            _nameChip = seal.AddComponent<Image>();
            // BrushBlob, not WaxSeal: the seal sprite is baked red and a
            // multiply tint can't show the true mix colour through it.
            _nameChip.sprite = InkKit.BrushBlob;

            // Reagent vials: each slider's fill wears its own pigment so the
            // lever→colour mapping is legible WHILE mixing.
            _dmgSlider    = BuildSliderRow(col.transform, "Damage",    slot: 1, OnDmgChanged,    out _dmgValue,    ConcoctionColor.LeverPigment(ConcoctionColor.DamageHue));
            _sizeSlider   = BuildSliderRow(col.transform, "Size",      slot: 2, OnSizeChanged,   out _sizeValue,   ConcoctionColor.LeverPigment(ConcoctionColor.SizeHue));
            _kbSlider     = BuildSliderRow(col.transform, "Knockback", slot: 3, OnKbChanged,     out _kbValue,     ConcoctionColor.LeverPigment(ConcoctionColor.KnockbackHue));
            _speedSlider  = BuildSliderRow(col.transform, "Speed",     slot: 4, OnSpeedChanged,  out _speedValue,  ConcoctionColor.LeverPigment(ConcoctionColor.SpeedHue));
            _spreadSlider = BuildSliderRow(col.transform, "Spread",    slot: 5, OnSpreadChanged, out _spreadValue, ConcoctionColor.LeverPigment(ConcoctionColor.SpreadHue));

            _cpuReadout = AddText(col.transform, "", new Vector2(0f, 0f), new Vector2(0f, 22f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), 12, FontStyle.Italic, TextAnchor.MiddleLeft, s_dim);
            _cpuReadout.rectTransform.pivot = new Vector2(0f, 1f);
            _cpuReadout.rectTransform.anchoredPosition = new Vector2(0f, -30f - 6 * 52f);

            // Action row — "Bottle" is the save; the brew takes with a pulse.
            BuildButton(col.transform, "Bottle it", new Vector2(0f, 0f), new Vector2(0f, 8f),   new Vector2(120f, 34f), Save);
            BuildButton(col.transform, "New mix",   new Vector2(0f, 0f), new Vector2(132f, 8f), new Vector2(110f, 34f), NewConcoction);
            _deleteLabel = BuildButton(col.transform, "Delete", new Vector2(0f, 0f), new Vector2(254f, 8f), new Vector2(110f, 34f), DeleteCurrent);
        }

        private static Image BuildSwirl(Transform parent, float size, float startAngle)
        {
            var go = NewChild("Swirl", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.45f, 0.55f); // off-centre pivot → wobble, not spin
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, startAngle);
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.BrushBlob;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false;
            return img;
        }

        private Slider BuildSliderRow(Transform parent, string label, int slot,
            UnityEngine.Events.UnityAction<float> onChanged, out Text valueText, Color fillPigment)
        {
            var row = NewChild($"Row_{label}", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-150f, 46f);   // leave the cauldron column clear
            rt.anchoredPosition = new Vector2(-75f, -slot * 52f - 16f);

            AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(140f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), 14, FontStyle.Normal, TextAnchor.MiddleLeft, UguiPalette.Ink);
            valueText = AddText(row.transform, "50%", new Vector2(-8f, 0f), new Vector2(-8f, 0f),
                new Vector2(1f, 0f), new Vector2(1f, 1f), 14, FontStyle.Bold, TextAnchor.MiddleRight, s_accent);
            valueText.rectTransform.sizeDelta = new Vector2(60f, 0f);

            var host = NewChild("Slider", row.transform);
            var srt = host.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0.5f); srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(110f, -10f);
            srt.offsetMax = new Vector2(-72f, 10f);

            var bg = NewChild("Background", host.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f); bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.sprite = InkKit.BarFill;
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0f, 0f, 0f, 0.15f);

            var fillArea = NewChild("Fill Area", host.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.35f); faRT.anchorMax = new Vector2(1f, 0.65f);
            faRT.offsetMin = new Vector2(8f, 0f); faRT.offsetMax = new Vector2(-8f, 0f);
            var fill = NewChild("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.sprite = InkKit.BarFill;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = fillPigment;   // the vial wears its reagent

            var handleArea = NewChild("Handle Slide Area", host.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(10f, 0f); haRT.offsetMax = new Vector2(-10f, 0f);
            var handle = NewChild("Handle", handleArea.transform);
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(16f, 22f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.sprite = InkKit.BrushBlob;
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

        private InputField BuildInputField(Transform parent, Vector2 anchoredPos, Vector2 size, string placeholderText)
        {
            var go = NewChild("Field", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.08f);

            var textGo = NewChild("Text", go.transform);
            Stretch(textGo.GetComponent<RectTransform>(), 8f);
            var text = textGo.AddComponent<Text>();
            text.font = UIFont; text.fontSize = 15; text.color = UguiPalette.Ink;
            text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var placeholderGo = NewChild("Placeholder", go.transform);
            Stretch(placeholderGo.GetComponent<RectTransform>(), 8f);
            var placeholder = placeholderGo.AddComponent<Text>();
            placeholder.font = UIFont; placeholder.fontSize = 15; placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = s_dim; placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.verticalOverflow = VerticalWrapMode.Overflow;
            placeholder.text = placeholderText;

            var field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 32;
            return field;
        }

        private Text BuildButton(Transform parent, string label, Vector2 anchor, Vector2 anchoredPos, Vector2 size, Action onClick)
        {
            var go = NewChild($"Btn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.sprite = InkKit.BarFill;
            img.type = Image.Type.Sliced;
            img.color = s_btnIdle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors; cols.highlightedColor = s_btnHi; cols.pressedColor = s_btnPress; btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());
            return AddText(go.transform, label, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one,
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
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
