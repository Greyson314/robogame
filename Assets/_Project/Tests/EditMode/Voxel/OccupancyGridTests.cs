using System.Collections.Generic;
using NUnit.Framework;
using Robogame.Voxel;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Tests.EditMode.Voxel
{
    /// <summary>
    /// Phase 5 machine gate per TERRAFORMING_PLAN.md §12: occupancy grid
    /// builds correctly from a known SDF, and A* finds a path through an
    /// authored tunnel. Incremental-update behaviour pinned by the
    /// chunk-boundary test below — mutating one chunk's SDF must only
    /// touch that chunk's slice of the grid.
    /// </summary>
    public sealed class OccupancyGridTests
    {
        private const int ChunkSizeCells = 32;
        private const float CellSize = 0.5f;

        // Test rig manages NativeArray lifetimes — `using var` makes the
        // local effectively read-only, which conflicts with our SDF
        // carving helpers. The tracked-list + TearDown idiom matches the
        // pattern in BrushApplicatorTests.
        private readonly List<NativeArray<sbyte>> _sdfs = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _sdfs) if (s.IsCreated) s.Dispose();
            _sdfs.Clear();
        }

        private NativeArray<sbyte> MakeHalfSpaceSdf()
        {
            int dim = ChunkSizeCells + 1;
            var sdf = new NativeArray<sbyte>(dim * dim * dim, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            int split = ChunkSizeCells / 2;
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                int v = (y - split) * 64;
                if (v < sbyte.MinValue) v = sbyte.MinValue;
                if (v > sbyte.MaxValue) v = sbyte.MaxValue;
                sdf[z * dim * dim + y * dim + x] = (sbyte)v;
            }
            _sdfs.Add(sdf);
            return sdf;
        }

        private NativeArray<sbyte> MakeAllExteriorSdf()
        {
            int dim = ChunkSizeCells + 1;
            var sdf = new NativeArray<sbyte>(dim * dim * dim, Allocator.Persistent);
            for (int i = 0; i < sdf.Length; i++) sdf[i] = 64;
            _sdfs.Add(sdf);
            return sdf;
        }

        // ------------------------------------------------------------------
        // Build correctness — pass 1 (solid vs open) + pass 2 (floor).
        // ------------------------------------------------------------------

        [Test]
        public void Build_HalfSpace_BottomFourLayersAreSolid_TopFourAreOpen()
        {
            var sdf = MakeHalfSpaceSdf();
            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, ChunkSizeCells + 1);

            // Surface at voxel y=16; occupancy cells span 4 voxel cells
            // each, so gy=0..3 cover voxel y=0..15 (all solid), gy=4..7
            // cover voxel y=16..31 (all open).
            for (int gy = 0; gy < 4; gy++)
                Assert.AreEqual(OccupancyCell.Solid, grid.GetCell(4, gy, 4),
                    $"Bottom-half cell (4, {gy}, 4) should be Solid.");
            for (int gy = 4; gy < 8; gy++)
                Assert.AreNotEqual(OccupancyCell.Solid, grid.GetCell(4, gy, 4),
                    $"Top-half cell (4, {gy}, 4) should be Open.");
        }

        [Test]
        public void Build_HalfSpace_FirstOpenLayerIsOpenWithFloor_HigherLayersOpenNoFloor()
        {
            var sdf = MakeHalfSpaceSdf();
            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, ChunkSizeCells + 1);

            // gy=4 is the first open cell above gy=3 (Solid). gy=5+ have
            // open cells below them.
            for (int gz = 0; gz < 8; gz++)
            for (int gx = 0; gx < 8; gx++)
            {
                Assert.AreEqual(OccupancyCell.OpenWithFloor, grid.GetCell(gx, 4, gz),
                    $"First open layer cell ({gx}, 4, {gz}) above a solid floor should be OpenWithFloor.");
                Assert.AreEqual(OccupancyCell.OpenNoFloor, grid.GetCell(gx, 5, gz),
                    $"Higher cell ({gx}, 5, {gz}) with non-solid -Y neighbour should be OpenNoFloor.");
            }
        }

        [Test]
        public void Build_GridBelowZoneFloor_HasNoFloor_DefaultsToOpenNoFloor()
        {
            // SDF entirely exterior (no solid material). Pass 1 marks
            // every cell open; pass 2 finds no -Y solid below the bottom
            // row, so gy=0 cells default to OpenNoFloor.
            var sdf = MakeAllExteriorSdf();
            int dim = ChunkSizeCells + 1;

            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, dim);

            Assert.AreEqual(OccupancyCell.OpenNoFloor, grid.GetCell(0, 0, 0),
                "gy=0 cell with no -Y neighbour should default to OpenNoFloor (can't stand on the void).");
        }

        // ------------------------------------------------------------------
        // World ↔ grid conversion.
        // ------------------------------------------------------------------

        [Test]
        public void WorldToGrid_GridToWorld_RoundTripsThroughCellCenter()
        {
            var grid = new OccupancyGrid(new Vector3(10f, 20f, 30f), 8, 8, 8, CellSize);

            // CellSize for default voxel 0.5m = 2m occupancy cell.
            // Cell (3, 4, 5) center is at worldOrigin + (3.5, 4.5, 5.5) × 2 = (17, 29, 41).
            Vector3 centerWorld = grid.GridToWorld(3, 4, 5);
            Assert.AreEqual(17f, centerWorld.x, 1e-4f);
            Assert.AreEqual(29f, centerWorld.y, 1e-4f);
            Assert.AreEqual(41f, centerWorld.z, 1e-4f);

            Vector3Int back = grid.WorldToGrid(centerWorld);
            Assert.AreEqual(new Vector3Int(3, 4, 5), back);
        }

        // ------------------------------------------------------------------
        // A* — straight walks, tunnels, no-path.
        // ------------------------------------------------------------------

        [Test]
        public void FindPath_AlongFloorRow_ReturnsContiguousPath()
        {
            var sdf = MakeHalfSpaceSdf();
            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, ChunkSizeCells + 1);

            var path = new List<Vector3Int>();
            bool found = grid.TryFindPath(
                new Vector3Int(0, 4, 0), new Vector3Int(7, 4, 7),
                OccupancyConnectivity.Cardinal6, allowFlying: false, path);

            Assert.IsTrue(found, "Path along the floor row should be findable for a non-flyer.");
            Assert.AreEqual(new Vector3Int(0, 4, 0), path[0], "Path must start at the start cell.");
            Assert.AreEqual(new Vector3Int(7, 4, 7), path[path.Count - 1], "Path must end at the goal cell.");

            // Path is contiguous: consecutive cells differ by exactly one
            // unit on exactly one axis (Cardinal6 connectivity).
            for (int i = 1; i < path.Count; i++)
            {
                Vector3Int step = path[i] - path[i - 1];
                int manhattanDist = Mathf.Abs(step.x) + Mathf.Abs(step.y) + Mathf.Abs(step.z);
                Assert.AreEqual(1, manhattanDist,
                    $"Cardinal6 step {i} from {path[i - 1]} to {path[i]} is not a unit move.");
            }

            // And every cell on the path is OpenWithFloor (no fly-only
            // cell visited by a ground bot).
            foreach (Vector3Int cell in path)
                Assert.AreEqual(OccupancyCell.OpenWithFloor, grid.GetCell(cell),
                    $"Ground-bot path crossed non-floor cell {cell}.");
        }

        [Test]
        public void FindPath_NonFlyer_ThroughTunnelCarvedInSolid_ReturnsPath()
        {
            // Half-space SDF with solid bottom. Carve a horizontal slab
            // through the solid half at sample y = 9..12 (voxel cells
            // y = 8..11). That makes occupancy gy=2 (voxel y=8..11,
            // samples 8..12) majority-exterior → Open, while gy=1
            // (voxel y=4..7, samples 4..8) stays majority-interior →
            // Solid (the tunnel's floor).
            var sdf = MakeHalfSpaceSdf();
            int dim = ChunkSizeCells + 1;
            int dimSq = dim * dim;
            for (int z = 0; z < dim; z++)
            for (int x = 0; x < dim; x++)
            for (int y = 9; y <= 12; y++)
                sdf[z * dimSq + y * dim + x] = 64;

            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, dim);

            Assert.AreEqual(OccupancyCell.Solid, grid.GetCell(4, 1, 4),
                "Cell below the slab should be solid (floor).");
            Assert.AreEqual(OccupancyCell.OpenWithFloor, grid.GetCell(4, 2, 4),
                "Cell inside the tunnel should be OpenWithFloor.");

            var path = new List<Vector3Int>();
            bool found = grid.TryFindPath(
                new Vector3Int(0, 2, 4), new Vector3Int(7, 2, 4),
                OccupancyConnectivity.Cardinal6, allowFlying: false, path);

            Assert.IsTrue(found, "Tunnel path should be findable for a ground bot.");
            Assert.AreEqual(new Vector3Int(0, 2, 4), path[0]);
            Assert.AreEqual(new Vector3Int(7, 2, 4), path[path.Count - 1]);
        }

        [Test]
        public void FindPath_NoPath_ReturnsFalse_EmptyPath()
        {
            // Half-space: bottom half solid, top half open. Try to path
            // from a solid cell (gy=1) to an open cell — the start is
            // not traversable, so no path.
            var sdf = MakeHalfSpaceSdf();
            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, ChunkSizeCells + 1);

            var path = new List<Vector3Int>();
            bool found = grid.TryFindPath(
                new Vector3Int(0, 1, 0), new Vector3Int(7, 4, 7),
                OccupancyConnectivity.Cardinal6, allowFlying: false, path);

            Assert.IsFalse(found, "Start cell is Solid — no traversable path should exist.");
            Assert.AreEqual(0, path.Count, "Path list must stay empty on no-path.");
        }

        [Test]
        public void FindPath_Flyer_AllowsOpenNoFloor_NonFlyer_Does_Not()
        {
            var sdf = MakeHalfSpaceSdf();
            var grid = new OccupancyGrid(Vector3.zero, 8, 8, 8, CellSize);
            grid.BuildFromChunkSdf(Vector3Int.zero, ChunkSizeCells, sdf, ChunkSizeCells + 1);

            // gy=6 is OpenNoFloor (no solid below).
            Assume.That(grid.GetCell(4, 6, 4), Is.EqualTo(OccupancyCell.OpenNoFloor));

            var path = new List<Vector3Int>();
            bool flyerFound = grid.TryFindPath(
                new Vector3Int(0, 6, 0), new Vector3Int(7, 6, 7),
                OccupancyConnectivity.Full26, allowFlying: true, path);
            Assert.IsTrue(flyerFound, "Flyer must reach the goal through OpenNoFloor cells.");

            path.Clear();
            bool grounderFound = grid.TryFindPath(
                new Vector3Int(0, 6, 0), new Vector3Int(7, 6, 7),
                OccupancyConnectivity.Cardinal6, allowFlying: false, path);
            Assert.IsFalse(grounderFound,
                "Non-flyer must not be able to start in an OpenNoFloor cell.");
        }

        // ------------------------------------------------------------------
        // Incremental updates — building one chunk's slice only touches
        // its own cells (plus the +Y floor-pass overlap).
        // ------------------------------------------------------------------

        [Test]
        public void BuildFromChunkSdf_OneChunk_DoesNotTouchOtherChunksCells()
        {
            // 2×1×2 chunk grid (occupancy 16×8×16). Build chunk (0,0,0)
            // from a half-space SDF — that should mark its 8×8×8 slice.
            // Build chunk (1,0,0) from an all-exterior SDF — its slice
            // should be Open / OpenNoFloor. Chunk (0,0,0)'s cells must
            // be unchanged after chunk (1,0,0)'s pass.
            var grid = new OccupancyGrid(Vector3.zero, 16, 8, 16, CellSize);

            var halfSpaceSdf = MakeHalfSpaceSdf();
            var exteriorSdf = MakeAllExteriorSdf();

            grid.BuildFromChunkSdf(new Vector3Int(0, 0, 0), ChunkSizeCells, halfSpaceSdf, ChunkSizeCells + 1);

            // Snapshot chunk (0,0,0)'s region before the second build.
            var snapshot = new OccupancyCell[8 * 8 * 8];
            for (int gz = 0; gz < 8; gz++)
            for (int gy = 0; gy < 8; gy++)
            for (int gx = 0; gx < 8; gx++)
                snapshot[(gz * 8 + gy) * 8 + gx] = grid.GetCell(gx, gy, gz);

            grid.BuildFromChunkSdf(new Vector3Int(1, 0, 0), ChunkSizeCells, exteriorSdf, ChunkSizeCells + 1);

            // Re-check chunk (0,0,0): must match snapshot.
            for (int gz = 0; gz < 8; gz++)
            for (int gy = 0; gy < 8; gy++)
            for (int gx = 0; gx < 8; gx++)
            {
                OccupancyCell now = grid.GetCell(gx, gy, gz);
                OccupancyCell before = snapshot[(gz * 8 + gy) * 8 + gx];
                Assert.AreEqual(before, now,
                    $"Chunk (0,0,0) cell ({gx},{gy},{gz}) changed from {before} to {now} after a (1,0,0) build.");
            }

            // And chunk (1,0,0)'s slice should be classified per the
            // exterior SDF (open, no floor below since gy=0 is at the
            // grid bottom).
            Assert.AreEqual(OccupancyCell.OpenNoFloor, grid.GetCell(8, 0, 0),
                "Chunk (1,0,0) bottom cell from exterior SDF should be OpenNoFloor.");
        }
    }
}
