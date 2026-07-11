using NUnit.Framework;
using Robogame.Block;
using UnityEngine;

namespace Robogame.Tests.EditMode.Concoctions
{
    /// <summary>
    /// Session-141 tests for <see cref="ConcoctionColor"/>: determinism,
    /// the special-case names (Standard/Raw Mixture), the single-lever
    /// pigment mapping, and the hue-opposed "everything maxed" case. The
    /// name/colour boundary values below are computed from the real
    /// MixCore math (weighted circular hue mean + mean-resultant-length
    /// dominance), not guessed — see the inline arithmetic on the
    /// all-maxed case, which is the one non-obvious result.
    /// </summary>
    public sealed class ConcoctionColorTests
    {
        [Test]
        public void Mix_SameRecipeTwice_ProducesIdenticalColor()
        {
            // No randomness, no hidden state — the Lab swatch, the
            // projectile tint, and the kill-feed chip all call Mix()
            // independently and must never disagree.
            var c = new Concoction("id", "x", 0.9f, 0.2f, 0.7f, 0.3f, 0.6f);
            Color a = ConcoctionColor.Mix(c);
            Color b = ConcoctionColor.Mix(c);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void MixedColor_Property_ForwardsToConcoctionColorMix()
        {
            var c = new Concoction("id", "x", 0.9f, 0.2f, 0.7f, 0.3f, 0.6f);
            Assert.AreEqual(ConcoctionColor.Mix(c), c.MixedColor);
        }

        // --- special-case names ---------------------------------------------

        [Test]
        public void DefaultName_AllLeversNeutral_IsStandardMixture()
        {
            // All five at 0.5: nothing crosses the above-neutral weight
            // deadzone, so weightSum == 0 and avgLevel == 0.5 (not below the
            // 0.45 Raw-Mixture boundary) → the untouched-recipe special case.
            var c = new Concoction("id", "x");
            Assert.AreEqual("Standard Mixture", ConcoctionColor.DefaultName(c));
        }

        [Test]
        public void DefaultName_OneLeverBelowNeutral_RestNeutral_IsRawMixture()
        {
            // damage=0, rest=0.5: weightSum stays 0 (nothing is ABOVE
            // neutral — damage dilutes but pours no pigment). avgLevel =
            // (0 + 0.5*4) / 5 = 0.4, which IS below DefaultPct-0.05 = 0.45,
            // so this crosses into "Raw Mixture" rather than "Standard".
            var c = new Concoction("id", "x", damagePct: 0f);
            Assert.AreEqual("Raw Mixture", ConcoctionColor.DefaultName(c));
        }

        // --- single-lever-maxed pigment mapping ------------------------------
        // Each case: one lever at 1.0, the other four at neutral (0.5) so
        // only one lever pours pigment (weightSum from a single lever,
        // dominance == 1.0 exactly) and avgLevel == 0.6 (below the 0.65
        // Dark-prefix threshold and above the 0.30 Pale-prefix threshold),
        // so the name is exactly "{Pigment} Concoction" with no prefix.

        [Test]
        public void DefaultName_DamageMaxed_IsMadderConcoction()
        {
            var c = new Concoction("id", "x", damagePct: 1f);
            Assert.AreEqual("Madder Concoction", ConcoctionColor.DefaultName(c));
        }

        [Test]
        public void DefaultName_SpeedMaxed_IsPrussianConcoction()
        {
            var c = new Concoction("id", "x", speedPct: 1f);
            Assert.AreEqual("Prussian Concoction", ConcoctionColor.DefaultName(c));
        }

        [Test]
        public void DefaultName_KnockbackMaxed_IsOchreConcoction()
        {
            var c = new Concoction("id", "x", knockbackPct: 1f);
            Assert.AreEqual("Ochre Concoction", ConcoctionColor.DefaultName(c));
        }

        [Test]
        public void DefaultName_SpreadMaxed_IsOrchidConcoction()
        {
            var c = new Concoction("id", "x", spreadPct: 1f);
            Assert.AreEqual("Orchid Concoction", ConcoctionColor.DefaultName(c));
        }

        [Test]
        public void DefaultName_SizeMaxed_IsIndigoConcoction()
        {
            var c = new Concoction("id", "x", sizePct: 1f);
            Assert.AreEqual("Indigo Concoction", ConcoctionColor.DefaultName(c));
        }

        // --- everything maxed: hue-opposed, but not opposed ENOUGH to hit
        // the "Black Bile" special case -----------------------------------

        [Test]
        public void DefaultName_AllLeversMaxed_IsDarkAmethystConcoction_NotBlackBile()
        {
            // All five anchors (350/245/38/215/300 deg) at full weight
            // (w=1 each) sum to a resultant vector of magnitude ~2.165 out
            // of a max possible 5, i.e. dominance = |sum|/weightSum ~= 0.433.
            // DefaultName's "Black Bile" special case requires dominance <
            // 0.15 AND avgLevel > 0.7 — 0.433 does not clear that bar, so
            // this recipe reads as a (desaturated-in-COLOUR-but-not-enough-
            // to-rename) potent mix instead of the sludge special case.
            // resultant angle atan2(sum) ~= 298.4 deg, which falls in the
            // PigmentName 270-300 "Amethyst" band, and avgLevel == 1.0 is
            // above the 0.65 Dark-prefix threshold.
            //
            // If a future anchor-hue retune changes this, that's a legible
            // signal the hue geometry moved — recompute and update this
            // assertion deliberately, don't just widen it to Contains().
            var c = new Concoction("id", "x", 1f, 1f, 1f, 1f, 1f);
            Assert.AreEqual("Dark Amethyst Concoction", ConcoctionColor.DefaultName(c));
        }

        // --- pigment band coverage -------------------------------------------

        [TestCase(0f, "Madder")]
        [TestCase(10f, "Madder")]
        [TestCase(50f, "Ochre")]
        [TestCase(215f, "Prussian")]
        [TestCase(245f, "Indigo")]
        [TestCase(300f, "Orchid")]
        [TestCase(335f, "Rose Madder")]
        public void PigmentName_CoversTheFullWheel(float hueDeg, string expected)
        {
            Assert.AreEqual(expected, ConcoctionColor.PigmentName(hueDeg));
        }
    }
}
