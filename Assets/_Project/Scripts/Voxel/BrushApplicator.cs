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
                    // Phase 3 will implement this when DrillBlock lands.
                    return 0;
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
    }
}
