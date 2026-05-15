using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Voxel;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Phase 3a: <see cref="BrushApplicator.Apply"/> with
    /// <see cref="BrushKind.CapsuleSubtract"/>. Pins capsule-shape
    /// correctness (along-axis cells carve, off-axis cells outside the
    /// radius stay untouched), degeneration to a sphere when p0 == p1,
    /// and the AABB-clip guard for brushes that miss the chunk.
    /// </summary>
    public sealed class BrushApplicatorTests
    {
        private readonly List<NativeArray<sbyte>> _sdfs = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _sdfs) if (s.IsCreated) s.Dispose();
            _sdfs.Clear();
        }

        private NativeArray<sbyte> AllocSdf(int dim, sbyte fill)
        {
            var arr = new NativeArray<sbyte>(dim * dim * dim, Allocator.TempJob);
            for (int i = 0; i < arr.Length; i++) arr[i] = fill;
            _sdfs.Add(arr);
            return arr;
        }

        private static BrushOp MakeCapsule(Vector3 p0, Vector3 p1, float radius)
        {
            return new BrushOp
            {
                kind = BrushKind.CapsuleSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(p0),
                p1 = Vector3Fixed.FromVector3(p1),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(radius * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };
        }

        // ------------------------------------------------------------------
        // Shape correctness
        // ------------------------------------------------------------------

        [Test]
        public void CapsuleSubtract_AxisAlignedX_CarvesTunnel()
        {
            // 17³ chunk at world origin, cellSize=1.0 → samples cover x ∈ [0,16] etc.
            // All-interior init. Capsule along X axis from (2, 8, 8) to (14, 8, 8) radius 1.5.
            const int dim = 17;
            const float cellSize = 1.0f;
            var sdf = AllocSdf(dim, fill: -100);

            BrushOp op = MakeCapsule(new Vector3(2, 8, 8), new Vector3(14, 8, 8), radius: 1.5f);
            int changed = BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);
            Assert.Greater(changed, 0);

            // Cells on the axis should be carved exterior.
            for (int x = 2; x <= 14; x++)
            {
                int idx = 8 * dim * dim + 8 * dim + x;
                Assert.GreaterOrEqual(sdf[idx], 0,
                    $"On-axis cell ({x},8,8) should be exterior after capsule subtract; got {sdf[idx]}.");
            }
        }

        [Test]
        public void CapsuleSubtract_CellsBeyondRadius_Unchanged()
        {
            const int dim = 17;
            const float cellSize = 1.0f;
            var sdf = AllocSdf(dim, fill: -100);

            BrushOp op = MakeCapsule(new Vector3(2, 8, 8), new Vector3(14, 8, 8), radius: 1.5f);
            BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);

            // A cell at (8, 8, 12) is 4 units off the X-axis through y=z=8.
            // Distance from axis = 4 > radius 1.5 → untouched.
            int idx = 12 * dim * dim + 8 * dim + 8;
            Assert.AreEqual((sbyte)-100, sdf[idx],
                "Cell well outside the capsule radius must not be touched.");
        }

        [Test]
        public void CapsuleSubtract_PastEndpoint_Unchanged()
        {
            // Cells past the capsule's endpoint (outside the segment + hemisphere
            // cap) must not be touched.
            const int dim = 17;
            const float cellSize = 1.0f;
            var sdf = AllocSdf(dim, fill: -100);

            BrushOp op = MakeCapsule(new Vector3(2, 8, 8), new Vector3(8, 8, 8), radius: 1.0f);
            BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);

            // Cell at (12, 8, 8) is 4 units past the +X endpoint along the axis.
            // Outside the +endpoint hemisphere cap (radius 1) → untouched.
            int idx = 8 * dim * dim + 8 * dim + 12;
            Assert.AreEqual((sbyte)-100, sdf[idx]);
        }

        [Test]
        public void CapsuleSubtract_HemisphereCapAtEndpoint_CarvesSphereVolume()
        {
            // The capsule's endpoints are hemispherical caps with the same
            // radius. A point on the axis at the endpoint is on the cap centre;
            // moving radially out by the radius, the cap surface; further is
            // outside.
            const int dim = 17;
            const float cellSize = 1.0f;
            var sdf = AllocSdf(dim, fill: -100);

            // Capsule from (8,8,8) to (8,8,8) — degenerate, same as a sphere.
            BrushOp op = MakeCapsule(new Vector3(8, 8, 8), new Vector3(8, 8, 8), radius: 2.0f);
            int changed = BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);
            Assert.Greater(changed, 0);

            // Cell at the centre — definitively carved.
            Assert.GreaterOrEqual(sdf[8 * dim * dim + 8 * dim + 8], 0);
            // Cell 4 units away on any axis — outside the radius=2 sphere.
            Assert.AreEqual((sbyte)-100, sdf[8 * dim * dim + 8 * dim + 12]);
        }

        // ------------------------------------------------------------------
        // Equivalence cases — degenerate capsule (p0 == p1) must match
        // SphereSubtract output cell-for-cell.
        // ------------------------------------------------------------------

        [Test]
        public void CapsuleSubtract_ZeroLength_MatchesSphereSubtract()
        {
            const int dim = 17;
            const float cellSize = 1.0f;
            Vector3 centre = new Vector3(8, 8, 8);
            const float radius = 3.0f;

            var sdfCapsule = AllocSdf(dim, fill: -100);
            var sdfSphere  = AllocSdf(dim, fill: -100);

            BrushOp capsule = MakeCapsule(centre, centre, radius);
            BrushOp sphere = new BrushOp
            {
                kind = BrushKind.SphereSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(centre),
                p1 = Vector3Fixed.FromVector3(centre),
                radiusFixed = (ushort)Mathf.RoundToInt(radius * Vector3Fixed.UnitsPerMeter),
            };

            int capsuleChanged = BrushApplicator.Apply(capsule, sdfCapsule, dim, cellSize, Vector3.zero);
            int sphereChanged  = BrushApplicator.Apply(sphere,  sdfSphere,  dim, cellSize, Vector3.zero);

            Assert.AreEqual(sphereChanged, capsuleChanged,
                "Degenerate capsule (zero-length axis) must change the same cell count as the equivalent sphere.");
            for (int i = 0; i < sdfCapsule.Length; i++)
            {
                Assert.AreEqual(sdfSphere[i], sdfCapsule[i],
                    $"Cell {i} diverged between degenerate-capsule and sphere brushes.");
            }
        }

        // ------------------------------------------------------------------
        // AABB clipping
        // ------------------------------------------------------------------

        [Test]
        public void CapsuleSubtract_OutsideChunk_NoCellsTouched()
        {
            const int dim = 17;
            const float cellSize = 1.0f;
            var sdf = AllocSdf(dim, fill: -100);

            // Capsule far outside the chunk (chunk covers world [0, 16]).
            BrushOp op = MakeCapsule(new Vector3(100, 100, 100), new Vector3(110, 100, 100), 2.0f);
            int changed = BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);

            Assert.AreEqual(0, changed);
            // All cells should be unchanged.
            for (int i = 0; i < sdf.Length; i++)
                Assert.AreEqual((sbyte)-100, sdf[i]);
        }

        // ------------------------------------------------------------------
        // Monotonicity invariant — re-applying the same capsule changes nothing.
        // ------------------------------------------------------------------

        [Test]
        public void CapsuleSubtract_AppliedTwice_SecondCallChangesNothing()
        {
            const int dim = 17;
            const float cellSize = 1.0f;
            var sdf = AllocSdf(dim, fill: -100);

            BrushOp op = MakeCapsule(new Vector3(2, 8, 8), new Vector3(14, 8, 8), 1.5f);
            int firstChanged = BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);
            Assume.That(firstChanged, Is.GreaterThan(0));

            int secondChanged = BrushApplicator.Apply(op, sdf, dim, cellSize, Vector3.zero);
            Assert.AreEqual(0, secondChanged,
                "Re-applying the same CapsuleSubtract must change zero cells (max-fold idempotency).");
        }
    }
}
