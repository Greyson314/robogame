using Robogame.Core;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Robogame.Voxel
{
    /// <summary>
    /// Single-chunk dig zone. Owns a 33³-sample SDF buffer at default
    /// settings (32 cells per side at 0.5 m → 16 m chunk). Implements
    /// <see cref="IDigZone"/> and registers with <see cref="DigField"/>
    /// on enable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 1c update: SDF and mesher buffers are
    /// <see cref="NativeArray{T}"/>-backed; meshing is Burst-compiled via
    /// <see cref="SurfaceNetsMesher"/>. Mesh upload uses
    /// <see cref="Mesh.SetVertexBufferData{T}(NativeArray{T},int,int,int,int,MeshUpdateFlags)"/>
    /// directly off the native buffer for zero managed allocation in the
    /// steady-state remesh path.
    /// </para>
    /// <para>
    /// MeshCollider is still cooked synchronously on the main thread —
    /// Phase 2 moves to <c>Physics.BakeMesh</c> on a worker.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class DigZone : MonoBehaviour, IDigZone
    {
        [SerializeField, Min(0.01f)] private float _cellSize = 0.5f;
        [SerializeField, Min(2)]     private int   _chunkSizeCells = 32;

        private NativeArray<sbyte> _sdf;
        private SurfaceNetsMesher.Buffers _meshBuffers;
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;

        public Bounds WorldBounds
        {
            get
            {
                float side = _chunkSizeCells * _cellSize;
                Vector3 size = new Vector3(side, side, side);
                return new Bounds(transform.position + size * 0.5f, size);
            }
        }

        public float CellSize => _cellSize;
        public int ChunkSizeCells => _chunkSizeCells;
        public bool ContainsPoint(Vector3 worldPosition) => WorldBounds.Contains(worldPosition);

        public int Dim => _chunkSizeCells + 1;

        public Mesh CurrentMesh => _mesh;

        /// <summary>SDF samples in z-major order. Trusted accessor for tests + diagnostics.</summary>
        public NativeArray<sbyte> Sdf => _sdf;

        private void Awake() => EnsureInitialised();

        private void OnEnable()
        {
            EnsureInitialised();
            DigField.Register(this);
        }

        private void OnDisable() => DigField.Unregister(this);

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

        private void EnsureInitialised()
        {
            if (_sdf.IsCreated) return;

            int dim = Dim;
            _sdf = new NativeArray<sbyte>(dim * dim * dim, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _meshBuffers = SurfaceNetsMesher.Allocate(dim, Allocator.Persistent);

            _mesh = new Mesh
            {
                name = $"{name}_DigZoneMesh",
                indexFormat = IndexFormat.UInt32,
            };
            _meshFilter = GetComponent<MeshFilter>();
            _meshFilter.sharedMesh = _mesh;
            _meshCollider = GetComponent<MeshCollider>();

            InitializeHalfSpace();
            RemeshNow();
        }

        /// <summary>
        /// Seed the SDF with a half-space: lower half (Y &lt; dim/2) solid,
        /// upper half exterior. Produces a flat top surface on first remesh.
        /// </summary>
        public void InitializeHalfSpace()
        {
            int dim = Dim;
            int split = dim / 2;
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                int v = (y - split) * 64;
                if (v < sbyte.MinValue) v = sbyte.MinValue;
                else if (v > sbyte.MaxValue) v = sbyte.MaxValue;
                _sdf[z * dim * dim + y * dim + x] = (sbyte)v;
            }
        }

        /// <summary>Apply one brush op (max-fold) and remesh. Returns changed-cell count.</summary>
        public int ApplyBrush(BrushOp op)
        {
            EnsureInitialised();
            int changed = BrushApplicator.Apply(op, _sdf, Dim, _cellSize, transform.position);
            if (changed > 0) RemeshNow();
            return changed;
        }

        /// <summary>Re-extract the surface mesh from the current SDF. Schedules + completes the Burst job synchronously.</summary>
        public void RemeshNow()
        {
            EnsureInitialised();
            int dim = Dim;

            // Burst job folds CellScale in, so positions come back in local
            // mesh units. No managed post-scale loop on the host.
            SurfaceNetsMesher.Mesh(_sdf, dim, _meshBuffers,
                out int vCount, out int iCount,
                cellScale: _cellSize);

            _mesh.Clear(keepVertexLayout: false);

            // High-level Mesh API with NativeArray inputs — zero managed
            // allocations on the host side. Reinterpret<Vector3>() is a
            // zero-copy view of NativeArray<float3>; float3 and Vector3 are
            // both 3-float structs.
            var vertsAsVec3 = _meshBuffers.Vertices.Reinterpret<Vector3>();
            _mesh.SetVertices(vertsAsVec3, 0, vCount);
            _mesh.SetIndices(_meshBuffers.Indices.GetSubArray(0, iCount),
                MeshTopology.Triangles, submesh: 0, calculateBounds: false);

            // Manual bounds — chunk occupies [0, side]³ in local space.
            float side = _chunkSizeCells * _cellSize;
            _mesh.bounds = new Bounds(new Vector3(side * 0.5f, side * 0.5f, side * 0.5f),
                                      new Vector3(side, side, side));

            // Required for URP/Lit shading. Phase 4 may inline normals
            // computation into the meshing job; for Phase 1c we accept
            // RecalculateNormals' O(triangles) cost.
            _mesh.RecalculateNormals();

            // MeshCollider re-cook is synchronous in Phase 1c. Phase 2 moves
            // to Physics.BakeMesh on a worker.
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;
        }
    }
}
