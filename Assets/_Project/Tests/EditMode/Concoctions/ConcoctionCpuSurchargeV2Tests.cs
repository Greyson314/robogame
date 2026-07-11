using NUnit.Framework;
using Robogame.Block;

namespace Robogame.Tests.EditMode.Concoctions
{
    /// <summary>
    /// Session-141 tests for the five-lever <see cref="Concoction.CpuSurcharge"/>
    /// (v2, <c>SurchargeFactorPerSliderSum</c> = 0.3) and the two new levers'
    /// Validate/Clone coverage. The calibration tests are the load-bearing
    /// part: 0.3 was chosen specifically to reproduce the OLD v1 3-lever
    /// formula's (factor 0.5) two anchor values, so a garage that priced a
    /// recipe before this session sees the same number after it, even though
    /// five sliders now feed the sum instead of three.
    /// </summary>
    public sealed class ConcoctionCpuSurchargeV2Tests
    {
        [Test]
        public void CpuSurcharge_AllNeutralFiveLevers_MatchesV1AllNeutralAnchor()
        {
            // v2: sliderSum = 5 * 0.5 = 2.5; 2.5 * 0.3 = 0.75x base.
            // v1 (pre-141, 3 levers, factor 0.5): sliderSum = 3 * 0.5 = 1.5;
            // 1.5 * 0.5 = 0.75x base too. Same price, different lever count.
            var c = new Concoction("id", "neutral"); // all five default to 0.5
            Assert.AreEqual(30, c.CpuSurcharge(40), "All-neutral must still price at 0.75x base cost.");
        }

        [Test]
        public void CpuSurcharge_AllMaxFiveLevers_MatchesV1AllMaxAnchor()
        {
            // v2: sliderSum = 5 * 1.0 = 5; 5 * 0.3 = 1.5x base.
            // v1 (pre-141, 3 levers, factor 0.5): sliderSum = 3; 3 * 0.5 = 1.5x
            // base — the same 60 CPU at base 40 asserted by the pre-141
            // ConcoctionTests.CpuSurcharge_AllMax test. This is the deliberate
            // pricing-compat contract from docs/decisions/0005, not a
            // coincidence — if it breaks, the surcharge factor drifted.
            var c = new Concoction("id", "max", 1f, 1f, 1f, 1f, 1f);
            Assert.AreEqual(60, c.CpuSurcharge(40), "All-max must still price at 1.5x base cost.");
        }

        [Test]
        public void CpuSurcharge_RaisingSpeedOrSpread_RaisesCostLikeTheOtherThreeLevers()
        {
            // The two new levers must feed the same sliderSum, not be
            // decorative sliders that don't actually cost anything.
            var baseline = new Concoction("a", "lo");
            var speedUp = new Concoction("b", "hi-speed", speedPct: 1f);
            var spreadUp = new Concoction("c", "hi-spread", spreadPct: 1f);

            Assert.Greater(speedUp.CpuSurcharge(40), baseline.CpuSurcharge(40),
                "Raising SpeedPct alone must raise the surcharge.");
            Assert.Greater(spreadUp.CpuSurcharge(40), baseline.CpuSurcharge(40),
                "Raising SpreadPct alone must raise the surcharge.");
        }

        // --- Speed/Spread ride the same shared Multiplier curve -------------

        [Test]
        public void SpeedAndSpreadMultiplier_ShareTheSamePiecewiseCurveAsTheOtherLevers()
        {
            var c = new Concoction("id", "x", speedPct: 0f, spreadPct: 1f);
            Assert.AreEqual(Concoction.MinMultiplier, c.SpeedMultiplier, 1e-4f);
            Assert.AreEqual(Concoction.MaxMultiplier, c.SpreadMultiplier, 1e-4f);

            var neutral = new Concoction("id2", "y");
            Assert.AreEqual(1f, neutral.SpeedMultiplier, 1e-4f, "Default 0.5 speed must be baseline 1x.");
            Assert.AreEqual(1f, neutral.SpreadMultiplier, 1e-4f, "Default 0.5 spread must be baseline 1x.");
        }

        // --- Validate / Clone must not have missed the two new fields -------

        [Test]
        public void Validate_ClampsSpeedAndSpreadToUnitRange()
        {
            var c = new Concoction("id", "bad", speedPct: -3f, spreadPct: 9f);
            c.Validate();
            Assert.AreEqual(0f, c.SpeedPct, 1e-4f);
            Assert.AreEqual(1f, c.SpreadPct, 1e-4f);
        }

        [Test]
        public void Clone_CopiesSpeedAndSpread_AsIndependentValues()
        {
            var original = new Concoction("id", "src", speedPct: 0.9f, spreadPct: 0.1f);
            Concoction clone = original.Clone();

            Assert.AreEqual(0.9f, clone.SpeedPct, 1e-4f);
            Assert.AreEqual(0.1f, clone.SpreadPct, 1e-4f);

            // Independence: mutating the clone must not touch the source —
            // the whole point of Clone() feeding an editable Lab field.
            clone.SpeedPct = 0f;
            Assert.AreEqual(0.9f, original.SpeedPct, 1e-4f, "Clone must be a deep copy, not a shared reference.");
        }
    }
}
