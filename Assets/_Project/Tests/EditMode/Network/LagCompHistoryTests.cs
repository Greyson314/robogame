using NUnit.Framework;
using Robogame.Network.Robot;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Tests.EditMode.Network
{
    /// <summary>
    /// Ring-buffer property tests for the Phase-6 lag-comp history
    /// (NETCODE_PLAN §9 variant C). The networked side — registration,
    /// QueryAll, ray-vs-sphere intersection telemetry — is exercised
    /// integration-style under MPPM. These EditMode tests pin the
    /// per-robot ring's behaviour at the boundaries: exact query,
    /// closest-tick search, out-of-window rejection, and wrap-around.
    /// </summary>
    public sealed class LagCompHistoryTests
    {
        private static (GameObject go, LagCompHistory hist) MakeHistory()
        {
            // NetworkBehaviour requires NetworkObject — add explicitly so
            // ordering is clear. No active NGO session is needed for the
            // ring-buffer methods.
            GameObject go = new GameObject("LagCompProbe");
            go.AddComponent<NetworkObject>();
            LagCompHistory hist = go.AddComponent<LagCompHistory>();
            hist.SetChassisBounds(3f);
            return (go, hist);
        }

        [Test]
        public void Sample_ThenExactTickQuery_ReturnsThatEntry()
        {
            var (go, hist) = MakeHistory();
            try
            {
                hist.Sample(new Vector3(1f, 2f, 3f), 100);
                Assert.IsTrue(hist.TryQueryAt(100, out RobotBoundsSnapshot snap));
                Assert.AreEqual(100u, snap.Tick);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), snap.Pos);
                Assert.AreEqual(3f, snap.Radius);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Query_ClosestTickWithinWindow_ReturnsNearestEntry()
        {
            var (go, hist) = MakeHistory();
            try
            {
                // Five entries at ticks 10..14.
                for (uint t = 10; t <= 14; t++)
                    hist.Sample(new Vector3(t, 0f, 0f), t);

                // Query tick 13: nearest is exactly 13.
                Assert.IsTrue(hist.TryQueryAt(13, out RobotBoundsSnapshot snap));
                Assert.AreEqual(13u, snap.Tick);

                // Query tick 12 (still exact).
                Assert.IsTrue(hist.TryQueryAt(12, out snap));
                Assert.AreEqual(12u, snap.Tick);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Query_FarFromAnySample_ReturnsFalse()
        {
            var (go, hist) = MakeHistory();
            try
            {
                // Buffer has 5 entries at ticks 10..14. The closest sample
                // to tick 100 is 86 ticks away — beyond the 25-tick window.
                for (uint t = 10; t <= 14; t++) hist.Sample(Vector3.zero, t);
                Assert.IsFalse(hist.TryQueryAt(100, out _),
                    "Query beyond the 25-tick rewind window must return false.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Query_OnEmptyBuffer_ReturnsFalse()
        {
            var (go, hist) = MakeHistory();
            try
            {
                Assert.IsFalse(hist.TryQueryAt(0, out _));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Ring_Overflow_OverwritesOldestEntry()
        {
            var (go, hist) = MakeHistory();
            try
            {
                // Fill past capacity. Ticks 0..(Capacity+5) = 30 samples.
                for (uint t = 0; t < (uint)LagCompHistory.Capacity + 5; t++)
                    hist.Sample(new Vector3(t, 0f, 0f), t);

                // Oldest live entry should be at tick 5 (= Capacity+5 - Capacity).
                // Ticks 0..4 should have been overwritten.
                Assert.AreEqual(LagCompHistory.Capacity, hist.Count);

                // Tick 0 is now far outside the live window (the closest
                // surviving sample is tick 5, dist=5 ≤ Capacity), so the
                // out-of-window guard still returns the nearest live sample.
                // The interesting check: ticks below 5 are GONE — the
                // 'closest' for tick 4 is tick 5, dist=1.
                Assert.IsTrue(hist.TryQueryAt(4, out RobotBoundsSnapshot snap));
                Assert.AreEqual(5u, snap.Tick, "Ticks 0..4 should have been overwritten.");

                // And the newest sample (tick Capacity+4 = 29) is reachable.
                Assert.IsTrue(hist.TryQueryAt((uint)LagCompHistory.Capacity + 4, out snap));
                Assert.AreEqual((uint)LagCompHistory.Capacity + 4, snap.Tick);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Reset_ClearsBuffer_PreservesRadius()
        {
            var (go, hist) = MakeHistory();
            try
            {
                hist.Sample(Vector3.one, 1);
                hist.Sample(Vector3.one * 2f, 2);
                Assert.AreEqual(2, hist.Count);

                hist.Reset();
                Assert.AreEqual(0, hist.Count);
                Assert.IsFalse(hist.TryQueryAt(1, out _));

                // Radius survives Reset (set once at chassis build time).
                Assert.AreEqual(3f, hist.ChassisRadius);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
