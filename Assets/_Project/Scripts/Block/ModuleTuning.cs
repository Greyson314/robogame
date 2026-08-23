using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// The single source of truth for module ability tuning: per-kind default
    /// power, base cooldown, and the power↔cooldown trade. A module's
    /// per-instance "power" rides <c>BlockBehaviour.ConfigValue</c> (Bucket B,
    /// blueprint-authoritative — invariant #1); cooldown scales with it so
    /// more power always costs a proportionally longer recharge.
    /// </summary>
    /// <remarks>
    /// Pure + Unity-free-ish (only <see cref="Mathf"/>) and cycle-free in
    /// <c>Robogame.Block</c> so the runtime (<c>ModuleSystem</c>), the garage
    /// readout (<c>VariantConfigPanel</c>), and the ability bar all compute the
    /// same numbers. Follows the code-constant convention (gameplay
    /// defaults live in code, not in a per-machine Tweakable).
    /// </remarks>
    public static class ModuleTuning
    {
        /// <summary>Resolved per-fire numbers for one module.</summary>
        public readonly struct Resolved
        {
            /// <summary>Recharge time after a fire, in seconds.</summary>
            public readonly float Cooldown;
            /// <summary>Primary effect axis: impulse N·s / radius m / range m (kind-dependent).</summary>
            public readonly float Magnitude;
            /// <summary>Effect lifetime in seconds (0 for instantaneous abilities).</summary>
            public readonly float Duration;

            public Resolved(float cooldown, float magnitude, float duration)
            {
                Cooldown = cooldown;
                Magnitude = magnitude;
                Duration = duration;
            }
        }

        // Per-kind canonical tuning. DefaultPower is the slider's centre (and
        // the value used when ConfigValue is 0 / untuned). BaseCooldown is the
        // cooldown at default power. BaseDuration's meaning depends on the
        // kind's duration mode (see Resolve).
        private readonly struct Row
        {
            public readonly float DefaultPower;
            public readonly float BaseCooldown;
            public readonly float BaseDuration;
            public Row(float defaultPower, float baseCooldown, float baseDuration)
            {
                DefaultPower = defaultPower;
                BaseCooldown = baseCooldown;
                BaseDuration = baseDuration;
            }
        }

        private static Row RowFor(ModuleKind kind) => kind switch
        {
            // power = launch impulse (N·s). Session 120 playtest: the ½-of-140
            // brief default read as too weak, so the untuned default is raised
            // toward the old full-strength spring; 10 s base cooldown.
            ModuleKind.Spring => new Row(120f, 10f, 0f),
            // power = lockout radius (m); fixed 3 s weapon disable.
            ModuleKind.EmpBurst => new Row(8f, 15f, 3f),
            // power = forward burst Δv (m/s); instantaneous afterburner kick.
            // Session 120: replaced the Blink teleport. 6 s base cooldown so it
            // reads as a repeatable boost, not a once-a-fight panic button.
            ModuleKind.SpeedBurst => new Row(14f, 6f, 0f),
            // power = dome radius (m); fixed 10 s lifetime. Default 10 m — a
            // big deployable ground dome (≈4× the original 2.5 m bubble).
            ModuleKind.DiscShield => new Row(10f, 20f, 10f),
            // power = cloud radius (m); duration scales with power. Session 120
            // playtest: bigger + longer-lived screen (was 6 m / 5 s).
            ModuleKind.Smoke => new Row(9f, 12f, 8f),
            // power = cloak duration (s) itself; 16 s base cooldown.
            ModuleKind.Invisibility => new Row(5f, 16f, 0f),
            // power = mine centre damage (HP); 8 s cooldown; BaseDuration is
            // the deployed mine's active lifetime (s) before it self-expires.
            ModuleKind.Mines => new Row(70f, 8f, 30f),
            // power = HP restored to each own block in range; instantaneous.
            // 60 default mends a cube (100 HP) past half on a 14 s cycle —
            // sustain, not invulnerability. Cooldown scales with power as usual.
            ModuleKind.Repair => new Row(60f, 14f, 0f),
            _ => new Row(1f, 10f, 0f),
        };

        /// <summary>
        /// Unit label for the power axis, shown after the slider value.
        /// "Power" means a different physical quantity per kind (see the
        /// Row comments) — surfacing the unit is what keeps the shared
        /// slider honest (169).
        /// </summary>
        public static string PowerUnit(ModuleKind kind) => kind switch
        {
            ModuleKind.Spring       => " N·s",
            ModuleKind.EmpBurst     => " m",
            ModuleKind.SpeedBurst   => " m/s",
            ModuleKind.DiscShield   => " m",
            ModuleKind.Smoke        => " m",
            ModuleKind.Invisibility => " s",
            ModuleKind.Mines        => " HP",
            ModuleKind.Repair       => " HP",
            _ => "",
        };

        /// <summary>One-line player-facing meaning of the power slider for a kind.</summary>
        public static string PowerTip(ModuleKind kind) => kind switch
        {
            ModuleKind.Spring       => "Launch impulse (N·s): how hard the spring throws your bot. Stronger launch, longer cooldown.",
            ModuleKind.EmpBurst     => "Lockout radius in metres — enemy weapons inside are disabled for 3 s. Bigger radius, longer cooldown.",
            ModuleKind.SpeedBurst   => "Instant forward speed boost in m/s. Bigger burst, longer cooldown.",
            ModuleKind.DiscShield   => "Shield dome radius in metres (10 s lifetime). Bigger dome, longer cooldown.",
            ModuleKind.Smoke        => "Smoke cloud radius in metres — bigger clouds also linger longer. Longer cooldown.",
            ModuleKind.Invisibility => "Cloak duration in seconds. Longer cloak, longer cooldown.",
            ModuleKind.Mines        => "Mine damage (HP) at the blast centre. Deadlier mines, longer cooldown.",
            ModuleKind.Repair       => "HP restored to each of your blocks in range. Stronger heal, longer cooldown.",
            _ => "Module strength.",
        };

        /// <summary>Default power (slider centre / untuned value) for a kind.</summary>
        public static float DefaultPower(ModuleKind kind) => RowFor(kind).DefaultPower;

        /// <summary>Slider lower bound: half default power.</summary>
        public static float MinPower(ModuleKind kind) => RowFor(kind).DefaultPower * 0.5f;

        /// <summary>Slider upper bound: twice default power.</summary>
        public static float MaxPower(ModuleKind kind) => RowFor(kind).DefaultPower * 2f;

        /// <summary>
        /// Resolve the per-fire numbers for <paramref name="kind"/> at the
        /// given per-instance <paramref name="power"/> (0 = use default).
        /// Cooldown = base × clamp(power/default, 0.5, 2).
        /// </summary>
        public static Resolved Resolve(ModuleKind kind, float power)
        {
            Row row = RowFor(kind);
            float p = power > 0f ? power : row.DefaultPower;
            float ratio = Mathf.Clamp(p / row.DefaultPower, 0.5f, 2f);
            float cooldown = row.BaseCooldown * ratio;

            float duration = kind switch
            {
                ModuleKind.Invisibility => p,                  // power IS the duration
                ModuleKind.Smoke => row.BaseDuration * ratio,  // duration scales with power
                ModuleKind.EmpBurst => row.BaseDuration,       // fixed lockout
                ModuleKind.DiscShield => row.BaseDuration,     // fixed 10 s deploy
                ModuleKind.Mines => row.BaseDuration,          // fixed mine lifetime
                _ => 0f,                                       // Spring / Blink: instantaneous
            };

            return new Resolved(cooldown, p, duration);
        }

        /// <summary>Convenience: cooldown at the given power (for garage readout).</summary>
        public static float CooldownFor(ModuleKind kind, float power) => Resolve(kind, power).Cooldown;
    }
}
