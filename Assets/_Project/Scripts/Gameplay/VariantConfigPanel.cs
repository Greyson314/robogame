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
        private GameObject _foilSection, _ropeSection, _rotorSection, _hoverSection, _moduleSection, _explosiveSection, _weaponSection;

        // Foil controls
        private Slider _foilPitchPrimary;
        private Text _foilPitchValue;
        private Slider _foilTeeterSlider;
        private Text _foilTeeterValue;
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
        private Slider _rotorRpmSlider;
        private Text _rotorRpmValue;
        private Text _rotorReadout;
        // Hover blade controls
        private Slider _hoverSizeSlider;
        private Text _hoverSizeValue;
        private Text _hoverReadout;
        // Module controls — one "Power" slider (writes ConfigValue) with a
        // live cooldown readout. Slider range is reconfigured per kind.
        private Slider _modulePowerSlider;
        private Text _modulePowerValue;
        private Text _moduleReadout;
        private ModuleKind _moduleKind;
        // Explosive controls — a click-to-open concoction chooser (ADR-0004).
        // Caption button shows the current pick; tapping it reveals a list of
        // "(none)" + every saved concoction. A live readout shows the CPU
        // surcharge the chosen recipe adds to this block.
        private Button _concoctionCaptionButton;
        private Text _concoctionCaptionText;
        private GameObject _concoctionList;
        private Text _concoctionCpuReadout;
        private bool _concoctionListOpen;
        // Ammo-configurable turret controls (SMG / Cannon) — one "Ammo"
        // multiplier slider (writes ConfigValue) with a live clip + CPU +
        // mass readout. See WeaponAmmoDefaults.
        private Slider _weaponAmmoSlider;
        private Text _weaponAmmoValue;
        private Text _weaponReadout;

        private string _activeBlockId;
        private bool _suppressCallbacks;
        private bool _foilAdvancedExpanded;

        // Visual constants — Robogame orange used for active-state accents
        // throughout the build HUD.
        private static readonly Color s_accent = UguiPalette.Accent;
        private static readonly Color s_dim    = UguiPalette.TextDim;
        private static readonly Color s_panelBg     = UguiPalette.PanelBg;
        private static readonly Color s_btnIdle     = UguiPalette.ButtonIdle;
        private static readonly Color s_btnHighlight = UguiPalette.Accent;
        private static readonly Color s_btnPressed  = UguiPalette.AccentPressed;

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

        /// <summary>
        /// Re-sync the panel's sliders/readouts from the session caches for
        /// <paramref name="blockId"/>. Public entry point for the
        /// middle-click eyedropper, which rewrites the caches without
        /// necessarily changing the hotbar selection (re-picking the
        /// already-selected type fires no SelectedBlockChanged).
        /// </summary>
        public void RefreshForBlock(string blockId) => HandleSelectedBlockChanged(blockId);

        private void HandleSelectedBlockChanged(string blockId)
        {
            _activeBlockId = blockId;
            bool foil = blockId == BlockIds.Aero || blockId == BlockIds.AeroFin;
            bool rope = blockId == BlockIds.Rope;
            bool rotor = blockId == BlockIds.Rotor;
            bool hover = blockId == BlockIds.HoverBlade;
            bool module = ModuleKinds.IsModuleId(blockId);
            bool explosive = ConcoctionRegistry.IsConcoctableBlock(blockId);
            bool weaponAmmo = WeaponAmmoDefaults.IsAmmoConfigurable(blockId);
            bool any = foil || rope || rotor || hover || module || explosive || weaponAmmo;
            SetVisible(any);
            if (!any) return;

            if (_titleText != null)
            {
                // "Editing" prefix + danger colour when a placed instance is
                // bound (Edit-mode click), so the player knows the sliders
                // drive that one block, not the next-placement defaults.
                bool editing = _session != null && _session.EditingInstance != null;
                string lead = editing ? "Editing" : "Variant";
                // Danger (vermilion) while instance-editing — white was
                // unreadable on the cream panel, and the mode change should
                // still read at a glance.
                _titleText.color = editing ? UguiPalette.Danger : s_accent;
                if (foil) _titleText.text = blockId == BlockIds.AeroFin
                    ? $"{lead} — Tail fin"
                    : $"{lead} — Aero wing";
                else if (rope) _titleText.text = $"{lead} — Rope";
                else if (rotor) _titleText.text = $"{lead} — Rotor";
                else if (hover) _titleText.text = $"{lead} — Hover blade";
                else if (explosive) _titleText.text = blockId == BlockIds.Mortar
                    ? $"{lead} — Mortar"
                    : $"{lead} — Bomb bay";
                else if (weaponAmmo) _titleText.text = blockId == BlockIds.Cannon
                    ? $"{lead} — Cannon"
                    : $"{lead} — SMG";
                else _titleText.text = $"{(editing ? "Editing" : "Module")} — {ModuleKinds.Label(ModuleKinds.ForBlockId(blockId) ?? ModuleKind.EmpBurst)}";
            }
            _foilSection.SetActive(foil);
            _ropeSection.SetActive(rope);
            _rotorSection.SetActive(rotor);
            _hoverSection.SetActive(hover);
            _moduleSection.SetActive(module);
            _explosiveSection.SetActive(explosive);
            _weaponSection.SetActive(weaponAmmo);

            _suppressCallbacks = true;
            if (foil)
            {
                Vector3 cached = GetDimsForBlock(blockId);
                float pitch = GetPitchForBlock(blockId);
                float teeter = GetTeeterForBlock(blockId);
                float span      = cached.x > 0f ? cached.x : AeroSurfaceBlock.DefaultSpan;
                float thickness = cached.y > 0f ? cached.y : AeroSurfaceBlock.DefaultThickness;
                float chord     = cached.z > 0f ? cached.z : AeroSurfaceBlock.DefaultChord;
                _foilSpanSlider.value      = span;
                _foilThicknessSlider.value = thickness;
                _foilChordSlider.value     = chord;
                _foilPitchPrimary.value    = pitch;
                _foilTeeterSlider.value    = teeter;
                UpdateValueText(_foilSpanValue,      span,      "F2");
                UpdateValueText(_foilThicknessValue, thickness, "F2");
                UpdateValueText(_foilChordValue,     chord,     "F2");
                UpdateFoilPitchValue(pitch);
                UpdateValueText(_foilTeeterValue, teeter, "F0");
                UpdateFoilReadout();
            }
            else if (rope)
            {
                Vector3 cached = GetDimsForBlock(blockId);
                int cells = cached.x > 0f ? Mathf.RoundToInt(cached.x) : RopeBlock.DefaultLengthCells;
                _ropeSegmentSlider.value = cells;
                UpdateValueText(_ropeSegmentValue, cells, "F0");
            }
            else if (rotor)
            {
                float pitch = GetPitchForBlock(blockId);
                _rotorCollectiveSlider.value = pitch;
                UpdateValueText(_rotorCollectiveValue, pitch, "F0");
                // Config cache 0 = "use default" — display the default RPM
                // without writing the cache, so an untouched rotor keeps
                // the 0 sentinel in its blueprint entry.
                float rpm = RotorDefaults.ResolveRpm(GetConfigForBlock(blockId));
                _rotorRpmSlider.value = rpm;
                UpdateValueText(_rotorRpmValue, rpm, "F0");
                UpdateRotorReadout();
            }
            else if (hover)
            {
                Vector3 cached = GetDimsForBlock(blockId);
                int size = BlockOccupancy.ResolveHoverBladeSize(cached);
                _hoverSizeSlider.value = size;
                UpdateValueText(_hoverSizeValue, size, "F0");
                UpdateHoverReadout();
            }
            else if (module)
            {
                _moduleKind = ModuleKinds.ForBlockId(blockId) ?? ModuleKind.EmpBurst;
                float def = ModuleTuning.DefaultPower(_moduleKind);
                float cachedPower = GetConfigForBlock(blockId);
                float power = cachedPower > 0f ? cachedPower : def;
                // Reconfigure the single slider to this kind's power range.
                _modulePowerSlider.minValue = ModuleTuning.MinPower(_moduleKind);
                _modulePowerSlider.maxValue = ModuleTuning.MaxPower(_moduleKind);
                _modulePowerSlider.value = power;
                UpdateValueText(_modulePowerValue, power, "F1");
                UpdateModuleReadout(power);
            }
            else if (explosive)
            {
                CloseConcoctionList();
                RefreshConcoctionCaption();
            }
            else if (weaponAmmo)
            {
                // Config cache 0 = "use default" — display 1.0× without
                // writing the cache (rotor-RPM pattern), so an untouched
                // turret keeps the 0 sentinel in its blueprint entry.
                float mult = WeaponAmmoDefaults.ResolveMultiplier(GetConfigForBlock(blockId));
                _weaponAmmoSlider.value = mult;
                UpdateValueText(_weaponAmmoValue, mult, "F2");
                UpdateWeaponReadout(mult);
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

        private void OnFoilTeeterChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = SnapInt(v);
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _foilTeeterSlider.value = snapped;
            _suppressCallbacks = false;
            _session?.SetVariantTeeter(id, snapped);
            UpdateValueText(_foilTeeterValue, snapped, "F0");
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

        private void OnRotorRpmChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = Mathf.Round(v / 10f) * 10f; // 10 RPM steps
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _rotorRpmSlider.value = snapped;
            _suppressCallbacks = false;
            _session?.SetVariantConfig(id, snapped);
            UpdateValueText(_rotorRpmValue, snapped, "F0");
            UpdateRotorReadout();
        }

        private void OnHoverSizeChanged(float v)
        {
            if (_suppressCallbacks) return;
            int snapped = Mathf.Clamp(
                Mathf.RoundToInt(v),
                BlockOccupancy.HoverBladeMinSize,
                BlockOccupancy.HoverBladeMaxSize);
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _hoverSizeSlider.value = snapped;
            _suppressCallbacks = false;
            Vector3 dims = GetDimsForBlock(id);
            dims.x = snapped;
            _session?.SetVariantDims(id, dims);
            UpdateValueText(_hoverSizeValue, snapped, "F0");
            UpdateHoverReadout();
        }

        private void UpdateHoverReadout()
        {
            if (_hoverReadout == null) return;
            Vector3 cached = GetDimsForBlock(_activeBlockId);
            int n = BlockOccupancy.ResolveHoverBladeSize(cached);
            // N² lift scaling: size-2 = 1.0× baseline (~800 N/m spring),
            // size-3 = 2.25×, size-4 = 4×. Mass/CPU don't scale per-instance
            // in v1, so the readout focuses on footprint + lift multiplier.
            float multiplier = (n / (float)BlockOccupancy.HoverBladeDefaultSize) *
                               (n / (float)BlockOccupancy.HoverBladeDefaultSize);
            _hoverReadout.text = $"{n}×{n}×1 footprint  •  {multiplier:F2}× lift";
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
        //   - Rotor blade: ω×r at the DIALED RPM and 1 m radius. Disc
        //     lift assumes 4 default blades; player-built rotors with
        //     bigger blades will lift more than the readout suggests
        //     (it's a conservative estimate, not a per-build calculation
        //     — that needs the live chassis).

        private const float CruiseSpeedMs       = 30f;
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
            float rpmCfg = GetConfigForBlock(_activeBlockId);
            float rpm = RotorDefaults.ResolveRpm(rpmCfg);
            float omega = rpm * Mathf.PI * 2f / 60f;
            float tipSpeed = omega * RotorRadiusNominal;
            float perBlade = EstimateFoilLift(
                AeroSurfaceBlock.DefaultSpan,
                AeroSurfaceBlock.DefaultChord,
                effectiveCollective,
                tipSpeed);
            float total = perBlade * RotorBladeCount;
            // Live CPU price at this RPM — the consequence the player is
            // trading lift against. Same pricing core the spend bar and
            // spawn-time TrimToFit use (RotorDefaults.CpuCostFor).
            BlockDefinition rotorDef = GameStateController.Instance != null && GameStateController.Instance.Library != null
                ? GameStateController.Instance.Library.Get(BlockIds.Rotor)
                : null;
            string cpuPart = rotorDef != null
                ? $"  •  CPU {RotorDefaults.CpuCostFor(rotorDef.CpuCost, rpmCfg)}"
                : string.Empty;
            _rotorReadout.text =
                $"≈ {total:F0} N disc ({RotorBladeCount} blades @ {rpm:F0} RPM){cpuPart}";
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

        // Rotor presets — per FOIL_ROTATION_PLAN §3.4. Collective + RPM
        // (per-rotor RPM landed with the RPM slider; direction is still
        // deferred). RPM choices straddle the 240 default so the CPU
        // price spread is visible: Heavy Lift pays ~2.25× sticker,
        // Light pays ~0.4×.
        private static readonly (string label, float collective, float rpm)[] s_rotorPresets =
        {
            ("Heavy Lift", 12f, 360f),
            ("Standard",    8f, 240f),
            ("Light",       5f, 150f),
        };

        private void ApplyFoilPreset(float span, float thickness, float chord, float pitchDeg)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id) || _session == null) return;
            _session.SetVariantDims(id, new Vector3(span, thickness, chord));
            _session.SetVariantPitch(id, pitchDeg);
            // Presets are full role snapshots — reset teeter so "Plane
            // Wing" after a teetered experiment really is a flat wing.
            _session.SetVariantTeeter(id, 0f);
            HandleSelectedBlockChanged(id); // re-syncs sliders
        }

        private void ApplyRotorPreset(float collective, float rpm)
        {
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id) || _session == null) return;
            _session.SetVariantPitch(id, collective);
            _session.SetVariantConfig(id, rpm);
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
                _foilAdvancedToggleText.text = _foilAdvancedExpanded ? "Advanced ▲" : "Advanced ▼";
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

            _titleText = AddText(panel.transform, "Variant", new Vector2(12f, -12f), new Vector2(-12f, -36f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 18, style: FontStyle.Bold, anchor: TextAnchor.MiddleLeft, color: s_accent);

            _foilSection  = BuildFoilSection(panel.transform);
            _ropeSection  = BuildRopeSection(panel.transform);
            _rotorSection = BuildRotorSection(panel.transform);
            _hoverSection = BuildHoverSection(panel.transform);
            _moduleSection = BuildModuleSection(panel.transform);
            _explosiveSection = BuildExplosiveSection(panel.transform);
            _weaponSection = BuildWeaponSection(panel.transform);

            BuildTipStrip(panel.transform);

            _foilSection.SetActive(false);
            _ropeSection.SetActive(false);
            _rotorSection.SetActive(false);
            _hoverSection.SetActive(false);
            _moduleSection.SetActive(false);
            _explosiveSection.SetActive(false);
            _weaponSection.SetActive(false);
        }

        // -----------------------------------------------------------------
        // Explosive section — concoction chooser (ADR-0004)
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
            _concoctionCaptionText = AddText(capGo.transform, "(none) ▼", new Vector2(10f, 0f), new Vector2(-10f, 0f),
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
        }

        private void CloseConcoctionList()
        {
            _concoctionListOpen = false;
            if (_concoctionList != null) _concoctionList.SetActive(false);
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
            const float rowH = 28f;
            var listRT = _concoctionList.GetComponent<RectTransform>();
            listRT.sizeDelta = new Vector2(0f, rows * rowH);

            AddConcoctionOption("(none)", string.Empty, 0, rowH);
            for (int i = 0; i < options.Count; i++)
                AddConcoctionOption(options[i].DisplayName, options[i].Id, i + 1, rowH);
        }

        private void AddConcoctionOption(string label, string id, int row, float rowH)
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

            AddText(go.transform, label, new Vector2(8f, 0f), new Vector2(-8f, 0f),
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 13, style: FontStyle.Normal, anchor: TextAnchor.MiddleLeft, color: UguiPalette.Ink);
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
            if (!string.IsNullOrEmpty(id) && ConcoctionRegistry.TryGet(id, out Concoction c))
                name = string.IsNullOrEmpty(c.DisplayName) ? "Concoction" : c.DisplayName;
            if (_concoctionCaptionText != null) _concoctionCaptionText.text = name + "  ▼";

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
                        $"dmg ×{cc.DamageMultiplier:0.0}  size ×{cc.SizeMultiplier:0.0}  kb ×{cc.KnockbackMultiplier:0.0}  •  +{surcharge} CPU";
                }
            }
        }

        // Best-effort base CPU lookup for the readout (the live garage bar is
        // the authoritative total). Null library → 0 surcharge shown.
        private static int ResolveBaseCpu(string blockId)
        {
            var state = GameStateController.Instance;
            BlockDefinition def = state != null && state.Library != null ? state.Library.Get(blockId) : null;
            return def != null ? Mathf.Max(0, def.CpuCost) : 0;
        }

        private GameObject BuildHoverSection(Transform parent)
        {
            var section = NewChild("Hover", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            // Single integer slider 2-4. SnapInt in the callback enforces
            // integer steps; the slider's wholeNumbers flag is set for
            // visual feedback during drag.
            _hoverSizeSlider = BuildLabeledSlider(section.transform, "Size", slot: 0,
                min: BlockOccupancy.HoverBladeMinSize,
                max: BlockOccupancy.HoverBladeMaxSize,
                def: BlockOccupancy.HoverBladeDefaultSize,
                onChanged: OnHoverSizeChanged, out _hoverSizeValue,
                tip: "Blade footprint in cells (N×N). Lift scales with the square of the size — see the readout below.");
            _hoverSizeSlider.wholeNumbers = true;

            _hoverReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
            var rrt = _hoverReadout.rectTransform;
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -56f - 4f);
            rrt.sizeDelta = new Vector2(0f, 22f);

            return section;
        }

        private GameObject BuildModuleSection(Transform parent)
        {
            var section = NewChild("Module", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            // Single "Power" slider; its range is reconfigured per module kind
            // in HandleSelectedBlockChanged. Writing it caches ConfigValue,
            // which rides the blueprint and trades power for cooldown.
            _modulePowerSlider = BuildLabeledSlider(section.transform, "Power", slot: 0,
                min: 0f, max: 1f, def: 0f, onChanged: OnModulePowerChanged, out _modulePowerValue,
                tip: "Module strength. Higher power means a stronger effect but a longer cooldown (see readout below).");

            _moduleReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
            var rrt = _moduleReadout.rectTransform;
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -56f - 4f);
            rrt.sizeDelta = new Vector2(0f, 22f);

            return section;
        }

        private GameObject BuildWeaponSection(Transform parent)
        {
            var section = NewChild("Weapon", parent);
            var rt = section.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -40f);

            // Single "Ammo" multiplier slider; writing it caches ConfigValue,
            // which rides the blueprint and trades CPU + mass for clip size.
            _weaponAmmoSlider = BuildLabeledSlider(section.transform, "Ammo ×", slot: 0,
                min: WeaponAmmoDefaults.MinMultiplier, max: WeaponAmmoDefaults.MaxMultiplier,
                def: WeaponAmmoDefaults.DefaultMultiplier,
                onChanged: OnWeaponAmmoChanged, out _weaponAmmoValue,
                tip: "Clip-size multiplier for this weapon. Bigger clips cost extra CPU and mass (see readout below).");

            _weaponReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
            var rrt = _weaponReadout.rectTransform;
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -56f - 4f);
            rrt.sizeDelta = new Vector2(0f, 22f);

            return section;
        }

        private void OnWeaponAmmoChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = Mathf.Round(v * 4f) / 4f; // 0.25× steps
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _weaponAmmoSlider.value = snapped;
            _suppressCallbacks = false;
            _session?.SetVariantConfig(id, snapped);
            UpdateValueText(_weaponAmmoValue, snapped, "F2");
            UpdateWeaponReadout(snapped);
        }

        private void UpdateWeaponReadout(float mult)
        {
            if (_weaponReadout == null) return;
            string id = _activeBlockId;
            BlockDefinition def = GameStateController.Instance != null && GameStateController.Instance.Library != null
                ? GameStateController.Instance.Library.Get(id)
                : null;
            // Live consequences at this multiplier — same pricing/mass cores
            // the spend bar, spawn-time TrimToFit and Robot aggregates use.
            int clip = def != null && def.ComponentData is Robogame.Combat.IWeaponStats stats
                ? WeaponAmmoDefaults.ClipFor(stats.ClipSize, mult)
                : 0;
            int cpu = def != null ? WeaponAmmoDefaults.CpuCostFor(def.CpuCost, mult) : 0;
            float massScale = WeaponAmmoDefaults.MassScaleFor(mult);
            _weaponReadout.text = clip > 0
                ? $"{clip} rds/gun  •  CPU {cpu}  •  {massScale:F2}× mass"
                : $"CPU {cpu}  •  {massScale:F2}× mass";
        }

        private void OnModulePowerChanged(float v)
        {
            if (_suppressCallbacks) return;
            float snapped = Mathf.Round(v * 2f) / 2f; // 0.5 steps
            string id = _activeBlockId;
            if (string.IsNullOrEmpty(id)) return;
            _suppressCallbacks = true;
            _modulePowerSlider.value = snapped;
            _suppressCallbacks = false;
            _session?.SetVariantConfig(id, snapped);
            UpdateValueText(_modulePowerValue, snapped, "F1");
            UpdateModuleReadout(snapped);
        }

        private void UpdateModuleReadout(float power)
        {
            if (_moduleReadout == null) return;
            float cd = ModuleTuning.CooldownFor(_moduleKind, power);
            _moduleReadout.text = $"{power:F1} power  •  {cd:F1}s cooldown";
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
                OnFoilSpanChanged,      out _foilSpanValue,
                tip: "Wingtip-to-wingtip length. Lift scales with span × chord (wing area); longer wings also make a bigger target.");
            _foilThicknessSlider = BuildLabeledSlider(section.transform, "Thickness (m)", slot: 2,
                AeroSurfaceBlock.MinThickness, AeroSurfaceBlock.MaxThickness, AeroSurfaceBlock.DefaultThickness,
                OnFoilThicknessChanged, out _foilThicknessValue,
                tip: "Vertical depth of the wing body. Shape and hitbox only — lift comes from span × chord.");
            _foilChordSlider     = BuildLabeledSlider(section.transform, "Chord (m)",     slot: 3,
                AeroSurfaceBlock.MinChord,     AeroSurfaceBlock.MaxChord,     AeroSurfaceBlock.DefaultChord,
                OnFoilChordChanged,     out _foilChordValue,
                tip: "Front-to-back width of the wing. Lift scales with span × chord (wing area).");

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
            img.color = UguiPalette.ButtonIdle;
            var btn = toggleGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(ToggleFoilAdvanced);
            _foilAdvancedToggleText = AddText(toggleGo.transform, "Advanced ▼",
                Vector2.zero, Vector2.zero,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: s_dim);

            // Advanced container — pitch + teeter sliders live here. Built
            // inactive; expander toggle shows it.
            _foilAdvanced = NewChild("Advanced", section.transform);
            var art = _foilAdvanced.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 1f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.sizeDelta = new Vector2(0f, 112f);
            art.anchoredPosition = new Vector2(0f, toggleY - 28f - 4f);

            _foilPitchPrimary = BuildLabeledSlider(_foilAdvanced.transform, "Pitch", slot: 0,
                min: -18f, max: 18f, def: 0f,
                onChanged: OnFoilPitchChanged, out _foilPitchValue,
                tip: "Fixed mounting tilt (degrees). Positive pitch angles the wing into the airflow for lift at speed; past the stall angle lift collapses.");
            UpdateFoilPitchValue(0f);

            // Teeter — chord-axis tilt (tip up/down). Visual-only in v1, so
            // a wider range than pitch is safe: no stall consequence.
            _foilTeeterSlider = BuildLabeledSlider(_foilAdvanced.transform, "Teeter", slot: 1,
                min: -45f, max: 45f, def: 0f,
                onChanged: OnFoilTeeterChanged, out _foilTeeterValue,
                tip: "Tilts the wing along its chord axis, raising or drooping the tip. Cosmetic for now — no effect on lift.");

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

            _ropeSegmentSlider = BuildLabeledSlider(section.transform, "Length (cells)", 0,
                RopeBlock.MinLengthCells, RopeBlock.MaxLengthCells, RopeBlock.DefaultLengthCells,
                OnRopeSegmentCountChanged, out _ropeSegmentValue,
                tip: "Rest length of the rope, in build-grid cells.");

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
                onChanged: OnRotorCollectiveChanged, out _rotorCollectiveValue,
                tip: "Blade pitch applied to every foil the rotor adopts. More collective = more lift per revolution, at more drag.");

            _rotorRpmSlider = BuildLabeledSlider(section.transform, "Max RPM", slot: 2,
                min: RotorDefaults.MinRpm, max: RotorDefaults.MaxRpm, def: RotorDefaults.DefaultRpm,
                onChanged: OnRotorRpmChanged, out _rotorRpmValue,
                tip: "Top rotor speed. Faster spin means more blade lift and a higher CPU price (see readout below).");

            _rotorReadout = AddText(section.transform, "", new Vector2(0f, 0f), new Vector2(0f, 24f),
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                size: 12, style: FontStyle.Italic, anchor: TextAnchor.MiddleCenter, color: s_dim);
            var rrt = _rotorReadout.rectTransform;
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, -56f * 3f - 4f);
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
                AddPresetButton(row.transform, p.label, i, () => ApplyRotorPreset(p.collective, p.rpm));
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
            string tip = null)
        {
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

            if (!string.IsNullOrEmpty(tip))
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
                size: 12, style: FontStyle.Bold, anchor: TextAnchor.MiddleCenter, color: UguiPalette.Ink);
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
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
