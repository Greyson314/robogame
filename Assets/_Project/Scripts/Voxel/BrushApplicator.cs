using Robogame.Core;
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
    /// The applicator iterates only the cells inside the brush's bounding
    /// box. Cells outside the AABB are not touched — that's both a perf
    /// optimisation and a correctness guard against the "deep-interior cells
    /// drifting toward zero" failure mode (a cell far from any brush should
    /// stay at its initial SDF, full stop).
    /// </para>
    /// <para>
    /// Phase 1b ships <see cref="BrushKind.SphereSubtract"/> only; Phase 3
    /// adds <see cref="BrushKind.CapsuleSubtract"/> when drill blocks emit
    /// swept-volume brushes.
    /// </para>
    /// </remarks>
    public static class BrushApplicator
    {
        /// <summary>
        /// Apply <paramref name="op"/> to <paramref name="sdf"/>. Returns
        /// the number of cells whose SDF value changed (useful for tests
        /// and dirty tracking).
        /// </summary>
        /// <param name="op">The brush op. <see cref="BrushOp.p0"/>, <see cref="BrushOp.p1"/>, and <see cref="BrushOp.radiusFixed"/> are in world space (1/256 m fixed-point).</param>
        /// <param name="sdf">SDF buffer, length must be <c>dim*dim*dim</c>, z-major order.</param>
        /// <param name="dim">Samples per chunk side (e.g. 33 for the default 32-cell chunk).</param>
        /// <param name="cellSize">Edge length of one cell in metres.</param>
        /// <param name="chunkOriginWorld">World-space position of the chunk's (0,0,0) corner.</param>
        public static int Apply(BrushOp op, sbyte[] sdf, int dim, float cellSize, Vector3 chunkOriginWorld)
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

        private static int ApplySphereSubtract(BrushOp op, sbyte[] sdf, int dim, float cellSize, Vector3 chunkOriginWorld)
        {
            // Centre and radius in cell-grid units (1 unit = 1 cell-edge = cellSize metres).
            Vector3 centreWorld = op.p0.ToVector3();
            Vector3 centreCells = (centreWorld - chunkOriginWorld) / cellSize;
            float radiusCells = op.RadiusMeters / cellSize;

            if (radiusCells <= 0f) return 0;

            // AABB in sample-index space, clipped to the chunk.
            int x0 = Mathf.Max(0,       Mathf.FloorToInt(centreCells.x - radiusCells));
            int x1 = Mathf.Min(dim - 1, Mathf.CeilToInt (centreCells.x + radiusCells));
            int y0 = Mathf.Max(0,       Mathf.FloorToInt(centreCells.y - radiusCells));
            int y1 = Mathf.Min(dim - 1, Mathf.CeilToInt (centreCells.y + radiusCells));
            int z0 = Mathf.Max(0,       Mathf.FloorToInt(centreCells.z - radiusCells));
            int z1 = Mathf.Min(dim - 1, Mathf.CeilToInt (centreCells.z + radiusCells));
            if (x0 > x1 || y0 > y1 || z0 > z1) return 0;   // brush misses the chunk

            // SDF storage scale: 1 sbyte unit = cellSize/64 m. A distance in
            // cell-grid units converts to sbyte units by multiplying by 64.
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
                float brushValueCells = radiusCells - distCells;   // > 0 inside the brush, < 0 outside

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
