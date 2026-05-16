using System.Collections.Generic;
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

        /// <summary>
        /// Optional <c>.dig</c> asset (Phase 2d). If assigned, the asset's
        /// header overrides <see cref="CellSize"/>, <see cref="ChunkSizeCells"/>,
        /// <see cref="ChunkGridSize"/>, and its payload seeds each chunk's
        /// SDF instead of <see cref="InitializeHalfSpace"/>.
        /// </summary>
        [SerializeField] private TextAsset _digAsset;

        [Header("Perimeter visual")]
        [Tooltip("Draw a wireframe cube around the dig zone so players can see where to dig.")]
        [SerializeField] private bool _drawPerimeter = true;
        [Tooltip("Wireframe colour. Defaults to hazard yellow.")]
        [SerializeField] private Color _perimeterColor = new Color(0.95f, 0.75f, 0.15f, 1f);

        [Header("LOD")]
        [Tooltip("Chunks farther than this from the view camera mesh at half resolution.")]
        [SerializeField, Min(0f)] private float _lodDistance1 = 32f;
        [Tooltip("Chunks farther than this mesh at quarter resolution.")]
        [SerializeField, Min(0f)] private float _lodDistance2 = 64f;
        [Tooltip("Disable to lock every chunk at LOD 0.")]
        [SerializeField] private bool _enableLod = true;

        [Header("Initial Carving (Phase 5 POI authoring stand-in)")]
        [Tooltip("Brushes applied to the SDF at init time, AFTER half-space / snapshot seeding " +
                 "but BEFORE the occupancy grid is built. Stand-in for the .dig baker's pre-carve " +
                 "of POI chambers — runtime-only, regenerates each scene load.")]
        [SerializeField] private List<InitialBrushSpec> _initialBrushes = new();

        [Tooltip("When true, every cell inside the zone is interior (solid) at init time — " +
                 "ignoring the half-space split. Use for zones authored as a 'block of dirt' " +
                 "the player tunnels into, vs. the default half-space 'plane of ground'. The " +
                 "outer-layer-exterior rule still applies, so the zone reads as a watertight cube.")]
        [SerializeField] private bool _initFullySolid;

        /// <summary>
        /// Editor-authored / scaffolder-authored brush to apply once at
        /// zone init, after SDF seeding but before occupancy + mesh
        /// builds. Used to pre-carve POI chambers without going through
        /// the full Phase 2d .dig baker pipeline.
        /// </summary>
        [System.Serializable]
        public struct InitialBrushSpec
        {
            public BrushKind Kind;
            public Vector3 CenterWorld;
            public Vector3 EndWorld;        // ignored for SphereSubtract; capsule end for CapsuleSubtract
            [Min(0.1f)] public float RadiusMeters;
        }

        private GameObject _perimeterObj;
        private Mesh _perimeterMesh;
        private Material _perimeterMaterial;

        private DigChunk[] _chunks;
        // Phase 5: coarse occupancy grid for AI pathfinding. Rebuilt
        // per-chunk after each remesh.
        private OccupancyGrid _occupancyGrid;
        // Phase 6: cumulative log of every brush op that actually
        // mutated the SDF. Late-join replay sends this list to a
        // connecting client; the client replays in any order
        // (commutativity per TERRAFORMING_PLAN § 2). Ops that touched
        // zero cells (e.g., a sphere brush that fell entirely outside
        // every chunk's AABB, or applied to an already-exterior region)
        // aren't logged — the log is "real changes only" so its size
        // tracks gameplay impact, not call rate.
        // Phase 7 compaction drops entries whose tick is at-or-before
        // the latest Checkpoint's tick; see Checkpoint() and OpLog.
        private readonly List<BrushOp> _opLog = new();

        // Phase 7 — op-log checkpointing. When the server calls
        // Checkpoint(tick), we serialise the current chunk SDFs to the
        // same wire format the .dig baker uses (DigZoneFormat) and drop
        // ops at-or-before the snapshot from _opLog. Late-join replays
        // (snapshot bytes + remaining ops) instead of the full from-
        // match-start trail. The byte buffer lives in memory; transport
        // hands it to the joining client over ClientRpc once that lands.
        private byte[] _snapshotBytes;
        private ushort _snapshotTick;
        private bool _hasSnapshot;

        /// <summary>Coarse AI-navigation grid covering the zone. Null
        /// until <see cref="EnsureInitialised"/> runs.</summary>
        public OccupancyGrid OccupancyGrid => _occupancyGrid;

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

        private void OnDestroy()
        {
            DestroyChildChunks();
            DestroyPerimeter();
        }

        private void Update()
        {
            // Phase 2c: poll async Physics.BakeMesh completions and
            // refresh collider sharedMesh.
            // Phase 4b: refresh per-chunk LOD from main-camera distance.
            if (_chunks == null) return;
            if (_enableLod)
            {
                Camera cam = Camera.main;
                if (cam != null) RefreshLod(cam.transform.position);
            }
            for (int i = 0; i < _chunks.Length; i++)
            {
                if (_chunks[i] != null) _chunks[i].PollBakeAndSwap();
            }
        }

        /// <summary>
        /// Choose a LOD level for every chunk based on its distance from
        /// <paramref name="viewWorldPos"/>. Chunks whose level changes are
        /// re-meshed (which schedules a fresh bake). If any chunk's LOD
        /// changed, runs a full <see cref="RebuildAllMeshes"/> pass
        /// afterwards so every chunk's <see cref="NeighbourLodStrides"/>
        /// reflects the post-change LOD topology (Phase 4c — a chunk needs
        /// its NEIGHBOUR's new LOD to decide whether to snap/suppress on
        /// the shared face). Exposed for tests.
        /// </summary>
        public void RefreshLod(Vector3 viewWorldPos)
        {
            if (_chunks == null) return;
            float d1Sq = _lodDistance1 * _lodDistance1;
            float d2Sq = _lodDistance2 * _lodDistance2;
            bool anyChanged = false;
            for (int i = 0; i < _chunks.Length; i++)
            {
                DigChunk c = _chunks[i];
                if (c == null) continue;
                float distSq = (c.WorldBounds.center - viewWorldPos).sqrMagnitude;
                int lod;
                if (distSq > d2Sq) lod = 2;
                else if (distSq > d1Sq) lod = 1;
                else lod = 0;
                if (lod != c.CurrentLodLevel)
                {
                    c.SetLodLevel(lod);   // remeshes with stale strides …
                    anyChanged = true;
                }
            }
            if (anyChanged) RebuildAllMeshes();  // … fresh strides land here.
        }

        private void EnsureInitialised()
        {
            if (_chunks != null) return;

            DestroyChildChunks();

            // Phase 2d: if a .dig asset is assigned, its header drives the
            // grid config. Parse before chunk spawn so the spawned grid
            // matches the asset's dimensions.
            DigZoneSnapshot snapshot = null;
            if (_digAsset != null && _digAsset.bytes != null && _digAsset.bytes.Length > 0)
            {
                snapshot = DigZoneFormat.Read(_digAsset.bytes);
                _cellSize = snapshot.CellSize;
                _chunkSizeCells = snapshot.ChunkSizeCells;
                _chunkGridSize = snapshot.ChunkGridSize;
            }

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

            if (snapshot != null)
            {
                ApplySnapshot(snapshot);
            }
            else
            {
                InitializeHalfSpace();
            }

            // Phase 5: bake the scaffolder-authored initial brushes
            // (POI chambers, pre-dug tunnels). Must run AFTER the SDF
            // is seeded and BEFORE the occupancy grid is built — the
            // grid classifies cells from the post-brush SDF.
            ApplyInitialBrushesToSdf();

            // Phase 5: occupancy grid covering the entire zone. The grid
            // is allocated empty here; RebuildAllMeshes (below) populates
            // it from each chunk's SDF.
            int occPerChunk = _chunkSizeCells / OccupancyGrid.VoxelCellsPerOccupancyCell;
            _occupancyGrid = new OccupancyGrid(
                worldOrigin: transform.position,
                sizeX: nx * occPerChunk,
                sizeY: ny * occPerChunk,
                sizeZ: nz * occPerChunk,
                voxelCellSize: _cellSize);

            RebuildAllMeshes();
            BuildPerimeter();
        }

        private void ApplyInitialBrushesToSdf()
        {
            if (_initialBrushes == null || _initialBrushes.Count == 0) return;
            for (int i = 0; i < _initialBrushes.Count; i++)
            {
                InitialBrushSpec spec = _initialBrushes[i];
                BrushOp op = new BrushOp
                {
                    kind = spec.Kind,
                    serverTick = 0,
                    p0 = Vector3Fixed.FromVector3(spec.CenterWorld),
                    p1 = Vector3Fixed.FromVector3(spec.Kind == BrushKind.SphereSubtract
                        ? spec.CenterWorld : spec.EndWorld),
                    radiusFixed = (ushort)Mathf.Clamp(
                        Mathf.RoundToInt(spec.RadiusMeters * Vector3Fixed.UnitsPerMeter),
                        0, ushort.MaxValue),
                };
                // Apply per-chunk WITHOUT routing through ApplyBrush —
                // ApplyBrush would call RebuildAllMeshes mid-init when
                // the occupancy grid + mesh pipeline aren't ready yet.
                for (int c = 0; c < _chunks.Length; c++)
                    _chunks[c].ApplyBrushNoRemesh(op);
            }
        }

        // -----------------------------------------------------------------
        // Perimeter visual — wireframe cube outlining the zone's extent.
        // Always-visible runtime line mesh; not a gameplay element. Lives
        // as a HideFlags.DontSave child like the chunks.
        // -----------------------------------------------------------------

        private void BuildPerimeter()
        {
            if (!_drawPerimeter) return;
            DestroyPerimeter();

            _perimeterObj = new GameObject("Perimeter") { hideFlags = HideFlags.DontSave };
            _perimeterObj.transform.SetParent(transform, worldPositionStays: false);
            _perimeterObj.transform.localPosition = Vector3.zero;

            var mf = _perimeterObj.AddComponent<MeshFilter>();
            var mr = _perimeterObj.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float chunkSide = _chunkSizeCells * _cellSize;
            Vector3 size = new Vector3(
                chunkSide * _chunkGridSize.x,
                chunkSide * _chunkGridSize.y,
                chunkSide * _chunkGridSize.z);

            _perimeterMesh = new Mesh { name = $"{name}_Perimeter" };
            Vector3[] verts =
            {
                new Vector3(0,      0,      0),       // 0
                new Vector3(size.x, 0,      0),       // 1
                new Vector3(0,      size.y, 0),       // 2
                new Vector3(size.x, size.y, 0),       // 3
                new Vector3(0,      0,      size.z),  // 4
                new Vector3(size.x, 0,      size.z),  // 5
                new Vector3(0,      size.y, size.z),  // 6
                new Vector3(size.x, size.y, size.z),  // 7
            };
            int[] lineIndices =
            {
                0, 1, 1, 5, 5, 4, 4, 0,   // bottom face perimeter
                2, 3, 3, 7, 7, 6, 6, 2,   // top face perimeter
                0, 2, 1, 3, 4, 6, 5, 7,   // 4 vertical edges
            };
            _perimeterMesh.vertices = verts;
            _perimeterMesh.SetIndices(lineIndices, MeshTopology.Lines, submesh: 0);
            _perimeterMesh.bounds = new Bounds(size * 0.5f, size);
            mf.sharedMesh = _perimeterMesh;

            // Best-effort unlit material. URP/Unlit if present (URP project);
            // otherwise fall back to the legacy Unlit/Color.
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _perimeterMaterial = new Material(sh) { name = "DigZonePerimeter" };
            _perimeterMaterial.color = _perimeterColor;
            if (_perimeterMaterial.HasProperty("_BaseColor"))
                _perimeterMaterial.SetColor("_BaseColor", _perimeterColor);
            mr.sharedMaterial = _perimeterMaterial;
        }

        private void DestroyPerimeter()
        {
            if (_perimeterObj != null)
            {
                if (Application.isPlaying) Destroy(_perimeterObj);
                else DestroyImmediate(_perimeterObj);
                _perimeterObj = null;
            }
            if (_perimeterMesh != null)
            {
                if (Application.isPlaying) Destroy(_perimeterMesh);
                else DestroyImmediate(_perimeterMesh);
                _perimeterMesh = null;
            }
            if (_perimeterMaterial != null)
            {
                if (Application.isPlaying) Destroy(_perimeterMaterial);
                else DestroyImmediate(_perimeterMaterial);
                _perimeterMaterial = null;
            }
        }

        /// <summary>
        /// Overwrite every chunk's SDF with the snapshot's payload. Used by
        /// the Phase 2d loader path (`.dig` asset → SDF) and by tests doing
        /// bake-and-load round-trips.
        /// </summary>
        public void ApplySnapshot(DigZoneSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            EnsureInitialised();

            if (snapshot.ChunkGridSize != _chunkGridSize ||
                snapshot.ChunkSizeCells != _chunkSizeCells ||
                !Mathf.Approximately(snapshot.CellSize, _cellSize))
            {
                throw new System.InvalidOperationException(
                    "Snapshot dimensions disagree with the DigZone's current config. " +
                    $"Snapshot: grid={snapshot.ChunkGridSize} chunkSize={snapshot.ChunkSizeCells} cellSize={snapshot.CellSize}. " +
                    $"Zone: grid={_chunkGridSize} chunkSize={_chunkSizeCells} cellSize={_cellSize}.");
            }

            int dim = _chunkSizeCells + 1;
            int sdfBytes = dim * dim * dim;
            foreach (DigZoneSnapshot.Chunk sc in snapshot.Chunks)
            {
                DigChunk chunk = GetChunk(sc.ChunkCoord);
                if (chunk == null)
                    throw new System.InvalidOperationException(
                        $"Snapshot references chunk {sc.ChunkCoord} which doesn't exist in the spawned grid.");
                if (sc.Sdf.Length != sdfBytes)
                    throw new System.InvalidOperationException(
                        $"Snapshot chunk {sc.ChunkCoord} SDF length {sc.Sdf.Length} != expected {sdfBytes}.");

                NativeArray<sbyte> dst = chunk.Sdf;
                for (int i = 0; i < sdfBytes; i++) dst[i] = (sbyte)sc.Sdf[i];
            }
        }

        /// <summary>Test-friendly setter for the .dig asset before activation. Throws once initialised.</summary>
        public TextAsset DigAsset
        {
            get => _digAsset;
            set { ThrowIfInitialised(nameof(DigAsset)); _digAsset = value; }
        }

        /// <summary>
        /// Append an initial brush spec to be applied at <see cref="EnsureInitialised"/>
        /// time, after SDF seeding but before the occupancy grid is built.
        /// Throws once the zone is initialised. Test/scaffolder helper —
        /// in shipping content these are authored via the SerializedField
        /// inspector or by <c>EnvironmentBuilder</c>.
        /// </summary>
        public void AddInitialBrush(InitialBrushSpec spec)
        {
            ThrowIfInitialised(nameof(AddInitialBrush));
            if (_initialBrushes == null) _initialBrushes = new List<InitialBrushSpec>();
            _initialBrushes.Add(spec);
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
        /// Seed every chunk's SDF. Default behaviour is a global half-space
        /// (lower half of the zone solid, upper half exterior). When
        /// <see cref="_initFullySolid"/> is true the entire zone interior
        /// is solid instead. In both modes, the outermost sample layer
        /// of the zone (the six face planes at globalX/Y/Z = 0 or =
        /// totalCells*) is forced exterior — that's what makes the zone
        /// read as a watertight cube in the surface-nets output, instead
        /// of a single floating half-space plane.
        /// </summary>
        public void InitializeHalfSpace()
        {
            EnsureInitialised();

            int totalCellsX = _chunkGridSize.x * _chunkSizeCells;
            int totalCellsY = _chunkGridSize.y * _chunkSizeCells;
            int totalCellsZ = _chunkGridSize.z * _chunkSizeCells;
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
                    int globalX = coord.x * _chunkSizeCells + x;
                    int globalY = coord.y * _chunkSizeCells + y;
                    int globalZ = coord.z * _chunkSizeCells + z;

                    sbyte sample;
                    bool isZoneBoundary =
                        globalX == 0 || globalX == totalCellsX ||
                        globalY == 0 || globalY == totalCellsY ||
                        globalZ == 0 || globalZ == totalCellsZ;

                    if (isZoneBoundary)
                    {
                        // Force exterior at the zone's outer face planes
                        // so the mesher emits a watertight cube boundary
                        // rather than running the SDF off into empty
                        // space.
                        sample = sbyte.MaxValue;
                    }
                    else if (_initFullySolid)
                    {
                        sample = sbyte.MinValue;
                    }
                    else
                    {
                        int v = (globalY - splitGlobal) * 64;
                        if (v < sbyte.MinValue) v = sbyte.MinValue;
                        else if (v > sbyte.MaxValue) v = sbyte.MaxValue;
                        sample = (sbyte)v;
                    }

                    sdf[z * dimSq + y * dim + x] = sample;
                }
            }
        }

        /// <summary>
        /// Apply one brush op. Two-pass: (1) brush every chunk's own SDF;
        /// (2) if anything changed, rebuild apron + remesh every chunk.
        /// Returns the total number of cells whose SDF changed.
        /// </summary>
        /// <remarks>
        /// Phase 6: ops that actually mutated the SDF append to the
        /// cumulative <see cref="OpLog"/>. Use <see cref="ReplayLog"/>
        /// on a fresh zone to converge to the same SDF (commutativity).
        /// </remarks>
        public int ApplyBrush(BrushOp op)
        {
            EnsureInitialised();
            int totalChanged = 0;
            for (int i = 0; i < _chunks.Length; i++)
                totalChanged += _chunks[i].ApplyBrushNoRemesh(op);

            if (totalChanged > 0)
            {
                _opLog.Add(op);
                RebuildAllMeshes();
            }
            return totalChanged;
        }

        /// <summary>
        /// Cumulative log of brush ops that actually mutated the SDF.
        /// Late-join replication sends this list to a connecting client.
        /// </summary>
        /// <remarks>
        /// Phase 7: after <see cref="Checkpoint"/>, this contains only ops
        /// whose tick is strictly after <see cref="SnapshotTick"/> —
        /// older entries are baked into <see cref="SnapshotBytes"/>.
        /// </remarks>
        public IReadOnlyList<BrushOp> OpLog => _opLog;

        /// <summary>
        /// Apply every op in <paramref name="log"/> in order. Equivalent
        /// to calling <see cref="ApplyBrush"/> per entry, except the log
        /// is appended to (not duplicated): only ops that change cells
        /// land in the new <see cref="OpLog"/>. Commutativity guarantees
        /// the SDF converges to the same state for any ordering of the
        /// same op set.
        /// </summary>
        public void ReplayLog(IReadOnlyList<BrushOp> log)
        {
            if (log == null) return;
            for (int i = 0; i < log.Count; i++) ApplyBrush(log[i]);
        }

        /// <summary>
        /// Phase 7 — capture the current chunk SDFs as a snapshot tied to
        /// <paramref name="serverTick"/>, then compact <see cref="OpLog"/>
        /// by dropping entries whose tick is at-or-before the snapshot.
        /// Late-join replication sends (<see cref="SnapshotBytes"/> +
        /// <see cref="OpLog"/>) to a joining client; the joiner applies
        /// the snapshot then replays the remaining ops to converge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Snapshot bytes use the same wire format as the Phase 2d
        /// <c>.dig</c> baker (<see cref="DigZoneFormat.Write"/>) — content-
        /// hash protected, parseable by <see cref="DigZoneFormat.Read"/>,
        /// applied via <see cref="ApplySnapshot"/>. Calling
        /// <see cref="Checkpoint"/> twice replaces the prior snapshot.
        /// </para>
        /// <para>
        /// Ticks are <see cref="ushort"/>; comparison uses serial-number
        /// arithmetic (RFC 1982-style) so an op with tick = 5 after a
        /// snapshot at tick 65530 is correctly retained. The comparison
        /// stays correct as long as snapshots happen at least every
        /// 2^15 = 32 768 ticks (~18 min at the 30 Hz drill rate).
        /// </para>
        /// </remarks>
        public void Checkpoint(ushort serverTick)
        {
            EnsureInitialised();
            _snapshotBytes = DigZoneFormat.Write(this);
            _snapshotTick = serverTick;
            _hasSnapshot = true;

            int kept = 0;
            for (int i = 0; i < _opLog.Count; i++)
            {
                if (IsTickAfter(_opLog[i].serverTick, _snapshotTick))
                    _opLog[kept++] = _opLog[i];
            }
            if (kept < _opLog.Count) _opLog.RemoveRange(kept, _opLog.Count - kept);
        }

        /// <summary>True after the first <see cref="Checkpoint"/> call.</summary>
        public bool HasSnapshot => _hasSnapshot;

        /// <summary>Server tick the latest <see cref="Checkpoint"/> captured at, or 0 if none.</summary>
        public ushort SnapshotTick => _hasSnapshot ? _snapshotTick : (ushort)0;

        /// <summary>
        /// Snapshot bytes from the latest <see cref="Checkpoint"/> in
        /// <c>.dig</c> wire format. Null until a checkpoint has run.
        /// Late-join transport sends this verbatim to the joining client,
        /// which feeds it to <see cref="DigZoneFormat.Read"/> +
        /// <see cref="ApplySnapshot"/>.
        /// </summary>
        public byte[] SnapshotBytes => _snapshotBytes;

        /// <summary>
        /// Serial-number-arithmetic "is <paramref name="a"/> after
        /// <paramref name="b"/>" predicate for the <see cref="ushort"/>
        /// tick space. Cast to signed so wraparound at 65535→0 is
        /// handled correctly when the two ticks fall within half the
        /// period of each other.
        /// </summary>
        private static bool IsTickAfter(ushort a, ushort b) => (short)(a - b) > 0;

        /// <summary>
        /// Rebuild aprons for every chunk and remesh every chunk. Phase 2b
        /// uses this whenever any chunk's SDF changes; Phase 2c will add
        /// proper dirty-set propagation so only the affected chunks +
        /// their -face neighbours rebuild. Phase 5 also rebuilds the
        /// occupancy slice for each chunk.
        /// </summary>
        public void RebuildAllMeshes()
        {
            EnsureInitialised();
            for (int i = 0; i < _chunks.Length; i++)
            {
                BuildApronFor(_chunks[i]);
                _chunks[i].RemeshNow();
                if (_occupancyGrid != null)
                {
                    _occupancyGrid.BuildFromChunkSdf(
                        _chunks[i].ChunkCoord, _chunkSizeCells,
                        _chunks[i].Sdf, _chunks[i].Dim);
                }
            }
        }

        /// <summary>
        /// Populate <paramref name="chunk"/>.<see cref="DigChunk.SdfWithApron"/>
        /// with the chunk's own SDF plus a one-cell rim from its +X / +Y / +Z
        /// (and +XY, +XZ, +YZ, +XYZ) neighbours. Missing neighbours
        /// replicate the chunk's own face sample so no false sign-crossings
        /// appear at the dig zone boundary.
        /// </summary>
        /// <remarks>
        /// Phase 4c: also writes <paramref name="chunk"/>.<see cref="DigChunk.NeighbourLodStrides"/>
        /// from each face-neighbour's current LOD relative to this chunk's
        /// own LOD. Must run AFTER <see cref="RefreshLod"/> has set per-chunk
        /// LOD levels — the current call order in <see cref="RebuildAllMeshes"/>
        /// satisfies this because <c>RefreshLod</c> runs in <c>Update</c> on
        /// the prior frame, and brush-triggered remeshes inherit the
        /// current LODs.
        /// </remarks>
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

            // Phase 4c: per-face LOD stride lookup. snap stride > 1 only
            // when the face-neighbour is meshing at coarser LOD than this
            // chunk; otherwise 1 (no snap, no quad suppression). Use this
            // chunk's own LOD as the fallback for missing neighbours so
            // boundary faces of the zone never snap (the "no neighbour"
            // case replicates own data into the apron — same-LOD geometry,
            // no seam).
            int ownLod = chunk.CurrentLodLevel;
            chunk.NeighbourLodStrides = new NeighbourLodStrides
            {
                NegX = ComputeNeighbourStride(coord.x - 1, coord.y, coord.z, ownLod),
                PosX = ComputeNeighbourStride(coord.x + 1, coord.y, coord.z, ownLod),
                NegY = ComputeNeighbourStride(coord.x, coord.y - 1, coord.z, ownLod),
                PosY = ComputeNeighbourStride(coord.x, coord.y + 1, coord.z, ownLod),
                NegZ = ComputeNeighbourStride(coord.x, coord.y, coord.z - 1, ownLod),
                PosZ = ComputeNeighbourStride(coord.x, coord.y, coord.z + 1, ownLod),
            };
        }

        private int ComputeNeighbourStride(int nx, int ny, int nz, int ownLod)
        {
            DigChunk neighbour = GetChunk(nx, ny, nz);
            if (neighbour == null) return 1;
            int neighbourLod = neighbour.CurrentLodLevel;
            if (neighbourLod <= ownLod) return 1;
            return 1 << (neighbourLod - ownLod);
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
