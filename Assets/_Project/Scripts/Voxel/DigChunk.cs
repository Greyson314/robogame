using Robogame.Core;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Robogame.Voxel
{
    /// <summary>
    /// One chunk within a multi-chunk <see cref="DigZone"/>. Owns its own
    /// SDF buffer plus an apron-augmented staging buffer used for meshing,
    /// the mesher buffers, the <see cref="Mesh"/>, and the renderer +
    /// collider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 2b: each chunk meshes a <c>(chunkSize+2)³</c> SDF region —
    /// the chunk's own <c>(chunkSize+1)³</c> samples plus a one-cell
    /// apron rim from its +X / +Y / +Z neighbours (and edge / corner
    /// overlap neighbours). The apron is rebuilt by the parent
    /// <see cref="DigZone"/> before each remesh; this chunk only meshes,
    /// it doesn't fetch.
    /// </para>
    /// <para>
    /// Why an apron: vertices for cells at the chunk's +face boundary
    /// (cell index = chunkSize) lie at world positions inside the +face
    /// neighbour's territory, and the boundary quads connecting them
    /// require knowing the SDF on the neighbour's side. Without the
    /// apron, the mesher skips those edges and visible seams appear at
    /// chunk boundaries. With it, both neighbouring chunks compute
    /// identical vertices for the shared rim cells (deterministic math
    /// on identical SDF samples) so the meshes meet seamlessly.
    /// </para>
    /// <para>
    /// MeshCollider cooking is synchronous; Phase 2c moves to
    /// <c>Physics.BakeMesh</c> on a worker thread.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class DigChunk : MonoBehaviour
    {
        private Vector3Int _chunkCoord;
        private float _cellSize;
        private int _chunkSizeCells;

        private NativeArray<sbyte> _sdf;           // Own SDF, (chunkSize+1)³ = dim³ samples. Brush-mutable.
        private NativeArray<sbyte> _sdfWithApron;  // Per-remesh staging: own + apron. (chunkSize+2)³.
        private NativeArray<sbyte> _sdfLod;        // Temp downsampled SDF for lod>0 meshing.
        private SurfaceNetsMesher.Buffers _meshBuffers;
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;
        private int _currentLodLevel;

        private bool _initialised;

        // Phase 2c async-bake state. RemeshNow schedules Physics.BakeMesh
        // on a worker; PollBakeAndSwap on DigZone.Update completes the
        // handle and reassigns the collider's sharedMesh. Throughout, the
        // collider's sharedMesh reference stays valid — only the
        // collider's cached cooked data is refreshed by the reassign.
        private JobHandle _bakeHandle;
        private bool _hasPendingBake;

        public Vector3Int ChunkCoord => _chunkCoord;
        public float CellSize => _cellSize;
        public int ChunkSizeCells => _chunkSizeCells;

        /// <summary>Samples per side of the chunk's own SDF. <c>chunkSizeCells + 1</c>.</summary>
        public int Dim => _chunkSizeCells + 1;

        /// <summary>Samples per side of the mesher input (own + 1-cell apron rim). <c>chunkSizeCells + 2</c>.</summary>
        public int DimWithApron => _chunkSizeCells + 2;

        public bool IsInitialised => _initialised;
        public Mesh CurrentMesh => _mesh;

        /// <summary>Own SDF samples. Brush-mutable, persistent. Index z*Dim²+y*Dim+x.</summary>
        public NativeArray<sbyte> Sdf => _sdf;

        /// <summary>Apron-augmented SDF staging buffer. Written by the parent zone before each remesh.</summary>
        public NativeArray<sbyte> SdfWithApron => _sdfWithApron;

        /// <summary>
        /// World-space AABB of this chunk's own region. (The meshed region
        /// extends 1 cell past this in each +face direction — the apron.)
        /// </summary>
        public Bounds WorldBounds
        {
            get
            {
                float side = _chunkSizeCells * _cellSize;
                Vector3 size = new Vector3(side, side, side);
                return new Bounds(transform.position + size * 0.5f, size);
            }
        }

        public void Initialize(Vector3Int chunkCoord, float cellSize, int chunkSizeCells)
        {
            if (_initialised)
                throw new System.InvalidOperationException($"DigChunk {chunkCoord} initialised twice.");

            _chunkCoord = chunkCoord;
            _cellSize = cellSize;
            _chunkSizeCells = chunkSizeCells;

            int dim = Dim;
            int dimWithApron = DimWithApron;
            _sdf = new NativeArray<sbyte>(dim * dim * dim, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _sdfWithApron = new NativeArray<sbyte>(dimWithApron * dimWithApron * dimWithApron,
                Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _meshBuffers = SurfaceNetsMesher.Allocate(dimWithApron, Allocator.Persistent);

            _mesh = new Mesh
            {
                name = $"DigChunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}_Mesh",
                indexFormat = IndexFormat.UInt32,
            };
            _meshFilter = GetComponent<MeshFilter>();
            _meshFilter.sharedMesh = _mesh;
            _meshCollider = GetComponent<MeshCollider>();
            // Assign immediately so the collider's sharedMesh is non-null
            // from chunk birth. RemeshNow refreshes the collider's cooked
            // data via Physics.BakeMesh + reassign on the same Mesh object.
            _meshCollider.sharedMesh = _mesh;

            _initialised = true;
        }

        private void OnDestroy()
        {
            // Drain any in-flight bake job — must complete before disposing
            // the Mesh asset it references (by instance ID).
            if (_hasPendingBake)
            {
                _bakeHandle.Complete();
                _hasPendingBake = false;
            }

            if (_sdf.IsCreated) _sdf.Dispose();
            if (_sdfWithApron.IsCreated) _sdfWithApron.Dispose();
            if (_sdfLod.IsCreated) _sdfLod.Dispose();
            if (_meshBuffers.IsCreated) _meshBuffers.Dispose();

            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        /// <summary>
        /// Apply a brush op to this chunk's own SDF (no remesh; the parent
        /// zone schedules apron-rebuild + remesh for all dirty chunks
        /// together so apron data from a freshly-edited neighbour is
        /// visible to its neighbours' remesh).
        /// </summary>
        public int ApplyBrushNoRemesh(BrushOp op)
        {
            if (!_initialised) throw new System.InvalidOperationException("DigChunk used before Initialize.");
            return BrushApplicator.Apply(op, _sdf, Dim, _cellSize, transform.position);
        }

        /// <summary>
        /// Current LOD level for this chunk's mesh. 0 = full res; 1 = half
        /// res (~9× fewer vertices); 2 = quarter res. Read-only; use
        /// <see cref="SetLodLevel"/> to change.
        /// </summary>
        public int CurrentLodLevel => _currentLodLevel;

        /// <summary>
        /// Switch this chunk to a different LOD level and remesh. No-op if
        /// the level is unchanged.
        /// </summary>
        public void SetLodLevel(int newLevel)
        {
            if (newLevel < 0) newLevel = 0;
            if (newLevel == _currentLodLevel) return;
            _currentLodLevel = newLevel;
            RemeshNow();
        }

        /// <summary>
        /// Re-extract the surface mesh from <see cref="SdfWithApron"/> at
        /// the chunk's <see cref="CurrentLodLevel"/> and schedule an async
        /// <c>Physics.BakeMesh</c> on a worker. See Phase 2c's session log
        /// for the atomic-swap pattern.
        /// </summary>
        public void RemeshNow()
        {
            if (!_initialised) throw new System.InvalidOperationException("DigChunk used before Initialize.");

            // Drain any previously-scheduled bake. If a brush fires faster
            // than the worker can keep up, the prior bake must complete
            // before we mutate the Mesh's contents below.
            if (_hasPendingBake)
            {
                _bakeHandle.Complete();
                _meshCollider.sharedMesh = _mesh;
                _hasPendingBake = false;
            }

            int vCount, iCount;
            if (_currentLodLevel <= 0)
            {
                SurfaceNetsMesher.Mesh(_sdfWithApron, DimWithApron, _meshBuffers,
                    out vCount, out iCount,
                    cellScale: _cellSize);
            }
            else
            {
                // Phase 4a: downsample the apron-staging buffer into _sdfLod,
                // mesh from there with an enlarged cell scale so output is
                // still in world units. Each LOD level halves the dim and
                // doubles the cell-grid step.
                int stride = 1 << _currentLodLevel;
                int lodDim = ((DimWithApron - 1) / stride) + 1;
                EnsureLodBuffer(lodDim);
                DownsampleSdf(_sdfWithApron, DimWithApron, _sdfLod, lodDim, stride);
                SurfaceNetsMesher.Mesh(_sdfLod, lodDim, _meshBuffers,
                    out vCount, out iCount,
                    cellScale: _cellSize * stride);
            }

            _mesh.Clear(keepVertexLayout: false);

            var vertsAsVec3 = _meshBuffers.Vertices.Reinterpret<Vector3>();
            _mesh.SetVertices(vertsAsVec3, 0, vCount);
            _mesh.SetIndices(_meshBuffers.Indices.GetSubArray(0, iCount),
                MeshTopology.Triangles, submesh: 0, calculateBounds: false);

            // Local bounds: the meshed region extends 1 cell past the own
            // region in each +face direction.
            float side = (_chunkSizeCells + 1) * _cellSize;
            _mesh.bounds = new Bounds(new Vector3(side * 0.5f, side * 0.5f, side * 0.5f),
                                      new Vector3(side, side, side));
            _mesh.RecalculateNormals();

            // Schedule the collider cook on a worker. The collider keeps
            // pointing at _mesh (its cached cooked data is the previous
            // state); PollBakeAndSwap re-assigns once the new cook is in.
            _bakeHandle = new BakeMeshJob { MeshEntityID = _mesh.GetEntityId() }.Schedule();
            _hasPendingBake = true;
        }

        /// <summary>
        /// If a bake is pending and has completed on the worker, finalise
        /// it by reassigning <see cref="MeshCollider.sharedMesh"/> so the
        /// collider picks up the fresh cooked data. Called every frame by
        /// the parent <see cref="DigZone.Update"/>.
        /// </summary>
        /// <returns>True if a swap actually happened this call.</returns>
        public bool PollBakeAndSwap()
        {
            if (!_hasPendingBake) return false;
            if (!_bakeHandle.IsCompleted) return false;
            _bakeHandle.Complete();
            _meshCollider.sharedMesh = _mesh;
            _hasPendingBake = false;
            return true;
        }

        /// <summary>True while a Physics.BakeMesh worker call is in flight.</summary>
        public bool HasPendingBake => _hasPendingBake;

        private void EnsureLodBuffer(int lodDim)
        {
            int needed = lodDim * lodDim * lodDim;
            if (_sdfLod.IsCreated && _sdfLod.Length == needed) return;
            if (_sdfLod.IsCreated) _sdfLod.Dispose();
            _sdfLod = new NativeArray<sbyte>(needed, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void DownsampleSdf(NativeArray<sbyte> src, int srcDim, NativeArray<sbyte> dst, int dstDim, int stride)
        {
            // Nearest-sample downsample. For dst[i,j,k], read src at
            // (i*stride, j*stride, k*stride), clamped to src's bounds so
            // the last dst sample is safe if (dstDim-1)*stride > srcDim-1.
            int srcMaxIdx = srcDim - 1;
            int srcDimSq = srcDim * srcDim;
            int dstDimSq = dstDim * dstDim;
            for (int z = 0; z < dstDim; z++)
            {
                int sz = z * stride; if (sz > srcMaxIdx) sz = srcMaxIdx;
                for (int y = 0; y < dstDim; y++)
                {
                    int sy = y * stride; if (sy > srcMaxIdx) sy = srcMaxIdx;
                    for (int x = 0; x < dstDim; x++)
                    {
                        int sx = x * stride; if (sx > srcMaxIdx) sx = srcMaxIdx;
                        dst[z * dstDimSq + y * dstDim + x] = src[sz * srcDimSq + sy * srcDim + sx];
                    }
                }
            }
        }

        private struct BakeMeshJob : IJob
        {
            public EntityId MeshEntityID;

            public void Execute()
            {
                // Physics.BakeMesh is thread-safe per Unity docs and the
                // entire point of this job is to keep the cook off the
                // main thread.
                Physics.BakeMesh(MeshEntityID, convex: false);
            }
        }
    }
}
