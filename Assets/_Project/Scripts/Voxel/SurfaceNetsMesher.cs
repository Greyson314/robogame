using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Robogame.Voxel
{
    /// <summary>
    /// Per-face strides telling the mesher how to handle LOD-boundary cells.
    /// Each field is the stride (in mesher cell-grid units) of a face's
    /// neighbour relative to this chunk's own meshing stride. A value of 1
    /// (the default) means no LOD difference on that face — emit normally.
    /// A value &gt; 1 means the neighbour is meshing at coarser resolution;
    /// the mesher snaps boundary-cell vertices to coarse-grid-aligned
    /// positions and suppresses the boundary face's outgoing quads so the
    /// coarser neighbour's mesh owns the seam.
    /// </summary>
    public struct NeighbourLodStrides
    {
        public int NegX, PosX, NegY, PosY, NegZ, PosZ;

        /// <summary>All strides == 1; no snapping, no suppression. Use as the
        /// default for callers that don't care about LOD seams.</summary>
        public static NeighbourLodStrides Identity => new NeighbourLodStrides
        {
            NegX = 1, PosX = 1, NegY = 1, PosY = 1, NegZ = 1, PosZ = 1,
        };

        public bool AnySnap =>
            NegX > 1 || PosX > 1 || NegY > 1 || PosY > 1 || NegZ > 1 || PosZ > 1;
    }

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
    /// <para>
    /// Phase 4c: when a chunk's neighbour meshes at coarser LOD on a face,
    /// the caller passes a <see cref="NeighbourLodStrides"/> describing the
    /// per-face stride ratio. The mesher snaps boundary-cell vertex
    /// positions to coarse-grid-aligned positions on those faces and
    /// suppresses the boundary face's X/Y/Z-axis-edge quads (whose 4 corner
    /// cells all lie in the boundary strip). The coarser neighbour's mesh
    /// covers the boundary geometry; the fine chunk's Y/Z-axis-edge quads
    /// in the boundary band stitch its interior to the snapped boundary
    /// vertices. A small per-triangle degenerate-area filter in Pass 2
    /// catches the rare case where multiple snapped corners collapse to
    /// identical positions.
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
        /// <param name="strides">Optional per-face LOD-stride hints (Phase 4c).
        /// <see cref="NeighbourLodStrides.Identity"/> means no LOD seam
        /// handling. Field values &gt; 1 enable boundary snap + quad
        /// suppression on the corresponding face.</param>
        public static void Mesh(
            NativeArray<sbyte> sdf,
            int dim,
            Buffers buffers,
            out int vertexCount,
            out int indexCount,
            float cellScale = 1.0f,
            NeighbourLodStrides strides = default)
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
                StrideNegX = strides.NegX, StridePosX = strides.PosX,
                StrideNegY = strides.NegY, StridePosY = strides.PosY,
                StrideNegZ = strides.NegZ, StridePosZ = strides.PosZ,
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
            float cellScale = 1.0f,
            NeighbourLodStrides strides = default)
        {
            return new MeshJob
            {
                Sdf = sdf,
                Dim = dim,
                CellScale = cellScale,
                StrideNegX = strides.NegX, StridePosX = strides.PosX,
                StrideNegY = strides.NegY, StridePosY = strides.PosY,
                StrideNegZ = strides.NegZ, StridePosZ = strides.PosZ,
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

            // Phase 4c: per-face neighbour LOD strides relative to this
            // mesher's own stride. Value <= 1 means "no LOD seam" — emit
            // normally. Value 2 means the neighbour is one LOD step coarser
            // (cell-grid 2x); value 4 means two steps coarser. The job
            // snaps boundary-cell vertex positions to coarse-grid centers
            // and suppresses the boundary-face quads on faces with stride
            // > 1.
            public int StrideNegX, StridePosX, StrideNegY, StridePosY, StrideNegZ, StridePosZ;

            public NativeArray<int> CellToVertex;
            public NativeArray<float3> Vertices;
            public NativeArray<int> Indices;
            public NativeArray<int> Counts;

            // Below this squared cross-product magnitude in cell-grid units²
            // a triangle is treated as zero-area and skipped. Chosen so that
            // a triangle whose two edges are within ~1e-3 cell-grid units of
            // collinear (or whose vertices coalesce to within ~1e-3 cells)
            // is rejected. At cellSize = 0.5m that's ~0.5mm — well below the
            // visible noise floor and well above float-rounding from a
            // single math.round.
            private const float DegenerateAreaThresholdSq = 1e-6f;

            public void Execute()
            {
                int cellDim   = Dim - 1;
                int dimSq     = Dim * Dim;
                int cellDimSq = cellDim * cellDim;
                int cellCount = cellDim * cellDimSq;
                int boundaryCx = cellDim - 1;

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

                    float3 pos = vertSum / crossingCount;

                    // Phase 4c: snap boundary-strip vertex axes to coarse
                    // neighbour grid. Only the axis PERPENDICULAR to a
                    // face is snapped — the seam between this chunk and a
                    // coarser neighbour is a face plane, and the in-plane
                    // axes (the other two) carry the surface's natural
                    // local variation, which a centre-of-cell snap would
                    // disturb for SDFs whose surface doesn't pass through
                    // the cell midpoint. This is a partial seam fix:
                    // perpendicular alignment is correct, in-plane spacing
                    // remains at fine resolution. A full seam fix would
                    // require computing the coarser neighbour's actual
                    // boundary-cell vertex from coarse-stride apron
                    // samples and snapping to it; that's a follow-up.
                    if (cx == 0          && StrideNegX > 1) pos.x = SnapCoarseCenter(pos.x, StrideNegX);
                    if (cx == boundaryCx && StridePosX > 1) pos.x = SnapCoarseCenter(pos.x, StridePosX);
                    if (cy == 0          && StrideNegY > 1) pos.y = SnapCoarseCenter(pos.y, StrideNegY);
                    if (cy == boundaryCx && StridePosY > 1) pos.y = SnapCoarseCenter(pos.y, StridePosY);
                    if (cz == 0          && StrideNegZ > 1) pos.z = SnapCoarseCenter(pos.z, StrideNegZ);
                    if (cz == boundaryCx && StridePosZ > 1) pos.z = SnapCoarseCenter(pos.z, StridePosZ);

                    int cellIdx = cz * cellDimSq + cy * cellDim + cx;
                    CellToVertex[cellIdx] = vCount;
                    Vertices[vCount++] = pos * CellScale;
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

                    // Phase 4c: suppress boundary-face quads on faces where
                    // the neighbour is coarser. All 4 corner cells of an
                    // X-axis-edge quad live at cx = i; if i is the -X or +X
                    // boundary cell and that face's stride > 1, the coarse
                    // neighbour owns the seam — skip.
                    if (i == 0          && StrideNegX > 1) continue;
                    if (i == boundaryCx && StridePosX > 1) continue;

                    int baseCell = k * cellDimSq + j * cellDim + i;
                    int v00 = CellToVertex[baseCell - cellDimSq - cellDim];
                    int v10 = CellToVertex[baseCell - cellDimSq];
                    int v11 = CellToVertex[baseCell];
                    int v01 = CellToVertex[baseCell - cellDim];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                    if (sA < 0)
                    {
                        EmitTriangle(v00, v10, v11, ref iCount);
                        EmitTriangle(v00, v11, v01, ref iCount);
                    }
                    else
                    {
                        EmitTriangle(v00, v11, v10, ref iCount);
                        EmitTriangle(v00, v01, v11, ref iCount);
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

                    if (j == 0          && StrideNegY > 1) continue;
                    if (j == boundaryCx && StridePosY > 1) continue;

                    int baseCell = k * cellDimSq + j * cellDim + i;
                    int v00 = CellToVertex[baseCell - cellDimSq - 1];
                    int v10 = CellToVertex[baseCell - cellDimSq];
                    int v11 = CellToVertex[baseCell];
                    int v01 = CellToVertex[baseCell - 1];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                    if (sA < 0)
                    {
                        EmitTriangle(v00, v11, v10, ref iCount);
                        EmitTriangle(v00, v01, v11, ref iCount);
                    }
                    else
                    {
                        EmitTriangle(v00, v10, v11, ref iCount);
                        EmitTriangle(v00, v11, v01, ref iCount);
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

                    if (k == 0          && StrideNegZ > 1) continue;
                    if (k == boundaryCx && StridePosZ > 1) continue;

                    int baseCell = k * cellDimSq + j * cellDim + i;
                    int v00 = CellToVertex[baseCell - cellDim - 1];
                    int v10 = CellToVertex[baseCell - cellDim];
                    int v11 = CellToVertex[baseCell];
                    int v01 = CellToVertex[baseCell - 1];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;

                    if (sA < 0)
                    {
                        EmitTriangle(v00, v10, v11, ref iCount);
                        EmitTriangle(v00, v11, v01, ref iCount);
                    }
                    else
                    {
                        EmitTriangle(v00, v11, v10, ref iCount);
                        EmitTriangle(v00, v01, v11, ref iCount);
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

            /// <summary>
            /// Snap a single coordinate (in fine cell-grid units) to the
            /// nearest coarse-cell center. Coarse cell N has corners at
            /// fine indices N×stride and (N+1)×stride, so center is at
            /// N×stride + stride/2 — i.e., the snap target lattice is
            /// {stride/2, stride/2 + stride, stride/2 + 2·stride, …}.
            /// </summary>
            private static float SnapCoarseCenter(float raw, int stride)
            {
                float half = stride * 0.5f;
                return math.round((raw - half) / stride) * stride + half;
            }

            private void EmitTriangle(int a, int b, int c, ref int iCount)
            {
                // Degenerate-triangle filter: any two snapped corners that
                // collapse to the same world position would produce a
                // zero-area triangle. Skip rather than emit. This is the
                // Phase 4c machine-gate guarantee — no zero-area triangles
                // at LOD-boundary faces even when snapping coalesces fine
                // cells in a 2×2 (or 4×4) block to one coarse position.
                float3 va = Vertices[a];
                float3 vb = Vertices[b];
                float3 vc = Vertices[c];
                float3 cross = math.cross(vb - va, vc - va);
                if (math.lengthsq(cross) < DegenerateAreaThresholdSq) return;
                Indices[iCount++] = a;
                Indices[iCount++] = b;
                Indices[iCount++] = c;
            }
        }
    }
}
