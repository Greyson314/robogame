using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Authoritative rotor tuning constants. Single source of truth for
    /// the per-rotor RPM default + slider range and the RPM→CPU pricing
    /// curve; <c>Robogame.Movement.RotorBlock</c> (spin rate),
    /// <see cref="CpuBudget"/> (budget math), and the build-mode variant
    /// panel (slider + readout) all read from here so the spin you see,
    /// the price you pay, and the number on the slider can never drift
    /// apart. Same schema-side placement precedent as
    /// <see cref="FoilDefaults"/>.
    /// </summary>
    public static class RotorDefaults
    {
        /// <summary>
        /// RPM when the blueprint entry's <c>BlockConfig</c> is 0 ("use
        /// default"). History: the old <c>Rotor.RPM</c> Tweakable shipped
        /// at 60 (slow enough to read individual blades by eye) and the
        /// per-block migration deferred the replacement slider, so every
        /// rotor ran at 60 — ~6% of the lift the variant panel's 240 RPM
        /// readout advertised. 240 makes a default rotor actually fly and
        /// matches what the readout always claimed.
        /// </summary>
        public const float DefaultRpm = 240f;

        /// <summary>Build-mode slider range for per-rotor RPM.</summary>
        public const float MinRpm = 30f, MaxRpm = 600f;

        /// <summary>
        /// RPM at which a rotor costs exactly its authored
        /// <see cref="BlockDefinition.CpuCost"/>. Equals
        /// <see cref="DefaultRpm"/> so an untouched rotor pays the
        /// sticker price.
        /// </summary>
        public const float CpuReferenceRpm = DefaultRpm;

        /// <summary>Blueprint <c>BlockConfig</c> → effective RPM (0 = default).</summary>
        public static float ResolveRpm(float blockConfig)
            => blockConfig > 0f ? blockConfig : DefaultRpm;

        /// <summary>
        /// CPU cost of a rotor at the given RPM config. Quadratic in RPM
        /// — blade lift scales with tip-speed², so pricing CPU at
        /// (rpm / 240)² keeps lift-per-CPU constant: a 600 RPM rotor
        /// lifts ~6.25× as much as a 240 one and costs ~6.25× the CPU.
        /// Slow decorative spinners floor at 1 CPU. An authored-free
        /// rotor (baseCost 0) stays free at any RPM.
        /// </summary>
        public static int CpuCostFor(int baseCost, float blockConfig)
        {
            if (baseCost <= 0) return 0;
            float rpm = ResolveRpm(blockConfig);
            float scale = (rpm / CpuReferenceRpm) * (rpm / CpuReferenceRpm);
            return Mathf.Max(1, Mathf.RoundToInt(baseCost * scale));
        }
    }
}
