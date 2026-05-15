using Robogame.Core;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Applies a <see cref="BrushOp"/> to a chunk's SDF buffer via max-fold,
    /// per the monotonicity invariant in TERRAFORMING_PLAN.md §2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sign convention (TERRAFORMING_PLAN §3): <c>sdf &lt; 0</c> = interior
    /// (solid), <c>sdf &gt;= 0</c> = exterior (empty). For a "subtract" brush
    /// (digging), the brush value is positive inside the brush volume — the
    /// brush wants those cells to become exterior. Outside the brush volume
    /// the brush value is negative, and <c>max(currentSdf, brushValue)</c>
    /// leaves the cell unchanged.
    /// </para>
    /// <para>
    /// Phase 1c: takes <see cref="NativeArray{T}"/> for consistency with the
    /// Burst-compiled mesher. The applicator itself is plain managed C# —
    /// brush ops touch hundreds of cells per call, negligible against the
    /// meshing cost. Phase 3+ may Burst-ify if drill-per-tick rates make
    /// it worth the friction.
    /// </para>
    /// <para>
    /// Phase 1b shipped <see cref="BrushKind.SphereSubtract"/> only;
    /// <see cref="BrushKind.CapsuleSubtract"/> is Phase 3 when drill blocks
    /// emit swept-volume brushes.
    /// </para>
    /// </remarks>
    public static class BrushApplicator
    {
        /// <summary>
        /// Apply <paramref name="op"/> to <paramref name="sdf"/>. Returns
        /// the number of cells whose SDF value changed.
        /// </summary>
        public static int Apply(BrushOp op, NativeArray<sbyte> sdf, int dim, float cellSize, Vector3 chunkOriginWorld)
        {
            switch (op.kind)
            {
                case BrushKind.SphereSubtract:
                    return ApplySphereSubtract(op, sdf, dim, cellSize, chunkOriginWorld);
                case BrushKind.CapsuleSubtract:
                    return ApplyCapsuleSubtract(op, sdf, dim, cellSize, chunkOriginWorld);
                default:
                    return 0;
            }
        }

        private static int ApplySphereSubtract(BrushOp op, NativeArray<sbyte> sdf, int dim, float cellSize, Vector3 chunkOriginWorld)
        {
            Vector3 centreWorld = op.p0.ToVector3();
            Vector3 centreCells = (centreWorld - chunkOriginWorld) / cellSize;
            float radiusCells = op.RadiusMeters / cellSize;

            if (radiusCells <= 0f) return 0;

            int x0 = Mathf.Max(0,       Mathf.FloorToInt(centreCells.x - radiusCells));
            int x1 = Mathf.Min(dim - 1, Mathf.CeilToInt (centreCells.x + radiusCells));
            int y0 = Mathf.Max(0,       Mathf.FloorToInt(centreCells.y - radiusCells));
            int y1 = Mathf.Min(dim - 1, Mathf.CeilToInt (centreCells.y + radiusCells));
            int z0 = Mathf.Max(0,       Mathf.FloorToInt(centreCells.z - radiusCells));
            int z1 = Mathf.Min(dim - 1, Mathf.CeilToInt (centreCells.z + radiusCells));
            if (x0 > x1 || y0 > y1 || z0 > z1) return 0;

            const float SbyteUnitsPerCell = 64f;
            int dimSq = dim * dim;
            int changedCount = 0;

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - centreCells.x;
                float dy = y - centreCells.y;
                float dz = z - centreCells.z;
                float distCells = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                float brushValueCells = radiusCells - distCells;

                int brushValueSbyte = Mathf.RoundToInt(brushValueCells * SbyteUnitsPerCell);
                if (brushValueSbyte < sbyte.MinValue) brushValueSbyte = sbyte.MinValue;
                else if (brushValueSbyte > sbyte.MaxValue) brushValueSbyte = sbyte.MaxValue;

                int idx = z * dimSq + y * dim + x;
                if (brushValueSbyte > sdf[idx])
                {
                    sdf[idx] = (sbyte)brushValueSbyte;
                    changedCount++;
                }
            }

            return changedCount;
        }

        /// <summary>
        /// CapsuleSubtract: a swept-volume brush from <c>p0</c> to <c>p1</c>
        /// with constant <c>radius</c>. Used by the Phase 3 <c>DrillBlock</c>
        /// which emits one capsule per FixedUpdate tick covering the drill
        /// tip's motion. Degenerates to <see cref="ApplySphereSubtract"/>
        /// when <c>p0 == p1</c>.
        /// </summary>
        private static int ApplyCapsuleSubtract(BrushOp op, NativeArray<sbyte> sdf, int dim, float cellSize, Vector3 chunkOriginWorld)
        {
            Vector3 p0World = op.p0.ToVector3();
            Vector3 p1World = op.p1.ToVector3();
            Vector3 p0Cells = (p0World - chunkOriginWorld) / cellSize;
            Vector3 p1Cells = (p1World - chunkOriginWorld) / cellSize;
            float radiusCells = op.RadiusMeters / cellSize;

            if (radiusCells <= 0f) return 0;

            // AABB enclosing both endpoint spheres, clipped to the chunk.
            float minX = Mathf.Min(p0Cells.x, p1Cells.x) - radiusCells;
            float maxX = Mathf.Max(p0Cells.x, p1Cells.x) + radiusCells;
            float minY = Mathf.Min(p0Cells.y, p1Cells.y) - radiusCells;
            float maxY = Mathf.Max(p0Cells.y, p1Cells.y) + radiusCells;
            float minZ = Mathf.Min(p0Cells.z, p1Cells.z) - radiusCells;
            float maxZ = Mathf.Max(p0Cells.z, p1Cells.z) + radiusCells;

            int x0 = Mathf.Max(0,       Mathf.FloorToInt(minX));
            int x1 = Mathf.Min(dim - 1, Mathf.CeilToInt (maxX));
            int y0 = Mathf.Max(0,       Mathf.FloorToInt(minY));
            int y1 = Mathf.Min(dim - 1, Mathf.CeilToInt (maxY));
            int z0 = Mathf.Max(0,       Mathf.FloorToInt(minZ));
            int z1 = Mathf.Min(dim - 1, Mathf.CeilToInt (maxZ));
            if (x0 > x1 || y0 > y1 || z0 > z1) return 0;

            const float SbyteUnitsPerCell = 64f;
            int dimSq = dim * dim;
            int changedCount = 0;

            Vector3 axisCells = p1Cells - p0Cells;
            float axisLengthSq = axisCells.sqrMagnitude;
            const float DegenerateAxisEpsilon = 1e-6f;

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float distCells;
                if (axisLengthSq < DegenerateAxisEpsilon)
                {
                    // Degenerate capsule (zero-length axis) — point distance to p0.
                    float dx = x - p0Cells.x;
                    float dy = y - p0Cells.y;
                    float dz = z - p0Cells.z;
                    distCells = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                }
                else
                {
                    // Project (cell - p0) onto axis, clamp parametric to [0,1],
                    // distance from cell to the resulting closest point on segment.
                    float relX = x - p0Cells.x;
                    float relY = y - p0Cells.y;
                    float relZ = z - p0Cells.z;
                    float t = (relX * axisCells.x + relY * axisCells.y + relZ * axisCells.z) / axisLengthSq;
                    if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                    float closestX = p0Cells.x + axisCells.x * t;
                    float closestY = p0Cells.y + axisCells.y * t;
                    float closestZ = p0Cells.z + axisCells.z * t;
                    float dx = x - closestX;
                    float dy = y - closestY;
                    float dz = z - closestZ;
                    distCells = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                }

                float brushValueCells = radiusCells - distCells;
                int brushValueSbyte = Mathf.RoundToInt(brushValueCells * SbyteUnitsPerCell);
                if (brushValueSbyte < sbyte.MinValue) brushValueSbyte = sbyte.MinValue;
                else if (brushValueSbyte > sbyte.MaxValue) brushValueSbyte = sbyte.MaxValue;

                int idx = z * dimSq + y * dim + x;
                if (brushValueSbyte > sdf[idx])
                {
                    sdf[idx] = (sbyte)brushValueSbyte;
                    changedCount++;
                }
            }

            return changedCount;
        }
    }
}
