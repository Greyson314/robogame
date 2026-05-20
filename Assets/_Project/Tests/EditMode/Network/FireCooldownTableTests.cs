// Assumed type signature for FireCooldownTable
// (lives in Assets/_Project/Scripts/Network/Robot/FireCooldownTable.cs,
//  namespace Robogame.Network.Robot):
//
//   public sealed class FireCooldownTable
//   {
//       public int RejectedCount { get; }
//       public bool TryAccept(Vector3Int weaponPos, float now, float cooldown);
//       public void Reset();   // optional; used by [SetUp] if the type is stateful
//   }
//
// TryAccept returns true  → fire accepted, lastFireTime[weaponPos] updated.
// TryAccept returns false → fire rejected, RejectedCount incremented.
// Independent per-Vector3Int entry — no shared cooldown window across positions.

using NUnit.Framework;
using Robogame.Network.Robot;
using UnityEngine;

namespace Robogame.Tests.EditMode.Network
{
    /// <summary>
    /// Encodes the Phase-2 server-validation invariants for per-weapon cooldown
    /// enforcement in NetworkRobotCombat. These tests target FireCooldownTable
    /// directly — pure C# logic, no NGO harness required.
    ///
    /// Invariants covered:
    ///   1. A fire within the cooldown window of the same weapon is rejected and
    ///      increments RejectedCount.
    ///   2. A fire outside the cooldown window of the same weapon is accepted
    ///      without incrementing RejectedCount.
    ///   3. Distinct weapon positions cooldown independently; firing one does not
    ///      gate the other.
    ///   4. RejectedCount tracks only rejections, not total calls.
    /// </summary>
    public sealed class FireCooldownTableTests
    {
        private FireCooldownTable _table;

        [SetUp]
        public void SetUp()
        {
            _table = new FireCooldownTable();
        }

        // ------------------------------------------------------------------ //
        // Invariant 1 — same weapon, second call inside cooldown → rejected   //
        // ------------------------------------------------------------------ //

        [Test]
        public void TryAccept_SamePosition_SecondCallWithinCooldown_ReturnsFalse()
        {
            var pos = new Vector3Int(0, 0, 0);
            const float cooldown = 1.0f;
            const float firstFire = 10.0f;
            const float secondFire = firstFire + 0.5f; // 0.5 s < 1.0 s cooldown

            _table.TryAccept(pos, firstFire, cooldown);
            bool accepted = _table.TryAccept(pos, secondFire, cooldown);

            Assert.IsFalse(accepted,
                "A fire arriving before the cooldown expires must be rejected.");
        }

        [Test]
        public void TryAccept_SamePosition_SecondCallWithinCooldown_IncrementsRejectedCount()
        {
            var pos = new Vector3Int(1, 0, 0);
            const float cooldown = 2.0f;
            const float firstFire = 5.0f;
            const float secondFire = firstFire + 1.9f; // just inside window

            _table.TryAccept(pos, firstFire, cooldown);
            _table.TryAccept(pos, secondFire, cooldown);

            Assert.AreEqual(1, _table.RejectedCount,
                "Exactly one rejection must be recorded for the blocked second fire.");
        }

        // ------------------------------------------------------------------ //
        // Invariant 2 — same weapon, second call outside cooldown → accepted  //
        // ------------------------------------------------------------------ //

        [Test]
        public void TryAccept_SamePosition_SecondCallOutsideCooldown_ReturnsTrue()
        {
            var pos = new Vector3Int(0, 1, 0);
            const float cooldown = 0.5f;
            const float firstFire = 20.0f;
            const float secondFire = firstFire + 0.5f; // exactly at boundary — must be accepted

            _table.TryAccept(pos, firstFire, cooldown);
            bool accepted = _table.TryAccept(pos, secondFire, cooldown);

            Assert.IsTrue(accepted,
                "A fire at or after the cooldown expiry must be accepted.");
        }

