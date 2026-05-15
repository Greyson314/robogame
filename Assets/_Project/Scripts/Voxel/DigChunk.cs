using Robogame.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Robogame.Voxel
{
    /// <summary>
    /// One chunk within a multi-chunk <see cref="DigZone"/>. Owns its own
    /// SDF buffer, mesher buffers, <see cref="Mesh"/>, and renderer/collider.
    /// Lifecycle: instantiated as a hidden child of <see cref="DigZone"/>;
    /// <see cref="Initialize"/> must be called before the GameObject is
    /// activated (the parent zone configures cell size + chunk coord, then
    /// activates the chunk).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 2a deliverable. Chunks are not registered with
    /// <see cref="DigField"/> directly — the parent <see cref="DigZone"/>
    /// is the <see cref="IDigZone"/>. Chunks are ephemeral runtime objects
    /// (<see cref="HideFlags.DontSave"/>) so the scene file persists only
    /// the parent zone; chunks are rebuilt on every scene load from the
    /// zone's stored configuration (or, Phase 2d, from a <c>.dig</c> asset).
    /// </para>
    /// <para>
    /// Apron handling lands in Phase 2b — for Phase 2a each chunk meshes
    /// independently, which produces visible seams at chunk boundaries.
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

        private NativeArray<sbyte> _sdf;
        private SurfaceNetsMesher.Buffers _meshBuffers;
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;

        private bool _initialised;

        public Vector3Int ChunkCoord => _chunkCoord;
        public float CellSize => _cellSize;
        public int ChunkSizeCells => _chunkSizeCells;
        public int Dim => _chunkSizeCells + 1;
        public bool IsInitialised => _initialised;
        public Mesh CurrentMesh => _mesh;

        /// <summary>Trusted accessor for tests + the parent zone's SDF seeding.</summary>
        public NativeArray<sbyte> Sdf => _sdf;

        /// <summary>
        /// World-space AABB of this chunk. Includes the +face boundary
        /// samples (which are shared with the next chunk in each axis).
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

        /// <summary>
        /// Configure the chunk. Must be called by the parent <see cref="DigZone"/>
        /// immediately after <c>AddComponent</c> and before the GameObject is
        /// activated.
        /// </summary>
        public void Initialize(Vector3Int chunkCoord, float cellSize, int chunkSizeCells)
        {
            if (_initialised)
                throw new System.InvalidOperationException($"DigChunk {chunkCoord} initialised twice.");

            _chunkCoord = chunkCoord;
            _cellSize = cellSize;
            _chunkSizeCells = chunkSizeCells;

            int dim = Dim;
            _sdf = new NativeArray<sbyte>(dim * dim * dim, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _meshBuffers = SurfaceNetsMesher.Allocate(dim, Allocator.Persistent);

            _mesh = new Mesh
            {
                name = $"DigChunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}_Mesh",
                indexFormat = IndexFormat.UInt32,
            };
            _meshFilter = GetComponent<MeshFilter>();
            _meshFilter.sharedMesh = _mesh;
            _meshCollider = GetComponent<MeshCollider>();

            _initialised = true;
        }

        private void OnDestroy()
        {
            if (_sdf.IsCreated) _sdf.Dispose();
            if (_meshBuffers.IsCreated) _meshBuffers.Dispose();

            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        /// <summary>Apply one brush op (max-fold) and remesh if anything changed. Returns changed-cell count.</summary>
        public int ApplyBrush(BrushOp op)
        {
            if (!_initialised) throw new System.InvalidOperationException("DigChunk used before Initialize.");
            int changed = BrushApplicator.Apply(op, _sdf, Dim, _cellSize, transform.position);
            if (changed > 0) RemeshNow();
            return changed;
        }

        /// <summary>Re-extract the surface mesh from the current SDF. Synchronous Burst job; synchronous MeshCollider re-cook.</summary>
        public void RemeshNow()
        {
            if (!_initialised) throw new System.InvalidOperationException("DigChunk used before Initialize.");
            int dim = Dim;

            SurfaceNetsMesher.Mesh(_sdf, dim, _meshBuffers,
                out int vCount, out int iCount,
                cellScale: _cellSize);

            _mesh.Clear(keepVertexLayout: false);

            var vertsAsVec3 = _meshBuffers.Vertices.Reinterpret<Vector3>();
            _mesh.SetVertices(vertsAsVec3, 0, vCount);
            _mesh.SetIndices(_meshBuffers.Indices.GetSubArray(0, iCount),
                MeshTopology.Triangles, submesh: 0, calculateBounds: false);

            float side = _chunkSizeCells * _cellSize;
            _mesh.bounds = new Bounds(new Vector3(side * 0.5f, side * 0.5f, side * 0.5f),
                                      new Vector3(side, side, side));
            _mesh.RecalculateNormals();

            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;
        }
    }
}
