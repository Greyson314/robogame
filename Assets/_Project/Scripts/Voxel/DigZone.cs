using Robogame.Core;
using Unity.Collections;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// A multi-chunk dig zone. <see cref="IDigZone"/> implementer + container
    /// that manages a 3D grid of <see cref="DigChunk"/> child objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 2b adds apron-based seam-free meshing: before each chunk
    /// remeshes, the zone copies the chunk's own SDF plus a one-cell rim
    /// of neighbour samples into the chunk's apron-staging buffer. Two
    /// adjacent chunks compute identical vertex positions for the shared
    /// rim cells (deterministic math on identical sample data), so their
    /// meshes meet seamlessly. Boundary chunks (no neighbour in some
    /// direction) replicate their own face sample into the apron to
    /// avoid false sign-crossings at the dig zone edge.
    /// </para>
    /// <para>
    /// Brush ops apply to chunks' own SDFs (untouched by the apron). The
    /// remesh pass then rebuilds aprons across ALL chunks and remeshes
    /// every chunk in the zone — Phase 2b accepts the full-rebuild cost
    /// for correctness; Phase 2c adds proper dirty-set propagation
    /// (only -face neighbours of brushed chunks need apron rebuild).
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    public sealed class DigZone : MonoBehaviour, IDigZone
    {
        [SerializeField, Min(0.01f)] private float _cellSize = 0.5f;
        [SerializeField, Min(2)]     private int   _chunkSizeCells = 32;
        [SerializeField] private Vector3Int _chunkGridSize = new Vector3Int(2, 2, 2);
        [SerializeField] private Material _chunkMaterial;

        private DigChunk[] _chunks;

        public float CellSize
        {
            get => _cellSize;
            set { ThrowIfInitialised(nameof(CellSize)); _cellSize = value; }
        }

        public int ChunkSizeCells
        {
            get => _chunkSizeCells;
            set { ThrowIfInitialised(nameof(ChunkSizeCells)); _chunkSizeCells = value; }
        }

        public Vector3Int ChunkGridSize
        {
            get => _chunkGridSize;
            set { ThrowIfInitialised(nameof(ChunkGridSize)); _chunkGridSize = value; }
        }

        public int ChunkCount => _chunks != null ? _chunks.Length : 0;

        private void ThrowIfInitialised(string fieldName)
        {
            if (_chunks != null)
                throw new System.InvalidOperationException(
                    $"{fieldName} cannot change after the DigZone is initialised. " +
                    "Set it on an inactive GameObject before SetActive(true).");
        }

        /// <summary>World-space AABB covering every chunk's own region.</summary>
        public Bounds WorldBounds
        {
            get
            {
                float chunkSide = _chunkSizeCells * _cellSize;
                Vector3 totalSize = new Vector3(
                    chunkSide * _chunkGridSize.x,
                    chunkSide * _chunkGridSize.y,
                    chunkSide * _chunkGridSize.z);
                return new Bounds(transform.position + totalSize * 0.5f, totalSize);
            }
        }

        public bool ContainsPoint(Vector3 worldPosition) => WorldBounds.Contains(worldPosition);

        private void Awake() => EnsureInitialised();

        private void OnEnable()
        {
            EnsureInitialised();
            DigField.Register(this);
        }

        private void OnDisable() => DigField.Unregister(this);

        private void OnDestroy() => DestroyChildChunks();

        private void Update()
        {
            // Phase 2c: poll each chunk's async Physics.BakeMesh job. When
            // the worker finishes, the chunk reassigns its MeshCollider's
            // sharedMesh to pick up the fresh cooked data.
            if (_chunks == null) return;
            for (int i = 0; i < _chunks.Length; i++)
            {
                if (_chunks[i] != null) _chunks[i].PollBakeAndSwap();
            }
        }

        private void EnsureInitialised()
        {
            if (_chunks != null) return;

            DestroyChildChunks();

            int nx = _chunkGridSize.x, ny = _chunkGridSize.y, nz = _chunkGridSize.z;
            _chunks = new DigChunk[nx * ny * nz];

            float chunkSideMeters = _chunkSizeCells * _cellSize;

            for (int cz = 0; cz < nz; cz++)
            for (int cy = 0; cy < ny; cy++)
            for (int cx = 0; cx < nx; cx++)
            {
                int idx = FlatIndex(cx, cy, cz);
                Vector3Int coord = new Vector3Int(cx, cy, cz);

                GameObject chunkObj = new GameObject($"Chunk_{cx}_{cy}_{cz}")
                {
                    hideFlags = HideFlags.DontSave,
                };
                chunkObj.transform.SetParent(transform, worldPositionStays: false);
                chunkObj.transform.localPosition = new Vector3(cx, cy, cz) * chunkSideMeters;

                chunkObj.AddComponent<MeshFilter>();
                var renderer = chunkObj.AddComponent<MeshRenderer>();
                if (_chunkMaterial != null) renderer.sharedMaterial = _chunkMaterial;
                chunkObj.AddComponent<MeshCollider>();

                var chunk = chunkObj.AddComponent<DigChunk>();
                chunk.Initialize(coord, _cellSize, _chunkSizeCells);
                _chunks[idx] = chunk;
            }

            InitializeHalfSpace();
            RebuildAllMeshes();
        }

        private void DestroyChildChunks()
        {
            if (_chunks != null)
            {
                for (int i = 0; i < _chunks.Length; i++)
                {
                    if (_chunks[i] != null)
                    {
                        if (Application.isPlaying) Destroy(_chunks[i].gameObject);
                        else DestroyImmediate(_chunks[i].gameObject);
                    }
                }
                _chunks = null;
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<DigChunk>() != null)
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>
        /// Seed every chunk's SDF with a global half-space: lower half of
        /// the entire zone (gy &lt; totalCellsY / 2) is solid, upper half
        /// is exterior.
        /// </summary>
        public void InitializeHalfSpace()
        {
            EnsureInitialised();

            int totalCellsY = _chunkGridSize.y * _chunkSizeCells;
            int splitGlobal = totalCellsY / 2;
            int dim = _chunkSizeCells + 1;
            int dimSq = dim * dim;

            for (int i = 0; i < _chunks.Length; i++)
            {
                DigChunk chunk = _chunks[i];
                Vector3Int coord = chunk.ChunkCoord;
                NativeArray<sbyte> sdf = chunk.Sdf;

                for (int z = 0; z < dim; z++)
                for (int y = 0; y < dim; y++)
                for (int x = 0; x < dim; x++)
                {
                    int globalY = coord.y * _chunkSizeCells + y;
                    int v = (globalY - splitGlobal) * 64;
                    if (v < sbyte.MinValue) v = sbyte.MinValue;
                    else if (v > sbyte.MaxValue) v = sbyte.MaxValue;
                    sdf[z * dimSq + y * dim + x] = (sbyte)v;
                }
            }
        }

        /// <summary>
        /// Apply one brush op. Two-pass: (1) brush every chunk's own SDF;
        /// (2) if anything changed, rebuild apron + remesh every chunk.
        /// Returns the total number of cells whose SDF changed.
        /// </summary>
        public int ApplyBrush(BrushOp op)
        {
            EnsureInitialised();
            int totalChanged = 0;
            for (int i = 0; i < _chunks.Length; i++)
                totalChanged += _chunks[i].ApplyBrushNoRemesh(op);

            if (totalChanged > 0) RebuildAllMeshes();
            return totalChanged;
        }

        /// <summary>
        /// Rebuild aprons for every chunk and remesh every chunk. Phase 2b
        /// uses this whenever any chunk's SDF changes; Phase 2c will add
        /// proper dirty-set propagation so only the affected chunks +
        /// their -face neighbours rebuild.
        /// </summary>
        public void RebuildAllMeshes()
        {
            EnsureInitialised();
            for (int i = 0; i < _chunks.Length; i++)
            {
                BuildApronFor(_chunks[i]);
                _chunks[i].RemeshNow();
            }
        }

        /// <summary>
        /// Populate <paramref name="chunk"/>.<see cref="DigChunk.SdfWithApron"/>
        /// with the chunk's own SDF plus a one-cell rim from its +X / +Y / +Z
        /// (and +XY, +XZ, +YZ, +XYZ) neighbours. Missing neighbours
        /// replicate the chunk's own face sample so no false sign-crossings
        /// appear at the dig zone boundary.
        /// </summary>
        public void BuildApronFor(DigChunk chunk)
        {
            Vector3Int coord = chunk.ChunkCoord;
            int dim = chunk.Dim;                   // chunkSize + 1
            int dimWithApron = chunk.DimWithApron; // chunkSize + 2
            int dimSq = dim * dim;
            int dimApronSq = dimWithApron * dimWithApron;
            NativeArray<sbyte> ownSdf = chunk.Sdf;
            NativeArray<sbyte> dst = chunk.SdfWithApron;

            for (int z = 0; z < dimWithApron; z++)
            for (int y = 0; y < dimWithApron; y++)
            for (int x = 0; x < dimWithApron; x++)
            {
                // Determine which chunk supplies this sample and the
                // sample's coords in that chunk's frame. For axes where the
                // local index is in own range [0, dim), the source chunk is
                // this chunk. For axes where the local index is == dim, the
                // source chunk's component on that axis is shifted by +1
                // and the source local coord is 1.
                int nx, ny, nz, lx, ly, lz;
                if (x < dim) { nx = coord.x; lx = x; }
                else         { nx = coord.x + 1; lx = 1; }
                if (y < dim) { ny = coord.y; ly = y; }
                else         { ny = coord.y + 1; ly = 1; }
                if (z < dim) { nz = coord.z; lz = z; }
                else         { nz = coord.z + 1; lz = 1; }

                sbyte v;
                if (nx == coord.x && ny == coord.y && nz == coord.z)
                {
                    v = ownSdf[lz * dimSq + ly * dim + lx];
                }
                else
                {
                    DigChunk neighbour = GetChunk(nx, ny, nz);
                    if (neighbour != null)
                    {
                        v = neighbour.Sdf[lz * dimSq + ly * dim + lx];
                    }
                    else
                    {
                        // No neighbour — replicate the own boundary sample
                        // along whichever axes are out of own range.
                        int rx = x < dim ? x : dim - 1;
                        int ry = y < dim ? y : dim - 1;
                        int rz = z < dim ? z : dim - 1;
                        v = ownSdf[rz * dimSq + ry * dim + rx];
                    }
                }

                dst[z * dimApronSq + y * dimWithApron + x] = v;
            }
        }

        /// <summary>Get the chunk at a grid coordinate, or null if out of range.</summary>
        public DigChunk GetChunk(int cx, int cy, int cz)
        {
            if (_chunks == null) return null;
            if (cx < 0 || cy < 0 || cz < 0 ||
                cx >= _chunkGridSize.x || cy >= _chunkGridSize.y || cz >= _chunkGridSize.z) return null;
            return _chunks[FlatIndex(cx, cy, cz)];
        }

        public DigChunk GetChunk(Vector3Int coord) => GetChunk(coord.x, coord.y, coord.z);

        private int FlatIndex(int cx, int cy, int cz)
            => (cz * _chunkGridSize.y + cy) * _chunkGridSize.x + cx;
    }
}
