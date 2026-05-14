using NUnit.Framework;
using Robogame.Voxel;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Correctness pinning for the Naive Surface Nets implementation.
    /// Phase 1c will port to Burst; these tests are the contract that
    /// the Burst version must continue to satisfy. WHY each test exists
    /// is documented inline — they each cover a class of bug the
    /// algorithm is naturally prone to.
    /// </summary>
    public sealed class SurfaceNetsMesherTests
    {
        // ------------------------------------------------------------------
        // Degenerate cases — chunks with no surface should emit nothing.
        // A regression here means the mesher is fabricating geometry from
        // uniform input, which is a hard fail at the apron boundary in
        // Phase 2.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_AllInterior_ProducesNoGeometry()
        {
            const int dim = 8;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int i = 0; i < sdf.Length; i++) sdf[i] = -100;
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(0, vCount, "Uniform-interior input must not emit vertices.");
            Assert.AreEqual(0, iCount, "Uniform-interior input must not emit triangles.");
        }

        [Test]
        public void Mesh_AllExterior_ProducesNoGeometry()
        {
            const int dim = 8;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 100;
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(0, vCount);
            Assert.AreEqual(0, iCount);
        }

        // ------------------------------------------------------------------
        // Half-space — pins active-cell count, vertex position, normal
        // direction. A flat plane is the strongest constraint on the
        // algorithm because the expected geometry is exactly predictable.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_HalfSpaceAlongX_ProducesFlatPlaneAtMidpoint()
        {
            const int dim = 8;
            const int splitSample = 4;   // solid: x ∈ [0,3], empty: x ∈ [4,7]
            sbyte[] sdf = MakeHalfSpaceX(dim, splitSample);
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            // Active cells: only the i=splitSample-1 column straddles the sign change.
            // For dim=8, cellDim=7, so active cells = 7*7 = 49.
            Assert.AreEqual(49, vCount, "Active cell count for a half-space along X should be (cellDim)² on a single column.");

            // All vertices should lie on the plane x = splitSample - 0.5.
            // The crossing on the X edge from sample 3 (sdf=-100) to sample 4 (sdf=+100)
            // is at t = -100 / (-100 - 100) = 0.5, so x = 3 + 0.5 = 3.5.
            for (int v = 0; v < vCount; v++)
            {
                Assert.AreEqual(splitSample - 0.5f, buffers.Vertices[v].x, 1e-4f,
                    $"Vertex {v} on a perfect half-space should have x = {splitSample - 0.5f} (got {buffers.Vertices[v].x}).");
            }

            // Quad count: only X-edges straddling i=splitSample-1→splitSample are active,
            // and only those with all 4 incident cells existing (j∈[1,dim-2], k∈[1,dim-2]).
            // That's 6×6 = 36 quads × 2 triangles × 3 indices = 216 indices.
            Assert.AreEqual(216, iCount, "Expected 36 quads at the chunk-interior boundary.");
        }

        [Test]
        public void Mesh_HalfSpaceAlongX_NormalsPointTowardExterior()
        {
            // The convention: sA<0 → sB≥0 means solid-to-empty along the edge,
            // so the outward (front-face) normal points +X. Triangle winding
            // (CCW from the outside in Unity's left-handed coords) is what
            // the second-pass quad emission must produce.
            const int dim = 8;
            sbyte[] sdf = MakeHalfSpaceX(dim, 4);
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out _, out int iCount);
            Assert.Greater(iCount, 0);

            // Every triangle in the output should have a +X-pointing normal
            // (cross-product with the standard winding interpretation).
            for (int t = 0; t < iCount; t += 3)
            {
                Vector3 a = buffers.Vertices[buffers.Indices[t]];
                Vector3 b = buffers.Vertices[buffers.Indices[t + 1]];
                Vector3 c = buffers.Vertices[buffers.Indices[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(n.x, 0.99f,
                    $"Triangle {t/3} normal should point +X for a +X-facing half-space (got {n}).");
            }
        }

        [Test]
        public void Mesh_HalfSpaceFlipped_NormalsPointOppositeWay()
        {
            // Same surface, opposite sign convention. Pins the else-branch of
            // the quad winding so we don't accidentally have both branches
            // produce the same orientation.
            const int dim = 8;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(x < 4 ? 100 : -100);   // solid on +X side
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out _, out int iCount);
            Assert.Greater(iCount, 0);

            for (int t = 0; t < iCount; t += 3)
            {
                Vector3 a = buffers.Vertices[buffers.Indices[t]];
                Vector3 b = buffers.Vertices[buffers.Indices[t + 1]];
                Vector3 c = buffers.Vertices[buffers.Indices[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Less(n.x, -0.99f, $"Triangle {t/3} should face -X when solid is on +X.");
            }
        }

        [Test]
        public void Mesh_HalfSpaceAlongY_ProducesFlatPlane()
        {
            // Y-axis pinning — different code path (the Y-edge quad loop has
            // a transposed winding to compensate for axis swap). This is the
            // test that caught an earlier sign mistake in dev.
            const int dim = 8;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(y < 4 ? -100 : 100);
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(49, vCount);
            Assert.AreEqual(216, iCount);
            for (int v = 0; v < vCount; v++)
                Assert.AreEqual(3.5f, buffers.Vertices[v].y, 1e-4f);

            for (int t = 0; t < iCount; t += 3)
            {
                Vector3 a = buffers.Vertices[buffers.Indices[t]];
                Vector3 b = buffers.Vertices[buffers.Indices[t + 1]];
                Vector3 c = buffers.Vertices[buffers.Indices[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(n.y, 0.99f);
            }
        }

        [Test]
        public void Mesh_HalfSpaceAlongZ_ProducesFlatPlane()
        {
            const int dim = 8;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(z < 4 ? -100 : 100);
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(49, vCount);
            Assert.AreEqual(216, iCount);
            for (int v = 0; v < vCount; v++)
                Assert.AreEqual(3.5f, buffers.Vertices[v].z, 1e-4f);

            for (int t = 0; t < iCount; t += 3)
            {
                Vector3 a = buffers.Vertices[buffers.Indices[t]];
                Vector3 b = buffers.Vertices[buffers.Indices[t + 1]];
                Vector3 c = buffers.Vertices[buffers.Indices[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(n.z, 0.99f);
            }
        }

        // ------------------------------------------------------------------
        // Single negative corner — minimal active-cell topology. This is
        // the smallest case that exercises Pass 1's vertex emission AND
        // Pass 2's "incident cells don't all exist" guard.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_SingleNegativeCornerAtChunkOrigin_EmitsOneVertexZeroTriangles()
        {
            // Corner (0,0,0) negative — active cell is (0,0,0). The 3
            // active edges all originate at sample (0,0,0); each is on
            // the chunk boundary, so the 4 cells incident on each don't
            // all exist (3 of them have negative cell-coords). Zero quads.
            const int dim = 4;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 100;
            sdf[0] = -100;
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(1, vCount, "Only cell (0,0,0) has a sign change among its corners.");
            Assert.AreEqual(0, iCount, "No quads — every active edge is on the chunk boundary.");
        }

        [Test]
        public void Mesh_SingleNegativeCornerAtInteriorPoint_EmitsClosedSurface()
        {
            // Corner (2,2,2) negative in a dim=5 grid. The 8 cells sharing
            // this corner are all active (each has one negative corner).
            // The 6 active edges fan out along ±X, ±Y, ±Z from (2,2,2),
            // each with all 4 incident cells present → 6 quads.
            const int dim = 5;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 100;
            sdf[2 * dim * dim + 2 * dim + 2] = -100;
            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(8, vCount, "The 8 cells sharing the negative corner are active.");
            Assert.AreEqual(36, iCount, "6 active edges × 2 triangles × 3 indices = 36.");

            AssertSurfaceIsClosed(buffers.Indices, iCount,
                "An isolated negative voxel must produce a watertight cube-topology mesh.");
        }

        // ------------------------------------------------------------------
        // Sphere SDF — sanity on the smooth case the algorithm exists for.
        // We don't pin exact triangle counts here (they depend on grid
        // alignment with the sphere) but we DO pin a few invariants any
        // valid Surface Nets output of a sphere must satisfy.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_SphereSdf_ProducesClosedSurfaceNearTargetRadius()
        {
            const int dim = 17;
            Vector3 centre = new Vector3(8f, 8f, 8f);
            const float radius = 5f;

            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                float d = Vector3.Distance(new Vector3(x, y, z), centre) - radius;
                // Fixed-point scale of 64 units per cell-edge (1 unit ≈ 0.0156
                // grid-cells); clamp into sbyte range.
                int scaled = Mathf.RoundToInt(d * 64f);
                if (scaled < -128) scaled = -128;
                else if (scaled > 127) scaled = 127;
                sdf[z * dim * dim + y * dim + x] = (sbyte)scaled;
            }

            SurfaceNetsMesher.Buffers buffers = SurfaceNetsMesher.Allocate(dim);
            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.Greater(vCount, 0, "Sphere should produce vertices.");
            Assert.Greater(iCount, 0, "Sphere should produce triangles.");
            Assert.AreEqual(0, iCount % 3, "Index count must be a multiple of 3.");

            // Every vertex should be within ~1 cell of the target radius.
            // Surface Nets snaps vertices to cell-grid resolution, so the
            // tolerance is the cell size (1.0 in cell-grid units).
            for (int v = 0; v < vCount; v++)
            {
                float r = Vector3.Distance(buffers.Vertices[v], centre);
                Assert.That(Mathf.Abs(r - radius), Is.LessThan(1f),
                    $"Vertex {v} at {buffers.Vertices[v]} (radius {r}) deviates from target radius {radius} by more than one cell.");
            }

            AssertSurfaceIsClosed(buffers.Indices, iCount,
                "A sphere mesh must be watertight — every edge shared by exactly two triangles.");
        }

        // ------------------------------------------------------------------
        // Determinism — Phase 6 netcode requires that two clients meshing
        // identical SDF input produce identical output (the brush-op
        // commutativity argument depends on this, see TERRAFORMING_PLAN §2).
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_DeterministicAcrossRuns()
        {
            const int dim = 12;
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                // Wavy SDF, deterministic from coords. Mixes signs everywhere
                // so the mesh is non-trivial.
                float f = Mathf.Sin(x * 0.7f) + Mathf.Cos(y * 0.5f) + Mathf.Sin(z * 0.6f);
                sdf[z * dim * dim + y * dim + x] = (sbyte)Mathf.Clamp(Mathf.RoundToInt(f * 32f), -128, 127);
            }

            SurfaceNetsMesher.Buffers a = SurfaceNetsMesher.Allocate(dim);
            SurfaceNetsMesher.Buffers b = SurfaceNetsMesher.Allocate(dim);
            SurfaceNetsMesher.Mesh(sdf, dim, a, out int vA, out int iA);
            SurfaceNetsMesher.Mesh(sdf, dim, b, out int vB, out int iB);

            Assert.AreEqual(vA, vB);
            Assert.AreEqual(iA, iB);
            for (int v = 0; v < vA; v++)
                Assert.AreEqual(a.Vertices[v], b.Vertices[v], $"Vertex {v} drifted between runs.");
            for (int i = 0; i < iA; i++)
                Assert.AreEqual(a.Indices[i], b.Indices[i], $"Index {i} drifted between runs.");
        }

        [Test]
        public void Mesh_BufferReuse_ProducesSameOutputAsFreshAlloc()
        {
            // Run the mesher twice with the same Buffers instance against
            // two different SDFs, then re-run the second against a fresh
            // Buffers. Outputs must match — the mesher is responsible for
            // resetting CellToVertex sentinels on every call (Pass 1 does
            // the reset loop). Otherwise stale entries from run 1 leak
            // into run 2's quad-emission pass.
            const int dim = 6;
            sbyte[] sdfA = MakeHalfSpaceX(dim, 3);
            sbyte[] sdfB = MakeHalfSpaceX(dim, 4);

            SurfaceNetsMesher.Buffers reused = SurfaceNetsMesher.Allocate(dim);
            SurfaceNetsMesher.Mesh(sdfA, dim, reused, out _, out _);
            SurfaceNetsMesher.Mesh(sdfB, dim, reused, out int vReused, out int iReused);

            SurfaceNetsMesher.Buffers fresh = SurfaceNetsMesher.Allocate(dim);
            SurfaceNetsMesher.Mesh(sdfB, dim, fresh, out int vFresh, out int iFresh);

            Assert.AreEqual(vFresh, vReused);
            Assert.AreEqual(iFresh, iReused);
            for (int v = 0; v < vFresh; v++)
                Assert.AreEqual(fresh.Vertices[v], reused.Vertices[v]);
            for (int i = 0; i < iFresh; i++)
                Assert.AreEqual(fresh.Indices[i], reused.Indices[i]);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static sbyte[] MakeHalfSpaceX(int dim, int splitSample)
        {
            sbyte[] sdf = new sbyte[dim * dim * dim];
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(x < splitSample ? -100 : 100);
            return sdf;
        }

        /// <summary>
        /// A closed (watertight) triangle surface has every directed edge
        /// shared with its reverse exactly once — i.e. for each edge (a→b)
        /// in the mesh, there is exactly one matching (b→a) on an adjacent
        /// triangle. Open boundary edges break this.
        /// </summary>
        private static void AssertSurfaceIsClosed(int[] indices, int indexCount, string failMessage)
        {
            var edgeCount = new System.Collections.Generic.Dictionary<long, int>();
            for (int t = 0; t < indexCount; t += 3)
            {
                AddDirectedEdge(edgeCount, indices[t],     indices[t + 1]);
                AddDirectedEdge(edgeCount, indices[t + 1], indices[t + 2]);
                AddDirectedEdge(edgeCount, indices[t + 2], indices[t]);
            }
            foreach (var kvp in edgeCount)
            {
                long key = kvp.Key;
                int a = (int)(key >> 32);
                int b = (int)(key & 0xFFFFFFFFL);
                long reverseKey = ((long)b << 32) | (uint)a;
                Assert.IsTrue(edgeCount.TryGetValue(reverseKey, out int reverseCount) && reverseCount == kvp.Value,
                    $"{failMessage} Edge ({a}→{b}) count={kvp.Value} but reverse ({b}→{a}) count={reverseCount}.");
            }
        }

        private static void AddDirectedEdge(System.Collections.Generic.Dictionary<long, int> map, int a, int b)
        {
            long key = ((long)a << 32) | (uint)b;
            map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;
        }
    }
}
