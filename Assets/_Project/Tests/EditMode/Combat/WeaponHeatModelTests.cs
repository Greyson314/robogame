using NUnit.Framework;
using Robogame.Combat;

namespace Robogame.Tests.EditMode.Combat
{
    /// <summary>
    /// EditMode tests for the planned <see cref="WeaponHeatModel"/> — a
    /// plain, deterministic C# class (no MonoBehaviour) driven entirely by
    /// explicit <see cref="WeaponHeatModel.Tick"/> calls: no <c>Time.time</c>,
    /// no coroutines, no per-instance hidden state beyond what the
    /// constructor + Tick sequence produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Interface assumption.</b> <see cref="WeaponHeatModel"/> does not
    /// exist yet. These tests assume this exact shape, taken from the spec
    /// verbatim; the constructor's positional order is the one open
    /// judgement call this file makes (the spec didn't pin it) — flag for
    /// reconciliation if the implementation lands with a different order or
    /// with an init-struct config instead:
    /// <code>
    /// new WeaponHeatModel(spinUpSeconds, spinDownSeconds, overheatSeconds,
    ///                     overheatCooldownSeconds, minFireRate, maxFireRate);
    /// void Tick(bool triggerHeld, float dt);
    /// float SpinUp01 { get; }
    /// float Heat01 { get; }
    /// bool IsOverheated { get; }
    /// float CurrentFireRate { get; }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Why these five.</b> Spin-up rewards commitment (hold to earn max
    /// rate), overheat punishes unbroken sustain (hold too long and you're
    /// locked out), and feathering — releasing before the heat cap — is the
    /// skill expression the whole model exists to create. Determinism
    /// matters because this is a candidate for server-authoritative replay
    /// (netcode.md contract): the same held/dt sequence must reproduce the
    /// same state with no hidden RNG or wall-clock dependency.
    /// </para>
    /// </remarks>
    public sealed class WeaponHeatModelTests
    {
        private static WeaponHeatModel MakeModel() =>
            new WeaponHeatModel(
                spinUpSeconds: 2f,
                spinDownSeconds: 1f,
                overheatSeconds: 5f,
                overheatCooldownSeconds: 3f,
                minFireRate: 2f,
                maxFireRate: 10f);

        [Test]
        public void Tick_FreshModel_FiresAtMinRate_ThenMaxRateAfterFullSpinUp()
        {
            // WHY: spin-up is the "commit to the rev" mechanic — tapping the
            // trigger should only ever earn minFireRate; maxFireRate is the
            // reward for holding through the full spinUpSeconds window. If a
            // fresh model already fired at max, spin-up wouldn't be rewarding
            // anything.
            WeaponHeatModel model = MakeModel();

            Assert.AreEqual(2f, model.CurrentFireRate, 1e-3f,
                "An untouched model must fire at minFireRate — no free spin-up.");

            model.Tick(triggerHeld: true, dt: 2f); // exactly spinUpSeconds

            Assert.AreEqual(1f, model.SpinUp01, 1e-3f, "A full spin-up window of holding must reach SpinUp01 == 1.");
            Assert.AreEqual(10f, model.CurrentFireRate, 1e-3f,
                "At SpinUp01 == 1 the model must fire at maxFireRate (lerp(min, max, 1) == max).");
        }

        [Test]
        public void Tick_HeldPastOverheatSeconds_LocksOut_ThenClearsAfterCooldown()
        {
            // WHY: overheat is the punishment side of sustained fire — hold
            // without a break and the weapon must go dark for
            // overheatCooldownSeconds, no matter how hard the player keeps
            // holding the trigger during the lockout. If holding through the
            // lockout re-armed or shortened it, "punish unbroken sustain"
            // degrades into "wait it out while still holding" — no actual
            // penalty for the greedy play.
            WeaponHeatModel model = MakeModel();

            model.Tick(triggerHeld: true, dt: 5f); // exactly overheatSeconds of continuous hold
            Assert.IsTrue(model.IsOverheated, "Heat01 reaching 1 must trip the lockout.");

            // Step through most of the cooldown window in small increments
            // (mirrors a real per-frame Update loop) while still holding —
            // it must stay locked the whole way.
            for (int i = 0; i < 58; i++) // 58 * 0.05s = 2.90s of the 3s cooldown
            {
                model.Tick(triggerHeld: true, dt: 0.05f);
                Assert.IsTrue(model.IsOverheated, $"Still inside the cooldown window at step {i} (t~{(i + 1) * 0.05f:F2}s).");
            }

            model.Tick(triggerHeld: true, dt: 0.3f); // crosses the 3.0s cooldown boundary

            Assert.IsFalse(model.IsOverheated, "Lockout must clear once overheatCooldownSeconds has fully elapsed.");
            Assert.AreEqual(0f, model.Heat01, 1e-3f, "Heat must reset to 0 the moment the lockout clears.");
        }

