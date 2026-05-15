using NUnit.Framework;
using Robogame.Core;
using Robogame.Voxel;
using UnityEngine;

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
