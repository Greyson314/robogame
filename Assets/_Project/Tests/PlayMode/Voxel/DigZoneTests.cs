using System.Collections;
using System.Collections.Generic;
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

        // ------------------------------------------------------------------
        // Phase 3b — DrillBlock emits CapsuleSubtract on contact and the
        // brush carves the SDF along the swept path.
        // ------------------------------------------------------------------

        [Test]
        public void DrillBlock_DrillInsideZone_EmitsBrushAndMutatesSdf()
        {
            // Single chunk; place the drill inside the lower (solid) half
            // of the half-space init so the brush has material to carve.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);

            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 startPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            // Create the drill at the start position; track its tip via
            // LateUpdate by calling Drill twice (the second call uses the
            // first call's snapshot as the prev-tip).
            GameObject drillGo = new GameObject("TestDrill");
            drillGo.transform.position = startPos;
            DrillBlock drill = drillGo.AddComponent<DrillBlock>();

            // Capture pre-state at a cell just below the start position.
            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int startX = Mathf.RoundToInt(startPos.x / zone.CellSize);
            int startY = Mathf.RoundToInt(startPos.y / zone.CellSize);
            int startZ = Mathf.RoundToInt(startPos.z / zone.CellSize);
            int testIdx = startZ * dimSq + startY * dim + startX;
            sbyte sdfBefore = chunk.Sdf[testIdx];
            Assert.Less(sdfBefore, 0, "Pre-condition: drill should start in interior (solid) material.");

            // Drive a synthetic drill cycle. First call has no previous tip,
            // so the capsule degenerates to a sphere at the current position
            // — still carves a chunk of material around the tip.
            int changed = drill.Drill(zone);
            Assert.Greater(changed, 0, "Drill at an interior point must mutate at least one SDF cell.");

            Assert.GreaterOrEqual(chunk.Sdf[testIdx], 0,
                "Cell at the drill position should be carved exterior.");

            // Move the drill +X by one cell, drill again — the second call's
            // capsule sweeps from the previous position to the new one,
            // carving a tunnel along the X axis.
            drillGo.transform.position = startPos + new Vector3(zone.CellSize, 0, 0);
            int secondChanged = drill.Drill(zone);
            Assert.Greater(secondChanged, 0, "Second drill call (with motion) should carve new material along the swept axis.");

            Object.DestroyImmediate(drillGo);
        }

        [Test]
        public void DrillBlock_NoMotion_ReDrillSamePoint_ChangesNothing()
        {
            // Drill once at a point, then drill again at the same point
            // without moving. The second call's capsule is degenerate (and
            // re-applies a brush onto already-exterior cells), so max-fold
            // idempotency means zero change.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));

            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 pos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            GameObject drillGo = new GameObject("TestDrill");
            drillGo.transform.position = pos;
            DrillBlock drill = drillGo.AddComponent<DrillBlock>();

            int first = drill.Drill(zone);
            Assume.That(first, Is.GreaterThan(0));

            int second = drill.Drill(zone);
            Assert.AreEqual(0, second,
                "Re-drilling the same point with no motion must hit max-fold idempotency (zero change).");

            Object.DestroyImmediate(drillGo);
        }

        // ------------------------------------------------------------------
        // Phase 3c — bomb crater integration via TerrainCratering.
        // OnBombDetonation looks up the DigZone at the world point and
        // emits a SphereSubtract. Outside any zone, it's a no-op.
        // ------------------------------------------------------------------

        [Test]
        public void TerrainCratering_BombInsideZone_CarvesSphereCrater()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);

            // Detonate inside the lower (solid) half of the half-space.
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 detonationPoint = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            // Pre-condition: cell at detonation point is interior.
            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int cx = Mathf.RoundToInt(detonationPoint.x / zone.CellSize);
            int cy = Mathf.RoundToInt(detonationPoint.y / zone.CellSize);
            int cz = Mathf.RoundToInt(detonationPoint.z / zone.CellSize);
            int testIdx = cz * dimSq + cy * dim + cx;
            Assume.That(chunk.Sdf[testIdx], Is.LessThan(0));

            int changed = TerrainCratering.OnBombDetonation(detonationPoint, radiusMeters: 2.0f);
            Assert.Greater(changed, 0, "Bomb inside a dig zone must mutate SDF cells.");
            Assert.GreaterOrEqual(chunk.Sdf[testIdx], 0,
                "Cell at detonation point should be carved exterior by the SphereSubtract.");
        }

        [Test]
        public void TerrainCratering_BombOutsideAnyZone_IsNoOp()
        {
            // No zone registered → no crater.
            int changed = TerrainCratering.OnBombDetonation(new Vector3(1000f, 1000f, 1000f), radiusMeters: 2.0f);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void TerrainCratering_BombInsideZoneButZeroRadius_IsNoOp()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            int changed = TerrainCratering.OnBombDetonation(zone.WorldBounds.center, radiusMeters: 0f);
            Assert.AreEqual(0, changed);
        }

        // ------------------------------------------------------------------
        // Phase 4a — LOD reduces vertex count proportionally.
        // ------------------------------------------------------------------

        [Test]
        public void SetLodLevel_ReducesVertexCountAtHigherLevels()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);

            int v0 = chunk.CurrentMesh.vertexCount;
            chunk.SetLodLevel(1);
            int v1 = chunk.CurrentMesh.vertexCount;
            chunk.SetLodLevel(2);
            int v2 = chunk.CurrentMesh.vertexCount;
            chunk.SetLodLevel(0);
            int v0again = chunk.CurrentMesh.vertexCount;

            Assert.Greater(v0, v1, $"LOD 1 should have fewer vertices than LOD 0 (lod0={v0}, lod1={v1}).");
            Assert.Greater(v1, v2, $"LOD 2 should have fewer vertices than LOD 1 (lod1={v1}, lod2={v2}).");
            Assert.AreEqual(v0, v0again, "Returning to LOD 0 should restore the original vertex count.");
        }

        // ------------------------------------------------------------------
        // Phase 4b — RefreshLod picks per-chunk LOD by view distance.
        // ------------------------------------------------------------------

        [Test]
        public void RefreshLod_NearView_ChunksStayAtLod0()
        {
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            // View at zone centre — all chunks are within d1.
            zone.RefreshLod(zone.WorldBounds.center);
            Assert.AreEqual(0, zone.GetChunk(0, 0, 0).CurrentLodLevel);
            Assert.AreEqual(0, zone.GetChunk(1, 0, 0).CurrentLodLevel);
        }

        [Test]
        public void RefreshLod_FarView_ChunksGetHigherLod()
        {
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            // View 1000 m away — every chunk is well past d2.
            zone.RefreshLod(zone.WorldBounds.center + new Vector3(1000f, 0f, 0f));
            Assert.AreEqual(2, zone.GetChunk(0, 0, 0).CurrentLodLevel);
            Assert.AreEqual(2, zone.GetChunk(1, 0, 0).CurrentLodLevel);
        }

        // ------------------------------------------------------------------
        // Phase 4d — triangle budget proxy. Plan §7 / §11 set the
        // 100-chunk worst-case budget at 1.5M triangles with LOD on.
        // Setting up 100 chunks in a unit test is heavy; instead, this
        // test proves the per-chunk scaling at lod=2 leaves comfortable
        // headroom under the per-chunk budget (15K tris = 1.5M / 100).
        // ------------------------------------------------------------------

        [Test]
        public void HighLod_HeavyExcavation_StaysUnderPerChunkBudget()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);

            // Excavate heavily: 50 random sphere brushes inside the chunk.
            // Drives the chunk toward worst-case surface area.
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            var rng = new System.Random(42);
            for (int i = 0; i < 50; i++)
            {
                Vector3 c = new Vector3(
                    (float)rng.NextDouble() * chunkSide,
                    (float)rng.NextDouble() * chunkSide,
                    (float)rng.NextDouble() * chunkSide);
                zone.ApplyBrush(MakeSphereBrush(c, radiusMeters: 1.5f));
            }

            // Force lod=2 (the production "far chunk" level).
            chunk.SetLodLevel(2);
            int tris = chunk.CurrentMesh.triangles.Length / 3;

            // Per-chunk budget at the 100-chunk × 1.5M plan target = 15K tris.
            Assert.Less(tris, 15000,
                $"Phase 4 machine gate: lod=2 worst-case chunk should fit in ~15K tris (got {tris}).");
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

        // ------------------------------------------------------------------
        // DrillCollisionForwarder — when a DrillBlock lives on a child of a
        // chassis-root Rigidbody, Unity's physics callbacks fire on the
        // root, not the child. The forwarder dispatches contact pairs
        // from the root to the correct drill block.
        // ------------------------------------------------------------------

        private GameObject _chassisGo;
        private List<GameObject> _ancillaryGameObjects = new();

        [TearDown]
        public void TearDownAncillary()
        {
            foreach (GameObject g in _ancillaryGameObjects) if (g != null) Object.DestroyImmediate(g);
            _ancillaryGameObjects.Clear();
            if (_chassisGo != null) Object.DestroyImmediate(_chassisGo);
            _chassisGo = null;
        }

        private (DrillCollisionForwarder forwarder, DrillBlock drill, Collider drillCol) BuildChassisWithDrill(Vector3 drillWorldPos)
        {
            _chassisGo = new GameObject("TestChassis");
            _chassisGo.transform.position = Vector3.zero;
            // Chassis root carries the Rigidbody so Unity routes physics
            // callbacks here.
            _chassisGo.AddComponent<Rigidbody>().isKinematic = true;
            var forwarder = _chassisGo.AddComponent<DrillCollisionForwarder>();

            var drillGo = new GameObject("TestDrill");
            drillGo.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            drillGo.transform.position = drillWorldPos;
            var drillCol = drillGo.AddComponent<BoxCollider>();
            drillCol.size = Vector3.one * 0.5f;
            var drill = drillGo.AddComponent<DrillBlock>();
            forwarder.RefreshDrills();

            return (forwarder, drill, drillCol);
        }

        [Test]
        public void DrillForwarder_DispatchContact_FromOwningDrillCollider_CarvesSdf()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 inSolidHalf = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            (DrillCollisionForwarder forwarder, _, Collider drillCol) = BuildChassisWithDrill(inSolidHalf);
            Collider chunkCol = chunk.GetComponent<MeshCollider>();
            Assert.IsNotNull(chunkCol, "Test pre-condition: chunk must have a MeshCollider.");

            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int cx = Mathf.RoundToInt(inSolidHalf.x / zone.CellSize);
            int cy = Mathf.RoundToInt(inSolidHalf.y / zone.CellSize);
            int cz = Mathf.RoundToInt(inSolidHalf.z / zone.CellSize);
            int probeIdx = cz * dimSq + cy * dim + cx;
            Assume.That(chunk.Sdf[probeIdx], Is.LessThan(0),
                "Test pre-condition: drill must start in solid material.");

            bool fired = forwarder.DispatchContact(drillCol, chunkCol);

            Assert.IsTrue(fired, "DispatchContact must return true when the chassis-side collider belongs to a drill.");
            Assert.GreaterOrEqual(chunk.Sdf[probeIdx], 0,
                "Drill at this position should have carved the cell to exterior via the forwarder.");
        }

        [Test]
        public void DrillForwarder_DispatchContact_FromUnknownCollider_Noop()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;

            (DrillCollisionForwarder forwarder, _, _) = BuildChassisWithDrill(
                new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f));

            // Create a stray collider that the forwarder doesn't know about.
            var strayGo = new GameObject("StrayCollider");
            strayGo.AddComponent<BoxCollider>();
            _ancillaryGameObjects.Add(strayGo);

            Collider chunkCol = chunk.GetComponent<MeshCollider>();
            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int probeIdx = (dim / 2) * dimSq + (dim / 4) * dim + (dim / 2);
            sbyte before = chunk.Sdf[probeIdx];

            bool fired = forwarder.DispatchContact(strayGo.GetComponent<Collider>(), chunkCol);

            Assert.IsFalse(fired, "DispatchContact from a non-drill collider must return false.");
            Assert.AreEqual(before, chunk.Sdf[probeIdx],
                "Drill must not have fired — SDF must be untouched.");
        }

        [UnityTest]
        public IEnumerator DrillBlock_InsideZone_AutoPollsViaFixedUpdate_CarvesSdf()
        {
            // The contact-only drilling path only fires when the drill's
            // collider physically intersects a chunk's surface MeshCollider.
            // A body-mounted chassis drill rarely contacts the surface
            // (wheels keep the body above the terrain), so DrillBlock
            // also polls DigField each FixedUpdate: if the drill's tip
            // sits inside a registered DigZone, it emits a brush even
            // without a contact event. This test pins that behaviour.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 startPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            GameObject drillGo = new GameObject("AutoPollDrill");
            drillGo.transform.position = startPos;
            drillGo.AddComponent<DrillBlock>();
            _ancillaryGameObjects.Add(drillGo);

            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int cx = Mathf.RoundToInt(startPos.x / zone.CellSize);
            int cy = Mathf.RoundToInt(startPos.y / zone.CellSize);
            int cz = Mathf.RoundToInt(startPos.z / zone.CellSize);
            int probeIdx = cz * dimSq + cy * dim + cx;
            Assume.That(chunk.Sdf[probeIdx], Is.LessThan(0),
                "Pre-condition: drill starts in solid material.");

            // Two FixedUpdates: first to fire the auto-poll past the
            // initial throttle, second as a safety margin if the first
            // happens to coincide with _lastEmitTime = NegativeInfinity
            // edge cases.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.GreaterOrEqual(chunk.Sdf[probeIdx], 0,
                "Drill inside the zone must auto-carve through the FixedUpdate poll path.");
        }

        [Test]
        public void DrillForwarder_RefreshDrills_CountMatchesAttachedDrillBlocks()
        {
            (DrillCollisionForwarder forwarder, _, _) = BuildChassisWithDrill(Vector3.zero);
            Assert.AreEqual(1, forwarder.BoundDrillCount, "One DrillBlock attached on construction.");

            // Add a second drill block on the chassis.
            var drillGo2 = new GameObject("TestDrill2");
            drillGo2.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            drillGo2.AddComponent<BoxCollider>();
            drillGo2.AddComponent<DrillBlock>();
            forwarder.RefreshDrills();

            Assert.AreEqual(2, forwarder.BoundDrillCount,
                "RefreshDrills must pick up the newly-added drill block.");
        }

        // ------------------------------------------------------------------
        // Phase 4c — LOD-boundary transition handling.
        //
        // When two adjacent chunks mesh at different LOD levels, the fine
        // side snaps its boundary-strip vertex positions to the coarse
        // grid along the axis perpendicular to the shared face, and
        // suppresses its boundary-face X/Y/Z-axis-edge quads (where all 4
        // corners lie in the boundary strip) so the coarse neighbour owns
        // the seam geometry. A small per-triangle degenerate-area filter
        // in Pass 2 catches the rare case where snapping coalesces
        // corners to identical positions.
        // ------------------------------------------------------------------

        /// <summary>
        /// Build a 2×1×1 zone with a sphere brush spanning the boundary,
        /// then force fine = LOD 0 on chunk (0,0,0) and coarse = a given
        /// LOD on chunk (1,0,0). Final <see cref="DigZone.RebuildAllMeshes"/>
        /// call refreshes the stride lookup so the fine chunk's mesh
        /// reflects the LOD mismatch.
        /// </summary>
        private DigZone MakeLodSeamZone(int coarseLod)
        {
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            // Carve a sphere straddling the +X face so both chunks have
            // surface near the seam; otherwise the boundary face has no
            // active edges and the suppression/snap logic is untestable.
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 brushCentre = new Vector3(chunkSide, chunkSide * 0.5f, chunkSide * 0.5f);
            int changed = zone.ApplyBrush(MakeSphereBrush(brushCentre, 4.0f));
            Assume.That(changed, Is.GreaterThan(0), "Sphere brush at the boundary must mutate cells.");

            zone.GetChunk(0, 0, 0).SetLodLevel(0);
            zone.GetChunk(1, 0, 0).SetLodLevel(coarseLod);
            zone.RebuildAllMeshes();   // refresh NeighbourLodStrides post-LOD-change.
            return zone;
        }

        [Test]
        public void LodSeam_FineSidePosXBoundaryVertices_SnapXToCoarseGrid()
        {
            // chunk(0,0,0) at LOD 0, chunk(1,0,0) at LOD 1 → fine side's
            // +X PosX stride = 2. Apron cells (cx = chunkSize) get their
            // X centroid snapped to the coarse-cell-center pattern (odd
            // integers in fine cell-grid units for stride 2). World X of
            // a snapped vertex on the seam plane is then (chunkSize+1) ×
            // cellSize. The chunkSize+1 fine-unit lattice = chunkSize×
            // cellSize + 0.5×coarseCellSize = the coarse neighbour's
            // first-cell-centre X.
            DigZone zone = MakeLodSeamZone(coarseLod: 1);
            DigChunk fine = zone.GetChunk(0, 0, 0);
            DigChunk coarse = zone.GetChunk(1, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            float coarseCellSize = zone.CellSize * 2f;
            float expectedSnappedWorldX = chunkSide + 0.5f * coarseCellSize;

            // Collect vertices on the fine side near the snapped X plane.
            Vector3 fineOrigin = fine.transform.position;
            var verts = fine.CurrentMesh.vertices;
            int onSnapPlane = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = fineOrigin + verts[i];
                // The snap is to the coarse-cell-center plane; raw fine
                // vertices in the apron column would be at chunkSide +
                // 0.25 (= 0.5 × cellSize). The snap moves them to
                // chunkSide + 0.5 × coarseCellSize = chunkSide + 0.5m
                // for cellSize 0.5. Filter for vertices at this exact X.
                if (Mathf.Abs(world.x - expectedSnappedWorldX) < 1e-3f)
                {
                    onSnapPlane++;
                    // And there should be NO vertex at the un-snapped X
                    // (chunkSide + 0.5 × cellSize) for a snap-active cell.
                }
            }
            Assert.Greater(onSnapPlane, 0,
                "Fine side's +X boundary cells must produce at least one vertex on the snapped X plane.");

            // Negative test: no fine vertex at the would-be RAW X
            // (chunkSide + 0.5 × cellSize = 16.25 m for default config) —
            // i.e., the snap actually moved them.
            float rawWorldX = chunkSide + 0.5f * zone.CellSize;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = fineOrigin + verts[i];
                Assert.IsFalse(Mathf.Abs(world.x - rawWorldX) < 1e-3f,
                    $"Fine vertex {world} sits at the un-snapped X={rawWorldX} m — boundary snap didn't run.");
            }

            // Sanity: coarse chunk also has vertices on the same snapped
            // plane (its first-cell-centre X), confirming the seam line
            // exists on both sides at the same world X.
            Vector3 coarseOrigin = coarse.transform.position;
            var coarseVerts = coarse.CurrentMesh.vertices;
            int coarseOnPlane = 0;
            for (int i = 0; i < coarseVerts.Length; i++)
            {
                Vector3 world = coarseOrigin + coarseVerts[i];
                if (Mathf.Abs(world.x - expectedSnappedWorldX) < 1e-3f) coarseOnPlane++;
            }
            Assert.Greater(coarseOnPlane, 0,
                "Coarse chunk must also place vertices on the snapped plane for the seam to match.");
        }

        [Test]
        public void LodSeam_NoDegenerateTrianglesAcrossLodMismatch_LodOneTwo()
        {
            // Machine gate. Either LOD level on the coarse side stresses
            // the snap + suppress path differently — run both.
            foreach (int coarseLod in new[] { 1, 2 })
            {
                DigZone zone = MakeLodSeamZone(coarseLod);
                AssertNoDegenerateTriangles(zone.GetChunk(0, 0, 0), coarseLod, "fine");
                AssertNoDegenerateTriangles(zone.GetChunk(1, 0, 0), coarseLod, "coarse");
                Object.DestroyImmediate(_go);
                _go = null;
                _zone = null;
            }
        }

        private static void AssertNoDegenerateTriangles(DigChunk chunk, int coarseLod, string side)
        {
            const float minAreaSq = 1e-6f;  // cross-product magnitude² in m² × m² ≈ (1 mm)²
            Mesh mesh = chunk.CurrentMesh;
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            int triCount = tris.Length / 3;
            int degenerates = 0;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = verts[tris[t * 3]];
                Vector3 b = verts[tris[t * 3 + 1]];
                Vector3 c = verts[tris[t * 3 + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < minAreaSq) degenerates++;
            }
            Assert.AreEqual(0, degenerates,
                $"coarseLod={coarseLod}, {side} side: {degenerates}/{triCount} triangles have near-zero area. " +
                "The Pass 2 degenerate-area filter must drop these before they enter the index buffer.");
        }

        [Test]
        public void LodSeam_FineAndCoarse_BothHaveVerticesOnSeamPlane()
        {
            // Seam continuity check: with snap active, fine + coarse both
            // emit vertices on the shared seam X plane (chunkSide + 0.5 ×
            // coarseCellSize). Both counts must be > 0; the exact numbers
            // differ (fine has more, since its in-plane Y/Z spacing stays
            // fine), but the meshes share the seam line in world space.
            DigZone zone = MakeLodSeamZone(coarseLod: 1);
            DigChunk fine = zone.GetChunk(0, 0, 0);
            DigChunk coarse = zone.GetChunk(1, 0, 0);

            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            float coarseCellSize = zone.CellSize * 2f;
            float seamWorldX = chunkSide + 0.5f * coarseCellSize;

            int fineOnPlane = CountVerticesNearX(fine, seamWorldX);
            int coarseOnPlane = CountVerticesNearX(coarse, seamWorldX);

            Assert.Greater(fineOnPlane, 0, "Fine side: no vertices on the seam plane — snap didn't run.");
            Assert.Greater(coarseOnPlane, 0, "Coarse side: no vertices on the seam plane — coarse mesh missing.");
        }

        private static int CountVerticesNearX(DigChunk chunk, float targetWorldX, float tol = 1e-3f)
        {
            Vector3 origin = chunk.transform.position;
            var verts = chunk.CurrentMesh.vertices;
            int count = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = origin + verts[i];
                if (Mathf.Abs(world.x - targetWorldX) < tol) count++;
            }
            return count;
        }

        [Test]
        public void LodSeam_SameLod_NeighbourStridesAreIdentity_NoSnapNoSuppress()
        {
            // Regression guard: when neighbours are at same LOD, the
            // Phase 4c plumbing produces all-1 strides and the mesher
            // takes the no-snap/no-suppress path. The post-Phase-4c
            // mesh of a same-LOD pair must therefore match the
            // pre-Phase-4c reference: same vertex count, identical
            // positions to float precision.
            DigZone zone = MakeLodSeamZone(coarseLod: 0);
            DigChunk fine = zone.GetChunk(0, 0, 0);
            NeighbourLodStrides strides = fine.NeighbourLodStrides;
            Assert.AreEqual(1, strides.NegX, "NegX stride should be 1 — no -X neighbour.");
            Assert.AreEqual(1, strides.PosX, "PosX stride should be 1 — same-LOD neighbour.");
            Assert.AreEqual(1, strides.NegY, "NegY stride should be 1 — no -Y neighbour.");
            Assert.AreEqual(1, strides.PosY, "PosY stride should be 1 — no +Y neighbour.");
            Assert.AreEqual(1, strides.NegZ, "NegZ stride should be 1 — no -Z neighbour.");
            Assert.AreEqual(1, strides.PosZ, "PosZ stride should be 1 — no +Z neighbour.");
            Assert.IsFalse(strides.AnySnap, "Same-LOD neighbours: no boundary snap should be active.");

            // No vertex on the fine side should sit at the SNAPPED X
            // (would only appear if snap actually ran).
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            float snappedX = chunkSide + 0.5f * (zone.CellSize * 2f);
            float rawX = chunkSide + 0.5f * zone.CellSize;
            Vector3 fineOrigin = fine.transform.position;
            var verts = fine.CurrentMesh.vertices;
            bool sawRawX = false;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = fineOrigin + verts[i];
                if (Mathf.Abs(world.x - rawX) < 1e-3f) sawRawX = true;
            }
            Assert.IsTrue(sawRawX,
                $"Same-LOD neighbours: at least one fine vertex must sit at the un-snapped X={rawX} m.");
        }
    }
}
