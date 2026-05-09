using System.Collections.Generic;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.UI;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Build-mode side panel that surfaces the per-instance "variable
    /// part" config for the currently-selected hotbar block. Per
    /// <c>FOIL_ROTATION_PLAN.md §3.4</c>, the layout is:
    ///   1. Header (block name).
    ///   2. Preset cards — 3-4 named buttons that snap dims + pitch
    ///      to sensible defaults for the role.
    ///   3. Primary slider — the single dominant parameter (foil =
    ///      pitch / incidence; rotor = collective; rope = segment count).
    ///   4. Advanced expander — explicit sliders for the rest (foil
    ///      span / thickness / chord). Toggle to show/hide.
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
        private GameObject _foilSection, _ropeSection, _rotorSection;

        // Foil controls
        private Slider _foilPitchPrimary;
        private Text _foilPitchValue;
        private Text _foilReadout;
        private GameObject _foilAdvanced;
        private Text _foilAdvancedToggleText;
        private Slider _foilSpanSlider, _foilThicknessSlider, _foilChordSlider;
        private Text _foilSpanValue, _foilThicknessValue, _foilChordValue;
        // Rope controls
        private Slider _ropeSegmentSlider;
        private Text _ropeSegmentValue;
        // Rotor controls
        private Slider _rotorCollectiveSlider;
        private Text _rotorCollectiveValue;
        private Text _rotorReadout;

        private string _activeBlockId;
        private bool _suppressCallbacks;
        private bool _foilAdvancedExpanded;

        // Visual constants — Robogame orange used for active-state accents
        // throughout the build HUD.
        private static readonly Color s_accent = new Color(0.95f, 0.55f, 0.10f, 1f);
        private static readonly Color s_dim    = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color s_panelBg     = new Color(0.06f, 0.07f, 0.10f, 0.93f);
        private static readonly Color s_btnIdle     = new Color(0.18f, 0.22f, 0.28f, 1f);
        private static readonly Color s_btnHighlight = new Color(0.95f, 0.55f, 0.10f, 1f);
        private static readonly Color s_btnPressed  = new Color(0.7f, 0.4f, 0.05f, 1f);

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
        /// True when the block id participates in the variant config UI.
        /// Delegates to <see cref="BlockVariants.HasVariantConfigId"/>
        /// so the hotbar 'VAR' badge, the panel visibility, and any
        /// future schema-side reader stay aligned on a single answer.
        /// </summary>
        public static bool IsVariableBlock(string id) => BlockVariants.HasVariantConfigId(id);

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

        private void HandleSelectedBlockChanged(string blockId)
        {
            _activeBlockId = blockId;
            bool foil = blockId == BlockIds.Aero || blockId == BlockIds.AeroFin;
            bool rope = blockId == BlockIds.Rope;
            bool rotor = blockId == BlockIds.Rotor;
            bool any = foil || rope || rotor;
            SetVisible(any);
            if (!any) return;

            if (_titleText != null)
            {
                if (foil) _titleText.text = blockId == BlockIds.AeroFin
                    ? "VARIANT — TAIL FIN"
                    : "VARIANT — AERO WING";
                else if (rope) _titleText.text = "VARIANT — ROPE";
                else _titleText.text = "VARIANT — ROTOR";
            }
            _foilSection.SetActive(foil);
            _ropeSection.SetActive(rope);
            _rotorSection.SetActive(rotor);

            _suppressCallbacks = true;
            if (foil)
            {
                Vector3 cached = GetDimsForBlock(blockId);
                float pitch = GetPitchForBlock(blockId);
                float span      = cached.x > 0f ? cached.x : AeroSurfaceBlock.DefaultSpan;
                float thickness = cached.y > 0f ? cached.y : AeroSurfaceBlock.DefaultThickness;
                float chord     = cached.z > 0f ? cached.z : AeroSurfaceBlock.DefaultChord;
                _foilSpanSlider.value      = span;
                _foilThicknessSlider.value = thickness;
                _foilChordSlider.value     = chord;
                _foilPitchPrimary.value    = pitch;
                UpdateValueText(_foilSpanValue,      span,      "F2");
                UpdateValueText(_foilThicknessValue, thickness, "F2");
                UpdateValueText(_foilChordValue,     chord,     "F2");
                UpdateFoilPitchValue(pitch);
                UpdateFoilReadout();
            }
            else if (rope)
            {
                Vector3 cached = GetDimsForBlock(blockId);
                int count = cached.x > 0f ? Mathf.RoundToInt(cached.x) : RopeBlock.DefaultSegmentCount;
                _ropeSegmentSlider.value = count;
                UpdateValueText(_ropeSegmentValue, count, "F0");
            }
            else if (rotor)
            {
                float pitch = GetPitchForBlock(blockId);
                _rotorCollectiveSlider.value = pitch;
                UpdateValueText(_rotorCollectiveValue, pitch, "F0");
                UpdateRotorReadout();
            }
            _suppressCallbacks = false;
        }

        // -----------------------------------------------------------------
        // Slider callbacks — snap on commit
        // -----------------------------------------------------------------

        // Length dims snap to 0.25 m, pitch / segments to integers.
        private static float SnapLength(float v) => Mathf.Round(v * 4f) * 0.25f;
        private static float SnapInt(float v)    => Mathf.Round(v);

        private void OnFoilSpanChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = SnapLength(v);
            ApplyFoilDim(0, snapped, _foilSpanSlider, _foilSpanValue, "F2");
        }

        private void OnFoilThicknessChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = SnapLength(v);
            ApplyFoilDim(1, snapped, _foilThicknessSlider, _foilThicknessValue, "F2");
        }

        private void OnFoilChordChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = SnapLength(v);
            ApplyFoilDim(2, snapped, _foilChordSlider, _foilChordValue, "F2");
        }

        private void OnFoilPitchChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = SnapInt(v);
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _foilPitchPrimary.value = snapped;
            _suppressCallbacks = false;
            _session?.SetVariantPitch(id, snapped);
            UpdateFoilPitchValue(snapped);
            UpdateFoilReadout();
        }

        private void OnRopeSegmentCountChanged(float v)
        {
            if (_suppressCallbacks) return;
            int rounded = Mathf.RoundToInt(v);
            _suppressCallbacks = true;
            _ropeSegmentSlider.value = rounded;
            _suppressCallbacks = false;
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            Vector3 dims = GetDimsForBlock(id);
            dims.x = rounded;
            _session?.SetVariantDims(id, dims);
            UpdateValueText(_ropeSegmentValue, rounded, "F0");
        }

        private void OnRotorCollectiveChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = SnapInt(v);
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _rotorCollectiveSlider.value = snapped;
            _suppressCallbacks = false;
            _session?.SetVariantPitch(id, snapped);
            UpdateValueText(_rotorCollectiveValue, snapped, "F0");
            UpdateRotorReadout();
        }

        // Snap-and-cache helper for foil dim sliders (span/thickness/chord).
        private void ApplyFoilDim(int axis, float snapped, Slider slider, Text valueText, string fmt)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            slider.value = snapped;
            _suppressCallbacks = false;
            Vector3 dims = GetDimsForBlock(id);
            if (axis == 0) dims.x = snapped;
            else if (axis == 1) dims.y = snapped;
            else dims.z = snapped;
            _session?.SetVariantDims(id, dims);
            UpdateValueText(valueText, snapped, fmt);
            UpdateFoilReadout();
        }

        private void UpdateFoilPitchValue(float pitchDeg)
        {
            if (_foilPitchValue == null) return;
            _foilPitchValue.text = $"{pitchDeg:F0}°";
            // Stall warning past ±18° (BlueprintValidator soft limit).
            bool stall = Mathf.Abs(pitchDeg) > BlueprintValidator.PitchSoftLimitDeg;
            _foilPitchValue.color = stall ? new Color(1f, 0.3f, 0.3f, 1f) : s_accent;
        }

        // -----------------------------------------------------------------
        // Live consequence readouts (Phase 4)
        // -----------------------------------------------------------------
        // Mirrors the AeroSurfaceBlock.FixedUpdate lift formula so the
        // player can see the consequence of their tuning before they
        // place anything. Reference values:
        //   - Free-wing cruise: 30 m/s (typical plane forward speed).
        //   - Rotor blade: ω×r at 240 RPM and 1 m radius — matches the
        //     shipped helicopter's blade config. Disc lift assumes 4 default
        //     blades; player-built rotors with bigger blades will lift more
        //     than the readout suggests (it's a conservative estimate, not
        //     a per-build calculation — that needs the live chassis).

        private const float CruiseSpeedMs       = 30f;
        private const float RotorRpmNominal     = 240f;
        private const float RotorRadiusNominal  = 1f;
        private const int   RotorBladeCount     = 4;
        private const float LiftCoefDefault     = 0.95f;   // matches AeroSurfaceBlock._liftCoef
        private const float StallAoaRad         = 0.35f;   // matches AeroSurfaceBlock._stallAoA
        private const float PostStallLift       = 0.55f;   // matches AeroSurfaceBlock._postStallLift

        // Static estimate of lift in newtons for one foil at the given
        // dims and pitch, mirroring AeroSurfaceBlock.FixedUpdate's math.
        // Vertical=true (i.e. how the binder configures every player-placed
        // foil) means biasTerm=0 — at zero pitch you get zero estimated
        // lift, which IS the correct result and the player education we
        // want from this readout.
        private static float EstimateFoilLift(float span, float chord, float pitchDeg, float airspeedMs)
        {
            float pitchRad = pitchDeg * Mathf.Deg2Rad;
            float aoaClamped = Mathf.Clamp(pitchRad, -StallAoaRad, StallAoaRad);
            float stallFalloff = Mathf.Abs(pitchRad) > StallAoaRad
                ? Mathf.Lerp(1f, PostStallLift,
                    Mathf.Clamp01((Mathf.Abs(pitchRad) - StallAoaRad) / StallAoaRad))
                : 1f;
            float liftFactor = aoaClamped * stallFalloff; // biasTerm=0 for vertical=true
            float areaScale = (span * chord) / (AeroSurfaceBlock.DefaultSpan * AeroSurfaceBlock.DefaultChord);
            return airspeedMs * airspeedMs * LiftCoefDefault * areaScale * liftFactor;
        }

        private void UpdateFoilReadout()
        {
            if (_foilReadout == null) return;
            string id = _activeBlockId;
            Vector3 cached = GetDimsForBlock(id);
            float pitch = GetPitchForBlock(id);
            float span  = cached.x > 0f ? cached.x : AeroSurfaceBlock.DefaultSpan;
            float chord = cached.z > 0f ? cached.z : AeroSurfaceBlock.DefaultChord;
            float lift = EstimateFoilLift(span, chord, pitch, CruiseSpeedMs);
            bool stall = Mathf.Abs(pitch) > BlueprintValidator.PitchSoftLimitDeg;
            _foilReadout.text = stall
                ? $"≈ {lift:F0} N @ {CruiseSpeedMs:F0} m/s — STALL"
                : $"≈ {lift:F0} N @ {CruiseSpeedMs:F0} m/s";
            _foilReadout.color = stall ? new Color(1f, 0.3f, 0.3f, 1f) : s_dim;
        }

        private void UpdateRotorReadout()
        {
            if (_rotorReadout == null) return;
            float collective = GetPitchForBlock(_activeBlockId);
            // collective=0 in the cache means "use rotor's authored
            // default" (RotorBlock._collectivePitchDeg, currently 8°).
            // Mirror that for the readout so the player sees the actual
            // post-place value.
            float effectiveCollective = collective > 0f ? collective : 8f;
            float omega = RotorRpmNominal * Mathf.PI * 2f / 60f;
            float tipSpeed = omega * RotorRadiusNominal;
            float perBlade = EstimateFoilLift(
                AeroSurfaceBlock.DefaultSpan,
                AeroSurfaceBlock.DefaultChord,
                effectiveCollective,
                tipSpeed);
            float total = perBlade * RotorBladeCount;
            _rotorReadout.text =
                $"≈ {total:F0} N disc ({RotorBladeCount} default blades, {RotorRpmNominal:F0} RPM)";
        }

        // -----------------------------------------------------------------
        // Presets
        // -----------------------------------------------------------------

        // Foil presets per FOIL_ROTATION_PLAN §3.5. (span, thickness, chord, pitchDeg).
        private static readonly (string label, float span, float thickness, float chord, float pitch)[] s_foilPresets =
        {
            ("Heli Blade",  1.50f, 0.06f, 0.60f,  8f),
            ("Plane Wing",  4.00f, 0.08f, 0.90f,  2f),
            ("Tail Stab",   2.00f, 0.08f, 0.70f, -1f),
            ("Vert Fin",    2.00f, 0.08f, 0.90f,  0f),
        };

        // Rotor presets — per FOIL_ROTATION_PLAN §3.4. v1 only writes
        // collective; per-rotor RPM / direction are deferred.
        private static readonly (string label, float collective)[] s_rotorPresets =
        {
            ("Heavy Lift", 12f),
            ("Standard",    8f),
            ("Light",       5f),
        };

        private void ApplyFoilPreset(float span, float thickness, float chord, float pitchDeg)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id) || _session == null) return;
            _session.SetVariantDims(id, new Vector3(span, thickness, chord));
            _session.SetVariantPitch(id, pitchDeg);
            HandleSelectedBlockChanged(id); // re-syncs sliders
        }

        private void ApplyRotorPreset(float collective)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id) || _session == null) return;
            _session.SetVariantPitch(id, collective);
            HandleSelectedBlockChanged(id);
        }

        // -----------------------------------------------------------------
        // Advanced expander
        // -----------------------------------------------------------------

        private void ToggleFoilAdvanced()
        {
            _foilAdvancedExpanded = !_foilAdvancedExpanded;
            if (_foilAdvanced != null) _foilAdvanced.SetActive(_foilAdvancedExpanded);
            if (_foilAdvancedToggleText != null)
                _foilAdvancedToggleText.text = _foilAdvancedExpanded ? "ADVANCED ▲" : "ADVANCED ▼";
        }

        // -----------------------------------------------------------------
        // UGUI build
        // -----------------------------------------------------------------

        private static Font UIFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private void BuildCanvas()
        {
            _root = new GameObject("VariantConfigCanvas");
            _root.transform.SetParent(transform, worldPositionStays: false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 96;
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _root.AddComponent<GraphicRaycaster>();

            // Top-right anchored panel, sized for the foil section's full
            // expanded layout (the rope / rotor sections leave whitespace).
            var panel = NewChild("Panel", _root.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.sizeDelta = new Vector2(340f, 460f);
            prt.anchoredPosition = new Vector2(-24f, -24f);
            panel.AddComponent<Image>().color = s_panelBg;

            _titleText = AddText(panel.transform, "VARIANT", new Vector2(12f, -12f), new Vector2(-12f, -36f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 18, style: FontStyle.Bold, anchor: TextAnchor.MiddleLeft, color: s_accent);

            _foilSection  = BuildFoilSection(panel.transform);
            _ropeSection  = BuildRopeSection(panel.transform);
            _rotorSection = BuildRotorSection(panel.transform);

            _foilSection.SetActive(false);
            _ropeSection.SetActive(false);
            _rotorSection.SetActive(false);
        }

        private GameObject BuildFoilSection(Transform parent)
        {
            var section = NewChild("Foil", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            // Layout (top → bottom):
            //   Preset row (4 buttons)             — slot 0
            //   Primary: span / thickness / chord  — slots 1, 2, 3
            //   Live lift readout text             — below slot 3
            //   Advanced toggle button             — below readout
            //   Advanced container: pitch slider   — below toggle (collapsed by default)
            //
            // Pitch is in Advanced because the dim sliders are what most
            // players reach for (it's where the foil's GEOMETRY lives);
            // pitch is the power-user knob.
            BuildFoilPresetRow(section.transform, slot: 0);

            _foilSpanSlider      = BuildLabeledSlider(section.transform, "Span (m)",      slot: 1,
                AeroSurfaceBlock.MinSpan,      AeroSurfaceBlock.MaxSpan,      AeroSurfaceBlock.DefaultSpan,
                OnFoilSpanChanged,      out _foilSpanValue);
            _foilThicknessSlider = BuildLabeledSlider(section.transform, "Thickness (m)", slot: 2,
                AeroSurfaceBlock.MinThickness, AeroSurfaceBlock.MaxThickness, AeroSurfaceBlock.DefaultThickness,
                OnFoilThicknessChanged, out _foilThicknessValue);
            _foilChordSlider     = BuildLabeledSlider(section.transform, "Chord (m)",     slot: 3,
                AeroSurfaceBlock.MinChord,     AeroSurfaceBlock.MaxChord,     AeroSurfaceBlock.DefaultChord,
                OnFoilChordChanged,     out _foilChordValue);

            // Live lift readout — sits under the chord slider.
            const float primaryBottom = 56f * 4f; // = 4 slots' worth of vertical
            _foilReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
            var rrt = _foilReadout.rectTransform;
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -primaryBottom - 4f);
            rrt.sizeDelta = new Vector2(0f, 22f);

            // Advanced toggle: small button below the readout.
            const float toggleY = -primaryBottom - 4f - 22f - 4f;
            var toggleGo = NewChild("AdvancedToggle", section.transform);
            var trt = toggleGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 28f);
            trt.anchoredPosition = new Vector2(0f, toggleY);
            var img = toggleGo.AddComponent<Image>();
            img.color = new Color(0.10f, 0.13f, 0.18f, 1f);
            var btn = toggleGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(ToggleFoilAdvanced);
            _foilAdvancedToggleText = AddText(toggleGo.transform, "ADVANCED ▼",
                Vector2.zero, Vector2.zero,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: s_dim);

            // Advanced container — pitch slider lives here. Built inactive;
            // expander toggle shows it.
            _foilAdvanced = NewChild("Advanced", section.transform);
            var art = _foilAdvanced.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 1f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(0f, 56f);
            art.anchoredPosition = new Vector2(0f, toggleY - 28f - 4f);

            _foilPitchPrimary = BuildLabeledSlider(_foilAdvanced.transform, "Pitch", slot: 0,
                min: -18f, max: 18f, def: 0f,
                onChanged: OnFoilPitchChanged, out _foilPitchValue);
            UpdateFoilPitchValue(0f);

            _foilAdvanced.SetActive(false);
            _foilAdvancedExpanded = false;

            return section;
        }

        private GameObject BuildRopeSection(Transform parent)
        {
            var section = NewChild("Rope", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            _ropeSegmentSlider = BuildLabeledSlider(section.transform, "Segments", 0,
                RopeBlock.MinSegmentCount, RopeBlock.MaxSegmentCount, RopeBlock.DefaultSegmentCount,
                OnRopeSegmentCountChanged, out _ropeSegmentValue);

            return section;
        }

        private GameObject BuildRotorSection(Transform parent)
        {
            var section = NewChild("Rotor", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            BuildRotorPresetRow(section.transform, slot: 0);

            _rotorCollectiveSlider = BuildLabeledSlider(section.transform, "Collective", slot: 1,
                min: 0f, max: 18f, def: 0f,
                onChanged: OnRotorCollectiveChanged, out _rotorCollectiveValue);

            _rotorReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
            var rrt = _rotorReadout.rectTransform;
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -56f * 2f - 4f);
            rrt.sizeDelta = new Vector2(0f, 22f);

            return section;
        }

        private void BuildFoilPresetRow(Transform parent, int slot)
        {
            var row = NewChild("FoilPresets", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 36f);
            rt.anchoredPosition = new Vector2(0f, -slot * 56f);
            for (int i = 0; i < s_foilPresets.Length; i++)
            {
                var p = s_foilPresets[i];
                AddPresetButton(row.transform, p.label, i, () => ApplyFoilPreset(p.span, p.thickness, p.chord, p.pitch));
            }
        }

        private void BuildRotorPresetRow(Transform parent, int slot)
        {
            var row = NewChild("RotorPresets", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 36f);
            rt.anchoredPosition = new Vector2(0f, -slot * 56f);
            for (int i = 0; i < s_rotorPresets.Length; i++)
            {
                var p = s_rotorPresets[i];
                AddPresetButton(row.transform, p.label, i, () => ApplyRotorPreset(p.collective));
            }
        }

        // -----------------------------------------------------------------
        // UGUI primitives
        // -----------------------------------------------------------------

        private static void UpdateValueText(Text t, float v, string fmt)
        {
            if (t != null) t.text = v.ToString(fmt);
        }

        private void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        // Labeled slider row at vertical slot `index` (0 = top of section,
        // grows downward at 56px steps).
        private Slider BuildLabeledSlider(Transform parent, string label, int slot,
            float min, float max, float def, UnityEngine.Events.UnityAction<float> onChanged, out Text valueText)
        {
            var row = NewChild($"Row_{label}", parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 50f);
            rt.anchoredPosition = new Vector2(0f, -slot * 56f);

            AddText(row.transform, label, new Vector2(0f, 0f), new Vector2(140f, 0f),
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                size: 14, style: FontStyle.Normal, anchor: TextAnchor.MiddleLeft, color: Color.white);

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
            bg.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

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
            handleImg.color = Color.white;

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

        private void AddPresetButton(Transform parent, string label, int index, System.Action onClick)
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
                size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: Color.white);
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
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = UIFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            return t;
        }
    }
}
