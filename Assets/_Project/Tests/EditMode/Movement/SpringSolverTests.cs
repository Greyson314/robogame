using NUnit.Framework;
using Robogame.Movement;

namespace Robogame.Tests.EditMode.Movement
{
    /// <summary>
    /// Pure-math gate for <see cref="SpringSolver"/> — the reusable spring
    /// primitive shared by HoverBladeBlock + SpringBlock (session 104). No
    /// GameObject needed; these pin the formula + the load-bearing ≥0 clamp.
    /// </summary>
    public sealed class SpringSolverTests
    {
        [Test]
        public void HookeDamped_BelowTarget_NoVelocity_ReturnsHookeForce()
        {
            // stiffness 100, target 2, displacement 0.5, no velocity →
            // 100 * (2 - 0.5) = 150.
            float f = SpringSolver.HookeDamped(stiffness: 100f, damping: 10f, displacement: 0.5f, target: 2f, velocity: 0f);
            Assert.AreEqual(150f, f, 1e-3f);
        }

        [Test]
        public void HookeDamped_AtOrAboveTarget_ReturnsZero()
        {
            // displacement == target → spring term 0, no velocity → 0.
            Assert.AreEqual(0f, SpringSolver.HookeDamped(100f, 10f, 2f, 2f, 0f), 1e-3f);
            // displacement past target → would be negative, clamped to 0
            // (a spring pushes, never pulls).
            Assert.AreEqual(0f, SpringSolver.HookeDamped(100f, 10f, 3f, 2f, 0f), 1e-3f);
        }

        [Test]
        public void HookeDamped_DampingSubtractsProportionalToVelocity()
        {
            // 100 * (2 - 0.5) - 10 * 5 = 150 - 50 = 100.
            float f = SpringSolver.HookeDamped(100f, 10f, 0.5f, 2f, 5f);
            Assert.AreEqual(100f, f, 1e-3f);
        }

        [Test]
        public void HookeDamped_OverdampedClimb_ClampsToZero()
        {
            // Strong outward velocity drives spring - damping negative →
            // clamped to 0 so a fast-climbing body isn't decelerated below 0
            // (which would read as suction).
            float f = SpringSolver.HookeDamped(100f, 50f, 1.5f, 2f, 100f);
            Assert.AreEqual(0f, f, 1e-3f);
        }

        [Test]
        public void ResolveImpulse_ConfigValueZero_UsesDefault()
        {
            Assert.AreEqual(14f, SpringSolver.ResolveImpulse(configValue: 0f, defaultImpulse: 14f), 1e-4f);
        }

        [Test]
        public void ResolveImpulse_ConfigValuePositive_UsesConfig()
        {
            Assert.AreEqual(30f, SpringSolver.ResolveImpulse(configValue: 30f, defaultImpulse: 14f), 1e-4f);
        }
    }
}
