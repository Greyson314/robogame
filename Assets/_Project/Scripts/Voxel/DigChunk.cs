using Robogame.Core;
using Unity.Collections;
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
        private SurfaceNetsMesher.Buffers _meshBuffers;
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;

        private bool _initialised;

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

            _initialised = true;
        }

        private void OnDestroy()
        {
            if (_sdf.IsCreated) _sdf.Dispose();
            if (_sdfWithApron.IsCreated) _sdfWithApron.Dispose();
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
        /// Re-extract the surface mesh from <see cref="SdfWithApron"/>.
        /// Caller (the parent <see cref="DigZone"/>) must have populated
        /// the apron staging buffer first.
        /// </summary>
        public void RemeshNow()
        {
            if (!_initialised) throw new System.InvalidOperationException("DigChunk used before Initialize.");

            SurfaceNetsMesher.Mesh(_sdfWithApron, DimWithApron, _meshBuffers,
                out int vCount, out int iCount,
                cellScale: _cellSize);

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

            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;
        }
    }
}
