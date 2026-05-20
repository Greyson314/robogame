using NUnit.Framework;
using Robogame.Network.Robot;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.EditMode.Network
{
    /// <summary>
    /// Phase-4 hardening tests (NETCODE_PLAN §15 Phase 4): the
    /// <see cref="DestroyedBlockLog"/> late-join scaffold and the
    /// <see cref="NetworkRobotCombat"/> aim-bounds validator. The
    /// network-level integration tests (BlockHitBatch dedup under load,
    /// orphan-RPC convergence) require a real NGO session and are the
    /// MPPM qualitative gate, not an automated assertion — see the §15
    /// Phase-4 exit criterion. These EditMode tests pin the
    /// non-network-routed properties that should never regress silently.
    /// </summary>
    public sealed class Phase4HardeningTests
    {
        // -----------------------------------------------------------------
        // DestroyedBlockLog (NETCODE_PLAN §7c late-join scaffold)
        // -----------------------------------------------------------------

        [Test]
        public void DestroyedBlockLog_RecordAndCopyTo_RoundTripsInOrder()
        {
            var log = new DestroyedBlockLog();
            log.Record(7);
            log.Record(42);
            log.Record(13);

            Assert.AreEqual(3, log.Count);

            var buf = new ushort[3];
            int n = log.CopyTo(buf);
            Assert.AreEqual(3, n);
            Assert.AreEqual(7, buf[0]);
            Assert.AreEqual(42, buf[1]);
            Assert.AreEqual(13, buf[2]);
        }

        [Test]
        public void DestroyedBlockLog_ToArray_AllocatesExactSnapshot()
        {
            var log = new DestroyedBlockLog();
            log.Record(1);
            log.Record(2);

            ushort[] arr = log.ToArray();
            Assert.AreEqual(2, arr.Length);
            Assert.AreEqual(1, arr[0]);
            Assert.AreEqual(2, arr[1]);
        }

        [Test]
        public void DestroyedBlockLog_Overflow_LogsWarningOnceAndDropsRest()
        {
            var log = new DestroyedBlockLog();
            for (int i = 0; i < DestroyedBlockLog.Capacity; i++)
                log.Record((ushort)i);

            // One past capacity: expect a single warning, count stays capped.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[DestroyedBlockLog\] Overflow"));
            log.Record(9999);
            Assert.AreEqual(DestroyedBlockLog.Capacity, log.Count,
                "Beyond Capacity, Record must be a no-op.");

            // Subsequent overflow appends are silent (no second warning).
            log.Record(9998);
            Assert.AreEqual(DestroyedBlockLog.Capacity, log.Count);
        }

        [Test]
        public void DestroyedBlockLog_Reset_ClearsAndRearmsOverflowWarning()
        {
            var log = new DestroyedBlockLog();
            for (int i = 0; i < DestroyedBlockLog.Capacity; i++) log.Record((ushort)i);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[DestroyedBlockLog\] Overflow"));
            log.Record(9999);

            log.Reset();
            Assert.AreEqual(0, log.Count);

            // After reset, overfilling again should re-warn once.
            for (int i = 0; i < DestroyedBlockLog.Capacity; i++) log.Record((ushort)i);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[DestroyedBlockLog\] Overflow"));
            log.Record(9999);
        }

        // -----------------------------------------------------------------
        // Aim-bounds validation (NETCODE_PLAN §13 anti-cheat hardening)
        // -----------------------------------------------------------------

        private static (GameObject go, NetworkRobotCombat combat) MakeBareCombat()
        {
            // NetworkRobotCombat requires NetworkObject — add it first so
            // Unity doesn't auto-add one (it would, but explicit order keeps
            // the test grounded). NetworkObject's Awake is safe without an
            // active NetworkManager — only RPC routing depends on a session.
            GameObject go = new GameObject("Phase4CombatProbe");
            go.AddComponent<NetworkObject>();
            NetworkRobotCombat combat = go.AddComponent<NetworkRobotCombat>();
            return (go, combat);
        }

        [Test]
        public void AimValidation_FirstCommand_IsAccepted()
        {
            var (go, combat) = MakeBareCombat();
            try
            {
                var cmd = new FireCommand { AimDir = Vector3.forward };
                Assert.IsTrue(combat.ServerValidateAim(in cmd),
                    "The first command after spawn has no prior aim — it must be accepted to seed the validator.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AimValidation_UnderThreshold_IsAccepted()
        {
            var (go, combat) = MakeBareCombat();
            try
            {
                // Seed.
                combat.ServerValidateAim(new FireCommand { AimDir = Vector3.forward });
                // 89° from forward — under MaxAimDeltaDeg (90).
                Vector3 nearLimit = Quaternion.AngleAxis(89f, Vector3.up) * Vector3.forward;
                bool ok = combat.ServerValidateAim(new FireCommand { AimDir = nearLimit });
                Assert.IsTrue(ok, $"Aim {Vector3.Angle(Vector3.forward, nearLimit):F1}° must be under the {NetworkRobotCombat.MaxAimDeltaDeg}° bound.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AimValidation_OverThreshold_IsRejected_AndCounterIncrements()
        {
            var (go, combat) = MakeBareCombat();
            try
            {
                // Seed.
                combat.ServerProcessFireCommand(new FireCommand { AimDir = Vector3.forward });
                int before = combat.RejectedFireCount;

                // 120° from forward — well over MaxAimDeltaDeg.
                Vector3 wild = Quaternion.AngleAxis(120f, Vector3.up) * Vector3.forward;
                LogAssert.ignoreFailingMessages = true; // the [Warning] log is incidental
                combat.ServerProcessFireCommand(new FireCommand { AimDir = wild });

                Assert.Greater(combat.RejectedFireCount, before,
                    "Aim > MaxAimDeltaDeg must increment RejectedFireCount via the aim path.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AimValidation_DegenerateZero_IsAccepted_DoesNotPoisonState()
        {
            var (go, combat) = MakeBareCombat();
            try
            {
                Assert.IsTrue(combat.ServerValidateAim(new FireCommand { AimDir = Vector3.zero }),
                    "Zero AimDir is degenerate (early-spawn frame) and must be accepted without flipping the validator into an unrecoverable state.");

                // Subsequent valid command should still succeed (zero did not seed a poison reference).
                Assert.IsTrue(combat.ServerValidateAim(new FireCommand { AimDir = Vector3.forward }));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
