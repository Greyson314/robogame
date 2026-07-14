using NUnit.Framework;
using Robogame.Core;

namespace Robogame.Tests.EditMode
{
    /// <summary>
    /// Beat-grid quantisation and stinger tiering (ADR-0006). These
    /// encode the musical contract: stingers land ON grid slots derived
    /// from the track's DSP start time — never "roughly now" — and a
    /// hit window's weight decides its tier. If MusicMath changes and
    /// stingers start landing off the off-beat or chip damage starts
    /// triggering fanfares, these fail.
    /// </summary>
    public sealed class MusicMathTests
    {
        private const double Spb = 0.6;   // 100 BPM

        [Test]
        public void NextSlot_LandsOnOffbeatGrid()
        {
            // Track started at dsp=100. Off-beat 8ths sit at 100.3, 100.9, 101.5…
            double slot = MusicMath.NextSlot(
                startDsp: 100.0, nowDsp: 100.95, secondsPerBeat: Spb,
                subdivisionBeats: 1.0, offsetBeats: 0.5, minLeadSeconds: 0.0);
            Assert.AreEqual(101.5, slot, 1e-9);
        }

        [Test]
        public void NextSlot_MinLeadPushesToFollowingSlot()
        {
            // 100.9 is the next off-beat, but a 0.3 s lead makes it
            // unreachable from t=100.85 — scheduling must skip to 101.5,
            // not squeeze inside the DSP buffer.
            double slot = MusicMath.NextSlot(
                100.0, 100.85, Spb, 1.0, 0.5, 0.3);
            Assert.AreEqual(101.5, slot, 1e-9);
        }

        [Test]
        public void NextSlot_ExactlyOnSlotWithZeroLeadReturnsThatSlot()
        {
            double slot = MusicMath.NextSlot(100.0, 100.9, Spb, 1.0, 0.5, 0.0);
            Assert.AreEqual(100.9, slot, 1e-9);
        }

        [Test]
        public void NextSlot_BeforeTrackStartReturnsFirstGridPoint()
        {
            // Track scheduled in the future (PlayScheduled lead): the
            // first legal slot is the grid origin, never earlier.
            double slot = MusicMath.NextSlot(100.0, 99.0, Spb, 1.0, 0.5, 0.0);
            Assert.AreEqual(100.3, slot, 1e-9);
        }

        [Test]
        public void NextSlot_OnBeatGridForKills()
        {
            double slot = MusicMath.NextSlot(100.0, 100.61, Spb, 1.0, 0.0, 0.0);
            Assert.AreEqual(101.2, slot, 1e-9);
        }

        [Test]
        public void NextSlot_GridSurvivesLongPlaytime()
        {
            // 10 minutes in, slots must still be exact multiples — the
            // grid is arithmetic on start time, not accumulated floats.
            double slot = MusicMath.NextSlot(100.0, 700.05, Spb, 1.0, 0.5, 0.0);
            double beatsFromOrigin = (slot - 100.3) / Spb;
            Assert.AreEqual(beatsFromOrigin, System.Math.Round(beatsFromOrigin), 1e-9);
            Assert.GreaterOrEqual(slot, 700.05);
            Assert.Less(slot - 700.05, Spb + 1e-9);
        }

        [Test]
        public void TierFor_ChipDamageIsNote()
        {
            Assert.AreEqual(MusicMath.StingerTier.Note,
                MusicMath.TierFor(MusicMath.FlourishDamageThreshold - 1f, killed: false));
        }

        [Test]
        public void TierFor_HeavyWindowIsFlourish()
        {
            Assert.AreEqual(MusicMath.StingerTier.Flourish,
                MusicMath.TierFor(MusicMath.FlourishDamageThreshold, killed: false));
        }

        [Test]
        public void TierFor_KillIsAlwaysPhrase_EvenAtChipDamage()
        {
            Assert.AreEqual(MusicMath.StingerTier.Phrase,
                MusicMath.TierFor(1f, killed: true));
        }
    }
}
