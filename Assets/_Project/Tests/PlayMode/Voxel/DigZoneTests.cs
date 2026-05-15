using System.Collections;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Voxel;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Voxel
{
    /// <summary>
    /// Phase 2a machine gate plus the IDigZone surface that 1b/1c pinned.
    /// Tests instantiate DigZones in code with explicit chunkGridSize per
    /// test: 1×1×1 for the single-chunk regression suite, 2×1×1 for the
    /// new multi-chunk dispatch test.
    /// </summary>
    public sealed class DigZoneTests
    {
        private GameObject _go;
        private DigZone _zone;

        private DigZone MakeZone(Vector3Int chunkGridSize, int chunkSizeCells = 32, float cellSize = 0.5f)
        {
            _go = new GameObject("TestDigZone");
            _go.transform.position = Vector3.zero;
            _go.SetActive(false);   // Awake blocked until configured.
            _zone = _go.AddComponent<DigZone>();
            _zone.ChunkGridSize = chunkGridSize;
            _zone.ChunkSizeCells = chunkSizeCells;
            _zone.CellSize = cellSize;
            _go.SetActive(true);    // Awake fires, chunks spawn.
            return _zone;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _zone = null;
        }

        private BrushOp MakeSphereBrush(Vector3 centre, float radiusMeters)
        {
            return new BrushOp
            {
                kind = BrushKind.SphereSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(centre),
                p1 = Vector3Fixed.FromVector3(centre),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(radiusMeters * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };
        }

        // ------------------------------------------------------------------
        // IDigZone surface — preserved from Phase 1c.
        // ------------------------------------------------------------------

        [Test]
        public void Awake_SingleChunkHalfSpaceSeed_ProducesNonEmptyMesh()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);

            Assert.IsNotNull(chunk, "1×1×1 zone must spawn one chunk.");
            Assert.IsNotNull(chunk.CurrentMesh);
            Assert.Greater(chunk.CurrentMesh.vertexCount, 0,
                "Half-space SDF seed straddles y = totalCellsY/2; the mesher must emit a surface plane.");
        }

        [Test]
        public void OnEnable_RegistersWithDigField()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            IDigZone zoneAt = DigField.ZoneAt(zone.WorldBounds.center);
            Assert.AreSame(zone, zoneAt);
        }

        [Test]
        public void WorldBounds_MatchesAggregateChunkExtent()
        {
            DigZone zone = MakeZone(new Vector3Int(2, 2, 2));
            Bounds b = zone.WorldBounds;
            float expectedSide = 2 * zone.ChunkSizeCells * zone.CellSize;
            Assert.AreEqual(expectedSide, b.size.x, 1e-4f);
            Assert.AreEqual(expectedSide, b.size.y, 1e-4f);
            Assert.AreEqual(expectedSide, b.size.z, 1e-4f);
            Assert.AreEqual(expectedSide * 0.5f, b.center.x, 1e-4f);
        }

        // ------------------------------------------------------------------
        // Brush application — single-chunk regression suite.
        // ------------------------------------------------------------------

        [Test]
        public void ApplyBrush_SphereSubtractAtChunkCentre_MutatesSdfInsideBrush()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);

            // Single chunk: split is at the chunk centre. Cell just below
            // the split plane is interior pre-brush.
            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int midX = dim / 2, midY = dim / 2, midZ = dim / 2;
            int interiorIdx = midZ * dimSq + (midY - 1) * dim + midX;

            Assert.Less(chunk.Sdf[interiorIdx], 0,
                "Pre-condition: cell just below half-space plane must be interior.");

            int changed = zone.ApplyBrush(MakeSphereBrush(zone.WorldBounds.center, 2.0f));
            Assert.Greater(changed, 0);

            Assert.GreaterOrEqual(chunk.Sdf[interiorIdx], 0,
                "Inside a centred 2 m sphere brush, the cell must become exterior.");
        }

        [Test]
        public void ApplyBrush_CellsOutsideBrushAabb_UnchangedSdf()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            sbyte before = chunk.Sdf[0];

            zone.ApplyBrush(MakeSphereBrush(zone.WorldBounds.center, 2.0f));

            Assert.AreEqual(before, chunk.Sdf[0],
                "Cells outside the brush AABB must not be touched (max-fold restricted to brush AABB).");
        }

        [Test]
        public void ApplyBrush_RemeshesSurface_VertexCountChanges()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            int preCount = chunk.CurrentMesh.vertexCount;

            int changed = zone.ApplyBrush(MakeSphereBrush(zone.WorldBounds.center, 2.0f));
            Assume.That(changed, Is.GreaterThan(0));

            Assert.AreNotEqual(preCount, chunk.CurrentMesh.vertexCount,
                "Carving the half-space must change the chunk's active-cell count → vertex count.");
        }

        [Test]
        public void ApplyBrush_MeshColliderSwapped()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            MeshCollider mc = chunk.GetComponent<MeshCollider>();
            Assert.IsNotNull(mc.sharedMesh, "Pre-brush: chunk collider must reference a cooked mesh.");

            zone.ApplyBrush(MakeSphereBrush(zone.WorldBounds.center, 2.0f));

            Assert.IsNotNull(mc.sharedMesh, "Post-brush: collider still non-null.");
            Assert.AreSame(chunk.CurrentMesh, mc.sharedMesh);
        }

        [Test]
        public void ApplyBrush_AppliedTwice_SecondApplicationChangesNothing()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            BrushOp op = MakeSphereBrush(zone.WorldBounds.center, 2.0f);

            int firstChanged = zone.ApplyBrush(op);
            Assume.That(firstChanged, Is.GreaterThan(0));

            int secondChanged = zone.ApplyBrush(op);
            Assert.AreEqual(0, secondChanged,
                "Re-applying the same SphereSubtract must change nothing — the max-fold invariant.");
        }

        // ------------------------------------------------------------------
        // Phase 2a machine gate — multi-chunk dispatch.
        // ------------------------------------------------------------------

        [Test]
        public void ApplyBrush_AtChunkBoundary_MutatesBothChunks()
        {
            // 2 chunks along X. The boundary between chunk(0,0,0) and
            // chunk(1,0,0) is at x = chunkSizeCells × cellSize = 16 m.
            // A brush of radius 3 m centred at x=16 reaches into both chunks.
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            DigChunk left  = zone.GetChunk(0, 0, 0);
            DigChunk right = zone.GetChunk(1, 0, 0);
            Assert.IsNotNull(left);
            Assert.IsNotNull(right);

            // Capture pre-brush mesh sizes — what we expect to change.
            int leftBefore  = left.CurrentMesh.vertexCount;
            int rightBefore = right.CurrentMesh.vertexCount;

            float chunkSideMeters = zone.ChunkSizeCells * zone.CellSize;
            // Brush centre at the boundary plane (x = chunkSideMeters),
            // y at the half-space plane so we actually carve solid material.
            Vector3 brushCentre = new Vector3(chunkSideMeters, chunkSideMeters * 0.5f, chunkSideMeters * 0.5f);

            int changed = zone.ApplyBrush(MakeSphereBrush(brushCentre, 3.0f));
            Assert.Greater(changed, 0, "Brush spanning two chunks must mutate at least one cell total.");

            // Direct SDF check — pick a cell just inside the brush radius on each side of the boundary.
            int dim = left.Dim;
            int dimSq = dim * dim;
            // Cell at left chunk's +X face, mid Y/Z (about 1 cell in from boundary).
            int leftCheckIdx = (dim / 2) * dimSq + (dim / 2 - 1) * dim + (dim - 2);
            // Cell at right chunk's -X face, mid Y/Z.
            int rightCheckIdx = (dim / 2) * dimSq + (dim / 2 - 1) * dim + 1;

            Assert.GreaterOrEqual(left.Sdf[leftCheckIdx], 0,
                "Left chunk: cell just inside +X face should be carved exterior by the boundary brush.");
            Assert.GreaterOrEqual(right.Sdf[rightCheckIdx], 0,
                "Right chunk: cell just inside -X face should be carved exterior by the boundary brush.");

            // Mesh-level check: both chunks should have remeshed (their
            // vertex counts changed because new sign-crossings appeared).
            Assert.AreNotEqual(leftBefore,  left.CurrentMesh.vertexCount,  "Left chunk must remesh.");
            Assert.AreNotEqual(rightBefore, right.CurrentMesh.vertexCount, "Right chunk must remesh.");
        }

        [Test]
        public void ApplyBrush_OutsideZone_NoChunksTouched()
        {
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            DigChunk left  = zone.GetChunk(0, 0, 0);
            DigChunk right = zone.GetChunk(1, 0, 0);

            sbyte leftCorner  = left.Sdf[0];
            sbyte rightCorner = right.Sdf[0];

            // Brush far above the zone.
            Vector3 centre = zone.WorldBounds.center + Vector3.up * 1000f;
            int changed = zone.ApplyBrush(MakeSphereBrush(centre, 2.0f));

            Assert.AreEqual(0, changed, "A brush outside every chunk's AABB must mutate zero cells.");
            Assert.AreEqual(leftCorner,  left.Sdf[0]);
            Assert.AreEqual(rightCorner, right.Sdf[0]);
        }

        // ------------------------------------------------------------------
        // Phase 2b machine gate — apron-based seam-free meshing.
        // For two adjacent chunks meshed independently with apron support,
        // the boundary-region vertices must agree to within 1e-4 m so the
        // meshes meet without visible seams.
        // ------------------------------------------------------------------

        [Test]
        public void Mesh_TwoAdjacentChunks_VerticesOnSharedXBoundaryAgreeToTolerance()
        {
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            DigChunk left  = zone.GetChunk(0, 0, 0);
            DigChunk right = zone.GetChunk(1, 0, 0);

            float chunkSideMeters = zone.ChunkSizeCells * zone.CellSize;
            Vector3 leftOrigin  = left.transform.position;
            Vector3 rightOrigin = right.transform.position;

            // Boundary plane at world x = chunkSideMeters. Apron-meshed
            // vertices in chunk A and own -X-face vertices in chunk B both
            // land at world x = chunkSideMeters + 0.5 × cellSize for a
            // half-space SDF (the X position is the average of the cell's
            // X-edge crossings, which for a Y-only-varying SDF is the cell's
            // X-midpoint).
            float expectedBoundaryWorldX = chunkSideMeters + 0.5f * zone.CellSize;
            const float tolerance = 1e-4f;

            var leftBoundary = CollectVerticesNear(left, leftOrigin, expectedBoundaryWorldX, tolerance);
            var rightBoundary = CollectVerticesNear(right, rightOrigin, expectedBoundaryWorldX, tolerance);

            Assert.Greater(leftBoundary.Count, 0,
                "Left chunk must emit vertices on the shared boundary plane (otherwise apron isn't working).");
            Assert.AreEqual(leftBoundary.Count, rightBoundary.Count,
                "Both chunks must emit the same number of vertices on the shared X boundary.");

            // For each left boundary vertex, find a matching right one.
            foreach (var lv in leftBoundary)
            {
                bool found = false;
                foreach (var rv in rightBoundary)
                {
                    if (Vector3.Distance(lv, rv) < tolerance) { found = true; break; }
                }
                Assert.IsTrue(found,
                    $"Left vertex {lv} has no matching right vertex within {tolerance} m. " +
                    "Seam-free meshing is broken — chunk A's apron disagreed with chunk B's own data.");
            }
        }

        private static System.Collections.Generic.List<Vector3> CollectVerticesNear(
            DigChunk chunk, Vector3 origin, float worldX, float tolerance)
        {
            var list = new System.Collections.Generic.List<Vector3>();
            var verts = chunk.CurrentMesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = origin + verts[i];
                if (Mathf.Abs(world.x - worldX) < tolerance) list.Add(world);
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Phase 2c machine gate — MeshCollider.sharedMesh is never
        // transiently null across an ApplyBrush + async Physics.BakeMesh
        // cycle.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ApplyBrush_AsyncBake_SharedMeshNonNullThroughout()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            MeshCollider mc = chunk.GetComponent<MeshCollider>();

            Assert.IsNotNull(mc.sharedMesh, "Pre-brush: collider must already reference the chunk's Mesh.");
            Assert.AreSame(chunk.CurrentMesh, mc.sharedMesh);

            zone.ApplyBrush(MakeSphereBrush(zone.WorldBounds.center, 2.0f));

            // Poll across frames. Throughout, sharedMesh stays pinned at
            // chunk.CurrentMesh — only the collider's cached cooked data
            // swaps when the worker bake completes (driven by
            // DigZone.Update → DigChunk.PollBakeAndSwap).
            const int maxFrames = 60;
            int polls = 0;
            while (polls < maxFrames && chunk.HasPendingBake)
            {
                Assert.IsNotNull(mc.sharedMesh,
                    $"Frame {polls}: collider sharedMesh became null while a bake was in flight.");
                Assert.AreSame(chunk.CurrentMesh, mc.sharedMesh,
                    $"Frame {polls}: collider sharedMesh diverged from chunk's Mesh.");
                yield return null;
                polls++;
            }

            Assert.IsFalse(chunk.HasPendingBake,
                $"Bake should have completed within {maxFrames} frames (took {polls}).");
            Assert.IsNotNull(mc.sharedMesh, "Post-bake: collider sharedMesh must be non-null.");
            Assert.AreSame(chunk.CurrentMesh, mc.sharedMesh, "Post-bake: collider must match chunk's Mesh.");
        }

        // ------------------------------------------------------------------
        // Phase 2d machine gate — DigZone bake/load round-trip via
        // DigZoneFormat. A modified zone baked + read + applied to a
        // fresh zone must produce SDF byte-identical to the source.
        // ------------------------------------------------------------------

        [Test]
        public void BakeAndLoad_ViaDigZone_SdfsByteIdentical()
        {
            // Source zone — half-space init + a brush.
            DigZone zoneA = MakeZone(new Vector3Int(2, 1, 1));
            int changed = zoneA.ApplyBrush(MakeSphereBrush(zoneA.WorldBounds.center, 3.0f));
            Assume.That(changed, Is.GreaterThan(0), "Brush must mutate something for the test to be meaningful.");

            // Bake.
            byte[] bytes = DigZoneFormat.Write(zoneA);

            // Tear down zoneA's GameObject (preserves SDFs on the in-memory
            // snapshot, frees the test rig).
            Object.DestroyImmediate(_go);
            _go = null;
            _zone = null;

            // Read into snapshot.
            DigZoneSnapshot snapshot = DigZoneFormat.Read(bytes);

            // Fresh zone with the same config.
            DigZone zoneB = MakeZone(new Vector3Int(2, 1, 1));
            zoneB.ApplySnapshot(snapshot);

            // Compare SDFs chunk-by-chunk.
            int dim = zoneB.ChunkSizeCells + 1;
            int sdfBytes = dim * dim * dim;
            for (int i = 0; i < snapshot.Chunks.Length; i++)
            {
                Vector3Int coord = snapshot.Chunks[i].ChunkCoord;
                DigChunk chunk = zoneB.GetChunk(coord);
                Assert.IsNotNull(chunk);
                for (int j = 0; j < sdfBytes; j++)
                {
                    Assert.AreEqual((sbyte)snapshot.Chunks[i].Sdf[j], chunk.Sdf[j],
                        $"Chunk {coord} SDF byte {j} diverged after round-trip.");
                }
            }

            // Re-bake — bytes should be identical (content hash stable).
            byte[] reBytes = DigZoneFormat.Write(zoneB);
            CollectionAssert.AreEqual(bytes, reBytes,
                "Re-baking the loaded zone must produce byte-identical output to the original bake.");
        }

        [Test]
        public void ChunkCount_MatchesGridSize()
        {
            DigZone zone = MakeZone(new Vector3Int(2, 2, 2));
            Assert.AreEqual(8, zone.ChunkCount);

            for (int z = 0; z < 2; z++)
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                Assert.IsNotNull(zone.GetChunk(x, y, z), $"Chunk ({x},{y},{z}) must exist.");
        }
    }
}
