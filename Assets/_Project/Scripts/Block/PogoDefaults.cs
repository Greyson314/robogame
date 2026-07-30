using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Authoritative tuning for per-instance pogo config. A pogo entry's
    /// <c>BlockConfig</c> stores a bounce-HEIGHT multiplier (0 = "use
    /// default 1×"); <c>PogoBlock</c> (takeoff speed, via √power since
    /// height ∝ v²) and the build-mode variant panel (slider + readout)
    /// both read from here so the hop you get and the number on the
    /// slider can never drift apart. Same schema-side placement precedent
    /// as <see cref="WeaponAmmoDefaults"/> / <see cref="RotorDefaults"/>.
    /// </summary>
    public static class PogoDefaults
    {
        /// <summary>Height multiplier when the entry's <c>BlockConfig</c> is 0 ("use default").</summary>
        public const float DefaultPower = 1f;

        /// <summary>Build-mode slider range for the per-pogo bounce-height multiplier.</summary>
        public const float MinPower = 0.8f, MaxPower = 1.8f;

        /// <summary>
        /// Solo-hop apex at 1× power, metres — for panel readouts only
        /// (14 m/s takeoff → v²/2g ≈ 10 m). Update if PogoBlock's default
        /// bounce speed changes.
        /// </summary>
        public const float NominalApexMeters = 10f;

        /// <summary>Blueprint <c>BlockConfig</c> → effective height multiplier (0 = default).</summary>
        public static float ResolvePower(float blockConfig)
            => blockConfig > 0f
                ? Mathf.Clamp(blockConfig, MinPower, MaxPower)
                : DefaultPower;
    }
}
