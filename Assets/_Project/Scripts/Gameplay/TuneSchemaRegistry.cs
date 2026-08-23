using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Per-block-id <see cref="TuneSchema"/> registry — the declarative
    /// source for every schema-driven section of the
    /// <see cref="VariantConfigPanel"/>. One registration line per block
    /// id; ids without an entry either have a bespoke section (the
    /// concoction chooser) or no variant UI at all.
    /// </summary>
    /// <remarks>
    /// The live consequence readouts mirror gameplay formulas
    /// (<see cref="AeroSurfaceBlock"/> lift, RotorDefaults /
    /// WeaponAmmoDefaults / PogoDefaults pricing) so the player sees the
    /// consequence of a tuning before placing anything. Reference values:
    /// free-wing cruise 30 m/s; rotor ω×r at the dialed RPM and 1 m
    /// radius, 4 default blades (a conservative estimate, not a per-build
    /// calculation — that needs the live chassis).
    /// </remarks>
    // TRACE[ADR-0008]: ghost-recipe-pattern registry — one entry per block
    // id, plain static-initialised dictionary (no lazy init, no Unity
    // objects inside) so it is domain-reload safe without a reset hook.
    public static class TuneSchemaRegistry
    {
        // ----- commit snaps -----
        private static float SnapLength(float v) => Mathf.Round(v * 4f) * 0.25f; // 0.25 m
        private static float SnapInt(float v) => Mathf.Round(v);

        // ----- readout constants (mirror AeroSurfaceBlock.FixedUpdate) -----
        private const float CruiseSpeedMs      = 30f;
        private const float RotorRadiusNominal = 1f;
        private const int   RotorBladeCount    = 4;
        private const float LiftCoefDefault    = 0.95f;   // matches AeroSurfaceBlock._liftCoef
        private const float StallAoaRad        = 0.35f;   // matches AeroSurfaceBlock._stallAoA
        private const float PostStallLift      = 0.55f;   // matches AeroSurfaceBlock._postStallLift

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

        private static ModuleKind KindFor(string blockId)
            => ModuleKinds.ForBlockId(blockId) ?? ModuleKind.EmpBurst;

        // -----------------------------------------------------------------
        // Foil family — Aero / AeroFin / Wing share one schema; only the
        // slider bounds and zero-dim fallbacks differ per id (WingDefaults
        // vs AeroSurfaceBlock), which is why Min/Max are id functions.
        // Pitch is in Advanced because the dim sliders are what most
        // players reach for (it's where the foil's GEOMETRY lives);
        // pitch is the power-user knob.
        // -----------------------------------------------------------------

        private static readonly TuneSchema s_foil = new TuneSchema
        {
            Title = id => id == BlockIds.AeroFin ? "Tail fin"
                : id == BlockIds.Wing ? "Wing" : "Aero wing",
            // Foil presets per FOIL_ROTATION_PLAN §3.5. Presets are full
            // role snapshots — teeter resets so "Plane Wing" after a
            // teetered experiment really is a flat wing.
            Presets = new[]
            {
                new TunePreset { Label = "Heli Blade", Tip = "Short, high-pitch blade for a rotor to spin — place next to a rotor hub.", Writes = new[] { (TuneTarget.DimsX, 1.50f), (TuneTarget.DimsY, 0.06f), (TuneTarget.DimsZ, 0.60f), (TuneTarget.Pitch,  8f), (TuneTarget.Teeter, 0f) } },
                new TunePreset { Label = "Plane Wing", Tip = "Long main wing with a little built-in lift tilt — carries the plane.", Writes = new[] { (TuneTarget.DimsX, 4.00f), (TuneTarget.DimsY, 0.08f), (TuneTarget.DimsZ, 0.90f), (TuneTarget.Pitch,  2f), (TuneTarget.Teeter, 0f) } },
                new TunePreset { Label = "Tail Stab",  Tip = "Small horizontal tail wing (stabiliser). Mount at the back to steady the nose up/down.", Writes = new[] { (TuneTarget.DimsX, 2.00f), (TuneTarget.DimsY, 0.08f), (TuneTarget.DimsZ, 0.70f), (TuneTarget.Pitch, -1f), (TuneTarget.Teeter, 0f) } },
                new TunePreset { Label = "Vert Fin",   Tip = "Vertical tail fin. Mount at the back, standing up, to keep the nose pointing where you fly.", Writes = new[] { (TuneTarget.DimsX, 2.00f), (TuneTarget.DimsY, 0.08f), (TuneTarget.DimsZ, 0.90f), (TuneTarget.Pitch,  0f), (TuneTarget.Teeter, 0f) } },
            },
            Fields = new[]
            {
                new TuneField
                {
                    Label = "Span (m)", Target = TuneTarget.DimsX, Snap = SnapLength,
                    Min = id => id == BlockIds.Wing ? WingDefaults.MinSpan : AeroSurfaceBlock.MinSpan,
                    Max = id => id == BlockIds.Wing ? WingDefaults.MaxSpan : AeroSurfaceBlock.MaxSpan,
                    Resolve = ctx => { AeroShape.ResolveDims(ctx.BlockId, ctx.Dims, out float s, out _, out _); return s; },
                    Tip = "Wingtip-to-wingtip length. Lift scales with span × chord (wing area); longer wings also make a bigger target.",
                },
                new TuneField
                {
                    Label = "Thickness (m)", Target = TuneTarget.DimsY, Snap = SnapLength,
                    Min = id => id == BlockIds.Wing ? WingDefaults.MinThickness : AeroSurfaceBlock.MinThickness,
                    Max = id => id == BlockIds.Wing ? WingDefaults.MaxThickness : AeroSurfaceBlock.MaxThickness,
                    Resolve = ctx => { AeroShape.ResolveDims(ctx.BlockId, ctx.Dims, out _, out float t, out _); return t; },
                    Tip = "Vertical depth of the wing body. Shape and hitbox only — lift comes from span × chord.",
                },
                new TuneField
                {
                    Label = "Chord (m)", Target = TuneTarget.DimsZ, Snap = SnapLength,
                    Min = id => id == BlockIds.Wing ? WingDefaults.MinChord : AeroSurfaceBlock.MinChord,
                    Max = id => id == BlockIds.Wing ? WingDefaults.MaxChord : AeroSurfaceBlock.MaxChord,
                    Resolve = ctx => { AeroShape.ResolveDims(ctx.BlockId, ctx.Dims, out _, out _, out float c); return c; },
                    Tip = "Front-to-back width of the wing. Lift scales with span × chord (wing area).",
                },
                new TuneField
                {
                    Group = TuneFieldGroup.Advanced,
                    Label = "Pitch", Target = TuneTarget.Pitch, Snap = SnapInt,
                    Format = "F0", Suffix = "°",
                    Min = _ => -18f, Max = _ => 18f,
                    Resolve = ctx => ctx.Pitch,
                    // Stall warning past ±18° (BlueprintValidator soft limit).
                    Warn = v => Mathf.Abs(v) > BlueprintValidator.PitchSoftLimitDeg,
                    Tip = "Fixed mounting tilt (degrees). Positive pitch angles the wing into the airflow for lift at speed; past the stall angle lift collapses.",
                },
                new TuneField
                {
                    Group = TuneFieldGroup.Advanced,
                    // Teeter — chord-axis tilt (tip up/down). Visual-only in
                    // v1, so a wider range than pitch is safe: no stall
                    // consequence.
                    Label = "Teeter (visual)", Target = TuneTarget.Teeter, Snap = SnapInt, Format = "F0", Suffix = "°",
                    Min = _ => -45f, Max = _ => 45f,
                    Resolve = ctx => ctx.Teeter,
                    Tip = "Tilts the wing along its chord axis, raising or drooping the tip. Cosmetic for now — no effect on lift.",
                },
                new TuneField
                {
                    Group = TuneFieldGroup.Advanced,
                    // TRACE[ADR-0009]: per-foil control authority knob. 0 in
                    // the blueprint = "use the shared default throw"
                    // (FoilDefaults.ControlThrowDeg) so old saves fly
                    // identically; any non-zero value is this foil's own
                    // full-stick deflection.
                    Label = "Control Throw", Target = TuneTarget.Config, Snap = SnapInt,
                    Format = "F0", Suffix = "°",
                    Min = _ => FoilDefaults.MinControlThrowDeg, Max = _ => FoilDefaults.MaxControlThrowDeg,
                    Resolve = ctx => FoilDefaults.ResolveControlThrow(ctx.Config),
                    Tip = "How far this wing deflects at full stick (degrees). More throw = sharper response from THIS wing; its share of pitch vs roll still comes from where it sits on the bot.",
                },
            },
            Readout = ctx =>
            {
                AeroShape.ResolveDims(ctx.BlockId, ctx.Dims, out float span, out _, out float chord);
                float pitch = ctx.Pitch;
                float lift = EstimateFoilLift(span, chord, pitch, CruiseSpeedMs);
                bool stall = Mathf.Abs(pitch) > BlueprintValidator.PitchSoftLimitDeg;
                return new TuneReadout(stall
                    ? $"≈ {lift:F0} N @ {CruiseSpeedMs:F0} m/s — STALL"
                    : $"≈ {lift:F0} N @ {CruiseSpeedMs:F0} m/s", stall);
            },
        };

        private static readonly TuneSchema s_rope = new TuneSchema
        {
            Title = _ => "Rope",
            Fields = new[]
            {
                new TuneField
                {
                    Label = "Length (cells)", Target = TuneTarget.DimsX, Snap = SnapInt, Format = "F0",
                    Min = _ => RopeBlock.MinLengthCells, Max = _ => RopeBlock.MaxLengthCells,
                    Resolve = ctx => ctx.Dims.x > 0f ? Mathf.RoundToInt(ctx.Dims.x) : RopeBlock.DefaultLengthCells,
                    Tip = "Rest length of the rope, in build-grid cells.",
                },
            },
        };

        private static readonly TuneSchema s_rotor = new TuneSchema
        {
            Title = _ => "Rotor",
            // Rotor presets — per FOIL_ROTATION_PLAN §3.4. Collective + RPM
            // (per-rotor RPM landed with the RPM slider; direction is still
            // deferred). RPM choices straddle the 240 default so the CPU
            // price spread is visible: Heavy Lift pays ~2.25× sticker,
            // Light pays ~0.4×.
            Presets = new[]
            {
                new TunePreset { Label = "Heavy Lift", Writes = new[] { (TuneTarget.Pitch, 12f), (TuneTarget.Config, 360f) } },
                new TunePreset { Label = "Standard",   Writes = new[] { (TuneTarget.Pitch,  8f), (TuneTarget.Config, 240f) } },
                new TunePreset { Label = "Light",      Writes = new[] { (TuneTarget.Pitch,  5f), (TuneTarget.Config, 150f) } },
            },
            Fields = new[]
            {
                new TuneField
                {
                    Label = "Collective", Target = TuneTarget.Pitch, Snap = SnapInt, Format = "F0", Suffix = "°",
                    Min = _ => 0f, Max = _ => 18f,
                    // 0 = "use the rotor's authored default" — display that
                    // default instead of a misleading 0 (the sentinel rule
                    // this class doc mandates; fixed 169).
                    Resolve = ctx => ctx.Pitch > 0f ? ctx.Pitch : RotorDefaults.DefaultCollectiveDeg,
                    Tip = "Blade pitch in degrees, applied to every foil the rotor adopts. More collective = more lift per revolution, at more drag.",
                },
                new TuneField
                {
                    Label = "Max RPM", Target = TuneTarget.Config,
                    Snap = v => Mathf.Round(v / 10f) * 10f, // 10 RPM steps
                    Format = "F0",
                    Min = _ => RotorDefaults.MinRpm, Max = _ => RotorDefaults.MaxRpm,
                    // Config cache 0 = "use default" — display the default RPM
                    // without writing the cache, so an untouched rotor keeps
                    // the 0 sentinel in its blueprint entry.
                    Resolve = ctx => RotorDefaults.ResolveRpm(ctx.Config),
                    Tip = "Top rotor speed. Faster spin means more blade lift and a higher CPU price (see readout below).",
                },
            },
            Readout = ctx =>
            {
                float collective = ctx.Pitch;
                // collective=0 in the cache means "use rotor's authored
                // default". Mirror that for the readout so the player sees
                // the actual post-place value.
                float effectiveCollective = collective > 0f ? collective : RotorDefaults.DefaultCollectiveDeg;
                float rpmCfg = ctx.Config;
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
                return new TuneReadout(
                    $"≈ {total:F0} N disc ({RotorBladeCount} blades @ {rpm:F0} RPM){cpuPart}");
            },
        };

        private static readonly TuneSchema s_hover = new TuneSchema
        {
            Title = _ => "Hover blade",
            Fields = new[]
            {
                new TuneField
                {
                    // Integer slider 2-4. Snap enforces integer steps; the
                    // IntSlider kind sets wholeNumbers for visual feedback
                    // during drag.
                    Kind = TuneFieldKind.IntSlider,
                    Label = "Footprint", Target = TuneTarget.DimsX, Format = "F0", Suffix = " cells",
                    Snap = v => Mathf.Clamp(Mathf.RoundToInt(v),
                        BlockOccupancy.HoverBladeMinSize, BlockOccupancy.HoverBladeMaxSize),
                    Min = _ => BlockOccupancy.HoverBladeMinSize,
                    Max = _ => BlockOccupancy.HoverBladeMaxSize,
                    Resolve = ctx => BlockOccupancy.ResolveHoverBladeSize(ctx.Dims),
                    Tip = "Blade footprint in cells (N×N). Lift scales with the square of the size — see the readout below.",
                },
            },
            Readout = ctx =>
            {
                int n = BlockOccupancy.ResolveHoverBladeSize(ctx.Dims);
                // N² lift scaling: size-2 = 1.0× baseline (~800 N/m spring),
                // size-3 = 2.25×, size-4 = 4×. Mass/CPU don't scale
                // per-instance in v1, so the readout focuses on footprint +
                // lift multiplier.
                float multiplier = (n / (float)BlockOccupancy.HoverBladeDefaultSize) *
                                   (n / (float)BlockOccupancy.HoverBladeDefaultSize);
                return new TuneReadout($"{n}×{n}×1 footprint  •  {multiplier:F2}× lift");
            },
        };

        // One schema serves every module id — the power range and default
        // are per-kind (ModuleTuning), resolved from the block id.
        private static readonly TuneSchema s_module = new TuneSchema
        {
            Title = id => ModuleKinds.Label(KindFor(id)),
            IdleLead = "Module",
            Fields = new[]
            {
                new TuneField
                {
                    Label = "Power", Target = TuneTarget.Config, Format = "F1",
                    Snap = v => Mathf.Round(v * 2f) / 2f, // 0.5 steps
                    Min = id => ModuleTuning.MinPower(KindFor(id)),
                    Max = id => ModuleTuning.MaxPower(KindFor(id)),
                    Resolve = ctx => ctx.Config > 0f ? ctx.Config : ModuleTuning.DefaultPower(KindFor(ctx.BlockId)),
                    // "Power" means a different physical unit per module
                    // kind (metres / seconds / HP / N·s / m/s) — the suffix
                    // and tip resolve per id so the number is never
                    // unit-less (169).
                    SuffixFor = id => ModuleTuning.PowerUnit(KindFor(id)),
                    TipFor = id => ModuleTuning.PowerTip(KindFor(id)),
                },
            },
            Readout = ctx =>
            {
                ModuleKind kind = KindFor(ctx.BlockId);
                float power = ctx.Config > 0f ? ctx.Config : ModuleTuning.DefaultPower(kind);
                float cd = ModuleTuning.CooldownFor(kind, power);
                return new TuneReadout($"{power:F1}{ModuleTuning.PowerUnit(kind)}  •  {cd:F1}s cooldown");
            },
        };

        private static readonly TuneSchema s_weapon = new TuneSchema
        {
            Title = id => id == BlockIds.Cannon ? "Cannon" : "SMG",
            Fields = new[]
            {
                new TuneField
                {
                    Label = "Ammo ×", Target = TuneTarget.Config, Format = "F2",
                    Snap = v => Mathf.Round(v * 4f) / 4f, // 0.25× steps
                    Min = _ => WeaponAmmoDefaults.MinMultiplier,
                    Max = _ => WeaponAmmoDefaults.MaxMultiplier,
                    // Config cache 0 = "use default" — display 1.0× without
                    // writing the cache (rotor-RPM pattern), so an untouched
                    // turret keeps the 0 sentinel in its blueprint entry.
                    Resolve = ctx => WeaponAmmoDefaults.ResolveMultiplier(ctx.Config),
                    Tip = "Clip-size multiplier for this weapon. Bigger clips cost extra CPU and mass (see readout below).",
                },
            },
            Readout = ctx =>
            {
                float mult = WeaponAmmoDefaults.ResolveMultiplier(ctx.Config);
                BlockDefinition def = GameStateController.Instance != null && GameStateController.Instance.Library != null
                    ? GameStateController.Instance.Library.Get(ctx.BlockId)
                    : null;
                // Live consequences at this multiplier — same pricing/mass
                // cores the spend bar, spawn-time TrimToFit and Robot
                // aggregates use.
                int clip = def != null && def.ComponentData is Robogame.Combat.IWeaponStats stats
                    ? WeaponAmmoDefaults.ClipFor(stats.ClipSize, mult)
                    : 0;
                int cpu = def != null ? WeaponAmmoDefaults.CpuCostFor(def.CpuCost, mult) : 0;
                float massScale = WeaponAmmoDefaults.MassScaleFor(mult);
                return new TuneReadout(clip > 0
                    ? $"{clip} rds/gun  •  CPU {cpu}  •  {massScale:F2}× mass"
                    : $"CPU {cpu}  •  {massScale:F2}× mass");
            },
        };

        private static readonly TuneSchema s_pogo = new TuneSchema
        {
            Title = _ => "Pogo",
            Fields = new[]
            {
                new TuneField
                {
                    // ConfigValue rides the blueprint as a bounce-HEIGHT
                    // multiplier (PogoBlock takes √power on takeoff speed).
                    Label = "Power ×", Target = TuneTarget.Config, Format = "F2",
                    Snap = v => Mathf.Round(v * 20f) / 20f, // 0.05× steps
                    Min = _ => PogoDefaults.MinPower,
                    Max = _ => PogoDefaults.MaxPower,
                    // Config cache 0 = "use default 1×" — display without
                    // writing the cache (rotor-RPM pattern), so an untouched
                    // pogo keeps the 0 sentinel in its blueprint entry.
                    Resolve = ctx => PogoDefaults.ResolvePower(ctx.Config),
                    Tip = "Bounce-height multiplier for this pogo. Momentum from drops stacks on top either way.",
                },
            },
            Readout = ctx =>
            {
                float power = PogoDefaults.ResolvePower(ctx.Config);
                return new TuneReadout(
                    $"{power:F2}× bounce height  •  ≈ {PogoDefaults.NominalApexMeters * power:F1} m solo hop");
            },
        };

        /// <summary>Distinct schema instances — the panel builds one UGUI section per entry.</summary>
        public static readonly TuneSchema[] All = { s_foil, s_rope, s_rotor, s_hover, s_module, s_weapon, s_pogo };

        private static readonly Dictionary<string, TuneSchema> s_schemas =
            new Dictionary<string, TuneSchema>
            {
                [BlockIds.Aero]         = s_foil,
                [BlockIds.AeroFin]      = s_foil,
                [BlockIds.Wing]         = s_foil,
                [BlockIds.Rope]         = s_rope,
                [BlockIds.Rotor]        = s_rotor,
                [BlockIds.HoverBlade]   = s_hover,
                [BlockIds.Spring]       = s_module,
                [BlockIds.ModuleEmp]    = s_module,
                [BlockIds.ModuleBlink]  = s_module,
                [BlockIds.ModuleShield] = s_module,
                [BlockIds.ModuleSmoke]  = s_module,
                [BlockIds.ModuleInvis]  = s_module,
                [BlockIds.ModuleMines]  = s_module,
                [BlockIds.ModuleRepair] = s_module,
                [BlockIds.Weapon]       = s_weapon,  // SMG
                [BlockIds.Cannon]       = s_weapon,
                [BlockIds.Pogo]         = s_pogo,
            };

        /// <summary>Schema for a block id, or false (concoction-only ids, plain blocks).</summary>
        public static bool TryGet(string blockId, out TuneSchema schema)
        {
            if (string.IsNullOrEmpty(blockId))
            {
                schema = null;
                return false;
            }
            return s_schemas.TryGetValue(blockId, out schema);
        }
    }
}
