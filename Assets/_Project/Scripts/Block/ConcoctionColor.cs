using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// The concoction colour chemistry: deterministic lever→pigment mixing
    /// and the pigment-name lookup that drives default concoction names
    /// ("Dark Madder Concoction"). Pure logic, no MonoBehaviour — EditMode
    /// tests drive it directly, and every consumer (Lab swatch, projectile
    /// tint, kill-feed chip, default naming) reads the same mix so the
    /// colour can never disagree with the name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mixing model</b> (session 141, design-pass): weighted CIRCULAR
    /// hue blending, not naive RGB averaging (which converges to brown).
    /// Each lever has a fixed hue anchor; only the part of a slider ABOVE
    /// the neutral 50% contributes pigment (raising a lever pours its
    /// reagent in; lowering below neutral dilutes but adds no hue). The
    /// blended hue is the weighted circular mean; <i>dominance</i> (the
    /// mean resultant length) drives saturation, so hue-opposed levers
    /// visibly desaturate toward sludge — the legible tell for
    /// "tried to do everything". Total slider level drives darkness:
    /// dilute mixes are pale, potent mixes are dark, which is what
    /// generates the Dark/Pale name prefixes for free.
    /// </para>
    /// <para>
    /// <b>Reserved hue bands</b>: anchors deliberately avoid vermilion
    /// (#C33D1F — rationed UI chrome) and the green band 90–165°
    /// (repair/regen signal). See docs/decisions/0005.
    /// </para>
    /// </remarks>
    public static class ConcoctionColor
    {
        // Hue anchors in degrees + display colour per lever (full-strength
        // pigment). Used for the mix AND for tinting each Lab reagent vial.
        public const float DamageHue    = 350f; // Madder — deep crimson, off vermilion's hue
        public const float SizeHue      = 245f; // Indigo — reuses the UI's utility read
        public const float KnockbackHue = 38f;  // Ochre — impact amber
        public const float SpeedHue     = 215f; // Prussian — cold fast blue
        public const float SpreadHue    = 300f; // Orchid — chaotic scattershot violet

        /// <summary>Full-strength pigment colour for one lever (vial tint in the Lab).</summary>
        public static Color LeverPigment(float hueDeg) =>
            Color.HSVToRGB(Mathf.Repeat(hueDeg, 360f) / 360f, 0.80f, 0.75f);

        // Saturation band: dominance 0 (hue-opposed sludge) → 1 (one clear leader).
        private const float MinSaturation = 0.15f, MaxSaturation = 0.85f;
        // Value band: average slider level 0 (dilute, pale) → 1 (potent, dark).
        private const float PaleValue = 0.90f, DarkValue = 0.30f;
        // A slider must exceed neutral by this much before its pigment counts.
        private const float WeightDeadzone = 0.02f;

        /// <summary>
        /// Deterministic mixed colour for a recipe. Same-value recipes always
        /// produce the same colour (no randomness, no state).
        /// </summary>
        public static Color Mix(Concoction c)
        {
            if (c == null) return Color.gray;
            MixCore(c, out float hue, out float dominance, out float weightSum, out float avgLevel);

            float value = Mathf.Lerp(PaleValue, DarkValue, avgLevel);
            if (weightSum <= 0f)
            {
                // No pigment poured (nothing above neutral): a neutral wash
                // whose darkness still tracks total level.
                return Color.HSVToRGB(0f, 0f, value);
            }
            float saturation = Mathf.Lerp(MinSaturation, MaxSaturation, dominance);
            return Color.HSVToRGB(hue / 360f, saturation, value);
        }

        /// <summary>
        /// Default display name for a recipe: "{prefix }{pigment} Concoction",
        /// e.g. "Dark Madder Concoction", "Murky Orchid Concoction". Special
        /// cases: an untouched recipe is "Standard Mixture", an all-diluted
        /// one "Raw Mixture", and a maxed-everything hue-cancelling one earns
        /// "Black Bile". Collision suffixing is the caller's job (the Lab
        /// appends " (2)" etc. against its saved list).
        /// </summary>
        public static string DefaultName(Concoction c)
        {
            if (c == null) return "Concoction";
            MixCore(c, out float hue, out float dominance, out float weightSum, out float avgLevel);

            if (weightSum <= 0f)
                return avgLevel < Concoction.DefaultPct - 0.05f ? "Raw Mixture" : "Standard Mixture";
            if (dominance < 0.15f && avgLevel > 0.7f)
                return "Black Bile";

            string pigment = PigmentName(hue);
            if (dominance < 0.35f) return $"Murky {pigment} Concoction";
            if (avgLevel > 0.65f) return $"Dark {pigment} Concoction";
            if (avgLevel < 0.30f) return $"Pale {pigment} Concoction";
            return $"{pigment} Concoction";
        }

        /// <summary>
        /// Pigment name for a hue (degrees). 30°-band coverage of the whole
        /// wheel — circular blending can land anywhere, including the
        /// "unreachable by anchor" green bands.
        /// </summary>
        public static string PigmentName(float hueDeg)
        {
            float h = Mathf.Repeat(hueDeg, 360f);
            if (h >= 340f || h < 20f) return "Madder";
            if (h < 55f)  return "Ochre";
            if (h < 90f)  return "Saffron";
            if (h < 120f) return "Citron";
            if (h < 150f) return "Verdigris";
            if (h < 180f) return "Teal";
            if (h < 210f) return "Cerulean";
            if (h < 240f) return "Prussian";
            if (h < 270f) return "Indigo";
            if (h < 300f) return "Amethyst";
            if (h < 330f) return "Orchid";
            return "Rose Madder"; // 330–340
        }

        // Shared core: blended hue (deg), dominance (0..1 mean resultant
        // length), pigment weight sum, and average slider level across all
        // levers. One implementation so Mix and DefaultName cannot drift.
        private static void MixCore(Concoction c,
            out float hueDeg, out float dominance, out float weightSum, out float avgLevel)
        {
            float x = 0f, y = 0f;
            weightSum = 0f;

            Accumulate(c.DamagePct,    DamageHue,    ref x, ref y, ref weightSum);
            Accumulate(c.SizePct,      SizeHue,      ref x, ref y, ref weightSum);
            Accumulate(c.KnockbackPct, KnockbackHue, ref x, ref y, ref weightSum);
            Accumulate(c.SpeedPct,     SpeedHue,     ref x, ref y, ref weightSum);
            Accumulate(c.SpreadPct,    SpreadHue,    ref x, ref y, ref weightSum);

            avgLevel = (Mathf.Clamp01(c.DamagePct) + Mathf.Clamp01(c.SizePct)
                      + Mathf.Clamp01(c.KnockbackPct) + Mathf.Clamp01(c.SpeedPct)
                      + Mathf.Clamp01(c.SpreadPct)) / 5f;

            if (weightSum <= 0f)
            {
                hueDeg = 0f;
                dominance = 0f;
                return;
            }
            hueDeg = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg, 360f);
            dominance = Mathf.Clamp01(Mathf.Sqrt(x * x + y * y) / weightSum);
        }

        private static void Accumulate(float pct, float hueDeg,
            ref float x, ref float y, ref float weightSum)
        {
            // Pigment weight: only the part of the slider ABOVE neutral pours
            // reagent in. Below-neutral positions dilute (they lower avgLevel)
            // but contribute no hue.
            float w = Mathf.Clamp01(pct) - Concoction.DefaultPct;
            if (w <= WeightDeadzone) return;
            w *= 2f; // rescale (0.5..1] → (0..1]
            float rad = hueDeg * Mathf.Deg2Rad;
            x += w * Mathf.Cos(rad);
            y += w * Mathf.Sin(rad);
            weightSum += w;
        }
    }
}
