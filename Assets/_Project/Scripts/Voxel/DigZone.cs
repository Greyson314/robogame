using System;
using Robogame.Core;
using UnityEngine;

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
    /// Phase 1b deliverable. Plain managed C# meshing (Phase 1c ports
    /// to Burst). Synchronous remesh + synchronous <c>MeshCollider</c>
    /// swap on the main thread; Phase 1c uses
    /// <c>Physics.BakeMesh</c> on a worker for the cook.
    /// </para>
    /// <para>
    /// The initial SDF state is set by <see cref="InitializeHalfSpace"/>
    /// or any future <c>.dig</c> asset loader (Phase 2). Brush ops are
    /// applied via <see cref="ApplyBrush"/> which delegates to
    /// <see cref="BrushApplicator"/> (max-fold per TERRAFORMING_PLAN §2)
    /// and then triggers a remesh.
    /// </para>
    /// <para>
    /// <c>[ExecuteAlways]</c> so the chunk meshes in Edit Mode as well
    /// as Play Mode — the Phase 1b test scene shows the meshed surface
    /// without entering Play Mode.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class DigZone : MonoBehaviour, IDigZone
    {
        [SerializeField, Min(0.01f)] private float _cellSize = 0.5f;
        [SerializeField, Min(2)]     private int   _chunkSizeCells = 32;

        // SDF buffer is non-serialised — it's reinitialised on Awake from
        // a stored seed (currently HalfSpace; Phase 2 swaps this for a
        // .dig asset reference).
        private sbyte[] _sdf;
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

        /// <summary>SDF samples per chunk side. <c>ChunkSizeCells + 1</c>.</summary>
        public int Dim => _chunkSizeCells + 1;

        /// <summary>The remeshed surface mesh. Null until <see cref="EnsureInitialised"/> runs.</summary>
        public Mesh CurrentMesh => _mesh;

        /// <summary>Direct access to the SDF buffer for tests + diagnostics. Trusted callers only.</summary>
        public ReadOnlySpan<sbyte> Sdf => _sdf;

        private void Awake() => EnsureInitialised();

        private void OnEnable()
        {
            EnsureInitialised();
            DigField.Register(this);
        }

        private void OnDisable() => DigField.Unregister(this);

        private void OnDestroy()
        {
            // Destroy the runtime-created mesh so we don't leak it across
            // domain reloads / scene reloads.
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        private void EnsureInitialised()
        {
            if (_sdf != null) return;

            int dim = Dim;
            _sdf = new sbyte[dim * dim * dim];
            _meshBuffers = SurfaceNetsMesher.Allocate(dim);

            _mesh = new Mesh
            {
                name = $"{name}_DigZoneMesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };

            _meshFilter = GetComponent<MeshFilter>();
            _meshFilter.sharedMesh = _mesh;
            _meshCollider = GetComponent<MeshCollider>();

            InitializeHalfSpace();
            RemeshNow();
        }

        /// <summary>
        /// Seed the SDF with a half-space: lower half of the chunk (Y &lt; dim/2)
        /// is solid, upper half is exterior. Produces a flat top surface
        /// the first remesh will extract as a plane.
        /// </summary>
        public void InitializeHalfSpace()
        {
            int dim = Dim;
            int split = dim / 2;
            for (int z = 0; z < dim; z++)
            for (int y = 0; y < dim; y++)
            for (int x = 0; x < dim; x++)
            {
                // Signed-distance to the y = split plane in cell-grid units,
                // converted to sbyte units (64 sbyte/cell).
                int v = (y - split) * 64;
                if (v < sbyte.MinValue) v = sbyte.MinValue;
                else if (v > sbyte.MaxValue) v = sbyte.MaxValue;
                _sdf[z * dim * dim + y * dim + x] = (sbyte)v;
            }
        }

        /// <summary>
        /// Apply one brush op to the chunk and remesh. Synchronous.
        /// Returns the number of cells whose SDF changed.
        /// </summary>
        public int ApplyBrush(BrushOp op)
        {
            EnsureInitialised();
            int changed = BrushApplicator.Apply(op, _sdf, Dim, _cellSize, transform.position);
            if (changed > 0) RemeshNow();
            return changed;
        }

        /// <summary>
        /// Re-extract the surface mesh from the current SDF. Phase 1c
        /// moves this to a Burst job; Phase 1b is plain managed C#.
        /// </summary>
        public void RemeshNow()
        {
            EnsureInitialised();
            int dim = Dim;

            SurfaceNetsMesher.Mesh(_sdf, dim, _meshBuffers, out int vCount, out int iCount);

            // Convert cell-grid positions to local mesh positions (scaled
            // by cellSize). The GameObject's transform handles world placement.
            // Phase 1c will pre-scale into a NativeArray<Vector3> and call
            // Mesh.SetVertexBufferData directly to skip the per-frame
            // managed-array allocation.
            Vector3[] verts = new Vector3[vCount];
            for (int v = 0; v < vCount; v++)
            {
                Vector3 g = _meshBuffers.Vertices[v];
                verts[v] = new Vector3(g.x * _cellSize, g.y * _cellSize, g.z * _cellSize);
            }

            int[] tris = new int[iCount];
            Array.Copy(_meshBuffers.Indices, tris, iCount);

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetIndices(tris, MeshTopology.Triangles, submesh: 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            // Force the MeshCollider to re-cook by null-then-reassigning.
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;
        }
    }
}
