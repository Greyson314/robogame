using NUnit.Framework;
using Robogame.Core;
using Robogame.Voxel;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Tests.PlayMode.Voxel
{
    /// <summary>
    /// Machine gate for the full-footprint, heightmap-seeded diggable
    /// ground (docs/changes/83): the voxel surface follows the same
    /// heightmap the grass mesh is baked from, the zone covers the whole
    /// arena, you can dig anywhere (not just an authored cube), and the
    /// worst-case triangle budget holds.
    /// </summary>
    public sealed class DigZoneHeightmapTests
    {
        private GameObject _go;
        private DigZone _zone;

        private DigZone MakeZone(
            Vector3 worldPos, Vector3Int grid, int chunkCells, float cellSize,
            HeightmapParams hp)
        {
            _go = new GameObject("TestHeightmapZone");
            _go.transform.position = worldPos;
            _go.SetActive(false);
            _zone = _go.AddComponent<DigZone>();
            _zone.ChunkGridSize = grid;
            _zone.ChunkSizeCells = chunkCells;
            _zone.CellSize = cellSize;
            _zone.SurfaceHeightmap = hp;
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

        private static HeightmapParams Flat() => new HeightmapParams
        {
            Enabled = true, // all amps zero → Sample == 0 everywhere
        };

        private static HeightmapParams PureHills(float ampLow) => new HeightmapParams
        {
            Enabled = true,
            NoiseOffset = new Vector2(137.31f, 91.47f),
            HillFreqLow = 0.03f,
            HillAmpLow = ampLow,
            HillFreqHigh = 0.09f,
            HillAmpHigh = ampLow * 0.2f,
            FlatRadius = 0f,            // inner falloff ≈ 1 everywhere
            RampOuter = 0f,
            EdgeFlatStart = 1e6f,
            EdgeFlatEnd = 1e6f + 1f,   // outer falloff ≈ 1 everywhere
        };

        private static BrushOp Sphere(Vector3 centre, float r) => new BrushOp
        {
            kind = BrushKind.SphereSubtract,
            serverTick = 0,
            p0 = Vector3Fixed.FromVector3(centre),
            p1 = Vector3Fixed.FromVector3(centre),
            radiusFixed = (ushort)Mathf.Clamp(
                Mathf.RoundToInt(r * Vector3Fixed.UnitsPerMeter), 0, ushort.MaxValue),
        };

        /// <summary>World-Y of the highest solid sample in the column at
        /// (worldX,worldZ), or float.NaN if the column is fully empty.
        /// Assumes a single-Y-chunk grid (true for the arena floor).</summary>
        private float TopSolidWorldY(DigZone zone, float worldX, float worldZ, int chunkCells, float cellSize)
        {
            Vector3 zp = zone.transform.position;
            int cx = Mathf.FloorToInt((worldX - zp.x) / (chunkCells * cellSize));
            int cz = Mathf.FloorToInt((worldZ - zp.z) / (chunkCells * cellSize));
            DigChunk chunk = zone.GetChunk(cx, 0, cz);
            Assert.IsNotNull(chunk, $"No chunk for column ({worldX},{worldZ}).");

            int dim = chunkCells + 1;
            int dimSq = dim * dim;
            float chunkOriginX = zp.x + cx * chunkCells * cellSize;
            float chunkOriginZ = zp.z + cz * chunkCells * cellSize;
            int lx = Mathf.Clamp(Mathf.RoundToInt((worldX - chunkOriginX) / cellSize), 0, dim - 1);
            int lz = Mathf.Clamp(Mathf.RoundToInt((worldZ - chunkOriginZ) / cellSize), 0, dim - 1);

            NativeArray<sbyte> sdf = chunk.Sdf;
            for (int y = dim - 1; y >= 0; y--)
            {
                if (sdf[lz * dimSq + y * dim + lx] < 0)
                    return zp.y + y * cellSize;
            }
            return float.NaN;
        }

        // ------------------------------------------------------------------

        [Test]
        public void FlatHeightmap_SeedsSolidBelow_ExteriorAbove()
        {
            // Flat surface sits at y ≈ -_surfaceSinkMeters (0.25 default).
            // Zone y-span: [-4, +4].
            DigZone zone = MakeZone(new Vector3(0f, -4f, 0f),
                new Vector3Int(2, 1, 2), chunkCells: 8, cellSize: 1.0f, Flat());

            DigChunk chunk = zone.GetChunk(0, 0, 0);
            int dim = 9, dimSq = dim * dim;
            // interior column x=4,z=4 (away from forced-exterior faces).
            int Idx(int y) => 4 * dimSq + y * dim + 4;

            // worldY at globalY=1 is -3 (well below surface) → solid.
            Assert.Less(chunk.Sdf[Idx(1)], (sbyte)0, "Below the surface must be solid.");
            // worldY at globalY=7 is +3 (well above surface) → exterior.
            Assert.GreaterOrEqual(chunk.Sdf[Idx(7)], (sbyte)0, "Above the surface must be empty.");
        }

        [Test]
        public void SlopedHeightmap_TopSolidTracksSampledHeight()
        {
            var hp = PureHills(ampLow: 10f);
            DigZone zone = MakeZone(new Vector3(-16f, -8f, -16f),
                new Vector3Int(2, 1, 2), chunkCells: 8, cellSize: 2.0f, hp);

            // Find the two in-zone columns with the largest sampled-height
            // delta, then assert the carved voxel surface ranks them the
            // same way (surface FOLLOWS the heightmap).
            float bestDelta = 0f;
            Vector3 lo = default, hiP = default;
            for (float x = -14f; x <= 14f; x += 4f)
            for (float z = -14f; z <= 14f; z += 4f)
            for (float x2 = -14f; x2 <= 14f; x2 += 4f)
            for (float z2 = -14f; z2 <= 14f; z2 += 4f)
            {
                float ha = HeightmapField.Sample(hp, x, z);
                float hb = HeightmapField.Sample(hp, x2, z2);
                if (hb - ha > bestDelta) { bestDelta = hb - ha; lo = new Vector3(x, 0, z); hiP = new Vector3(x2, 0, z2); }
            }

            Assert.Greater(bestDelta, 3.0f,
                "Test needs a pair of columns > 1 cell apart in height; pick a stronger hill if this fails.");

            float yLo = TopSolidWorldY(zone, lo.x, lo.z, 8, 2.0f);
            float yHi = TopSolidWorldY(zone, hiP.x, hiP.z, 8, 2.0f);
            Assert.IsFalse(float.IsNaN(yLo) || float.IsNaN(yHi), "Both columns must have solid ground.");
            Assert.Greater(yHi, yLo,
                "The voxel surface must be higher where the heightmap is higher.");
        }

        [Test]
        public void FullArenaConfig_CoversArena_AndContainsPlay()
        {
            // Real 6×1×6 grid shape, coarsened cells so 36 chunks mesh
            // fast: 8 cells × 4 m = 32 m/chunk → 192 m footprint, same as
            // the shipped 32-cell × 1 m config.
            DigZone zone = MakeZone(new Vector3(-96f, -16f, -96f),
                new Vector3Int(6, 1, 6), chunkCells: 8, cellSize: 4.0f, Flat());

            Assert.AreEqual(36, zone.ChunkCount, "6×1×6 must spawn 36 chunks.");
            Bounds b = zone.WorldBounds;
            Assert.AreEqual(192f, b.size.x, 0.01f);
            Assert.AreEqual(192f, b.size.z, 0.01f);
            Assert.AreEqual(32f, b.size.y, 0.01f);
            Assert.IsTrue(zone.ContainsPoint(new Vector3(0f, 0f, 0f)), "Arena origin must be inside.");
            Assert.IsTrue(zone.ContainsPoint(new Vector3(90f, 0f, -90f)), "Far playfield must be inside.");
            Assert.IsFalse(zone.ContainsPoint(new Vector3(300f, 0f, 0f)), "Well outside must be outside.");
        }

        [Test]
        public void DrillAnywhere_FarFromCentre_CarvesGround()
        {
            // Pre-83 only a tiny authored cube was diggable. Now any
            // surface column is. Dig at (70,·,70), far from the old cube.
            DigZone zone = MakeZone(new Vector3(-96f, -16f, -96f),
                new Vector3Int(6, 1, 6), chunkCells: 8, cellSize: 4.0f, Flat());

            // Flat surface ≈ y = -0.25; centre the brush on it.
            int changed = zone.ApplyBrush(Sphere(new Vector3(70f, 0f, 70f), 6f));
            Assert.Greater(changed, 0, "The ground must be diggable far from the arena centre.");
        }

        [Test]
        public void WorstCaseTriangleBudget_HoldsForFullGrid()
        {
            // Per-chunk proxy: a populated + roughened 32³ chunk must stay
            // under the plan's ~20 K-tri worst-case, so the 36-chunk grid
            // stays under the 1.5 M target.
            DigZone zone = MakeZone(new Vector3(0f, -16f, 0f),
                new Vector3Int(1, 1, 1), chunkCells: 32, cellSize: 1.0f, Flat());

            // Roughen the surface with overlapping bites to push tri count
            // toward the realistic carved worst case.
            for (int i = 0; i < 6; i++)
                zone.ApplyBrush(Sphere(new Vector3(8f + i * 3f, -2f, 8f + i * 2f), 5f));

            DigChunk chunk = zone.GetChunk(0, 0, 0);
            int tris = chunk.CurrentMesh.triangles.Length / 3;
            const int PerChunkWorstCase = 20000;
            Assert.Less(tris, PerChunkWorstCase,
                $"Single chunk emitted {tris} tris; over the {PerChunkWorstCase} worst-case proxy.");
            Assert.Less(PerChunkWorstCase * 36, 1_500_000,
                "36-chunk grid at worst case must stay under the 1.5 M triangle target.");
        }
    }
}
