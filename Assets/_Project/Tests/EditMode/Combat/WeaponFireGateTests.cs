using NUnit.Framework;
using Robogame.Combat;
using UnityEngine;

namespace Robogame.Tests.EditMode.Combat
{
    /// <summary>
    /// Pins the held-fire cooldown contract (ADR-0003 phase D). The four
    /// held-fire weapons (SMG / cannon / mortar / bomb) all route through
    /// <see cref="WeaponFireGate"/>; the invariant that matters is "a held
    /// trigger fires at most once per interval, and the cooldown is armed on
    /// the accepting tick — not bypassable by toggling the button." These run
    /// with a null ammo pool, exercising the cooldown path in isolation (the
    /// ammo + dry-click path needs a live chassis + AudioRouter and is covered
    /// by play-mode / manual verification).
    /// </summary>
    public sealed class WeaponFireGateTests
    {
        private const float Interval = 0.5f;

        private static bool Try(ref WeaponFireGate gate, bool held, float now) =>
            gate.TryFire(held, now, Interval, ammo: null, blockId: null,
                         emptyClickPos: Vector3.zero, emptyClickThrottle: 0.2f);

        [Test]
        public void FireNotHeld_NeverFires()
        {
            var gate = new WeaponFireGate();
            Assert.IsFalse(Try(ref gate, held: false, now: 0f));
            Assert.IsFalse(Try(ref gate, held: false, now: 10f));
        }

        [Test]
        public void FirstHeldTick_Fires()
        {
            var gate = new WeaponFireGate();
            Assert.IsTrue(Try(ref gate, held: true, now: 0f),
                "a fresh gate has no cooldown — the first held tick fires");
        }

        [Test]
        public void HeldThroughCooldown_DoesNotRefire_UntilIntervalElapsed()
        {
            var gate = new WeaponFireGate();
            Assert.IsTrue(Try(ref gate, held: true, now: 0f));
            // Still inside the interval → suppressed.
            Assert.IsFalse(Try(ref gate, held: true, now: Interval - 0.01f));
            // Exactly at the interval → fires again.
            Assert.IsTrue(Try(ref gate, held: true, now: Interval));
        }

        [Test]
        public void ReleasingAndRepressing_DoesNotBypassCooldown()
        {
            // The cooldown is wall-clock, not edge-triggered — letting go and
            // re-pressing inside the interval must NOT fire (no full-auto-by-
            // mashing exploit).
            var gate = new WeaponFireGate();
            Assert.IsTrue(Try(ref gate, held: true, now: 0f));
            Assert.IsFalse(Try(ref gate, held: false, now: 0.1f));
            Assert.IsFalse(Try(ref gate, held: true, now: 0.2f));
            Assert.IsTrue(Try(ref gate, held: true, now: Interval + 0.001f));
        }

        [Test]
        public void SustainedHold_FiresOncePerInterval()
        {
            var gate = new WeaponFireGate();
            int shots = 0;
            // Sample at 100 Hz for 1.05 s → expect ceil(1.05 / 0.5) = 3 shots
            // (t=0, t=0.5, t=1.0).
            for (int i = 0; i <= 105; i++)
            {
                float now = i * 0.01f;
                if (Try(ref gate, held: true, now)) shots++;
            }
            Assert.AreEqual(3, shots);
        }
    }
}