        [Test]
        public void TryAccept_SamePosition_SecondCallOutsideCooldown_DoesNotIncrementRejectedCount()
        {
            var pos = new Vector3Int(0, 2, 0);
            const float cooldown = 0.25f;
            const float firstFire = 100.0f;
            const float secondFire = firstFire + 1.0f; // well outside window

            _table.TryAccept(pos, firstFire, cooldown);
            _table.TryAccept(pos, secondFire, cooldown);

            Assert.AreEqual(0, _table.RejectedCount,
                "No rejection must be recorded when both fires are outside each other's cooldown.");
        }

        // ------------------------------------------------------------------ //
        // Invariant 3 — distinct positions cooldown independently             //
        // ------------------------------------------------------------------ //

        [Test]
        public void TryAccept_DifferentPositions_WithinSharedTimeWindow_BothAccepted()
        {
            var posA = new Vector3Int(0, 0, 0);
            var posB = new Vector3Int(1, 0, 0);
            const float cooldown = 2.0f;
            const float now = 50.0f;

            // Fire both weapons at the same timestamp.
            bool acceptedA = _table.TryAccept(posA, now, cooldown);
            bool acceptedB = _table.TryAccept(posB, now, cooldown);

            Assert.IsTrue(acceptedA, "First fire on weapon A must be accepted.");
            Assert.IsTrue(acceptedB,
                "First fire on weapon B must be accepted independently of weapon A.");
            Assert.AreEqual(0, _table.RejectedCount,
                "Firing two different weapons must not trigger any rejection.");
        }

        [Test]
        public void TryAccept_DifferentPositions_SecondFireWithinWindowOfA_OnlyAIsGated()
        {
            var posA = new Vector3Int(2, 0, 0);
            var posB = new Vector3Int(3, 0, 0);
            const float cooldown = 1.0f;
            const float t0 = 0.0f;
            const float t1 = 0.3f; // inside cooldown for A

            _table.TryAccept(posA, t0, cooldown);
            _table.TryAccept(posB, t0, cooldown);

            bool aAccepted = _table.TryAccept(posA, t1, cooldown); // should reject
            bool bAccepted = _table.TryAccept(posB, t1, cooldown); // should reject too — B also fired at t0

            Assert.IsFalse(aAccepted, "Re-firing weapon A inside its cooldown must be rejected.");
            Assert.IsFalse(bAccepted, "Re-firing weapon B inside its own cooldown must be rejected.");
            Assert.AreEqual(2, _table.RejectedCount,
                "Each weapon's own cooldown is enforced independently — two rejections total.");
        }

        // ------------------------------------------------------------------ //
        // Invariant 4 — RejectedCount tracks rejections only, not all calls   //
        // ------------------------------------------------------------------ //

        [Test]
        public void RejectedCount_OnlyCountsRejections_NotAcceptances()
        {
            var pos = new Vector3Int(0, 0, 1);
            const float cooldown = 0.1f;

            // Three accepted fires spaced well apart.
            _table.TryAccept(pos, 0.0f, cooldown);
            _table.TryAccept(pos, 0.2f, cooldown);
            _table.TryAccept(pos, 0.4f, cooldown);

            // One rejection in the middle.
            _table.TryAccept(pos, 0.41f, cooldown); // within 0.1 s of 0.4

            Assert.AreEqual(1, _table.RejectedCount,
                "RejectedCount must reflect exactly the blocked calls, not accepted ones.");
        }

        [Test]
        public void RejectedCount_MultipleRejections_AccumulatesCorrectly()
        {
            var pos = new Vector3Int(0, 0, 2);
            const float cooldown = 1.0f;
            const float fireTime = 0.0f;

            _table.TryAccept(pos, fireTime, cooldown);

            // Three rapid follow-up calls all within cooldown.
            _table.TryAccept(pos, 0.1f, cooldown);
            _table.TryAccept(pos, 0.2f, cooldown);
            _table.TryAccept(pos, 0.3f, cooldown);

            Assert.AreEqual(3, _table.RejectedCount,
                "Each blocked rapid-fire must increment the counter once.");
        }
    }
}
