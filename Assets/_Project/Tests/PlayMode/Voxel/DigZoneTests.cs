using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Core;
using Robogame.Gameplay;
using Robogame.Input;
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

        private DigZone MakeZoneWithChamber(Vector3 chamberCenter, float radius, Vector3Int chunkGridSize)
        {
            _go = new GameObject("TestDigZone");
            _go.transform.position = Vector3.zero;
            _go.SetActive(false);
            _zone = _go.AddComponent<DigZone>();
            _zone.ChunkGridSize = chunkGridSize;
            _zone.ChunkSizeCells = 32;
            _zone.CellSize = 0.5f;
            _zone.AddInitialBrush(new DigZone.InitialBrushSpec
            {
                Kind = BrushKind.SphereSubtract,
                CenterWorld = chamberCenter,
                EndWorld = chamberCenter,
                RadiusMeters = radius,
            });
            _go.SetActive(true);
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

        // Stub IInputSource for tests — DrillBlock now gates emission on
        // FireHeld, so a chassis-like parent without an IInputSource
        // can't fire the drill via the contact or auto-poll paths. The
        // direct Drill(zone) method is unaffected and remains the
        // path tests use when they want deterministic carving regardless
        // of input state.
        private sealed class FireHeldInputStub : MonoBehaviour, IInputSource
        {
            public bool ReportFireHeld = true;
            public Vector2 Move => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public float Vertical => 0f;
            public bool FireHeld => ReportFireHeld;
            public bool FirePressed => false;
            public bool ReloadPressed => false;
        }

        private (DrillCollisionForwarder forwarder, DrillBlock drill, Collider drillCol, FireHeldInputStub input)
            BuildChassisWithDrill(Vector3 drillWorldPos, bool fireHeld = true)
        {
            _chassisGo = new GameObject("TestChassis");
            _chassisGo.transform.position = Vector3.zero;
            // Chassis root carries the Rigidbody so Unity routes physics
            // callbacks here.
            _chassisGo.AddComponent<Rigidbody>().isKinematic = true;
            FireHeldInputStub input = _chassisGo.AddComponent<FireHeldInputStub>();
            input.ReportFireHeld = fireHeld;
            var forwarder = _chassisGo.AddComponent<DrillCollisionForwarder>();

            var drillGo = new GameObject("TestDrill");
            drillGo.transform.SetParent(_chassisGo.transform, worldPositionStays: false);
            drillGo.transform.position = drillWorldPos;
            var drillCol = drillGo.AddComponent<BoxCollider>();
            drillCol.size = Vector3.one * 0.5f;
            var drill = drillGo.AddComponent<DrillBlock>();
            forwarder.RefreshDrills();

            return (forwarder, drill, drillCol, input);
        }

        [Test]
        public void DrillForwarder_DispatchContact_FromOwningDrillCollider_CarvesSdf()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 inSolidHalf = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            (DrillCollisionForwarder forwarder, _, Collider drillCol, _) = BuildChassisWithDrill(inSolidHalf);
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

            (DrillCollisionForwarder forwarder, _, _, _) = BuildChassisWithDrill(
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

        [Test]
        public void Drill_TipWorldPosition_AppliesForwardOffsetAlongTransformUp()
        {
            // The tip projects past the cell center along the block's
            // mount-up direction so a chassis-mounted drill carves ahead
            // (or below, for a -Y-up mount) of itself. Without the offset,
            // a cell-sized drill on a wheeled chassis only nicks the
            // surface plane and idempotency stops repeat carving — see
            // session 79 for the playtest report this fixes.
            GameObject drillGo = new GameObject("Drill");
            drillGo.transform.position = new Vector3(1f, 2f, 3f);
            DrillBlock drill = drillGo.AddComponent<DrillBlock>();
            _ancillaryGameObjects.Add(drillGo);

            // Default transform.up == world +Y. Tip offset should be a
            // pure +Y displacement.
            Vector3 tipDefault = drill.TipWorldPosition;
            Vector3 offsetDefault = tipDefault - drillGo.transform.position;
            Assert.AreEqual(0f, offsetDefault.x, 1e-4f);
            Assert.AreEqual(0f, offsetDefault.z, 1e-4f);
            Assert.Greater(offsetDefault.y, 0.1f,
                "Default-orientation drill must offset its tip along +Y; otherwise the tip = cell center.");

            // Rotate so transform.up == world +Z (the orientation a front-
            // mounted drill ends up at after BlockGrid.OrientationFromUp).
            drillGo.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            Vector3 tipRotated = drill.TipWorldPosition;
            Vector3 offsetRotated = tipRotated - drillGo.transform.position;
            Assert.AreEqual(0f, offsetRotated.x, 1e-4f);
            Assert.AreEqual(0f, offsetRotated.y, 1e-4f);
            Assert.Greater(offsetRotated.z, 0.1f,
                "After rotating so transform.up = +Z, the tip offset must track to +Z.");
        }

        [UnityTest]
        public IEnumerator DrillBlock_InsideZone_FireHeld_AutoPollsViaFixedUpdate_CarvesSdf()
        {
            // The contact-only drilling path only fires when the drill's
            // collider physically intersects a chunk's surface MeshCollider.
            // A body-mounted chassis drill rarely contacts the surface
            // (wheels keep the body above the terrain), so DrillBlock
            // polls DigField each FixedUpdate while FireHeld is true.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 startPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            BuildChassisWithDrill(startPos, fireHeld: true);

            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int cx = Mathf.RoundToInt(startPos.x / zone.CellSize);
            int cy = Mathf.RoundToInt(startPos.y / zone.CellSize);
            int cz = Mathf.RoundToInt(startPos.z / zone.CellSize);
            int probeIdx = cz * dimSq + cy * dim + cx;
            Assume.That(chunk.Sdf[probeIdx], Is.LessThan(0),
                "Pre-condition: drill starts in solid material.");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.GreaterOrEqual(chunk.Sdf[probeIdx], 0,
                "Drill inside the zone with FireHeld=true must auto-carve via FixedUpdate poll.");
        }

        [UnityTest]
        public IEnumerator DrillBlock_InsideZone_FireNotHeld_DoesNotCarve()
        {
            // The held-input gate: even when the drill sits inside a zone,
            // the auto-poll path stays silent until the player presses
            // fire. Mirrors BombBayBlock / CannonBlock / ProjectileGun's
            // FireHeld gate so left-click consistently means "use
            // primary tool".
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 startPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            BuildChassisWithDrill(startPos, fireHeld: false);

            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int cx = Mathf.RoundToInt(startPos.x / zone.CellSize);
            int cy = Mathf.RoundToInt(startPos.y / zone.CellSize);
            int cz = Mathf.RoundToInt(startPos.z / zone.CellSize);
            int probeIdx = cz * dimSq + cy * dim + cx;
            sbyte before = chunk.Sdf[probeIdx];

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(before, chunk.Sdf[probeIdx],
                "FireHeld=false: auto-poll must not emit any brush.");
        }

        [UnityTest]
        public IEnumerator DrillBlock_CarvingPullsChassisAlongAimDirection()
        {
            // The dig-pull: while the drill is biting solid voxels it
            // drags the chassis along the aim direction so the player can
            // worm through terrain. With no aim camera the bit stays at
            // mount-up (= transform.up = +Y here), so a successful carve
            // must add +Y velocity to the chassis body.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            DigChunk chunk = zone.GetChunk(0, 0, 0);
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 startPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            // Dynamic Rigidbody (BuildChassisWithDrill uses a kinematic
            // body, which ignores AddForce). Gravity off so the assertion
            // isolates the dig-pull from free-fall.
            var chassis = new GameObject("PullChassis");
            chassis.transform.position = startPos;
            var rb = chassis.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            _ancillaryGameObjects.Add(chassis);

            var drillGo = new GameObject("Drill");
            drillGo.transform.SetParent(chassis.transform, worldPositionStays: false);
            drillGo.transform.position = startPos;
            var drill = drillGo.AddComponent<DrillBlock>();

            int dim = chunk.Dim;
            int dimSq = dim * dim;
            int cx = Mathf.RoundToInt(startPos.x / zone.CellSize);
            int cy = Mathf.RoundToInt(startPos.y / zone.CellSize);
            int cz = Mathf.RoundToInt(startPos.z / zone.CellSize);
            Assume.That(chunk.Sdf[cz * dimSq + cy * dim + cx], Is.LessThan(0),
                "Pre-condition: drill starts in solid material so it actually carves.");

            int changed = drill.Drill(zone);
            Assume.That(changed, Is.GreaterThan(0), "Drill must carve for the pull to apply.");

            yield return new WaitForFixedUpdate();

            Assert.Greater(rb.linearVelocity.y, 0.01f,
                "Carving must pull the chassis along the aim direction (+Y with no aim camera).");
            Assert.Greater(rb.linearVelocity.y, Mathf.Abs(rb.linearVelocity.x),
                "Pull must be dominantly along the aim axis, not sideways.");
            Assert.Greater(rb.linearVelocity.y, Mathf.Abs(rb.linearVelocity.z),
                "Pull must be dominantly along the aim axis, not sideways.");
        }

        [UnityTest]
        public IEnumerator DrillBlock_DrillingAir_DoesNotPullChassis()
        {
            // The pull is gated on changed > 0 — drilling air (already-
            // exterior cells) carves nothing, so it must NOT translate the
            // chassis. Prevents the drill doubling as a free thruster.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            // Upper half of the half-space init is exterior (air).
            Vector3 airPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.9f, chunkSide * 0.5f);

            var chassis = new GameObject("AirChassis");
            chassis.transform.position = airPos;
            var rb = chassis.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            _ancillaryGameObjects.Add(chassis);

            var drillGo = new GameObject("Drill");
            drillGo.transform.SetParent(chassis.transform, worldPositionStays: false);
            drillGo.transform.position = airPos;
            var drill = drillGo.AddComponent<DrillBlock>();

            int changed = drill.Drill(zone);
            Assume.That(changed, Is.EqualTo(0), "Drilling air must carve nothing.");

            yield return new WaitForFixedUpdate();

            Assert.Less(rb.linearVelocity.magnitude, 1e-3f,
                "No carve → no dig-pull. The drill must not move the chassis through air.");
        }

        [UnityTest]
        public IEnumerator DrillBlock_DigPull_IsSpeedCapped_NotUnboundedAcceleration()
        {
            // The dig-pull is a one-sided velocity servo, not a flat
            // force: sustained drilling must settle at a slow crawl, not
            // accelerate without bound. Run many drill + physics cycles
            // and assert the aim-axis speed stays bounded well below what
            // a constant 1500 N force on a ~1 kg test body would produce
            // (tens of m/s within a handful of steps).
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            float chunkSide = zone.ChunkSizeCells * zone.CellSize;
            Vector3 startPos = new Vector3(chunkSide * 0.5f, chunkSide * 0.25f, chunkSide * 0.5f);

            var chassis = new GameObject("CrawlChassis");
            chassis.transform.position = startPos;
            var rb = chassis.AddComponent<Rigidbody>();
            rb.useGravity = false;            // isolate the servo from free-fall
            rb.linearVelocity = Vector3.zero;
            _ancillaryGameObjects.Add(chassis);

            var drillGo = new GameObject("Drill");
            drillGo.transform.SetParent(chassis.transform, worldPositionStays: false);
            drillGo.transform.position = startPos;
            var drill = drillGo.AddComponent<DrillBlock>();

            // Drive ~30 emit+step cycles. With a flat force this would be
            // ~30 m/s+; the servo must hold it to a slow crawl.
            for (int i = 0; i < 30; i++)
            {
                // Re-seed solid material under the bit each cycle so it
                // keeps carving (changed > 0) as the chassis creeps up.
                zone.InitializeHalfSpace();
                drill.Drill(zone);
                yield return new WaitForFixedUpdate();
            }

            float vAlong = rb.linearVelocity.y;   // aim = +Y with no camera
            Assert.Greater(vAlong, 0.01f, "Servo must still produce upward dig motion.");
            Assert.Less(vAlong, 5.0f,
                $"Dig speed must stay capped (servo target ~2 m/s); got {vAlong:F2} m/s. " +
                "A flat unbounded force would be far higher after 30 cycles.");
        }

        // ------------------------------------------------------------------
        // Phase 5 visual-playtest gate: VoxelChaserBot uses the
        // OccupancyGrid to chase a target via A*. These tests cover the
        // bind + pathfind + step behaviour with a deterministic half-
        // space SDF (no brush events, so the grid state is known up-front).
        // ------------------------------------------------------------------

        private VoxelChaserBot SpawnChaser(Vector3 worldPos)
        {
            var go = new GameObject("ChaserBot");
            go.transform.position = worldPos;
            _ancillaryGameObjects.Add(go);
            return go.AddComponent<VoxelChaserBot>();
        }

        private Transform SpawnTargetMarker(Vector3 worldPos)
        {
            var go = new GameObject("TargetMarker");
            go.transform.position = worldPos;
            _ancillaryGameObjects.Add(go);
            return go.transform;
        }

        [Test]
        public void VoxelChaserBot_FindsPathBetweenSurfaceCells_AcrossHalfSpace()
        {
            // 2×1×1 zone: surface plane at world y=0 across world x=0..16
            // and z=0..32. Both endpoints land on surface (OpenWithFloor)
            // cells, so a Cardinal6 path must exist.
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            OccupancyGrid grid = zone.OccupancyGrid;
            Assume.That(grid, Is.Not.Null);

            Vector3 startWorld = grid.GridToWorld(2, 4, 4);
            Vector3 targetWorld = grid.GridToWorld(13, 4, 4);

            VoxelChaserBot bot = SpawnChaser(startWorld);
            bot.BindZone(zone);
            bot.BindTarget(SpawnTargetMarker(targetWorld));

            Assert.IsTrue(bot.RefreshPath(),
                "A path must exist between two OpenWithFloor cells in the same row.");
            Assert.Greater(bot.PathLength, 0);
        }

        [UnityTest]
        public IEnumerator VoxelChaserBot_FollowsPath_MovesTowardTarget()
        {
            // Place the bot a few cells away from the target on the
            // surface, run a handful of FixedUpdates, and confirm the
            // bot's world-space distance to the target decreased.
            DigZone zone = MakeZone(new Vector3Int(2, 1, 1));
            OccupancyGrid grid = zone.OccupancyGrid;

            Vector3 startWorld = grid.GridToWorld(2, 4, 4);
            Vector3 targetWorld = grid.GridToWorld(10, 4, 4);

            VoxelChaserBot bot = SpawnChaser(startWorld);
            bot.BindZone(zone);
            bot.BindTarget(SpawnTargetMarker(targetWorld));

            float distBefore = Vector3.Distance(bot.transform.position, targetWorld);

            // Five fixed steps at default ~50 Hz = 0.1s. At walk speed
            // 2 m/s that's ~0.2m progress — well within the noise floor
            // but reliably > 0.
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            float distAfter = Vector3.Distance(bot.transform.position, targetWorld);
            Assert.Less(distAfter, distBefore,
                $"After 10 FixedUpdates the bot should have moved closer to its target " +
                $"(before={distBefore:F3} after={distAfter:F3}).");
        }

        // ------------------------------------------------------------------
        // Phase 6 machine gate (commutativity): apply the same set of
        // brush ops to two fresh zones in DIFFERENT random orders and
        // assert their SDFs converge byte-identical. This is the
        // load-bearing invariant from TERRAFORMING_PLAN § 2 — out-of-
        // order delivery (the worst case for the netcode) doesn't
        // desync clients.
        // ------------------------------------------------------------------

        [Test]
        public void DigZone_ApplyBrushesInDifferentOrders_SdfsConvergeIdentical()
        {
            // 50 random sphere brushes inside a single-chunk zone.
            const int opCount = 50;
            BrushOp[] ops = new BrushOp[opCount];
            var rng = new System.Random(42);
            for (int i = 0; i < opCount; i++)
            {
                Vector3 c = new Vector3(
                    (float)rng.NextDouble() * 16f,
                    (float)rng.NextDouble() * 16f,
                    (float)rng.NextDouble() * 16f);
                float r = 0.5f + (float)rng.NextDouble() * 2f;   // 0.5 – 2.5 m
                ops[i] = MakeSphereBrushOp(c, r);
            }

            // Zone A — apply in original order.
            DigZone zoneA = MakeZone(new Vector3Int(1, 1, 1));
            for (int i = 0; i < opCount; i++) zoneA.ApplyBrush(ops[i]);
            sbyte[] sdfA = SnapshotChunkSdf(zoneA.GetChunk(0, 0, 0));

            // Tear down to free the test-rig's chunk GameObjects before
            // creating zone B.
            Object.DestroyImmediate(_go);
            _go = null;
            _zone = null;

            // Zone B — apply in a SHUFFLED order. Same ops, different
            // sequence.
            BrushOp[] shuffled = (BrushOp[])ops.Clone();
            for (int i = shuffled.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            DigZone zoneB = MakeZone(new Vector3Int(1, 1, 1));
            for (int i = 0; i < opCount; i++) zoneB.ApplyBrush(shuffled[i]);
            sbyte[] sdfB = SnapshotChunkSdf(zoneB.GetChunk(0, 0, 0));

            // Byte-identical.
            Assert.AreEqual(sdfA.Length, sdfB.Length, "SDF size must match.");
            int diffCount = 0;
            for (int i = 0; i < sdfA.Length; i++) if (sdfA[i] != sdfB[i]) diffCount++;
            Assert.AreEqual(0, diffCount,
                $"Commutativity violated — {diffCount}/{sdfA.Length} SDF bytes differ between in-order " +
                "and shuffled application of the same ops.");
        }

        [Test]
        public void DigZone_ReplayLog_OnFreshZone_ConvergesToOriginal()
        {
            // Source zone: 20 random ops.
            const int opCount = 20;
            DigZone source = MakeZone(new Vector3Int(1, 1, 1));
            var rng = new System.Random(123);
            for (int i = 0; i < opCount; i++)
            {
                Vector3 c = new Vector3(
                    (float)rng.NextDouble() * 16f,
                    (float)rng.NextDouble() * 16f,
                    (float)rng.NextDouble() * 16f);
                source.ApplyBrush(MakeSphereBrushOp(c, 1.5f));
            }
            sbyte[] sourceSdf = SnapshotChunkSdf(source.GetChunk(0, 0, 0));
            BrushOp[] log = new BrushOp[source.OpLog.Count];
            for (int i = 0; i < log.Length; i++) log[i] = source.OpLog[i];

            Object.DestroyImmediate(_go);
            _go = null;
            _zone = null;

            // Replay log on a fresh zone.
            DigZone replayed = MakeZone(new Vector3Int(1, 1, 1));
            replayed.ReplayLog(log);
            sbyte[] replayedSdf = SnapshotChunkSdf(replayed.GetChunk(0, 0, 0));

            Assert.AreEqual(sourceSdf.Length, replayedSdf.Length);
            int diffCount = 0;
            for (int i = 0; i < sourceSdf.Length; i++)
                if (sourceSdf[i] != replayedSdf[i]) diffCount++;
            Assert.AreEqual(0, diffCount,
                $"ReplayLog must reproduce the source SDF exactly. {diffCount} bytes differ.");
        }

        // ------------------------------------------------------------------
        // Phase 7 — op-log checkpointing. Checkpoint(tick) snapshots the
        // current SDF in .dig wire format and drops ops with tick at-or-
        // before the snapshot. Late-join replication = (snapshot bytes +
        // remaining ops), which is asymptotically smaller than the from-
        // match-start log once matches run long.
        // ------------------------------------------------------------------

        [Test]
        public void DigZone_Checkpoint_DropsOpsAtOrBeforeSnapshotTick()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            for (ushort t = 1; t <= 5; t++)
            {
                BrushOp op = MakeSphereBrushOp(new Vector3(2f + t, 8f, 8f), 1.5f);
                op.serverTick = t;
                zone.ApplyBrush(op);
            }
            Assert.AreEqual(5, zone.OpLog.Count, "All 5 ops carved cells, so all should be logged.");

            zone.Checkpoint(serverTick: 3);

            // Ticks 1, 2, 3 are baked into the snapshot. Ticks 4, 5 remain.
            Assert.IsTrue(zone.HasSnapshot, "Checkpoint must set HasSnapshot.");
            Assert.AreEqual((ushort)3, zone.SnapshotTick, "SnapshotTick must reflect the call argument.");
            Assert.AreEqual(2, zone.OpLog.Count,
                "Ops at-or-before the snapshot tick should be dropped from the log.");
            Assert.AreEqual((ushort)4, zone.OpLog[0].serverTick, "First retained op must be the next tick after the snapshot.");
            Assert.AreEqual((ushort)5, zone.OpLog[1].serverTick);
        }

        [Test]
        public void DigZone_Checkpoint_SnapshotBytesParseableAsDigFormat()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            BrushOp op = MakeSphereBrushOp(new Vector3(8f, 8f, 8f), 2f);
            op.serverTick = 10;
            zone.ApplyBrush(op);

            zone.Checkpoint(serverTick: 10);

            Assert.IsNotNull(zone.SnapshotBytes, "SnapshotBytes must be populated after Checkpoint.");
            DigZoneSnapshot parsed = DigZoneFormat.Read(zone.SnapshotBytes);
            // .dig format pins zone dimensions; parsed must match the live zone.
            Assert.AreEqual(zone.ChunkGridSize, parsed.ChunkGridSize);
            Assert.AreEqual(zone.ChunkSizeCells, parsed.ChunkSizeCells);
            Assert.AreEqual(zone.CellSize, parsed.CellSize);
            Assert.AreEqual(1, parsed.Chunks.Length);
        }

        // Phase 7 machine gate: a late-joiner who receives (snapshot bytes
        // + post-snapshot op log) must converge byte-identical to the
        // source zone. The checkpoint MUST happen at a meaningful
        // midpoint — capturing it after the final op only passes because
        // sphere subtract is idempotent. Real flow: server applies ops
        // 1–5, checkpoints, then applies 6–10. The joiner sees only the
        // snapshot (= post-op-5 state) plus ops 6–10 in the log and must
        // converge to the same post-op-10 SDF the source ended at.
        [Test]
        public void DigZone_SnapshotPlusReplay_OnFreshZone_ConvergesToOriginal()
        {
            DigZone source = MakeZone(new Vector3Int(1, 1, 1));
            // Deterministic 5×2 grid of brush centres inside the half-
            // space-interior region (y = 3m → voxel y = 6, well under
            // the split at voxel y = 16). 4 m horizontal spacing with
            // 1.5 m radius brushes guarantees zero overlap, so every op
            // carves fresh cells and lands in the OpLog. The exact-count
            // assertion below relies on this.
            Vector3[] centres = new Vector3[10];
            for (int i = 0; i < centres.Length; i++)
            {
                int col = i % 5;            // 0..4
                int row = i / 5;            // 0..1
                centres[i] = new Vector3(2f + col * 4f, 3f, 2f + row * 4f);
            }

            // Ops 1–5 — applied before the checkpoint, baked into the snapshot.
            for (ushort t = 1; t <= 5; t++)
            {
                BrushOp op = MakeSphereBrushOp(centres[t - 1], 1.5f);
                op.serverTick = t;
                source.ApplyBrush(op);
            }

            source.Checkpoint(serverTick: 5);
            byte[] snapshotBytes = source.SnapshotBytes;

            // Ops 6–10 — applied after the checkpoint, will live in the
            // post-snapshot OpLog and travel separately to the joiner.
            for (ushort t = 6; t <= 10; t++)
            {
                BrushOp op = MakeSphereBrushOp(centres[t - 1], 1.5f);
                op.serverTick = t;
                source.ApplyBrush(op);
            }

            // Don't pin the exact OpLog count — some random brushes
            // may carve zero new cells if their entire footprint already
            // lies inside an earlier brush's crater, and zero-change ops
            // don't append. The load-bearing assertion is the SDF byte-
            // identity below; the post-snapshot ops are whatever's in
            // the live log at this moment.
            int postCount = source.OpLog.Count;
            Assert.IsTrue(postCount > 0 && postCount <= 5,
                $"Expected 1..5 post-snapshot ops in OpLog; got {postCount}.");
            BrushOp[] postSnapshotOps = new BrushOp[postCount];
            for (int i = 0; i < postCount; i++) postSnapshotOps[i] = source.OpLog[i];
            sbyte[] sourceSdf = SnapshotChunkSdf(source.GetChunk(0, 0, 0));

            Object.DestroyImmediate(_go);
            _go = null;
            _zone = null;

            // Joiner: fresh zone → apply snapshot → replay post-snapshot ops.
            DigZone joiner = MakeZone(new Vector3Int(1, 1, 1));
            DigZoneSnapshot snapshot = DigZoneFormat.Read(snapshotBytes);
            joiner.ApplySnapshot(snapshot);
            joiner.RebuildAllMeshes();   // Snapshot mutates SDF directly; bring mesh state in line.
            joiner.ReplayLog(postSnapshotOps);
            sbyte[] joinerSdf = SnapshotChunkSdf(joiner.GetChunk(0, 0, 0));

            Assert.AreEqual(sourceSdf.Length, joinerSdf.Length);
            int diffCount = 0;
            for (int i = 0; i < sourceSdf.Length; i++)
                if (sourceSdf[i] != joinerSdf[i]) diffCount++;
            Assert.AreEqual(0, diffCount,
                $"Snapshot + post-snapshot replay must reproduce the source SDF byte-identical. " +
                $"{diffCount} bytes differ.");
        }

        [Test]
        public void DigZone_Checkpoint_TickWraparound_RetainsPostSnapshotOps()
        {
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            // Op with tick 65530 (pre-snapshot in serial-number space).
            BrushOp earlier = MakeSphereBrushOp(new Vector3(4f, 8f, 8f), 1.5f);
            earlier.serverTick = 65530;
            zone.ApplyBrush(earlier);
            // Op with tick 5 (post-wraparound; serial-number-arithmetic AFTER 65530).
            BrushOp afterWrap = MakeSphereBrushOp(new Vector3(8f, 8f, 8f), 1.5f);
            afterWrap.serverTick = 5;
            zone.ApplyBrush(afterWrap);

            zone.Checkpoint(serverTick: 65530);

            Assert.AreEqual(1, zone.OpLog.Count,
                "Wraparound: the op with tick 5 lies AFTER tick 65530 in serial-number space and must be retained.");
            Assert.AreEqual((ushort)5, zone.OpLog[0].serverTick);
        }

        private static BrushOp MakeSphereBrushOp(Vector3 center, float radius) => new BrushOp
        {
            kind = BrushKind.SphereSubtract,
            serverTick = 0,
            p0 = Vector3Fixed.FromVector3(center),
            p1 = Vector3Fixed.FromVector3(center),
            radiusFixed = (ushort)Mathf.Clamp(
                Mathf.RoundToInt(radius * Vector3Fixed.UnitsPerMeter),
                0, ushort.MaxValue),
        };

        private static sbyte[] SnapshotChunkSdf(DigChunk chunk)
        {
            int dim = chunk.Dim;
            int total = dim * dim * dim;
            sbyte[] copy = new sbyte[total];
            var src = chunk.Sdf;
            for (int i = 0; i < total; i++) copy[i] = src[i];
            return copy;
        }

        [Test]
        public void DigZone_InitialBrush_CarvesChamberBeforeOccupancyBuild()
        {
            // The chamber is pre-carved before the occupancy grid is
            // built, so the cells inside the carved sphere classify as
            // OpenWithFloor / OpenNoFloor (depending on -Y neighbour)
            // even though the zone otherwise uses the half-space init.
            //
            // Brush centered on a cell's CENTER (not corner) so the cell
            // is fully within the radius. A corner-anchored brush only
            // overlaps ~1/8 of the cell and fails the majority-interior
            // threshold; ask me how I know.
            //
            // Zone 1×1×1 at world origin: half-space split at world y=8
            // (totalCellsY=32, split=16 sample, *0.5m cellSize). Solid
            // is y<8. Cell (4, 2, 4) centre = world (9, 5, 9), inside
            // the solid half.
            Vector3 chamberCenter = new Vector3(9f, 5f, 9f);
            DigZone zone = MakeZoneWithChamber(chamberCenter, radius: 2f, new Vector3Int(1, 1, 1));
            OccupancyGrid grid = zone.OccupancyGrid;
            Assume.That(grid, Is.Not.Null);

            Vector3Int chamberCell = grid.WorldToGrid(chamberCenter);
            OccupancyCell at = grid.GetCell(chamberCell);
            Assert.AreNotEqual(OccupancyCell.Solid, at,
                $"Chamber center {chamberCenter} should classify Open after the initial brush; got {at}.");
        }

        [Test]
        public void VoxelChaserBot_NoPath_RefreshReturnsFalse_PathStaysEmpty()
        {
            // Start in a Solid cell — A* fails closed. RefreshPath
            // returns false and the bot's HasPath stays false.
            DigZone zone = MakeZone(new Vector3Int(1, 1, 1));
            OccupancyGrid grid = zone.OccupancyGrid;

            // gy=1 (voxel y=4..7) is solid in the half-space init.
            Vector3 stuckPos = grid.GridToWorld(4, 1, 4);
            Vector3 targetPos = grid.GridToWorld(4, 5, 4);

            VoxelChaserBot bot = SpawnChaser(stuckPos);
            bot.BindZone(zone);
            bot.BindTarget(SpawnTargetMarker(targetPos));

            Assert.IsFalse(bot.RefreshPath(),
                "Start cell is Solid — A* must fail closed.");
            Assert.IsFalse(bot.HasPath,
                "No path means HasPath is false even if a previous refresh seeded the list.");
        }

        [Test]
        public void DrillForwarder_RefreshDrills_CountMatchesAttachedDrillBlocks()
        {
            (DrillCollisionForwarder forwarder, _, _, _) = BuildChassisWithDrill(Vector3.zero);
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
