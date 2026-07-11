using System;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// A player-authored ammunition recipe. Five normalised sliders
    /// (damage / size / knockback / speed / spread, each 0..1, default 0.5)
    /// map to real stat multipliers applied to a concoctable weapon at fire
    /// time, and to a CPU surcharge added to the carrier block's cost. Each
    /// recipe also mixes a deterministic pigment colour
    /// (<see cref="ConcoctionColor"/>) that names it and tints its shots.
    /// Authored in the garage Laboratory, chosen per weapon block, baked into
    /// the frozen blueprint by id, and resolved + clamped server-side at
    /// match start.
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
        // Session 141: two more levers. Speed scales launch/muzzle velocity
        // on every concoctable weapon; Spread scales cone spread and only
        // has a stat to bite on for the SMG (splash weapons ignore it — the
        // multiplier is applied wherever a spread stat exists, no-op else).
        [Range(0f, 1f)] public float SpeedPct = DefaultPct;
        [Range(0f, 1f)] public float SpreadPct = DefaultPct;

        public Concoction() { }

        public Concoction(string id, string displayName,
            float damagePct = DefaultPct, float sizePct = DefaultPct, float knockbackPct = DefaultPct,
            float speedPct = DefaultPct, float spreadPct = DefaultPct)
        {
            Id = id;
            DisplayName = displayName;
            DamagePct = damagePct;
            SizePct = sizePct;
            KnockbackPct = knockbackPct;
            SpeedPct = speedPct;
            SpreadPct = spreadPct;
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
        public float SpeedMultiplier => Multiplier(SpeedPct);
        public float SpreadMultiplier => Multiplier(SpreadPct);

        /// <summary>The recipe's mixed pigment colour — see <see cref="ConcoctionColor"/>.</summary>
        public Color MixedColor => ConcoctionColor.Mix(this);

        /// <summary>
        /// Per-slider-sum CPU surcharge factor. v2 (session 141, 5 levers):
        /// 0.3 keeps the two calibration points players anchored on under
        /// the v1 3-lever formula (factor 0.5) — an all-neutral recipe still
        /// prices +75% of base, an all-max recipe still +150%. Mid recipes
        /// shift by up to ±0.2× base on re-load; accepted and recorded in
        /// docs/decisions/0005 rather than versioning formulas per recipe.
        /// </summary>
        public const float SurchargeFactorPerSliderSum = 0.3f;

        /// <summary>
        /// Extra CPU this concoction adds to the carrier block, monotonic in the
        /// slider values (raise any slider → costlier; all-min → no surcharge;
        /// all-max → 1.5× the block's base cost). Scaled by the carrier's own
        /// <paramref name="baseCpuCost"/> so the surcharge calibrates to whatever
        /// the carrier block costs, with no magic constant.
        /// </summary>
        public int CpuSurcharge(int baseCpuCost)
        {
            if (baseCpuCost <= 0) return 0;
            float sliderSum = DamagePct + SizePct + KnockbackPct + SpeedPct + SpreadPct; // 0..5
            return Mathf.RoundToInt(baseCpuCost * sliderSum * SurchargeFactorPerSliderSum);
        }

        /// <summary>Clamp every slider to [0,1]. The server-side sanitisation guard (INV-3).</summary>
        public void Validate()
        {
            DamagePct = Mathf.Clamp01(DamagePct);
            SizePct = Mathf.Clamp01(SizePct);
            KnockbackPct = Mathf.Clamp01(KnockbackPct);
            SpeedPct = Mathf.Clamp01(SpeedPct);
            SpreadPct = Mathf.Clamp01(SpreadPct);
        }

        /// <summary>Deep copy — used when handing a library concoction to an editable Lab field.</summary>
        public Concoction Clone() =>
            new Concoction(Id, DisplayName, DamagePct, SizePct, KnockbackPct, SpeedPct, SpreadPct);
    }
}
