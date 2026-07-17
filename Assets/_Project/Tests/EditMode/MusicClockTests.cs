using System;
using NUnit.Framework;
using Robogame.Core;

namespace Robogame.Tests.EditMode
{
    /// <summary>
    /// MusicClock estimates the constant offset between two same-rate
    /// audio clocks (FMOD DSP seconds -> Unity AudioSettings.dspTime)
    /// sampled from the main thread. The beat grid a caller builds from
    /// ToTarget() is only musically stable if this estimate resists the
    /// ±0.022 s per-sample staircase jitter and doesn't get yanked by a
    /// single bad read. These tests encode that contract, not the
    /// internal averaging/filtering strategy.
    /// </summary>
    public sealed class MusicClockTests
    {
        private const double TrueOffset = 41.43; // target - source, seconds

        [Test]
        public void Ready_BeforeWarmupSamples_IsFalse()
        {
            var clock = new MusicClock();
            for (int i = 0; i < MusicClock.WarmupSamples - 1; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + TrueOffset);
                Assert.IsFalse(clock.Ready, $"Should not be ready after {i + 1} sample(s).");
            }
        }

        [Test]
        public void Ready_AfterExactlyWarmupSamples_IsTrue()
        {
            var clock = new MusicClock();
            for (int i = 0; i < MusicClock.WarmupSamples; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + TrueOffset);
            }
            Assert.IsTrue(clock.Ready);
        }

        [Test]
        public void OffsetSeconds_BeforeFirstSample_IsZero()
        {
            var clock = new MusicClock();
            Assert.AreEqual(0.0, clock.OffsetSeconds, 1e-12);
        }

        [Test]
        public void OffsetSeconds_ConvergesToConstantOffset_AfterWarmup()
        {
            var clock = new MusicClock();
            for (int i = 0; i < MusicClock.WarmupSamples + 4; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + TrueOffset);
            }
            Assert.IsTrue(clock.Ready);
            Assert.AreEqual(TrueOffset, clock.OffsetSeconds, 1e-6);
        }

        [Test]
        public void OffsetSeconds_UnderUniformJitter_StaysWithinToleranceAndGridIsStable()
        {
            // Fixed seed for reproducibility: jitter models the ±one-mixer-block
            // (0.022 s) staircase read, here sampled as ±0.011 s uniform noise
            // on the target clock alone.
            var rng = new Random(1234);
            var clock = new MusicClock();

            double? previousOffset = null;
            for (int i = 0; i < 200; i++)
            {
                double source = i * 0.021;
                double jitter = (rng.NextDouble() * 2.0 - 1.0) * 0.011;
                double target = source + TrueOffset + jitter;
                clock.AddSample(source, target);

                if (!clock.Ready)
                {
                    continue;
                }

                Assert.LessOrEqual(Math.Abs(clock.OffsetSeconds - TrueOffset), 0.005,
                    $"Offset estimate drifted from the true mean at sample {i}.");

                if (previousOffset.HasValue)
                {
                    Assert.LessOrEqual(Math.Abs(clock.OffsetSeconds - previousOffset.Value), 0.005,
                        $"Beat grid moved more than 5 ms in one sample at index {i} — would audibly wobble.");
                }
                previousOffset = clock.OffsetSeconds;
            }

            Assert.IsTrue(previousOffset.HasValue, "Loop never reached Ready — check WarmupSamples vs sample count.");
        }

        [Test]
        public void OffsetSeconds_SingleOutlierSample_DoesNotYankEstimateAfterConvergence()
        {
            var clock = new MusicClock();
            for (int i = 0; i < MusicClock.WarmupSamples + 20; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + TrueOffset);
            }
            double convergedOffset = clock.OffsetSeconds;

            // One wildly bad read (e.g. a stalled main-thread frame) must not
            // yank the beat grid — a single stinger scheduled off a spike
            // would be audibly wrong.
            int n = MusicClock.WarmupSamples + 20;
            clock.AddSample(n * 0.021, n * 0.021 + TrueOffset + 0.5);

            Assert.Less(Math.Abs(clock.OffsetSeconds - convergedOffset), 0.01);
        }

        [Test]
        public void ToTarget_ReturnsSourcePlusCurrentOffset()
        {
            var clock = new MusicClock();
            for (int i = 0; i < MusicClock.WarmupSamples + 4; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + TrueOffset);
            }

            double source = 12.34;
            Assert.AreEqual(source + clock.OffsetSeconds, clock.ToTarget(source), 1e-12);
        }

        [Test]
        public void Reset_ReturnsToNotReadyAndZeroOffset_AndRewarmsCorrectly()
        {
            var clock = new MusicClock();
            for (int i = 0; i < MusicClock.WarmupSamples + 4; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + TrueOffset);
            }
            Assert.IsTrue(clock.Ready);

            clock.Reset();
            Assert.IsFalse(clock.Ready);
            Assert.AreEqual(0.0, clock.OffsetSeconds, 1e-12);

            // Different constant offset after reset — must not be
            // contaminated by pre-reset samples.
            const double newOffset = 7.0;
            for (int i = 0; i < MusicClock.WarmupSamples + 4; i++)
            {
                clock.AddSample(i * 0.021, i * 0.021 + newOffset);
            }
            Assert.IsTrue(clock.Ready);
            Assert.AreEqual(newOffset, clock.OffsetSeconds, 1e-6);
        }
    }
}
