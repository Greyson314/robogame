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
    /// same numbers. Mirrors the <c>SpringTuningConfig.Default</c> /
    /// code-constant convention (gameplay defaults live in code, not in a
    /// per-machine Tweakable).
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
            // power = launch impulse (N·s). Default ½ of the session-104
            // spring (140) per the design brief; 10 s base cooldown.
            ModuleKind.Spring => new Row(70f, 10f, 0f),
            // power = lockout radius (m); fixed 3 s weapon disable.
            ModuleKind.EmpBurst => new Row(8f, 15f, 3f),
            // power = teleport range (m); instantaneous.
            ModuleKind.Blink => new Row(12f, 10f, 0f),
            // power = bubble radius (m); duration scales with power.
            ModuleKind.DiscShield => new Row(2.5f, 20f, 4f),
            // power = cloud radius (m); duration scales with power.
            ModuleKind.Smoke => new Row(6f, 12f, 5f),
            // power = cloak duration (s) itself; 16 s base cooldown.
            ModuleKind.Invisibility => new Row(5f, 16f, 0f),
            _ => new Row(1f, 10f, 0f),
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
                ModuleKind.DiscShield => row.BaseDuration * ratio,
                ModuleKind.EmpBurst => row.BaseDuration,       // fixed lockout
                _ => 0f,                                       // Spring / Blink: instantaneous
            };

            return new Resolved(cooldown, p, duration);
        }

        /// <summary>Convenience: cooldown at the given power (for garage readout).</summary>
        public static float CooldownFor(ModuleKind kind, float power) => Resolve(kind, power).Cooldown;
    }
}
