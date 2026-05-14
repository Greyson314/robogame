using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Naive Surface Nets meshing of a regular SDF grid. One vertex per
    /// active cell positioned at the centroid of zero-crossings on the
    /// cell's 12 edges; one quad per active grid edge (connecting the 4
    /// cells incident on that edge).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference: Mikola Lysenko, <i>Smooth voxel terrain (part 2)</i>
    /// (https://0fps.net/2012/07/12/smooth-voxel-terrain-part-2/). Plain
    /// managed C# at this phase — Phase 1c ports to Burst-compiled
    /// IJobParallelFor for the target sub-millisecond budget. See
    /// TERRAFORMING_PLAN.md §5 "Meshing pipeline" and the phased rollout
    /// in §12.
    /// </para>
    /// <para>
    /// Input is a regular grid of <c>sbyte</c> SDF samples sized <c>dim³</c>;
    /// the meshed surface lies between samples whose signs differ
    /// (<c>sdf &lt; 0</c> = interior, <c>sdf &gt;= 0</c> = exterior). Output
    /// vertices are in <b>cell-grid units</b>; the caller scales by cell
    /// size and adds the chunk origin to convert to world space.
    /// </para>
    /// <para>
    /// Single-chunk only — this layer is unaware of inter-chunk aprons.
    /// Phase 2 wraps this in a chunk pipeline that supplies the outer
    /// layer of <c>sdf</c> from neighbour data so vertex positions on
    /// shared edges agree between adjacent chunks.
    /// </para>
    /// </remarks>
    public static class SurfaceNetsMesher
    {
        /// <summary>Sentinel for "cell has no vertex". Stored in <see cref="Buffers.CellToVertex"/>.</summary>
        public const int InactiveCell = -1;

        /// <summary>
        /// Number of cells for a grid of <paramref name="dim"/> samples per
        /// side. A grid of N samples has (N-1)³ cells.
        /// </summary>
        public static int MaxCellsForDim(int dim)
        {
            int n = dim - 1;
            return n * n * n;
        }

        /// <summary>Vertex upper bound. One vertex per active cell, all cells could theoretically be active.</summary>
        public static int MaxVerticesForDim(int dim) => MaxCellsForDim(dim);

        /// <summary>
        /// Index upper bound. Three axes of edges × roughly one quad per
        /// active edge × 6 indices per quad. Conservative; real chunks
        /// emit far fewer.
        /// </summary>
        public static int MaxIndicesForDim(int dim) => 18 * MaxCellsForDim(dim);

        /// <summary>
        /// Caller-owned working buffers. Production callers reuse a single
        /// <see cref="Buffers"/> across remeshes to keep allocations off
        /// the steady-state path; tests construct fresh ones via
        /// <see cref="Allocate"/>.
        /// </summary>
        public readonly struct Buffers
        {
            /// <summary>One slot per cell, indexed in z-major order. Stores the vertex index emitted for that cell, or <see cref="InactiveCell"/>.</summary>
            public readonly int[] CellToVertex;

            /// <summary>Output vertices in cell-grid units. Caller scales by cell size.</summary>
            public readonly Vector3[] Vertices;

            /// <summary>Output triangle indices (3 per triangle, 6 per quad).</summary>
            public readonly int[] Indices;

            public Buffers(int[] cellToVertex, Vector3[] vertices, int[] indices)
            {
                CellToVertex = cellToVertex;
                Vertices = vertices;
                Indices = indices;
            }
        }

        /// <summary>Allocate working buffers sized for an SDF grid of <paramref name="dim"/> samples per side.</summary>
        public static Buffers Allocate(int dim)
        {
            return new Buffers(
                cellToVertex: new int[MaxCellsForDim(dim)],
                vertices: new Vector3[MaxVerticesForDim(dim)],
                indices: new int[MaxIndicesForDim(dim)]);
        }

        /// <summary>
        /// Mesh a regular SDF grid. <paramref name="vertexCount"/> and
        /// <paramref name="indexCount"/> report how many entries of the
        /// output buffers are populated.
        /// </summary>
        /// <param name="sdf">Sample grid, length must be <c>dim*dim*dim</c>, z-major order: <c>sdf[z*dim*dim + y*dim + x]</c>.</param>
        /// <param name="dim">Samples per side. Must be ≥ 2.</param>
        /// <param name="buffers">Caller-owned working buffers (see <see cref="Allocate"/>).</param>
        /// <param name="vertexCount">How many entries of <see cref="Buffers.Vertices"/> are valid output.</param>
        /// <param name="indexCount">How many entries of <see cref="Buffers.Indices"/> are valid output (multiple of 3).</param>
        public static void Mesh(
            sbyte[] sdf,
            int dim,
            Buffers buffers,
            out int vertexCount,
            out int indexCount)
        {
            int cellDim = dim - 1;
            int cellCount = cellDim * cellDim * cellDim;

            int[] cellToVertex = buffers.CellToVertex;
            Vector3[] vertices = buffers.Vertices;
            int[] indices = buffers.Indices;

            for (int i = 0; i < cellCount; i++) cellToVertex[i] = InactiveCell;

            // ----- Pass 1: active cells and their vertex positions. -----
            int vCount = 0;

            int dimSq = dim * dim;

            for (int cz = 0; cz < cellDim; cz++)
            for (int cy = 0; cy < cellDim; cy++)
            for (int cx = 0; cx < cellDim; cx++)
            {
                // Index of corner (cx + dx, cy + dy, cz + dz) where each d ∈ {0, 1}.
                int b000 = cz * dimSq + cy * dim + cx;
                int b100 = b000 + 1;
                int b010 = b000 + dim;
                int b110 = b010 + 1;
                int b001 = b000 + dimSq;
                int b101 = b001 + 1;
                int b011 = b001 + dim;
                int b111 = b011 + 1;

                int s000 = sdf[b000];
                int s100 = sdf[b100];
                int s010 = sdf[b010];
                int s110 = sdf[b110];
                int s001 = sdf[b001];
                int s101 = sdf[b101];
                int s011 = sdf[b011];
                int s111 = sdf[b111];

                // Sign-bit mask of the 8 corners. 0x00 = all exterior, 0xFF = all interior;
                // any other value means the cell straddles the surface.
                int mask =
                    (s000 < 0 ? 0x01 : 0) |
                    (s100 < 0 ? 0x02 : 0) |
                    (s010 < 0 ? 0x04 : 0) |
                    (s110 < 0 ? 0x08 : 0) |
                    (s001 < 0 ? 0x10 : 0) |
                    (s101 < 0 ? 0x20 : 0) |
                    (s011 < 0 ? 0x40 : 0) |
                    (s111 < 0 ? 0x80 : 0);

                if (mask == 0x00 || mask == 0xFF) continue;

                Vector3 vertSum = Vector3.zero;
                int crossingCount = 0;

                // Twelve edges: 4 along X, 4 along Y, 4 along Z. Each edge is
                // (corner_a, corner_b) where b = a + axis. We compute the
                // parametric zero crossing if a and b have different signs.
                AccumulateCrossing(s000, s100, cx,     cy,     cz,     1, 0, 0, ref vertSum, ref crossingCount);
                AccumulateCrossing(s010, s110, cx,     cy + 1, cz,     1, 0, 0, ref vertSum, ref crossingCount);
                AccumulateCrossing(s001, s101, cx,     cy,     cz + 1, 1, 0, 0, ref vertSum, ref crossingCount);
                AccumulateCrossing(s011, s111, cx,     cy + 1, cz + 1, 1, 0, 0, ref vertSum, ref crossingCount);

                AccumulateCrossing(s000, s010, cx,     cy,     cz,     0, 1, 0, ref vertSum, ref crossingCount);
                AccumulateCrossing(s100, s110, cx + 1, cy,     cz,     0, 1, 0, ref vertSum, ref crossingCount);
                AccumulateCrossing(s001, s011, cx,     cy,     cz + 1, 0, 1, 0, ref vertSum, ref crossingCount);
                AccumulateCrossing(s101, s111, cx + 1, cy,     cz + 1, 0, 1, 0, ref vertSum, ref crossingCount);

                AccumulateCrossing(s000, s001, cx,     cy,     cz,     0, 0, 1, ref vertSum, ref crossingCount);
                AccumulateCrossing(s100, s101, cx + 1, cy,     cz,     0, 0, 1, ref vertSum, ref crossingCount);
                AccumulateCrossing(s010, s011, cx,     cy + 1, cz,     0, 0, 1, ref vertSum, ref crossingCount);
                AccumulateCrossing(s110, s111, cx + 1, cy + 1, cz,     0, 0, 1, ref vertSum, ref crossingCount);

                int cellIdx = cz * cellDim * cellDim + cy * cellDim + cx;
                cellToVertex[cellIdx] = vCount;
                vertices[vCount++] = vertSum / crossingCount;
            }

            vertexCount = vCount;

            // ----- Pass 2: emit quads for every active grid edge that has all 4 incident cells. -----
            int iCount = 0;
            int cellDimSq = cellDim * cellDim;

            // X-axis edges: connect (i,j,k) and (i+1,j,k). Incident cells: (i, j-1, k-1), (i, j, k-1), (i, j-1, k), (i, j, k).
            for (int k = 1; k < dim - 1; k++)
            for (int j = 1; j < dim - 1; j++)
            for (int i = 0; i < dim - 1; i++)
            {
                int idxA = k * dimSq + j * dim + i;
                int sA = sdf[idxA];
                int sB = sdf[idxA + 1];
                if ((sA < 0) == (sB < 0)) continue;

                int baseCell = k * cellDimSq + j * cellDim + i;
                int v00 = cellToVertex[baseCell - cellDimSq - cellDim];   // (i, j-1, k-1)
                int v10 = cellToVertex[baseCell - cellDimSq];             // (i, j,   k-1)
                int v11 = cellToVertex[baseCell];                         // (i, j,   k)
                int v01 = cellToVertex[baseCell - cellDim];               // (i, j-1, k)

                if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                if (sA < 0)
                {
                    indices[iCount++] = v00; indices[iCount++] = v10; indices[iCount++] = v11;
                    indices[iCount++] = v00; indices[iCount++] = v11; indices[iCount++] = v01;
                }
                else
                {
                    indices[iCount++] = v00; indices[iCount++] = v11; indices[iCount++] = v10;
                    indices[iCount++] = v00; indices[iCount++] = v01; indices[iCount++] = v11;
                }
            }

            // Y-axis edges: connect (i,j,k) and (i,j+1,k). Incident cells: (i-1, j, k-1), (i, j, k-1), (i-1, j, k), (i, j, k).
            for (int k = 1; k < dim - 1; k++)
            for (int j = 0; j < dim - 1; j++)
            for (int i = 1; i < dim - 1; i++)
            {
                int idxA = k * dimSq + j * dim + i;
                int sA = sdf[idxA];
                int sB = sdf[idxA + dim];
                if ((sA < 0) == (sB < 0)) continue;

                int baseCell = k * cellDimSq + j * cellDim + i;
                int v00 = cellToVertex[baseCell - cellDimSq - 1];   // (i-1, j, k-1)
                int v10 = cellToVertex[baseCell - cellDimSq];       // (i,   j, k-1)
                int v11 = cellToVertex[baseCell];                   // (i,   j, k)
                int v01 = cellToVertex[baseCell - 1];               // (i-1, j, k)

                if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                if (sA < 0)
                {
                    indices[iCount++] = v00; indices[iCount++] = v11; indices[iCount++] = v10;
                    indices[iCount++] = v00; indices[iCount++] = v01; indices[iCount++] = v11;
                }
                else
                {
                    indices[iCount++] = v00; indices[iCount++] = v10; indices[iCount++] = v11;
                    indices[iCount++] = v00; indices[iCount++] = v11; indices[iCount++] = v01;
                }
            }

            // Z-axis edges: connect (i,j,k) and (i,j,k+1). Incident cells: (i-1, j-1, k), (i, j-1, k), (i-1, j, k), (i, j, k).
            for (int k = 0; k < dim - 1; k++)
            for (int j = 1; j < dim - 1; j++)
            for (int i = 1; i < dim - 1; i++)
            {
                int idxA = k * dimSq + j * dim + i;
                int sA = sdf[idxA];
                int sB = sdf[idxA + dimSq];
                if ((sA < 0) == (sB < 0)) continue;

                int baseCell = k * cellDimSq + j * cellDim + i;
                int v00 = cellToVertex[baseCell - cellDim - 1];   // (i-1, j-1, k)
                int v10 = cellToVertex[baseCell - cellDim];       // (i,   j-1, k)
                int v11 = cellToVertex[baseCell];                 // (i,   j,   k)
                int v01 = cellToVertex[baseCell - 1];             // (i-1, j,   k)

                if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                if (sA < 0)
                {
                    indices[iCount++] = v00; indices[iCount++] = v10; indices[iCount++] = v11;
                    indices[iCount++] = v00; indices[iCount++] = v11; indices[iCount++] = v01;
                }
                else
                {
                    indices[iCount++] = v00; indices[iCount++] = v11; indices[iCount++] = v10;
                    indices[iCount++] = v00; indices[iCount++] = v01; indices[iCount++] = v11;
                }
            }

            indexCount = iCount;
        }

        private static void AccumulateCrossing(
            int sA, int sB,
            int aX, int aY, int aZ,
            int dX, int dY, int dZ,
            ref Vector3 vertSum, ref int count)
        {
            if ((sA < 0) == (sB < 0)) return;
            // t ∈ (0, 1) is the parametric zero-crossing along the edge from
            // corner A (signed sA) to corner B (signed sB). For sA and sB of
            // different sign, sA - sB ≠ 0 so the division is safe.
            float t = sA / (float)(sA - sB);
            vertSum.x += aX + t * dX;
            vertSum.y += aY + t * dY;
            vertSum.z += aZ + t * dZ;
            count++;
        }
    }
}
