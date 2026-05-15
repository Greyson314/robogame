using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Robogame.Voxel
{
    /// <summary>
    /// Naive Surface Nets meshing of a regular SDF grid, Burst-compiled.
    /// One vertex per active cell positioned at the centroid of zero-crossings
    /// on the cell's 12 edges; one quad per active grid edge (connecting the
    /// 4 cells incident on that edge).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference: Mikola Lysenko, <i>Smooth voxel terrain (part 2)</i>
    /// (https://0fps.net/2012/07/12/smooth-voxel-terrain-part-2/). Phase 1c
    /// port of the Phase 1a managed-C# implementation — see
    /// `docs/changes/64-terraforming-phase-1a.md`. Burst patterns + gotchas
    /// captured in `docs/BURST_NOTES.md`.
    /// </para>
    /// <para>
    /// Input is a regular grid of <c>sbyte</c> SDF samples sized <c>dim³</c>;
    /// the meshed surface lies between samples whose signs differ
    /// (<c>sdf &lt; 0</c> = interior, <c>sdf &gt;= 0</c> = exterior). Output
    /// vertices are in <b>cell-grid units</b>; the caller scales by cell
    /// size and adds the chunk origin to convert to world space.
    /// </para>
    /// <para>
    /// Caller-owned <see cref="Buffers"/> are <see cref="NativeArray{T}"/>s
    /// reused across remeshes. <see cref="Buffers.Dispose"/> on
    /// chunk destruction. The <see cref="MeshJob"/> is <c>IJob</c>
    /// (single-thread) — single-thread Burst+SIMD meets the Phase 1c
    /// &lt; 1 ms budget at dim=33 without paying parallel-coordination cost.
    /// </para>
    /// </remarks>
    public static class SurfaceNetsMesher
    {
        /// <summary>Sentinel for "cell has no vertex".</summary>
        public const int InactiveCell = -1;

        public static int MaxCellsForDim(int dim)
        {
            int n = dim - 1;
            return n * n * n;
        }

        public static int MaxVerticesForDim(int dim) => MaxCellsForDim(dim);

        /// <summary>Conservative index upper bound. 3 axes × 1 quad per edge × 6 indices per quad.</summary>
        public static int MaxIndicesForDim(int dim) => 18 * MaxCellsForDim(dim);

        /// <summary>
        /// Caller-owned working buffers. Allocate once per chunk via
        /// <see cref="Allocate"/> with <see cref="Allocator.Persistent"/>;
        /// reuse across remeshes; dispose on chunk destruction.
        /// </summary>
        public struct Buffers : IDisposable
        {
            /// <summary>One slot per cell, indexed z-major. Stores the emitted vertex index or <see cref="InactiveCell"/>.</summary>
            public NativeArray<int> CellToVertex;

            /// <summary>Output vertex positions in cell-grid units.</summary>
            public NativeArray<float3> Vertices;

            /// <summary>Output triangle indices (3 per triangle).</summary>
            public NativeArray<int> Indices;

            /// <summary>Length-2 result: [0] = vertex count, [1] = index count. Written by the job.</summary>
            public NativeArray<int> Counts;

            public int VertexCount => Counts.IsCreated ? Counts[0] : 0;
            public int IndexCount  => Counts.IsCreated ? Counts[1] : 0;

            public bool IsCreated => CellToVertex.IsCreated;

            public void Dispose()
            {
                if (CellToVertex.IsCreated) CellToVertex.Dispose();
                if (Vertices.IsCreated)     Vertices.Dispose();
                if (Indices.IsCreated)      Indices.Dispose();
                if (Counts.IsCreated)       Counts.Dispose();
            }
        }

        public static Buffers Allocate(int dim, Allocator allocator)
        {
            return new Buffers
            {
                CellToVertex = new NativeArray<int>(MaxCellsForDim(dim),     allocator, NativeArrayOptions.UninitializedMemory),
                Vertices     = new NativeArray<float3>(MaxVerticesForDim(dim), allocator, NativeArrayOptions.UninitializedMemory),
                Indices      = new NativeArray<int>(MaxIndicesForDim(dim),   allocator, NativeArrayOptions.UninitializedMemory),
                Counts       = new NativeArray<int>(2,                       allocator, NativeArrayOptions.ClearMemory),
            };
        }

        /// <summary>
        /// Synchronous mesh: schedule the Burst job and complete it. Vertex
        /// and index counts are also stored in <c>buffers.Counts</c>.
        /// </summary>
        /// <param name="cellScale">Multiplier applied to vertex positions in the
        /// job (default 1.0 = cell-grid units). Callers with a world-space
        /// cell size pass that value to get pre-scaled positions and skip a
        /// managed post-scale loop. Folding into the job is cheap on top of
        /// the existing per-vertex math.</param>
        public static void Mesh(
            NativeArray<sbyte> sdf,
            int dim,
            Buffers buffers,
            out int vertexCount,
            out int indexCount,
            float cellScale = 1.0f)
        {
            // Run() executes the Burst-compiled job synchronously on the
            // calling thread without going through the scheduler — avoids
            // any per-call scheduler allocations and is the right call for
            // the single-chunk synchronous path. Phase 2 multi-chunk uses
            // Schedule() for fan-out.
            new MeshJob
            {
                Sdf = sdf,
                Dim = dim,
                CellScale = cellScale,
                CellToVertex = buffers.CellToVertex,
                Vertices = buffers.Vertices,
                Indices = buffers.Indices,
                Counts = buffers.Counts,
            }.Run();
            vertexCount = buffers.Counts[0];
            indexCount  = buffers.Counts[1];
        }

        /// <summary>
        /// Schedule the Burst meshing job. Caller must <c>Complete()</c> the
        /// returned handle before reading <c>buffers.Counts</c> or the
        /// vertex/index arrays. Phase 2 multi-chunk meshing uses this for
        /// fan-out scheduling.
        /// </summary>
        public static JobHandle Schedule(
            NativeArray<sbyte> sdf,
            int dim,
            Buffers buffers,
            JobHandle dependency = default,
            float cellScale = 1.0f)
        {
            return ScheduleInternal(sdf, dim, buffers, cellScale, dependency);
        }

        private static JobHandle ScheduleInternal(
            NativeArray<sbyte> sdf,
            int dim,
            Buffers buffers,
            float cellScale,
            JobHandle dependency = default)
        {
            return new MeshJob
            {
                Sdf = sdf,
                Dim = dim,
                CellScale = cellScale,
                CellToVertex = buffers.CellToVertex,
                Vertices = buffers.Vertices,
                Indices = buffers.Indices,
                Counts = buffers.Counts,
            }.Schedule(dependency);
        }

        // -----------------------------------------------------------------
        // Burst job
        // -----------------------------------------------------------------

        [BurstCompile]
        private struct MeshJob : IJob
        {
            [ReadOnly] public NativeArray<sbyte> Sdf;
            public int Dim;
            public float CellScale;

            public NativeArray<int> CellToVertex;
            public NativeArray<float3> Vertices;
            public NativeArray<int> Indices;
            public NativeArray<int> Counts;

            public void Execute()
            {
                int cellDim   = Dim - 1;
                int dimSq     = Dim * Dim;
                int cellDimSq = cellDim * cellDim;
                int cellCount = cellDim * cellDimSq;

                // Reset cell→vertex sentinels so a reused buffer doesn't
                // leak vertex indices from the previous mesh.
                for (int i = 0; i < cellCount; i++) CellToVertex[i] = InactiveCell;

                // -------------------------------------------------------------
                // Pass 1 — active cells and their vertex positions.
                // -------------------------------------------------------------
                int vCount = 0;

                for (int cz = 0; cz < cellDim; cz++)
                for (int cy = 0; cy < cellDim; cy++)
                for (int cx = 0; cx < cellDim; cx++)
                {
                    int b000 = cz * dimSq + cy * Dim + cx;
                    int b100 = b000 + 1;
                    int b010 = b000 + Dim;
                    int b110 = b010 + 1;
                    int b001 = b000 + dimSq;
                    int b101 = b001 + 1;
                    int b011 = b001 + Dim;
                    int b111 = b011 + 1;

                    int s000 = Sdf[b000];
                    int s100 = Sdf[b100];
                    int s010 = Sdf[b010];
                    int s110 = Sdf[b110];
                    int s001 = Sdf[b001];
                    int s101 = Sdf[b101];
                    int s011 = Sdf[b011];
                    int s111 = Sdf[b111];

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

                    float3 vertSum = float3.zero;
                    int crossingCount = 0;

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

                    int cellIdx = cz * cellDimSq + cy * cellDim + cx;
                    CellToVertex[cellIdx] = vCount;
                    Vertices[vCount++] = (vertSum / crossingCount) * CellScale;
                }

                Counts[0] = vCount;

                // -------------------------------------------------------------
                // Pass 2 — quads for every active grid edge with 4 incident cells.
                // -------------------------------------------------------------
                int iCount = 0;

                // X-axis edges.
                for (int k = 1; k < Dim - 1; k++)
                for (int j = 1; j < Dim - 1; j++)
                for (int i = 0; i < Dim - 1; i++)
                {
                    int idxA = k * dimSq + j * Dim + i;
                    int sA = Sdf[idxA];
                    int sB = Sdf[idxA + 1];
                    if ((sA < 0) == (sB < 0)) continue;

                    int baseCell = k * cellDimSq + j * cellDim + i;
                    int v00 = CellToVertex[baseCell - cellDimSq - cellDim];
                    int v10 = CellToVertex[baseCell - cellDimSq];
                    int v11 = CellToVertex[baseCell];
                    int v01 = CellToVertex[baseCell - cellDim];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                    if (sA < 0)
                    {
                        Indices[iCount++] = v00; Indices[iCount++] = v10; Indices[iCount++] = v11;
                        Indices[iCount++] = v00; Indices[iCount++] = v11; Indices[iCount++] = v01;
                    }
                    else
                    {
                        Indices[iCount++] = v00; Indices[iCount++] = v11; Indices[iCount++] = v10;
                        Indices[iCount++] = v00; Indices[iCount++] = v01; Indices[iCount++] = v11;
                    }
                }

                // Y-axis edges.
                for (int k = 1; k < Dim - 1; k++)
                for (int j = 0; j < Dim - 1; j++)
                for (int i = 1; i < Dim - 1; i++)
                {
                    int idxA = k * dimSq + j * Dim + i;
                    int sA = Sdf[idxA];
                    int sB = Sdf[idxA + Dim];
                    if ((sA < 0) == (sB < 0)) continue;

                    int baseCell = k * cellDimSq + j * cellDim + i;
                    int v00 = CellToVertex[baseCell - cellDimSq - 1];
                    int v10 = CellToVertex[baseCell - cellDimSq];
                    int v11 = CellToVertex[baseCell];
                    int v01 = CellToVertex[baseCell - 1];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                    if (sA < 0)
                    {
                        Indices[iCount++] = v00; Indices[iCount++] = v11; Indices[iCount++] = v10;
                        Indices[iCount++] = v00; Indices[iCount++] = v01; Indices[iCount++] = v11;
                    }
                    else
                    {
                        Indices[iCount++] = v00; Indices[iCount++] = v10; Indices[iCount++] = v11;
                        Indices[iCount++] = v00; Indices[iCount++] = v11; Indices[iCount++] = v01;
                    }
                }

                // Z-axis edges.
                for (int k = 0; k < Dim - 1; k++)
                for (int j = 1; j < Dim - 1; j++)
                for (int i = 1; i < Dim - 1; i++)
                {
                    int idxA = k * dimSq + j * Dim + i;
                    int sA = Sdf[idxA];
                    int sB = Sdf[idxA + dimSq];
                    if ((sA < 0) == (sB < 0)) continue;

                    int baseCell = k * cellDimSq + j * cellDim + i;
                    int v00 = CellToVertex[baseCell - cellDim - 1];
                    int v10 = CellToVertex[baseCell - cellDim];
                    int v11 = CellToVertex[baseCell];
                    int v01 = CellToVertex[baseCell - 1];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                    if (sA < 0)
                    {
                        Indices[iCount++] = v00; Indices[iCount++] = v10; Indices[iCount++] = v11;
                        Indices[iCount++] = v00; Indices[iCount++] = v11; Indices[iCount++] = v01;
                    }
                    else
                    {
                        Indices[iCount++] = v00; Indices[iCount++] = v11; Indices[iCount++] = v10;
                        Indices[iCount++] = v00; Indices[iCount++] = v01; Indices[iCount++] = v11;
                    }
                }

                Counts[1] = iCount;
            }

            private static void AccumulateCrossing(
                int sA, int sB,
                int aX, int aY, int aZ,
                int dX, int dY, int dZ,
                ref float3 vertSum, ref int count)
            {
                if ((sA < 0) == (sB < 0)) return;
                float t = sA / (float)(sA - sB);
                vertSum.x += aX + t * dX;
                vertSum.y += aY + t * dY;
                vertSum.z += aZ + t * dZ;
                count++;
            }
        }
    }
}
