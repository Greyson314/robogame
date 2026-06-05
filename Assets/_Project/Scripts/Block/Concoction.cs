using System;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// A player-authored explosive payload recipe. Three normalised sliders
    /// (damage / size / knockback, each 0..1, default 0.5) map to real stat
    /// multipliers applied to an explosive weapon at fire time, and to a CPU
    /// surcharge added to the carrier block's cost. Authored in the garage
    /// Laboratory, chosen per explosive block, baked into the frozen blueprint
    /// by id, and resolved + clamped server-side at match start.
    /// </summary>
    /// <remarks>
    /// Plain <c>[Serializable]</c> data (no MonoBehaviour, no Unity dependency
    /// beyond <see cref="Mathf"/>) so EditMode tests drive it directly and
    /// <c>ConcoctionSerializer</c> round-trips it without I/O. Governance model
    /// and invariant rationale: <c>docs/decisions/0004-concoction-persistence.md</c>.
    /// </remarks>
    [Serializable]
    public sealed class Concoction
    {
        /// <summary>Neutral slider position: 50% = the weapon's baseline stat.</summary>
        public const float DefaultPct = 0.5f;

        /// <summary>Stat multiplier at slider = 0 (weak but functional, never zero).</summary>
        public const float MinMultiplier = 0.5f;

        /// <summary>Stat multiplier at slider = 1 (the max payoff for the CPU spend).</summary>
        public const float MaxMultiplier = 2.0f;

        [Tooltip("Stable id generated at creation; referenced by ChassisBlueprint.Entry.ConcoctionId. Never shown to the player.")]
        public string Id;

        [Tooltip("Human-readable name shown in the Lab list and the per-block dropdown.")]
        public string DisplayName;

        [Range(0f, 1f)] public float DamagePct = DefaultPct;
        [Range(0f, 1f)] public float SizePct = DefaultPct;
        [Range(0f, 1f)] public float KnockbackPct = DefaultPct;

        public Concoction() { }

        public Concoction(string id, string displayName,
            float damagePct = DefaultPct, float sizePct = DefaultPct, float knockbackPct = DefaultPct)
        {
            Id = id;
            DisplayName = displayName;
            DamagePct = damagePct;
            SizePct = sizePct;
            KnockbackPct = knockbackPct;
        }

        /// <summary>
        /// Piecewise-linear slider → stat multiplier with a knee at the neutral
        /// 50% so the default concoction equals the weapon's baseline stat:
        /// 0% → <see cref="MinMultiplier"/>, 50% → 1.0×, 100% → <see cref="MaxMultiplier"/>.
        /// </summary>
        public static float Multiplier(float pct)
        {
            pct = Mathf.Clamp01(pct);
            return pct < DefaultPct
                ? Mathf.Lerp(MinMultiplier, 1f, pct / DefaultPct)
                : Mathf.Lerp(1f, MaxMultiplier, (pct - DefaultPct) / (1f - DefaultPct));
        }

        public float DamageMultiplier => Multiplier(DamagePct);
        public float SizeMultiplier => Multiplier(SizePct);
        public float KnockbackMultiplier => Multiplier(KnockbackPct);

        /// <summary>
        /// Extra CPU this concoction adds to the carrier block, monotonic in the
        /// slider values (raise any slider → costlier; all-min → no surcharge;
        /// all-max → 1.5× the block's base cost). Scaled by the carrier's own
        /// <paramref name="baseCpuCost"/> so the surcharge calibrates to whatever
        /// the explosive block costs, with no magic constant.
        /// </summary>
        public int CpuSurcharge(int baseCpuCost)
        {
            if (baseCpuCost <= 0) return 0;
            float sliderSum = DamagePct + SizePct + KnockbackPct; // 0..3
            return Mathf.RoundToInt(baseCpuCost * sliderSum * 0.5f);
        }

        /// <summary>Clamp every slider to [0,1]. The server-side sanitisation guard (INV-3).</summary>
        public void Validate()
        {
            DamagePct = Mathf.Clamp01(DamagePct);
            SizePct = Mathf.Clamp01(SizePct);
            KnockbackPct = Mathf.Clamp01(KnockbackPct);
        }

        /// <summary>Deep copy — used when handing a library concoction to an editable Lab field.</summary>
        public Concoction Clone() => new Concoction(Id, DisplayName, DamagePct, SizePct, KnockbackPct);
    }
}
