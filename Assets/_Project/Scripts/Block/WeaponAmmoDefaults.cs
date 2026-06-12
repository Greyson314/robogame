using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Authoritative ammo-capacity tuning for per-instance weapon config.
    /// Single source of truth for the multiplier default + slider range,
    /// the ammo→CPU pricing curve, and the ammo→mass scale;
    /// <c>WeaponAmmoState</c> (pool sizing), <see cref="CpuBudget"/>
    /// (budget math), <c>Robot.EffectiveMass</c> (chassis weight) and the
    /// build-mode variant panel (slider + readout) all read from here so
    /// the clip you get, the price you pay, and the weight you carry can
    /// never drift apart. Same schema-side placement precedent as
    /// <see cref="RotorDefaults"/> / <see cref="FoilDefaults"/>.
    /// </summary>
    /// <remarks>
    /// A weapon entry's <c>BlockConfig</c> stores the ammo multiplier
    /// directly (0 = "use default 1×"). Scope today is the non-explosive
    /// turrets (SMG, Cannon) — Mortar / BombBay already spend their
    /// variant-panel real estate on the concoction chooser (ADR-0004);
    /// extending them needs a combined section layout first.
    /// </remarks>
    public static class WeaponAmmoDefaults
    {
        /// <summary>Multiplier when the entry's <c>BlockConfig</c> is 0 ("use default").</summary>
        public const float DefaultMultiplier = 1f;

        /// <summary>Build-mode slider range for the per-weapon ammo multiplier.</summary>
        public const float MinMultiplier = 0.5f, MaxMultiplier = 2.5f;

        /// <summary>
        /// Fraction of a weapon block's authored mass attributable to its
        /// ammo store — the part that scales with the multiplier. 0.4 means
        /// a 2.5× ammo cannon weighs 1.6× its sticker mass.
        /// </summary>
        public const float AmmoMassFraction = 0.4f;

        /// <summary>
        /// True for block ids whose ammo capacity is per-instance
        /// configurable. SMG + Cannon today (see class remarks for why the
        /// explosive weapons are excluded for now).
        /// </summary>
        public static bool IsAmmoConfigurable(string blockId)
            => blockId == BlockIds.Weapon || blockId == BlockIds.Cannon;

        /// <summary>Blueprint <c>BlockConfig</c> → effective ammo multiplier (0 = default).</summary>
        public static float ResolveMultiplier(float blockConfig)
            => blockConfig > 0f
                ? Mathf.Clamp(blockConfig, MinMultiplier, MaxMultiplier)
                : DefaultMultiplier;

        /// <summary>Effective rounds contributed by one weapon instance at this config.</summary>
        public static int ClipFor(int baseClip, float blockConfig)
            => Mathf.Max(1, Mathf.RoundToInt(baseClip * ResolveMultiplier(blockConfig)));

        /// <summary>
        /// CPU cost of an ammo-configurable weapon at the given config.
        /// Linear above 1× (ammo value is linear: 2× rounds ≈ 2× sustained
        /// uptime). Below 1× the discount is shallower (0.5 + 0.5·m) so a
        /// half-ammo build pays 75% — short clips are mostly erased by the
        /// fast manual reload, so a full linear discount would make
        /// gun-stacking strictly dominant. Untouched (config 0) pays the
        /// sticker price; an authored-free weapon stays free.
        /// </summary>
        public static int CpuCostFor(int baseCost, float blockConfig)
        {
            if (baseCost <= 0) return 0;
            float m = ResolveMultiplier(blockConfig);
            float scale = m >= 1f ? m : 0.5f + 0.5f * m;
            return Mathf.Max(1, Mathf.RoundToInt(baseCost * scale));
        }

        /// <summary>
        /// Chassis-mass scale for an ammo-configurable weapon at this
        /// config: 1 + <see cref="AmmoMassFraction"/>·(m − 1). "More ammo =
        /// more weight" — the Obsidian tradeoff, applied to the ammo
        /// fraction of the block only so the receiver's weight is constant.
        /// </summary>
        public static float MassScaleFor(float blockConfig)
        {
            float m = ResolveMultiplier(blockConfig);
            return 1f + AmmoMassFraction * (m - 1f);
        }
    }
}
