using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Coarse classification of a 2 m occupancy cell over voxel terrain.
    /// <see cref="Solid"/> = impassable (terrain). <see cref="OpenWithFloor"/>
    /// = open air with solid terrain directly below — ground AI can stand
    /// here. <see cref="OpenNoFloor"/> = open air with no floor — only
    /// flying AI may traverse.
    /// </summary>
    public enum OccupancyCell : byte
    {
        Solid = 0,
        OpenWithFloor = 1,
        OpenNoFloor = 2,
    }

    /// <summary>
    /// Neighbour topology for <see cref="OccupancyGrid.TryFindPath"/>.
    /// <see cref="Cardinal6"/> uses ±X, ±Y, ±Z only (good for floor-walking
    /// bots that don't fly diagonals). <see cref="Full26"/> includes all
    /// diagonals in a 3×3×3 — better for flying or grappling AI.
    /// </summary>
    public enum OccupancyConnectivity : byte
    {
        Cardinal6 = 0,
        Full26 = 1,
    }

    /// <summary>
    /// Coarse 3D occupancy grid for AI pathfinding over voxel terrain.
    /// Each cell covers a 4×4×4 voxel-cell block — 2 m on a side at the
    /// default 0.5 m voxel size. Per
    /// [`TERRAFORMING_PLAN.md` § 8](../../../docs/TERRAFORMING_PLAN.md#8-ai-pathing-on-voxel-terrain),
    /// 512 bytes per chunk; ~50 KB for a 100-chunk dig zone. Built
    /// incrementally from each chunk's SDF on remesh; A* search is
    /// over the global grid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Classification rule: an occupancy cell counts the 5×5×5 SDF samples
    /// covering its 4×4×4 voxel cells; if &gt; 50% are interior
    /// (sdf &lt; 0), the cell is <see cref="OccupancyCell.Solid"/>.
    /// Otherwise it's open, with floor designation following from the
    /// cell directly below in -Y: solid below → <see cref="OccupancyCell.OpenWithFloor"/>,
    /// otherwise <see cref="OccupancyCell.OpenNoFloor"/>. The -Y
    /// convention is for flat arenas; spherical-arena floor direction
    /// would need a per-cell gravity lookup, deferred.
    /// </para>
    /// <para>
    /// A* uses a linear-scan-for-min open set — O(N²) worst case per
    /// search, but for a 100-chunk zone (~50 K cells) the open set rarely
    /// exceeds a few thousand entries and the scan stays under a
    /// millisecond. A binary-heap upgrade is straightforward when the
    /// profile warrants it.
    /// </para>
    /// </remarks>
    public sealed class OccupancyGrid
    {
        public const int VoxelCellsPerOccupancyCell = 4;

        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }
        /// <summary>World-space size of a single occupancy cell (default 2 m).</summary>
        public float CellSize { get; }
        public Vector3 WorldOrigin { get; }

        private readonly byte[] _cells;
        private readonly int _sizeXY;

        public OccupancyGrid(Vector3 worldOrigin, int sizeX, int sizeY, int sizeZ, float voxelCellSize)
        {
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
                throw new System.ArgumentException($"OccupancyGrid dimensions must be positive ({sizeX}, {sizeY}, {sizeZ}).");

            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            CellSize = voxelCellSize * VoxelCellsPerOccupancyCell;
            WorldOrigin = worldOrigin;
            _cells = new byte[sizeX * sizeY * sizeZ];
            _sizeXY = sizeX * sizeY;
        }

        public int CellCount => _cells.Length;

        public bool IsInBounds(int gx, int gy, int gz) =>
            (uint)gx < (uint)SizeX && (uint)gy < (uint)SizeY && (uint)gz < (uint)SizeZ;

        public bool IsInBounds(Vector3Int g) => IsInBounds(g.x, g.y, g.z);

        /// <summary>
        /// Cell at the given grid index, or <see cref="OccupancyCell.Solid"/>
        /// for out-of-bounds queries (so pathfinders fail closed at zone
        /// edges instead of running off the grid).
        /// </summary>
        public OccupancyCell GetCell(int gx, int gy, int gz)
        {
            if (!IsInBounds(gx, gy, gz)) return OccupancyCell.Solid;
            return (OccupancyCell)_cells[FlatIndex(gx, gy, gz)];
        }

        public OccupancyCell GetCell(Vector3Int g) => GetCell(g.x, g.y, g.z);

        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 local = worldPos - WorldOrigin;
            return new Vector3Int(
                Mathf.FloorToInt(local.x / CellSize),
                Mathf.FloorToInt(local.y / CellSize),
                Mathf.FloorToInt(local.z / CellSize));
        }

        /// <summary>World-space center of the given cell.</summary>
        public Vector3 GridToWorld(int gx, int gy, int gz)
        {
            return WorldOrigin + new Vector3(
                (gx + 0.5f) * CellSize,
                (gy + 0.5f) * CellSize,
                (gz + 0.5f) * CellSize);
        }

        public Vector3 GridToWorld(Vector3Int g) => GridToWorld(g.x, g.y, g.z);

        /// <summary>
        /// Rebuild the slice of the occupancy grid covering one chunk.
        /// Two passes over the chunk's region: pass 1 classifies Solid vs
        /// Open by counting interior SDF samples; pass 2 promotes open
        /// cells to <see cref="OccupancyCell.OpenWithFloor"/> when the
        /// cell at -Y is solid. Pass 2 also revisits the row of cells
        /// directly above this chunk so a chunk dig that exposed new
        /// open air re-promotes the cell above into <see cref="OccupancyCell.OpenWithFloor"/>
        /// or back to <see cref="OccupancyCell.OpenNoFloor"/>.
        /// </summary>
        public void BuildFromChunkSdf(Vector3Int chunkCoord, int chunkSizeCells,
                                      NativeArray<sbyte> sdf, int sdfDim)
        {
            if (chunkSizeCells % VoxelCellsPerOccupancyCell != 0)
                throw new System.ArgumentException(
                    $"chunkSizeCells ({chunkSizeCells}) must be a multiple of " +
                    $"{VoxelCellsPerOccupancyCell} (the voxel-cells-per-occupancy-cell ratio).");

            int occPerChunk = chunkSizeCells / VoxelCellsPerOccupancyCell;
            int gStartX = chunkCoord.x * occPerChunk;
            int gStartY = chunkCoord.y * occPerChunk;
            int gStartZ = chunkCoord.z * occPerChunk;
            int sdfDimSq = sdfDim * sdfDim;

            // Total samples per 5×5×5 window: 125. Threshold: > 62 = >50%.
            const int totalSamples = (VoxelCellsPerOccupancyCell + 1) *
                                     (VoxelCellsPerOccupancyCell + 1) *
                                     (VoxelCellsPerOccupancyCell + 1);
            const int solidThreshold = totalSamples / 2;

            for (int dgz = 0; dgz < occPerChunk; dgz++)
            for (int dgy = 0; dgy < occPerChunk; dgy++)
            for (int dgx = 0; dgx < occPerChunk; dgx++)
            {
                int baseSampleX = dgx * VoxelCellsPerOccupancyCell;
                int baseSampleY = dgy * VoxelCellsPerOccupancyCell;
                int baseSampleZ = dgz * VoxelCellsPerOccupancyCell;

                int solidCount = 0;
                for (int sz = 0; sz <= VoxelCellsPerOccupancyCell; sz++)
                for (int sy = 0; sy <= VoxelCellsPerOccupancyCell; sy++)
                for (int sx = 0; sx <= VoxelCellsPerOccupancyCell; sx++)
                {
                    int sdfIdx = (baseSampleZ + sz) * sdfDimSq +
                                 (baseSampleY + sy) * sdfDim +
                                 (baseSampleX + sx);
                    if (sdf[sdfIdx] < 0) solidCount++;
                }

                int gx = gStartX + dgx;
                int gy = gStartY + dgy;
                int gz = gStartZ + dgz;
                int flat = FlatIndex(gx, gy, gz);
                // Stash as OpenNoFloor for now; pass 2 promotes to
                // OpenWithFloor if applicable.
                _cells[flat] = (solidCount > solidThreshold)
                    ? (byte)OccupancyCell.Solid
                    : (byte)OccupancyCell.OpenNoFloor;
            }

            // Pass 2: floor designation. Walk this chunk's range plus the
            // single layer above (gy = end) so an excavation that newly
            // exposed open air re-classifies the cell above as it goes
            // from OpenWithFloor → OpenNoFloor (or vice versa when a
            // chunk fills its top row with solid material — though
            // dig-only rules out the latter; included for safety).
            int gEndY = gStartY + occPerChunk;
            int floorPassEndY = Mathf.Min(SizeY, gEndY + 1);
            for (int gz = gStartZ; gz < gStartZ + occPerChunk; gz++)
            for (int gy = gStartY; gy < floorPassEndY; gy++)
            for (int gx = gStartX; gx < gStartX + occPerChunk; gx++)
            {
                int flat = FlatIndex(gx, gy, gz);
                if (_cells[flat] == (byte)OccupancyCell.Solid) continue;

                bool hasFloor = false;
                if (gy > 0)
                {
                    int belowFlat = FlatIndex(gx, gy - 1, gz);
                    hasFloor = _cells[belowFlat] == (byte)OccupancyCell.Solid;
                }
                _cells[flat] = hasFloor
                    ? (byte)OccupancyCell.OpenWithFloor
                    : (byte)OccupancyCell.OpenNoFloor;
            }
        }

        /// <summary>
        /// Run an A* search from <paramref name="start"/> to
        /// <paramref name="goal"/> over the occupancy grid. Returns true
        /// and writes the cell-by-cell path (inclusive of both endpoints)
        /// to <paramref name="outPath"/>; returns false and leaves
        /// <paramref name="outPath"/> empty if no path exists. The
        /// out-list is cleared on entry.
        /// </summary>
        /// <param name="allowFlying">If true, <see cref="OccupancyCell.OpenNoFloor"/>
        /// cells are traversable (flying enemies). If false, only
        /// <see cref="OccupancyCell.OpenWithFloor"/> cells are traversable.</param>
        public bool TryFindPath(Vector3Int start, Vector3Int goal,
                                OccupancyConnectivity connectivity,
                                bool allowFlying,
                                List<Vector3Int> outPath)
        {
            if (outPath == null) throw new System.ArgumentNullException(nameof(outPath));
            outPath.Clear();
            if (!IsInBounds(start) || !IsInBounds(goal)) return false;
            if (!IsTraversable(start, allowFlying) || !IsTraversable(goal, allowFlying)) return false;

            int n = _cells.Length;
            int startIdx = FlatIndex(start.x, start.y, start.z);
            int goalIdx = FlatIndex(goal.x, goal.y, goal.z);
            if (startIdx == goalIdx)
            {
                outPath.Add(start);
                return true;
            }

            // Allocate the per-search buffers eagerly. For a 100-chunk
            // zone (~50 K cells) that's ~600 KB total — fine for an
            // editor / playmode test. Production usage will want pooled
            // buffers, but that's an optimisation, not a correctness
            // concern.
            var gScore = new float[n];
            var fScore = new float[n];
            var cameFrom = new int[n];
            var inOpen = new bool[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                gScore[i] = float.PositiveInfinity;
                fScore[i] = float.PositiveInfinity;
                cameFrom[i] = -1;
            }

            Vector3Int[] offsets = connectivity == OccupancyConnectivity.Cardinal6
                ? Cardinal6Offsets
                : Full26Offsets;
            float[] offsetCosts = connectivity == OccupancyConnectivity.Cardinal6
                ? Cardinal6Costs
                : Full26Costs;

            gScore[startIdx] = 0f;
            fScore[startIdx] = Heuristic(start, goal);
            var openList = new List<int> { startIdx };
            inOpen[startIdx] = true;

            while (openList.Count > 0)
            {
                // Linear scan for the lowest-f open entry.
                int bestPos = 0;
                float bestF = fScore[openList[0]];
                for (int i = 1; i < openList.Count; i++)
                {
                    int idx = openList[i];
                    if (fScore[idx] < bestF) { bestF = fScore[idx]; bestPos = i; }
                }
                int current = openList[bestPos];
                if (current == goalIdx)
                {
                    ReconstructPath(cameFrom, current, outPath);
                    return true;
                }

                // Swap-remove from open list.
                int last = openList.Count - 1;
                openList[bestPos] = openList[last];
                openList.RemoveAt(last);
                inOpen[current] = false;
                closed[current] = true;

                Vector3Int cp = FlatToVector3Int(current);
                for (int k = 0; k < offsets.Length; k++)
                {
                    Vector3Int np = cp + offsets[k];
                    if (!IsInBounds(np)) continue;
                    int nFlat = FlatIndex(np.x, np.y, np.z);
                    if (closed[nFlat]) continue;
                    if (!IsTraversable(np, allowFlying)) continue;

                    float tentativeG = gScore[current] + offsetCosts[k];
                    if (tentativeG < gScore[nFlat])
                    {
                        cameFrom[nFlat] = current;
                        gScore[nFlat] = tentativeG;
                        fScore[nFlat] = tentativeG + Heuristic(np, goal);
                        if (!inOpen[nFlat])
                        {
                            openList.Add(nFlat);
                            inOpen[nFlat] = true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsTraversable(Vector3Int g, bool allowFlying)
        {
            OccupancyCell c = GetCell(g);
            if (c == OccupancyCell.Solid) return false;
            if (c == OccupancyCell.OpenWithFloor) return true;
            return allowFlying;   // OpenNoFloor only for flyers.
        }

        private void ReconstructPath(int[] cameFrom, int goalFlat, List<Vector3Int> outPath)
        {
            int c = goalFlat;
            while (c >= 0)
            {
                outPath.Add(FlatToVector3Int(c));
                c = cameFrom[c];
            }
            outPath.Reverse();
        }

        private static float Heuristic(Vector3Int a, Vector3Int b)
        {
            int dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private int FlatIndex(int gx, int gy, int gz) => (gz * SizeY + gy) * SizeX + gx;

        private Vector3Int FlatToVector3Int(int flat)
        {
            int gz = flat / _sizeXY;
            int rem = flat - gz * _sizeXY;
            int gy = rem / SizeX;
            int gx = rem - gy * SizeX;
            return new Vector3Int(gx, gy, gz);
        }

        // -----------------------------------------------------------------
        // Neighbour offset tables for A*. Costs are unit-cell Euclidean —
        // 1 for cardinal, sqrt(2) for face-diagonal, sqrt(3) for full
        // diagonal. The Heuristic uses the same metric so A* admissibility
        // holds.
        // -----------------------------------------------------------------

        private static readonly Vector3Int[] Cardinal6Offsets =
        {
            new Vector3Int(-1,  0,  0), new Vector3Int( 1,  0,  0),
            new Vector3Int( 0, -1,  0), new Vector3Int( 0,  1,  0),
            new Vector3Int( 0,  0, -1), new Vector3Int( 0,  0,  1),
        };

        private static readonly float[] Cardinal6Costs = { 1f, 1f, 1f, 1f, 1f, 1f };

        private static readonly Vector3Int[] Full26Offsets = BuildFull26Offsets();
        private static readonly float[] Full26Costs = BuildFull26Costs();

        private static Vector3Int[] BuildFull26Offsets()
        {
            var arr = new Vector3Int[26];
            int i = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                arr[i++] = new Vector3Int(dx, dy, dz);
            }
            return arr;
        }

        private static float[] BuildFull26Costs()
        {
            var offs = Full26Offsets;
            var arr = new float[offs.Length];
            for (int i = 0; i < offs.Length; i++)
                arr[i] = Mathf.Sqrt(offs[i].sqrMagnitude);
            return arr;
        }
    }
}
