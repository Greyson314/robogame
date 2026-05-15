using Robogame.Core;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// A multi-chunk dig zone. <see cref="IDigZone"/> implementer + container
    /// that manages a 3D grid of <see cref="DigChunk"/> child objects.
    /// Brush ops are dispatched to all chunks whose AABBs overlap the brush
    /// volume; each chunk decides for itself which of its cells are
    /// touched via <see cref="BrushApplicator"/>'s AABB clipping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 2a deliverable. The single-chunk path of Phases 1b/1c is the
    /// chunkGridSize = (1,1,1) case of this container.
    /// </para>
    /// <para>
    /// Chunks are spawned as <see cref="HideFlags.DontSave"/> children — the
    /// scene file only persists the DigZone GameObject, never the chunks
    /// themselves. On scene load, <see cref="EnsureInitialised"/> rebuilds
    /// chunks fresh from the zone configuration (and, Phase 2d, from a
    /// <c>.dig</c> asset reference).
    /// </para>
    /// <para>
    /// No apron handling at Phase 2a — chunks mesh independently and seams
    /// are visible at chunk boundaries. Phase 2b adds apron data flow so
    /// vertex positions agree on shared chunk edges. Phase 2c moves
    /// MeshCollider cooking to a worker thread via <c>Physics.BakeMesh</c>.
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

        /// <summary>World-space AABB covering every chunk in the zone.</summary>
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

        private void EnsureInitialised()
        {
            if (_chunks != null) return;

            DestroyChildChunks();   // defensive: clear any leaked children from a prior session.

            int nx = _chunkGridSize.x, ny = _chunkGridSize.y, nz = _chunkGridSize.z;
            _chunks = new DigChunk[nx * ny * nz];

            float chunkSideMeters = _chunkSizeCells * _cellSize;
            Vector3 zoneOrigin = transform.position;

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

            for (int i = 0; i < _chunks.Length; i++) _chunks[i].RemeshNow();
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

            // Also destroy any DontSave children that might be left over from
            // a domain reload mid-edit.
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
        /// is exterior. Produces a flat top surface plane at zone midline.
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
                Unity.Collections.NativeArray<sbyte> sdf = chunk.Sdf;

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
        /// Apply one brush op to every chunk whose AABB overlaps the brush.
        /// Returns the total number of cells whose SDF changed across all
        /// touched chunks.
        /// </summary>
        public int ApplyBrush(BrushOp op)
        {
            EnsureInitialised();
            int totalChanged = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                // Each chunk's ApplyBrush calls BrushApplicator which clips
                // to the chunk's AABB and returns 0 if the brush misses. So
                // we can dispatch to every chunk unconditionally; the cost
                // for a non-touched chunk is one AABB rejection (microseconds).
                totalChanged += _chunks[i].ApplyBrush(op);
            }
            return totalChanged;
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
