using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode side panel that surfaces the per-instance "variable
    /// part" config for the currently-selected hotbar block. Sections are
    /// schema-driven: each block family declares a <see cref="TuneSchema"/>
    /// (preset row, slider fields, live readout) in
    /// <see cref="TuneSchemaRegistry"/>, and one generic builder here
    /// turns it into the UGUI layout from <c>FOIL_ROTATION_PLAN.md §3.4</c>:
    ///   1. Header (block name).
    ///   2. Preset cards — named buttons that snap several caches at once.
    ///   3. Primary sliders.
    ///   4. Advanced expander — power-user sliders, collapsed by default.
    /// The concoction chooser (ADR-0004) is the one hand-built section —
    /// its dynamic asset-backed option list doesn't fit the field kinds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-block-id "next placement" caches live here so the player can
    /// dial in a wing, place several, switch to a fin, dial that, and
    /// come back to the wing without losing the original setting. Caches
    /// reset on build-mode entry so a fresh edit session starts from
    /// block defaults.
    /// </para>
    /// <para>
    /// The cache is consumed by
    /// <see cref="BlockEditor.TryPlace"/>, which feeds dims + pitch into
    /// <see cref="BlockGrid.PlaceBlock(BlockDefinition, Vector3Int, Vector3Int, Vector3, float)"/>.
    /// The blueprint serialiser carries both fields on save.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VariantConfigPanel : MonoBehaviour
    {
        [SerializeField] private BuildHotbar _hotbar;
        [SerializeField] private BuildModeController _buildMode;

        // Per-block "next placement" caches live on the BuildSession
        // (the plain-C# build-mode model). The panel reads/writes through
        // the session so editor + variant + ghost agree on one answer.
        private BuildSession _session;
        public BuildSession Session
        {
            get => _session;
            set => _session = value;
        }

        // ----- UGUI -----
        private GameObject _root;
        private Text _titleText;
        private GameObject _applyButtonGo; // "Apply to bot" — unbound mode only (LOG-172)

        // ----- Schema-driven sections -----
        // TRACE[LOG-163]: one runtime section per TuneSchemaRegistry entry;
        // the per-family hand-anchored builders this replaces lived here
        // until session 163.
        private sealed class FieldRow
        {
            public TuneField Field;
            public Slider Slider;
            public Text Value;
            public HoverTip Hover; // "?" chip relay; retargeted per block id on refresh (169)
        }

        private sealed class SchemaSection
        {
            public TuneSchema Schema;
            public GameObject Root;
            public FieldRow[] Rows;      // parallel to Schema.Fields
            public Text Readout;
            public GameObject Advanced;  // null when no advanced fields
            public Text AdvancedToggleText;
            public bool AdvancedExpanded;
        }

        private readonly Dictionary<TuneSchema, SchemaSection> _schemaSections =
            new Dictionary<TuneSchema, SchemaSection>();
        private SchemaSection _activeSchemaSection;

        // Explosive controls — a click-to-open concoction chooser (ADR-0004).
        // Caption button shows the current pick; tapping it reveals a list of
        // "(none)" + every saved concoction. A live readout shows the CPU
        // surcharge the chosen recipe adds to this block.
        private GameObject _explosiveSection;
        private Button _concoctionCaptionButton;
        private Text _concoctionCaptionText;
        private Image _concoctionCaptionChip;
        private GameObject _concoctionList;
        private Text _concoctionCpuReadout;
        private bool _concoctionListOpen;

        private string _activeBlockId;
        private bool _suppressCallbacks;

        // ----- Content-sized panel -----
        // The panel grows/shrinks to the active section instead of holding
        // one fixed worst-case height. Schema sections compute their height
        // from their row counts (SchemaContentHeight); the concoction
        // section keeps hand-tracked constants. The tip-strip band is
        // always reserved at the bottom so a hover tip never covers the
        // last slider.
        private RectTransform _panelRT;
        private int _concoctionRows;
        private const float PanelWidth   = 360f;
        private const float TitleBandH   = 40f;  // top margin + title row
        private const float FooterGapH   = 12f;
        private const float TipStripH    = 64f;
        private const float ExplosiveContentH = 86f;
        private const float ConcoctionRowH = 28f;

        // Resize the panel so `contentHeight` px of section content fits
        // between the title band and the reserved tip-strip footer.
        private void SetContentHeight(float contentHeight)
        {
            if (_panelRT == null) return;
            _panelRT.sizeDelta = new Vector2(PanelWidth, TitleBandH + contentHeight + FooterGapH + TipStripH);
        }

        // Content height of one schema section in the given expansion
        // state. Row geometry: 56px slot pitch (36px preset row, 50px
        // slider rows), 4px gap + 22px readout line, 4px gap + 28px
        // Advanced toggle, 4px gap + 56px per advanced row when expanded.
        private static float SchemaContentHeight(TuneSchema schema, bool expanded)
        {
            int primary = 0, advanced = 0;
            for (int i = 0; i < schema.Fields.Length; i++)
            {
                if (schema.Fields[i].Group == TuneFieldGroup.Advanced) advanced++;
                else primary++;
            }
            int slots = (schema.Presets != null && schema.Presets.Length > 0 ? 1 : 0) + primary;
            // Without a readout the section ends at the last 50px row
            // (slot pitch minus the 6px row gap).
            float h = slots * 56f + (schema.Readout != null ? 26f : -6f);
            if (advanced > 0)
            {
                h += 32f; // gap + toggle button
                if (expanded) h += 4f + advanced * 56f;
            }
            return h;
        }

        // Content height of the currently-active section in its current
        // expansion state — single source for section switches, the
        // Advanced toggle, and the concoction list open/close.
        private float ActiveContentHeight()
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return 50f;
            float h = 0f;
            if (TuneSchemaRegistry.TryGet(id, out TuneSchema schema)
                && _schemaSections.TryGetValue(schema, out SchemaSection s))
                h += SchemaContentHeight(schema, s.AdvancedExpanded);
            // Combined mode (SMG / Cannon since session 141): the
            // concoction chooser stacks below the schema-driven ammo
            // section.
            if (ConcoctionRegistry.IsConcoctableBlock(id))
                h += ExplosiveContentH + (_concoctionListOpen ? _concoctionRows * ConcoctionRowH : 0f);
            return h > 0f ? h : 50f;
        }

        // Visual constants — Robogame orange used for active-state accents
        // throughout the build HUD.
        private static readonly Color s_accent = UguiPalette.Accent;
        private static readonly Color s_dim    = UguiPalette.TextDim;
        private static readonly Color s_panelBg     = UguiPalette.PanelBg;
        private static readonly Color s_btnIdle     = UguiPalette.ButtonIdle;
        private static readonly Color s_btnHighlight = UguiPalette.Accent;
        private static readonly Color s_btnPressed  = UguiPalette.AccentPressed;
        // Stall-warning red. Predates palette centralization; kept verbatim
        // so the schema rework stays pixel-identical.
        private static readonly Color s_warnValue = new Color(1f, 0.3f, 0.3f, 1f);

        public BuildHotbar Hotbar
        {
            get => _hotbar;
            set
            {
                if (_hotbar != null) _hotbar.SelectedBlockChanged -= HandleSelectedBlockChanged;
                _hotbar = value;
                if (_hotbar != null) _hotbar.SelectedBlockChanged += HandleSelectedBlockChanged;
            }
        }

        public BuildModeController BuildMode
        {
            get => _buildMode;
            set
            {
                if (_buildMode != null)
                {
                    _buildMode.Entered -= HandleEntered;
                    _buildMode.Exited -= HandleExited;
                }
                _buildMode = value;
                if (_buildMode != null)
                {
                    // Defensive -= before += on BOTH events (Entered previously
                    // lacked it) so re-assigning the same controller per respawn
                    // can't accumulate duplicate Entered subscriptions.
                    _buildMode.Entered -= HandleEntered;
                    _buildMode.Entered += HandleEntered;
                    _buildMode.Exited -= HandleExited;
                    _buildMode.Exited += HandleExited;
                }
            }
        }

        /// <summary>
        /// Read the cached "next placement" dims for <paramref name="blockId"/>.
        /// Vector3.zero means "use block defaults" — callers should treat
        /// it that way and let the consuming block decide.
        /// </summary>
        public Vector3 GetDimsForBlock(string blockId)
            => _session != null ? _session.GetVariantDims(blockId) : Vector3.zero;

        /// <summary>
        /// Read the cached "next placement" pitch for <paramref name="blockId"/>
        /// in degrees. 0 means "use block defaults" (foils flat, rotors fall
        /// back to the SO collective).
        /// </summary>
        public float GetPitchForBlock(string blockId)
            => _session != null ? _session.GetVariantPitch(blockId) : 0f;

        /// <summary>
        /// Read the cached "next placement" teeter tilt for
        /// <paramref name="blockId"/> in degrees (world-intent, like pitch).
        /// 0 means flat.
        /// </summary>
        public float GetTeeterForBlock(string blockId)
            => _session != null ? _session.GetVariantTeeter(blockId) : 0f;

        /// <summary>
        /// Read the cached "next placement" scalar config for
        /// <paramref name="blockId"/> (module power, etc). 0 means "use the
        /// block default".
        /// </summary>
        public float GetConfigForBlock(string blockId)
            => _session != null ? _session.GetVariantConfig(blockId) : 0f;

        /// <summary>
        /// True when the block id participates in the variant config UI.
        /// Delegates to <see cref="BlockVariants.HasVariantConfig"/>
        /// so the hotbar 'VAR' badge, the panel visibility, and any
        /// future schema-side reader stay aligned on a single answer.
        /// </summary>
        public static bool IsVariableBlock(BlockDefinition def) => BlockVariants.HasVariantConfig(def);

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            BuildCanvas();
            if (_buildMode != null)
            {
                _buildMode.Entered += HandleEntered;
                _buildMode.Exited += HandleExited;
            }
            if (_hotbar != null)
            {
                _hotbar.SelectedBlockChanged += HandleSelectedBlockChanged;
            }
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_buildMode != null)
            {
                _buildMode.Entered -= HandleEntered;
                _buildMode.Exited -= HandleExited;
            }
            if (_hotbar != null)
            {
                _hotbar.SelectedBlockChanged -= HandleSelectedBlockChanged;
            }
        }

        private void HandleEntered()
        {
            // Fresh edit session starts from block defaults — see file
            // remarks on why we don't persist across sessions.
            if (_session != null) _session.ResetVariantCaches();
            if (_hotbar != null) HandleSelectedBlockChanged(_hotbar.SelectedBlockId);
        }

        private void HandleExited()
        {
            SetVisible(false);
            _activeBlockId = null;
        }

        /// <summary>
        /// Re-sync the panel's sliders/readouts from the session caches for
        /// <paramref name="blockId"/>. Public entry point for the
        /// middle-click eyedropper, which rewrites the caches without
        /// necessarily changing the hotbar selection (re-picking the
        /// already-selected type fires no SelectedBlockChanged).
        /// </summary>
        public void RefreshForBlock(string blockId) => HandleSelectedBlockChanged(blockId, seedFromPlaced: false);

        private void HandleSelectedBlockChanged(string blockId)
            => HandleSelectedBlockChanged(blockId, seedFromPlaced: true);

        private void HandleSelectedBlockChanged(string blockId, bool seedFromPlaced)
        {
            _activeBlockId = blockId;
            TuneSchemaRegistry.TryGet(blockId, out TuneSchema schema);
            bool explosive = !string.IsNullOrEmpty(blockId) && ConcoctionRegistry.IsConcoctableBlock(blockId);
            bool any = schema != null || explosive;
            SetVisible(any);
            if (!any) return;

            // Unbound selection: seed the caches from a placed block of
            // this id so the sliders show the bot's CURRENT tune (and so
            // the Apply button pushes what the player sees, not sentinel
            // defaults). Bound mode keeps its bind-time seed; the
            // eyedropper path calls RefreshForBlock right after writing
            // the PICKED block's values and must not be re-seeded from an
            // arbitrary first block — hence the flag.
            if (seedFromPlaced && _session != null && _session.EditingInstance == null)
                _session.SeedVariantCachesFromPlacedBlock(blockId);

            if (_titleText != null)
            {
                // "Tuning" prefix + danger colour when a placed instance is
                // bound (Edit-mode click), so the player knows the sliders
                // drive that one block, not the next-placement defaults.
                bool editing = _session != null && _session.EditingInstance != null;
                if (_applyButtonGo != null) _applyButtonGo.SetActive(!editing);
                // Danger (vermilion) while instance-editing — white was
                // unreadable on the cream panel, and the mode change should
                // still read at a glance.
                _titleText.color = editing ? UguiPalette.Danger : s_accent;
                if (schema != null)
                {
                    // Schema title wins over explosive: SMG / Cannon are
                    // BOTH since session 141 (combined ammo + concoction
                    // panel) and must not read as "Bomb bay".
                    string lead = editing ? "Tuning" : schema.IdleLead;
                    _titleText.text = $"{lead} — {schema.Title(blockId)}";
                }
                else
                {
                    string lead = editing ? "Tuning" : "Variant";
                    _titleText.text = blockId == BlockIds.Mortar
                        ? $"{lead} — Mortar"
                        : $"{lead} — Bomb bay";
                }
            }

            SchemaSection active = null;
            if (schema != null) _schemaSections.TryGetValue(schema, out active);
            foreach (KeyValuePair<TuneSchema, SchemaSection> kv in _schemaSections)
                kv.Value.Root.SetActive(kv.Value == active);
            _activeSchemaSection = active;
            if (_explosiveSection != null) _explosiveSection.SetActive(explosive);

            if (active != null) RefreshSchemaSection(active, blockId);
            if (explosive)
            {
                // Combined mode: stack the concoction chooser below the
                // schema-driven ammo section (offset by its content height).
                var ert = _explosiveSection != null ? _explosiveSection.GetComponent<RectTransform>() : null;
                if (ert != null)
                    ert.offsetMax = new Vector2(-12f,
                        schema != null ? -40f - SchemaContentHeight(schema, expanded: false) : -40f);
                CloseConcoctionList();
                RefreshConcoctionCaption();
            }
            SetContentHeight(ActiveContentHeight());
        }

        // -----------------------------------------------------------------
        // Schema section runtime — one generic refresh / write / readout
        // path for every registered family.
        // -----------------------------------------------------------------

        private void RefreshSchemaSection(SchemaSection s, string blockId)
        {
            _suppressCallbacks = true;
            var ctx = new TuneContext(blockId, _session);
            for (int i = 0; i < s.Rows.Length; i++)
            {
                FieldRow row = s.Rows[i];
                // Bounds go in before values so Unity's slider clamping
                // can't mangle a cached value (per-id bounds: Wing vs Aero,
                // module power per kind).
                if (row.Field.Min != null) row.Slider.minValue = row.Field.Min(blockId);
                if (row.Field.Max != null) row.Slider.maxValue = row.Field.Max(blockId);
                if (row.Hover != null) row.Hover.Tip = row.Field.ResolveTip(blockId);
                float v = row.Field.Resolve != null ? row.Field.Resolve(ctx) : 0f;
                row.Slider.value = v;
                UpdateFieldValueText(row, v);
            }
            UpdateSchemaReadout(s);
            _suppressCallbacks = false;
        }

        // TRACE[LOG-172]: the explicit whole-bot apply verb. Implicit
        // all-blocks propagation stays retired (span-isolation session);
        // a deliberate click is the sanctioned exception.
        private void OnApplyClicked()
        {
            string id = _activeBlockId;
            if (_session == null || string.IsNullOrEmpty(id)) return;
            int n = _session.ApplyVariantCachesToPlacedBlocks(id);
            ShowTip(n > 0
                ? $"Applied to {n} placed block{(n == 1 ? "" : "s")} and saved to the blueprint."
                : "Nothing of this type is placed yet — these settings shape the next block you place.");
        }

        private void OnSchemaFieldChanged(FieldRow row, float v)
        {
            if (_suppressCallbacks) return;
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            float snapped = row.Field.Snap != null ? row.Field.Snap(v) : v;
            _suppressCallbacks = true;
            row.Slider.value = snapped;
            _suppressCallbacks = false;
            WriteTarget(id, row.Field.Target, snapped);
            UpdateFieldValueText(row, snapped);
            UpdateSchemaReadout(_activeSchemaSection);
        }

        private void WriteTarget(string id, TuneTarget target, float v)
        {
            if (_session == null) return;
            switch (target)
            {
                case TuneTarget.DimsX: { Vector3 d = _session.GetVariantDims(id); d.x = v; _session.SetVariantDims(id, d); break; }
                case TuneTarget.DimsY: { Vector3 d = _session.GetVariantDims(id); d.y = v; _session.SetVariantDims(id, d); break; }
                case TuneTarget.DimsZ: { Vector3 d = _session.GetVariantDims(id); d.z = v; _session.SetVariantDims(id, d); break; }
                case TuneTarget.Pitch: _session.SetVariantPitch(id, v); break;
                case TuneTarget.Teeter: _session.SetVariantTeeter(id, v); break;
                case TuneTarget.Config: _session.SetVariantConfig(id, v); break;
            }
        }

        private void UpdateFieldValueText(FieldRow row, float v)
        {
            if (row.Value == null) return;
            row.Value.text = v.ToString(row.Field.Format) + row.Field.ResolveSuffix(_activeBlockId);
            if (row.Field.Warn != null)
                row.Value.color = row.Field.Warn(v) ? s_warnValue : s_accent;
        }

        private void UpdateSchemaReadout(SchemaSection s)
        {
            if (s == null || s.Readout == null || s.Schema.Readout == null) return;
            TuneReadout r = s.Schema.Readout(new TuneContext(_activeBlockId, _session));
            s.Readout.text = r.Text;
            s.Readout.color = r.Warn ? s_warnValue : s_dim;
        }

        private void ApplyPreset(TunePreset preset)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id) || _session == null) return;
            Vector3 dims = _session.GetVariantDims(id);
            bool dimsTouched = false;
            for (int i = 0; i < preset.Writes.Length; i++)
            {
                (TuneTarget target, float v) = preset.Writes[i];
                switch (target)
                {
                    case TuneTarget.DimsX: dims.x = v; dimsTouched = true; break;
                    case TuneTarget.DimsY: dims.y = v; dimsTouched = true; break;
                    case TuneTarget.DimsZ: dims.z = v; dimsTouched = true; break;
                    case TuneTarget.Pitch: _session.SetVariantPitch(id, v); break;
                    case TuneTarget.Teeter: _session.SetVariantTeeter(id, v); break;
                    case TuneTarget.Config: _session.SetVariantConfig(id, v); break;
                }
            }
            if (dimsTouched) _session.SetVariantDims(id, dims);
            HandleSelectedBlockChanged(id); // re-syncs sliders
        }

        private void ToggleAdvanced(SchemaSection s)
        {
            s.AdvancedExpanded = !s.AdvancedExpanded;
            if (s.Advanced != null) s.Advanced.SetActive(s.AdvancedExpanded);
            if (s.AdvancedToggleText != null)
                s.AdvancedToggleText.text = s.AdvancedExpanded ? "Advanced ▲" : "Advanced ▼";
            SetContentHeight(ActiveContentHeight());
        }

        // -----------------------------------------------------------------
        // UGUI build
        // -----------------------------------------------------------------

        private static Font UIFont => Robogame.Core.InkKit.Display;

        private void BuildCanvas()
        {
            _root = new GameObject("VariantConfigCanvas");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 96;
            // ScaleWithScreenSize so the build HUD keeps the same apparent
            // size as the Settings/Pause menus on high-res displays —
            // ConstantPixelSize rendered it tiny above 1080p.
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Top-right anchored panel. Height is set per active section by
            // SetContentHeight — a one-slider rope panel used to float in
            // 400px of dead cream, and the fully-expanded foil section
            // collided with the tip strip; sizing to content fixes both.
            var panel = NewChild("Panel", _root.transform);
            _panelRT = panel.GetComponent<RectTransform>();
            _panelRT.anchorMin = new Vector2(1f, 1f);
            _panelRT.anchorMax = new Vector2(1f, 1f);
            _panelRT.pivot = new Vector2(1f, 1f);
            _panelRT.sizeDelta = new Vector2(PanelWidth, 460f);
            _panelRT.anchoredPosition = new Vector2(-24f, -24f);
            panel.AddComponent<Image>().color = s_panelBg;

            // offsetMin is the BOTTOM edge, offsetMax the TOP — the old
            // (-12, -36) order gave the rect a negative height, so the
            // title (including the red "Tuning —" state signal) never
            // rendered at all. Caught by the session-138 live screenshot.
            _titleText = AddText(panel.transform, "Variant", new Vector2(12f, -36f), new Vector2(-12f, -12f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 18, style: FontStyle.Bold, anchor: TextAnchor.MiddleLeft, color: s_accent);

            // Apply button — title band, top-right. Unbound slider edits
            // only shape the NEXT placement; this button is the explicit
            // verb that pushes the panel's values onto the already-placed
            // blocks of the type (LOG-172). Hidden while a tune-mode
            // instance is bound — that flow is live per-instance.
            _applyButtonGo = NewChild("ApplyButton", panel.transform);
            var abImg = _applyButtonGo.AddComponent<Image>();
            abImg.color = s_btnIdle;
            var abBtn = _applyButtonGo.AddComponent<Button>();
            abBtn.targetGraphic = abImg;
            ColorBlock abCols = abBtn.colors;
            abCols.highlightedColor = s_btnHighlight;
            abCols.pressedColor = s_btnPressed;
            abBtn.colors = abCols;
            abBtn.onClick.AddListener(OnApplyClicked);
            var abRt = _applyButtonGo.GetComponent<RectTransform>();
            abRt.anchorMin = new Vector2(1f, 1f);
            abRt.anchorMax = new Vector2(1f, 1f);
            abRt.pivot = new Vector2(1f, 1f);
            abRt.sizeDelta = new Vector2(96f, 24f);
            abRt.anchoredPosition = new Vector2(-12f, -12f);
            AddText(_applyButtonGo.transform, "Apply to bot", Vector2.zero, Vector2.zero,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: UguiPalette.Ink);
            var abHover = _applyButtonGo.AddComponent<HoverTip>();
            abHover.Tip = "Write these settings onto every placed block of this type " +
                          "(and the blueprint). Sliders otherwise only affect the next block you place; " +
                          "to retune one part alone, press T and click it.";
            abHover.Show = ShowTip;
            abHover.Hide = HideTip;
            _applyButtonGo.SetActive(false);

            foreach (TuneSchema schema in TuneSchemaRegistry.All)
            {
                SchemaSection section = BuildSchemaSection(panel.transform, schema);
                _schemaSections[schema] = section;
                section.Root.SetActive(false);
            }
            _explosiveSection = BuildExplosiveSection(panel.transform);
            _explosiveSection.SetActive(false);

            BuildTipStrip(panel.transform);
        }

        // Build one schema-driven section: optional preset row, primary
        // slider rows, optional readout line, optional Advanced expander
        // (toggle + collapsed container holding the advanced rows).
        private SchemaSection BuildSchemaSection(Transform parent, TuneSchema schema)
        {
            var s = new SchemaSection { Schema = schema };
            var section = NewChild("TuneSection", parent);
            s.Root = section;
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            int slot = 0;
            if (schema.Presets != null && schema.Presets.Length > 0)
                BuildPresetRow(section.transform, slot++, schema.Presets);

            int fieldCount = schema.Fields.Length;
            s.Rows = new FieldRow[fieldCount];
            int advancedCount = 0;
            for (int i = 0; i < fieldCount; i++)
                if (schema.Fields[i].Group == TuneFieldGroup.Advanced) advancedCount++;

            for (int i = 0; i < fieldCount; i++)
            {
                if (schema.Fields[i].Group != TuneFieldGroup.Primary) continue;
                s.Rows[i] = BuildFieldRow(section.transform, schema.Fields[i], slot++);
            }

            float primaryBottom = slot * 56f;
            float readoutH = 0f;
            if (schema.Readout != null)
            {
                s.Readout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                    anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                    size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
                var rrt = s.Readout.rectTransform;
                rrt.pivot = new Vector2(0.5f, 1f);
                rrt.anchoredPosition = new Vector2(0f, -primaryBottom - 4f);
                rrt.sizeDelta = new Vector2(0f, 22f);
                readoutH = 26f;
            }

            if (advancedCount > 0)
            {
                float toggleY = -primaryBottom - readoutH - 4f;
                var toggleGo = NewChild("AdvancedToggle", section.transform);
                var trt = toggleGo.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0f, 1f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.pivot = new Vector2(0.5f, 1f);
                trt.sizeDelta = new Vector2(0f, 28f);
                trt.anchoredPosition = new Vector2(0f, toggleY);
                var img = toggleGo.AddComponent<Image>();
                img.color = UguiPalette.ButtonIdle;
                var btn = toggleGo.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => ToggleAdvanced(s));
                s.AdvancedToggleText = AddText(toggleGo.transform, "Advanced ▼",
                    Vector2.zero, Vector2.zero,
                    anchorMin: Vector2.zero, anchorMax: Vector2.one,
                    size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: s_dim);

                // Advanced container — built inactive; the toggle shows it.
                s.Advanced = NewChild("Advanced", section.transform);
                var art = s.Advanced.GetComponent<RectTransform>();
                art.anchorMin = new Vector2(0f, 1f);
                art.anchorMax = new Vector2(1f, 1f);
                art.pivot = new Vector2(0.5f, 1f);
                art.sizeDelta = new Vector2(0f, advancedCount * 56f);
                art.anchoredPosition = new Vector2(0f, toggleY - 28f - 4f);

                int advSlot = 0;
                for (int i = 0; i < fieldCount; i++)
                {
                    if (schema.Fields[i].Group != TuneFieldGroup.Advanced) continue;
                    s.Rows[i] = BuildFieldRow(s.Advanced.transform, schema.Fields[i], advSlot++);
                }
                s.Advanced.SetActive(false);
            }

            return s;
        }

        private FieldRow BuildFieldRow(Transform parent, TuneField field, int slot)
        {
            var row = new FieldRow { Field = field };
            // Bounds and value are placeholders — RefreshSchemaSection
            // applies the per-id bounds and the resolved cache value before
            // the section is ever shown.
            row.Slider = BuildLabeledSlider(parent, field.Label, slot,
                min: 0f, max: 1f, def: 0f,
                onChanged: v => OnSchemaFieldChanged(row, v), out row.Value,
                tip: field.Tip ?? (field.TipFor != null ? string.Empty : null),
                tipHover: out row.Hover);
            if (field.Kind == TuneFieldKind.IntSlider) row.Slider.wholeNumbers = true;
            return row;
        }

        private void BuildPresetRow(Transform parent, int slot, TunePreset[] presets)
        {
            var row = NewChild("Presets", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 36f);
            rt.anchoredPosition = new Vector2(0f, -slot * 56f);
            for (int i = 0; i < presets.Length; i++)
            {
                TunePreset p = presets[i];
                AddPresetButton(row.transform, p.Label, i, () => ApplyPreset(p), p.Tip);
            }
        }

        // -----------------------------------------------------------------
        // Explosive section — concoction chooser (ADR-0004). Hand-built:
        // its option set is a live ConcoctionRegistry read with per-row
        // pigment chips, which doesn't reduce to a TuneField kind.
        // -----------------------------------------------------------------

        private GameObject BuildExplosiveSection(Transform parent)
        {
            var section = NewChild("Explosive", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            AddText(section.transform, "Concoction", new Vector2(0f, -4f), new Vector2(0f, 20f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 13, style: FontStyle.Bold, anchor: TextAnchor.MiddleLeft, color: s_dim);

            // Caption button — shows current pick, click to open the list.
            var capGo = NewChild("ConcoctionCaption", section.transform);
            var capRT = capGo.GetComponent<RectTransform>();
            capRT.anchorMin = new Vector2(0f, 1f);
            capRT.anchorMax = new Vector2(1f, 1f);
            capRT.pivot = new Vector2(0.5f, 1f);
            capRT.sizeDelta = new Vector2(0f, 32f);
            capRT.anchoredPosition = new Vector2(0f, -26f);
            var capImg = capGo.AddComponent<Image>();
            capImg.color = s_btnIdle;
            _concoctionCaptionButton = capGo.AddComponent<Button>();
            _concoctionCaptionButton.targetGraphic = capImg;
            _concoctionCaptionButton.onClick.AddListener(ToggleConcoctionList);
            // Pigment chip: the recipe's mixed colour, the same swatch the
            // Lab shows — hidden while "(none)" is picked.
            _concoctionCaptionChip = BuildChip(capGo.transform, new Vector2(8f, 0f), 18f);
            _concoctionCaptionText = AddText(capGo.transform, "(none) ▼", new Vector2(32f, 0f), new Vector2(-10f, 0f),
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 14, style: FontStyle.Bold, anchor: TextAnchor.MiddleLeft, color: UguiPalette.Ink);

            // Live CPU surcharge readout for the current pick on this block.
            _concoctionCpuReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 20f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleLeft, color: s_dim);
            var roRT = _concoctionCpuReadout.rectTransform;
            roRT.pivot = new Vector2(0.5f, 1f);
            roRT.anchoredPosition = new Vector2(0f, -62f);
            roRT.sizeDelta = new Vector2(0f, 20f);

            // Option list container (built empty; populated on open).
            _concoctionList = NewChild("ConcoctionList", section.transform);
            var listRT = _concoctionList.GetComponent<RectTransform>();
            listRT.anchorMin = new Vector2(0f, 1f);
            listRT.anchorMax = new Vector2(1f, 1f);
            listRT.pivot = new Vector2(0.5f, 1f);
            listRT.sizeDelta = new Vector2(0f, 0f); // height set per-populate
            listRT.anchoredPosition = new Vector2(0f, -86f);
            _concoctionList.AddComponent<Image>().color = UguiPalette.Backdrop;
            _concoctionList.SetActive(false);

            return section;
        }

        private void ToggleConcoctionList()
        {
            if (_concoctionListOpen) { CloseConcoctionList(); return; }
            PopulateConcoctionList();
            _concoctionListOpen = true;
            if (_concoctionList != null) _concoctionList.SetActive(true);
            SetContentHeight(ActiveContentHeight());
        }

        private void CloseConcoctionList()
        {
            _concoctionListOpen = false;
            if (_concoctionList != null) _concoctionList.SetActive(false);
            SetContentHeight(ActiveContentHeight());
        }

        // Rebuild the option buttons from the live registry: "(none)" + every
        // saved concoction. Called each open so newly-authored recipes appear.
        private void PopulateConcoctionList()
        {
            if (_concoctionList == null) return;
            for (int i = _concoctionList.transform.childCount - 1; i >= 0; i--)
                Destroy(_concoctionList.transform.GetChild(i).gameObject);

            var options = ConcoctionRegistry.GetAll();
            int rows = options.Count + 1; // +1 for "(none)"
            _concoctionRows = rows;       // panel height tracks the open list
            const float rowH = ConcoctionRowH;
            var listRT = _concoctionList.GetComponent<RectTransform>();
            listRT.sizeDelta = new Vector2(0f, rows * rowH);

            AddConcoctionOption("(none)", string.Empty, 0, rowH, null);
            for (int i = 0; i < options.Count; i++)
                AddConcoctionOption(options[i].DisplayName, options[i].Id, i + 1, rowH, options[i]);
        }

        private void AddConcoctionOption(string label, string id, int row, float rowH, Concoction recipe)
        {
            var go = NewChild($"Opt_{row}", _concoctionList.transform);
            var img = go.AddComponent<Image>();
            img.color = s_btnIdle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.highlightedColor = s_btnHighlight;
            cols.pressedColor = s_btnPressed;
            btn.colors = cols;
            string captured = id;
            btn.onClick.AddListener(() => SelectConcoction(captured));

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-4f, rowH - 2f);
            rt.anchoredPosition = new Vector2(0f, -row * rowH - 1f);

            float labelInset = 8f;
            if (recipe != null)
            {
                Image chip = BuildChip(go.transform, new Vector2(6f, 0f), 14f);
                chip.color = recipe.MixedColor;
                labelInset = 26f;
            }
            AddText(go.transform, label, new Vector2(labelInset, 0f), new Vector2(-8f, 0f),
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 13, style: FontStyle.Normal, anchor: TextAnchor.MiddleLeft, color: UguiPalette.Ink);
        }

        // Small square pigment swatch, vertically centred, left-anchored.
        private static Image BuildChip(Transform parent, Vector2 anchoredPos, float size)
        {
            var go = NewChild("Chip", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = anchoredPos;
            return go.AddComponent<Image>();
        }

        private void SelectConcoction(string id)
        {
            string blockId = _activeBlockId;
            if (!string.IsNullOrEmpty(blockId))
                _session?.SetVariantConcoctionId(blockId, id);
            CloseConcoctionList();
            RefreshConcoctionCaption();
        }

        // Sync the caption + CPU readout to the session's current pick.
        private void RefreshConcoctionCaption()
        {
            string blockId = _activeBlockId;
            string id = _session != null ? _session.GetVariantConcoctionId(blockId) : string.Empty;
            string name = "(none)";
            Concoction picked = null;
            if (!string.IsNullOrEmpty(id) && ConcoctionRegistry.TryGet(id, out Concoction c))
            {
                picked = c;
                name = string.IsNullOrEmpty(c.DisplayName) ? "Concoction" : c.DisplayName;
            }
            if (_concoctionCaptionText != null) _concoctionCaptionText.text = name + "  ▼";
            if (_concoctionCaptionChip != null)
            {
                _concoctionCaptionChip.gameObject.SetActive(picked != null);
                if (picked != null) _concoctionCaptionChip.color = picked.MixedColor;
            }

            if (_concoctionCpuReadout != null)
            {
                if (string.IsNullOrEmpty(id) || !ConcoctionRegistry.TryGet(id, out Concoction cc))
                {
                    _concoctionCpuReadout.text = "baseline stats • +0 CPU";
                }
                else
                {
                    // Absolute surcharge needs the block's base CPU cost.
                    int baseCpu = ResolveBaseCpu(blockId);
                    int surcharge = cc.CpuSurcharge(baseCpu);
                    _concoctionCpuReadout.text =
                        $"dmg ×{cc.DamageMultiplier:0.0}  size ×{cc.SizeMultiplier:0.0}  kb ×{cc.KnockbackMultiplier:0.0}  spd ×{cc.SpeedMultiplier:0.0}  spr ×{cc.SpreadMultiplier:0.0}  •  +{surcharge} CPU";
                }
                EnsureConcoctionReadoutTip();
            }
        }

        // The readout's abbreviations (dmg/size/kb/spd/spr) were never
        // expanded anywhere in the game — hover the stat line for the
        // legend (169). Idempotent: attaches the HoverTip once.
        private void EnsureConcoctionReadoutTip()
        {
            if (_concoctionCpuReadout == null) return;
            var hover = _concoctionCpuReadout.GetComponent<HoverTip>();
            if (hover == null)
            {
                _concoctionCpuReadout.raycastTarget = true;
                hover = _concoctionCpuReadout.gameObject.AddComponent<HoverTip>();
                hover.Show = ShowTip;
                hover.Hide = HideTip;
            }
            hover.Tip = "Multipliers vs the default shell — dmg: damage, size: blast size, kb: knockback, spd: projectile speed, spr: spread. CPU is the extra cost of this concoction.";
        }

        // Best-effort base CPU lookup for the readout (the live garage bar is
        // the authoritative total). Null library → 0 surcharge shown.
        private static int ResolveBaseCpu(string blockId)
        {
            var state = GameStateController.Instance;
            BlockDefinition def = state != null && state.Library != null ? state.Library.Get(blockId) : null;
            return def != null ? Mathf.Max(0, def.CpuCost) : 0;
        }

        // -----------------------------------------------------------------
        // UGUI primitives
        // -----------------------------------------------------------------

        private void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        // Shared tooltip surface for the slider "?" chips — a strip docked
        // to the bottom of the panel, hidden until a chip is hovered. One
        // strip serves every section; HoverTip enter/exit drives it.
        private GameObject _tipStrip;
        private Text _tipStripText;

        private void BuildTipStrip(Transform panel)
        {
            _tipStrip = NewChild("TipStrip", panel);
            var rt = _tipStrip.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 64f);
            rt.anchoredPosition = Vector2.zero;
            var img = _tipStrip.AddComponent<Image>();
            img.color = UguiPalette.Backdrop;
            img.raycastTarget = false; // never eat clicks aimed at sliders
            _tipStripText = AddText(_tipStrip.transform, "", new Vector2(10f, 6f), new Vector2(-10f, -6f),
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 12, style: FontStyle.Normal, anchor: TextAnchor.UpperLeft, color: UguiPalette.Text);
            _tipStripText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipStripText.raycastTarget = false;
            _tipStrip.SetActive(false);
        }

        private void ShowTip(string tip)
        {
            if (_tipStrip == null || string.IsNullOrEmpty(tip)) return;
            _tipStripText.text = tip;
            _tipStrip.SetActive(true);
        }

        private void HideTip()
        {
            if (_tipStrip != null) _tipStrip.SetActive(false);
        }

        // Labeled slider row at vertical slot `index` (0 = top of section,
        // grows downward at 56px steps). `tip` (optional) adds a "?" chip
        // between the label and the slider; hovering it shows the text in
        // the shared bottom strip.
        private Slider BuildLabeledSlider(Transform parent, string label, int slot,
            float min, float max, float def, UnityEngine.Events.UnityAction<float> onChanged, out Text valueText,
            string tip, out HoverTip tipHover)
        {
            tipHover = null;
            var row = NewChild($"Row_{label}", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 50f);
            rt.anchoredPosition = new Vector2(0f, -slot * 56f);

            // Ink, not white — the panel background is cream (session 132
            // palette); white labels were unreadable on it.
            AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(140f, 0f),
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                size: 14, style: FontStyle.Normal, anchor: TextAnchor.MiddleLeft, color: UguiPalette.Text);

            // tip == null → no chip. tip == "" → chip whose text is filled
            // later (per-block-id tips retargeted in RefreshSchemaSection).
            if (tip != null)
            {
                var tipGo = NewChild("TipChip", row.transform);
                var tipRT = tipGo.GetComponent<RectTransform>();
                tipRT.anchorMin = new Vector2(0f, 0.5f);
                tipRT.anchorMax = new Vector2(0f, 0.5f);
                tipRT.pivot = new Vector2(0.5f, 0.5f);
                tipRT.sizeDelta = new Vector2(16f, 16f);
                tipRT.anchoredPosition = new Vector2(108f, 0f);
                var chipText = tipGo.AddComponent<Text>();
                chipText.text = "?";
                chipText.font = UIFont;
                chipText.fontSize = 12;
                chipText.fontStyle = FontStyle.Bold;
                chipText.color = s_dim;
                chipText.alignment = TextAnchor.MiddleCenter;
                chipText.raycastTarget = true; // hover target
                var hover = tipGo.AddComponent<HoverTip>();
                hover.Tip = tip;
                hover.Show = ShowTip;
                hover.Hide = HideTip;
                tipHover = hover;
            }

            valueText = AddText(row.transform, def.ToString("F2"), new Vector2(-8f, 0f), new Vector2(-8f, 0f),
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f),
                size: 14, style: FontStyle.Bold, anchor: TextAnchor.MiddleRight, color: s_accent);
            var vtRT = valueText.rectTransform;
            vtRT.sizeDelta = new Vector2(60f, 0f);
            vtRT.anchoredPosition = new Vector2(-8f, 0f);

            var sliderHost = NewChild("Slider", row.transform);
            var srt = sliderHost.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(120f, -10f);
            srt.offsetMax = new Vector2(-72f, 10f);

            var bg = NewChild("Background", sliderHost.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f);
            bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            // Ink-tinted track — the old white@0.18 vanished on the cream panel.
            bg.AddComponent<Image>().color = UguiPalette.FrameLine;

            var fillArea = NewChild("Fill Area", sliderHost.transform);
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
            fill.AddComponent<Image>().color = s_accent;

            var handleArea = NewChild("Handle Slide Area", sliderHost.transform);
            var haRT = handleArea.GetComponent<RectTransform>();
            haRT.anchorMin = new Vector2(0f, 0f);
            haRT.anchorMax = new Vector2(1f, 1f);
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);
            var handle = NewChild("Handle", handleArea.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(16f, 22f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = UguiPalette.Ink; // white handle was invisible on cream

            var slider = sliderHost.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = def;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private void AddPresetButton(Transform parent, string label, int index, System.Action onClick, string tip = null)
        {
            // Buttons are evenly distributed across the row width using
            // anchor stretching. Index drives anchorMin/Max.x so a row of
            // N buttons divides the row into N equal slices.
            int count = parent.childCount > 0 ? Mathf.Max(parent.childCount, index + 1) : index + 1;
            // Re-anchor: each button gets 1/count of the row width, less a small gap.
            // We don't know N at button-build time; use a fixed cell width based on the
            // most common case (4 foil presets, 3 rotor presets) and rely on row sizing.
            const float cellW = 76f;
            const float cellGap = 4f;
            var go = NewChild($"Preset_{label}", parent);
            var img = go.AddComponent<Image>();
            img.color = s_btnIdle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cols = btn.colors;
            cols.highlightedColor = s_btnHighlight;
            cols.pressedColor = s_btnPressed;
            btn.colors = cols;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(cellW, 0f);
            rt.anchoredPosition = new Vector2(index * (cellW + cellGap), 0f);

            AddText(go.transform, label, Vector2.zero, Vector2.zero,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: UguiPalette.Ink);

            // Preset names are role jargon ("Tail Stab", "Vert Fin") — hover
            // the whole button for the plain-language explanation (169).
            if (!string.IsNullOrEmpty(tip))
            {
                var hover = go.AddComponent<HoverTip>();
                hover.Tip = tip;
                hover.Show = ShowTip;
                hover.Hide = HideTip;
            }
        }

        private static GameObject NewChild(string name, Transform parent)
            => Robogame.Core.UguiKit.NewChild(name, parent);

        private static Text AddText(Transform parent, string text, Vector2 offsetMin, Vector2 offsetMax,
            Vector2 anchorMin, Vector2 anchorMax, int size, FontStyle style, TextAnchor anchor, Color color)
            => Robogame.Core.UguiKit.AddText(parent, text, UIFont, size, style, color, anchor,
                anchorMin, anchorMax, offsetMin, offsetMax);
    }
}
