using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Voxel;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Correctness pinning for the Naive Surface Nets implementation.
    /// Phase 1a shipped the managed C# version; Phase 1c ported to Burst.
    /// These tests are the contract both versions must satisfy — running
    /// them against the Burst version proves the port preserves behavior.
    /// WHY each test exists is documented inline.
    /// </summary>
    /// <remarks>
    /// Disposal: tests can't use <c>using var sdf = new NativeArray&lt;sbyte&gt;(...)</c>
    /// because C# 8 treats <c>using</c> variables as readonly, blocking
    /// the indexer setter. We track allocations in fields and dispose in
    /// <c>[TearDown]</c> instead — runs whether the test passes or fails,
    /// no leaked NativeContainers between tests.
    /// </remarks>
    public sealed class SurfaceNetsMesherTests
    {
        private readonly List<NativeArray<sbyte>> _sdfs = new();
        private readonly List<SurfaceNetsMesher.Buffers> _buffers = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _sdfs) if (s.IsCreated) s.Dispose();
            foreach (var b in _buffers) if (b.IsCreated) b.Dispose();
            _sdfs.Clear();
            _buffers.Clear();
        }

        private NativeArray<sbyte> AllocSdf(int dim)
        {
            var arr = new NativeArray<sbyte>(dim * dim * dim, Allocator.TempJob);
            _sdfs.Add(arr);
            return arr;
        }

        private SurfaceNetsMesher.Buffers AllocBuffers(int dim)
        {
            var buf = SurfaceNetsMesher.Allocate(dim, Allocator.TempJob);
            _buffers.Add(buf);
            return buf;
        }

        // ------------------------------------------------------------------
        // Degenerate cases — chunks with no surface should emit nothing.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_AllInterior_ProducesNoGeometry()
        {
            const int dim = 8;
            var sdf = AllocSdf(dim);
            for (int i = 0; i < sdf.Length; i++) sdf[i] = -100;
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(0, vCount, "Uniform-interior input must not emit vertices.");
            Assert.AreEqual(0, iCount, "Uniform-interior input must not emit triangles.");
        }

        [Test]
        public void Mesh_AllExterior_ProducesNoGeometry()
        {
            const int dim = 8;
            var sdf = AllocSdf(dim);
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 100;
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(0, vCount);
            Assert.AreEqual(0, iCount);
        }

        // ------------------------------------------------------------------
        // Half-space pinning on all three axes.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_HalfSpaceAlongX_ProducesFlatPlaneAtMidpoint()
        {
            const int dim = 8;
            const int splitSample = 4;
            var sdf = AllocSdf(dim);
            FillHalfSpaceX(sdf, dim, splitSample);
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(49, vCount, "Active cell count for a half-space along X should be cellDim².");
            for (int v = 0; v < vCount; v++)
            {
                Assert.AreEqual(splitSample - 0.5f, buffers.Vertices[v].x, 1e-4f,
                    $"Vertex {v} on a perfect half-space should have x = {splitSample - 0.5f}.");
            }
            Assert.AreEqual(216, iCount, "Expected 36 quads at the chunk-interior boundary.");
        }

        [Test]
        public void Mesh_HalfSpaceAlongX_NormalsPointTowardExterior()
        {
            const int dim = 8;
            var sdf = AllocSdf(dim);
            FillHalfSpaceX(sdf, dim, 4);
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out _, out int iCount);
            Assert.Greater(iCount, 0);

            for (int t = 0; t < iCount; t += 3)
            {
                Vector3 a = buffers.Vertices[buffers.Indices[t]];
                Vector3 b = buffers.Vertices[buffers.Indices[t + 1]];
                Vector3 c = buffers.Vertices[buffers.Indices[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(n.x, 0.99f,
                    $"Triangle {t / 3} normal should point +X for a +X-facing half-space (got {n}).");
            }
        }

        [Test]
        public void Mesh_HalfSpaceFlipped_NormalsPointOppositeWay()
        {
            const int dim = 8;
            var sdf = AllocSdf(dim);
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(x < 4 ? 100 : -100);
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out _, out int iCount);
            Assert.Greater(iCount, 0);

            for (int t = 0; t < iCount; t += 3)
            {
                Vector3 a = buffers.Vertices[buffers.Indices[t]];
                Vector3 b = buffers.Vertices[buffers.Indices[t + 1]];
                Vector3 c = buffers.Vertices[buffers.Indices[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Assert.Less(n.x, -0.99f, $"Triangle {t / 3} should face -X when solid is on +X.");
            }
        }

        [Test]
        public void Mesh_HalfSpaceAlongY_ProducesFlatPlane()
        {
            const int dim = 8;
            var sdf = AllocSdf(dim);
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(y < 4 ? -100 : 100);
            var buffers = AllocBuffers(dim);

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
            var sdf = AllocSdf(dim);
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(z < 4 ? -100 : 100);
            var buffers = AllocBuffers(dim);

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
        // Single negative corner — minimal topology cases.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_SingleNegativeCornerAtChunkOrigin_EmitsOneVertexZeroTriangles()
        {
            const int dim = 4;
            var sdf = AllocSdf(dim);
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 100;
            sdf[0] = -100;
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(1, vCount, "Only cell (0,0,0) has a sign change among its corners.");
            Assert.AreEqual(0, iCount, "No quads — every active edge is on the chunk boundary.");
        }

        [Test]
        public void Mesh_SingleNegativeCornerAtInteriorPoint_EmitsClosedSurface()
        {
            const int dim = 5;
            var sdf = AllocSdf(dim);
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 100;
            sdf[2 * dim * dim + 2 * dim + 2] = -100;
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.AreEqual(8, vCount);
            Assert.AreEqual(36, iCount);
            AssertSurfaceIsClosed(buffers.Indices, iCount,
                "An isolated negative voxel must produce a watertight cube-topology mesh.");
        }

        // ------------------------------------------------------------------
        // Sphere SDF — closedness on a smooth iso-surface.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_SphereSdf_ProducesClosedSurfaceNearTargetRadius()
        {
            const int dim = 17;
            Vector3 centre = new Vector3(8f, 8f, 8f);
            const float radius = 5f;

            var sdf = AllocSdf(dim);
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                float d = Vector3.Distance(new Vector3(x, y, z), centre) - radius;
                int scaled = Mathf.RoundToInt(d * 64f);
                if (scaled < -128) scaled = -128;
                else if (scaled > 127) scaled = 127;
                sdf[z * dim * dim + y * dim + x] = (sbyte)scaled;
            }
            var buffers = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, buffers, out int vCount, out int iCount);

            Assert.Greater(vCount, 0);
            Assert.Greater(iCount, 0);
            Assert.AreEqual(0, iCount % 3, "Index count must be a multiple of 3.");

            for (int v = 0; v < vCount; v++)
            {
                Vector3 pos = buffers.Vertices[v];
                float r = Vector3.Distance(pos, centre);
                Assert.That(Mathf.Abs(r - radius), Is.LessThan(1f),
                    $"Vertex {v} at {pos} (radius {r}) deviates from target radius {radius} by more than one cell.");
            }

            AssertSurfaceIsClosed(buffers.Indices, iCount,
                "A sphere mesh must be watertight — every edge shared by exactly two triangles.");
        }

        // ------------------------------------------------------------------
        // Determinism + buffer reuse.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_DeterministicAcrossRuns()
        {
            const int dim = 12;
            var sdf = AllocSdf(dim);
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                float f = Mathf.Sin(x * 0.7f) + Mathf.Cos(y * 0.5f) + Mathf.Sin(z * 0.6f);
                sdf[z * dim * dim + y * dim + x] = (sbyte)Mathf.Clamp(Mathf.RoundToInt(f * 32f), -128, 127);
            }

            var a = AllocBuffers(dim);
            var b = AllocBuffers(dim);
            SurfaceNetsMesher.Mesh(sdf, dim, a, out int vA, out int iA);
            SurfaceNetsMesher.Mesh(sdf, dim, b, out int vB, out int iB);

            Assert.AreEqual(vA, vB);
            Assert.AreEqual(iA, iB);
            for (int v = 0; v < vA; v++)
                Assert.AreEqual((Vector3)a.Vertices[v], (Vector3)b.Vertices[v], $"Vertex {v} drifted between runs.");
            for (int i = 0; i < iA; i++)
                Assert.AreEqual(a.Indices[i], b.Indices[i], $"Index {i} drifted between runs.");
        }

        [Test]
        public void Mesh_BufferReuse_ProducesSameOutputAsFreshAlloc()
        {
            const int dim = 6;
            var sdfA = AllocSdf(dim);
            FillHalfSpaceX(sdfA, dim, 3);
            var sdfB = AllocSdf(dim);
            FillHalfSpaceX(sdfB, dim, 4);

            var reused = AllocBuffers(dim);
            SurfaceNetsMesher.Mesh(sdfA, dim, reused, out _, out _);
            SurfaceNetsMesher.Mesh(sdfB, dim, reused, out int vReused, out int iReused);

            var fresh = AllocBuffers(dim);
            SurfaceNetsMesher.Mesh(sdfB, dim, fresh, out int vFresh, out int iFresh);

            Assert.AreEqual(vFresh, vReused);
            Assert.AreEqual(iFresh, iReused);
            for (int v = 0; v < vFresh; v++)
                Assert.AreEqual((Vector3)fresh.Vertices[v], (Vector3)reused.Vertices[v]);
            for (int i = 0; i < iFresh; i++)
                Assert.AreEqual(fresh.Indices[i], reused.Indices[i]);
        }

        // ------------------------------------------------------------------
        // CellScale parameter — Phase 1c addition. Passing scale != 1 should
        // multiply all output positions by that factor; with scale=1 the
        // original cell-grid output is preserved.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_CellScale_ScalesVertexPositionsLinearly()
        {
            const int dim = 8;
            var sdf = AllocSdf(dim);
            FillHalfSpaceX(sdf, dim, 4);
            var unscaled = AllocBuffers(dim);
            var scaled   = AllocBuffers(dim);

            SurfaceNetsMesher.Mesh(sdf, dim, unscaled, out int vU, out _, cellScale: 1.0f);
            SurfaceNetsMesher.Mesh(sdf, dim, scaled,   out int vS, out _, cellScale: 0.5f);

            Assert.AreEqual(vU, vS);
            for (int v = 0; v < vU; v++)
            {
                Vector3 u = unscaled.Vertices[v];
                Vector3 s = scaled.Vertices[v];
                Assert.AreEqual(u.x * 0.5f, s.x, 1e-4f);
                Assert.AreEqual(u.y * 0.5f, s.y, 1e-4f);
                Assert.AreEqual(u.z * 0.5f, s.z, 1e-4f);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void FillHalfSpaceX(NativeArray<sbyte> sdf, int dim, int splitSample)
        {
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
                sdf[z * dim * dim + y * dim + x] = (sbyte)(x < splitSample ? -100 : 100);
        }

        private static void AssertSurfaceIsClosed(NativeArray<int> indices, int indexCount, string failMessage)
        {
            var edgeCount = new Dictionary<long, int>();
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

        private static void AddDirectedEdge(Dictionary<long, int> map, int a, int b)
        {
            long key = ((long)a << 32) | (uint)b;
            map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;
        }
    }
}
