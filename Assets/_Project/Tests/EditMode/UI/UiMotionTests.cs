using NUnit.Framework;
using Robogame.Core;

namespace Robogame.Tests.EditMode.UI
{
    /// <summary>
    /// Pure-math gate for <see cref="UiMotion.Evaluate"/> — the shared 65-sample
    /// easing LUT every <c>UiTween</c> channel reads (docs/research/
    /// ui-design-handoff-motion.md). No GameObject needed. Pins the three
    /// properties UiMotion's own doc comments promise: exact endpoints (no
    /// residue baked into the LUT's first/last sample — this is what lets a
    /// completed tween land exactly on its target with zero further error),
    /// monotonic 0→1 for every ease ("nothing bounces" — paper is calm), and
    /// clamping of out-of-range t so a caller racing the driver's own
    /// duration math never indexes off the curve.
    /// </summary>
    public sealed class UiMotionTests
    {
        private static readonly UiMotion.Ease[] AllEases =
        {
            UiMotion.Ease.Linear, UiMotion.Ease.Settle, UiMotion.Ease.Draw, UiMotion.Ease.Page,
        };

        [Test]
        public void Evaluate_AtZero_ReturnsExactZero_ForEveryEase()
        {
            foreach (UiMotion.Ease ease in AllEases)
            {
                Assert.AreEqual(0f, UiMotion.Evaluate(ease, 0f), 0f,
                    $"{ease}: t=0 must land exactly on the curve's start sample — any residue here " +
                    "means a freshly-scheduled tween would read one LUT-step above its from-state on frame 1.");
            }
        }

        [Test]
        public void Evaluate_AtOne_ReturnsExactOne_ForEveryEase()
        {
            foreach (UiMotion.Ease ease in AllEases)
            {
                Assert.AreEqual(1f, UiMotion.Evaluate(ease, 1f), 0f,
                    $"{ease}: t=1 must land exactly on the curve's end sample — this is the guarantee " +
                    "UiTween relies on for a naturally-completed tween to equal its requested target exactly.");
            }
        }

        [Test]
        public void Evaluate_BelowZero_ClampsToTheZeroEndpoint()
        {
            foreach (UiMotion.Ease ease in AllEases)
            {
                float clamped = UiMotion.Evaluate(ease, -5f);
                Assert.AreEqual(UiMotion.Evaluate(ease, 0f), clamped, 0f,
                    $"{ease}: t=-5 must clamp to the same sample as t=0. Without the Clamp01 guard this " +
                    "indexes the LUT with a negative index instead (an out-of-range read, not a wrong value).");
            }
        }

        [Test]
        public void Evaluate_AboveOne_ClampsToTheOneEndpoint()
        {
            foreach (UiMotion.Ease ease in AllEases)
            {
                float clamped = UiMotion.Evaluate(ease, 5f);
                Assert.AreEqual(UiMotion.Evaluate(ease, 1f), clamped, 0f,
                    $"{ease}: t=5 must clamp to the same sample as t=1, not extrapolate past the curve's end.");
            }
        }

        /// <summary>
        /// Samples each ease densely across its domain and checks two
        /// things at once: the value never leaves [0,1] (no overshoot — the
        /// handoff explicitly bans bounce), and it never drops below the
        /// previous sample (monotonic non-decreasing — a tween's value must
        /// never visibly reverse mid-flight even though it's driven purely
        /// by elapsed time increasing).
        /// </summary>
        [Test]
        public void Evaluate_IsMonotonicNonDecreasing_AndStaysWithinZeroOne_AcrossTheFullRange()
        {
            const int samples = 200;
            const float epsilon = 1e-5f;

            foreach (UiMotion.Ease ease in AllEases)
            {
                float prev = UiMotion.Evaluate(ease, 0f);
                for (int i = 1; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    float v = UiMotion.Evaluate(ease, t);

                    Assert.GreaterOrEqual(v, -epsilon,
                        $"{ease} at t={t:F3}: value {v} dipped below 0 — the motion handoff bans overshoot ('nothing bounces').");
                    Assert.LessOrEqual(v, 1f + epsilon,
                        $"{ease} at t={t:F3}: value {v} exceeded 1 — the motion handoff bans overshoot ('nothing bounces').");
                    Assert.GreaterOrEqual(v, prev - epsilon,
                        $"{ease} at t={t:F3}: value {v} dropped below the previous sample {prev} — " +
                        "a non-monotonic curve would make a tweened value visibly reverse mid-flight.");

                    prev = v;
                }
            }
        }
    }
}