        [Test]
        public void Tick_FeatheringAtHalfDutyCycle_NeverOverheats()
        {
            // WHY: feathering (hold, release, hold, release, keeping each
            // hold under the overheat cap) is the skill expression this
            // model exists to reward. A player disciplined enough to never
            // fill Heat01 should never eat the lockout penalty, no matter
            // how many cycles they sustain it for.
            WeaponHeatModel model = MakeModel();

            for (int cycle = 0; cycle < 5; cycle++)
            {
                // Hold for half the overheat window — Heat01 should approach
                // ~0.5, never reach the 1.0 trip threshold.
                model.Tick(triggerHeld: true, dt: 2.5f);
                Assert.IsFalse(model.IsOverheated, $"Half-duty hold must never overheat (cycle {cycle}).");
                Assert.Less(model.Heat01, 1f, $"Heat must stay under the trip threshold (cycle {cycle}).");

                // Release for the same window — heat decays symmetrically
                // (over overheatSeconds) back toward 0, so the next hold
                // starts from a cool weapon.
                model.Tick(triggerHeld: false, dt: 2.5f);
                Assert.IsFalse(model.IsOverheated, $"Releasing must never itself trip the lockout (cycle {cycle}).");
            }

            Assert.Less(model.Heat01, 0.1f,
                "After feathering, the trailing release must have bled heat back down close to 0.");
        }

        [Test]
        public void Tick_ReleaseAfterFullSpinUp_DecaysLinearly_HalfwayCheckAtHalfSpinDownTime()
        {
            // WHY: spin-down is what makes spin-up a real commitment rather
            // than a free toggle — letting go of the trigger must cost the
            // player something proportional to spinDownSeconds, not snap
            // back to minFireRate instantly (which would make holding
            // pointless) or hold at maxFireRate forever (which would make
            // releasing pointless).
            WeaponHeatModel model = MakeModel();

            model.Tick(triggerHeld: true, dt: 2f); // fully spun up
            Assume.That(model.SpinUp01, Is.EqualTo(1f).Within(1e-3f), "test precondition: fully spun up");

            model.Tick(triggerHeld: false, dt: 0.5f); // half of spinDownSeconds (1s)
            Assert.AreEqual(0.5f, model.SpinUp01, 0.02f,
                "Halfway through the spinDownSeconds release window, SpinUp01 must be ~half decayed (linear decay).");

            model.Tick(triggerHeld: false, dt: 0.5f); // remaining half
            Assert.AreEqual(0f, model.SpinUp01, 1e-3f,
                "Fully releasing for spinDownSeconds must bottom SpinUp01 back out at 0.");
        }

        [Test]
        public void Tick_IdenticalInputSequence_ProducesIdenticalState_OnTwoInstances()
        {
            // WHY: this model is a replay-readiness candidate — a server
            // that resets state and re-runs the same held/dt sequence
            // (reconnect, lag-comp rewind, deterministic lockstep) must land
            // on exactly the same fire-rate / heat / overheat state as the
            // first run. Any hidden dependency on Time.time, instance
            // identity, or non-determinism would break that contract.
            WeaponHeatModel a = MakeModel();
            WeaponHeatModel b = MakeModel();

            (bool held, float dt)[] sequence =
            {
                (true, 0.3f), (true, 0.3f), (false, 0.1f), (true, 1.0f),
                (true, 5.0f),                 // trips overheat mid-sequence
                (true, 1.5f), (false, 1.5f),  // held through part of lockout, then released
                (false, 2.0f),
            };

            foreach ((bool held, float dt) in sequence)
            {
                a.Tick(held, dt);
                b.Tick(held, dt);
            }

            Assert.AreEqual(a.SpinUp01, b.SpinUp01, 1e-6f, "SpinUp01 must match exactly across identical sequences.");
            Assert.AreEqual(a.Heat01, b.Heat01, 1e-6f, "Heat01 must match exactly across identical sequences.");
            Assert.AreEqual(a.IsOverheated, b.IsOverheated, "IsOverheated must match exactly across identical sequences.");
            Assert.AreEqual(a.CurrentFireRate, b.CurrentFireRate, 1e-6f, "CurrentFireRate must match exactly across identical sequences.");
        }
    }
}
