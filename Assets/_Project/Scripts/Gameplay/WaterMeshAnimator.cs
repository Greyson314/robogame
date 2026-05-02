using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Procedural tessellated water plane that animates its vertices each
    /// frame to match <see cref="WaterSurface.SampleHeight"/>. The mesh is
    /// generated once in <see cref="Awake"/>, then a cached vertex array
    /// is rewritten in <see cref="Update"/> and pushed back via
    /// <see cref="Mesh.SetVertices(System.Collections.Generic.List{Vector3})"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sits next to <see cref="WaterVolume"/> on the same GameObject —
    /// the volume is the data marker, the animator is the visual.
    /// Buoyancy keeps reading from <see cref="WaterSurface"/> directly,
    /// so the mesh and the physics are guaranteed to agree by sharing
    /// one math source.
    /// </para>
    /// <para>
    /// Performance: at the default 64×64 tessellation that's 4 225 verts
    /// updated per frame. Each vert is one <see cref="WaterSurface.SampleHeight"/>
    /// call (3 sin/cos) plus a Vector3 write. Roughly 1 M trig ops/sec
    /// at 60 FPS — well within budget on any modern CPU. We use
    /// <see cref="Mesh.MarkDynamic"/> + a re-used vertex array to avoid
    /// per-frame GC.
    /// </para>
    /// <para>
    /// Normals: recalculated every frame from the new vertex positions.
    /// That's the biggest single cost in this component (Unity's built-in
    /// is fine for our resolution but is not free). If profiling later
    /// shows a hotspot, switch to analytic normals via
    /// <see cref="WaterSurface.SampleNormal"/> per vertex — same math
    /// the buoyancy code uses.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(WaterVolume))]
    public sealed class WaterMeshAnimator : MonoBehaviour
    {
        [Tooltip("Total side length of the water plane in metres. Should match " +
                 "the arena's halfExtent * 2 so the mesh covers the full bay.")]
        [SerializeField, Min(1f)] private float _size = 200f;

        [Tooltip("Quads per side. 64 → ~3 m per quad on a 200 m plane (visible " +
                 "curvature, ~4 k verts). Tessellation cost scales with N², so " +
                 "doubling this multiplies the per-frame cost by 4.")]
        [SerializeField, Range(2, 256)] private int _tessellation = 64;

        [Tooltip("If true, recalculate normals on the CPU each frame so " +
                 "lighting follows the waves. Cost is ~25% of the vertex update; " +
                 "leave on unless profiling demands otherwise.")]
        [SerializeField] private bool _recalculateNormals = true;

        [Header("Foam")]
        [Tooltip("Width in metres of the foam band along the arena perimeter. " +
                 "Verts within this distance of the nearest edge fade from full " +
                 "foam at the wall to no foam inland.")]
        [SerializeField, Min(0f)] private float _perimeterFoamWidth = 6f;

        [Tooltip("Fraction of the configured wave amplitude (Tweakables.WaveAmplitude) " +
                 "above which a crest starts producing foam. 0.7 means only the top " +
                 "30% of each wave reads as foamy.")]
        [SerializeField, Range(0f, 1f)] private float _crestFoamThreshold = 0.70f;

        [Tooltip("Multiplier on the crest foam amount. 0.5–0.7 reads as occasional " +
                 "highlights on the wave tips; crank to 1+ for breaking-wave whitecaps.")]
        [SerializeField, Range(0f, 4f)] private float _crestFoamStrength = 0.60f;

        [Tooltip("Radius in metres around each block-on-waterline contact within " +
                 "which wake foam is laid down. Larger = wider, softer wakes; smaller = " +
                 "crisper trailing tracks. ~2–3 m feels chunky and hand-painted.")]
        [SerializeField, Min(0.1f)] private float _wakeFoamRadius = 2.5f;

        [Tooltip("Peak wake foam strength at a contact point (smooth-falloff to 0 at " +
                 "the radius edge). Max-blended with perimeter and crest foam, so this " +
                 "caps how white the wake gets.")]
        [SerializeField, Range(0f, 2f)] private float _wakeFoamStrength = 0.85f;

        private Mesh _mesh;
        private Vector3[] _baseVerts;   // flat-grid positions (y = 0)
        private Vector3[] _liveVerts;   // mutable buffer pushed to Mesh
        private Color[] _liveColors;    // live foam mask (perimeter ∪ crests)
        private float[] _perimeterFoam; // 0..1 per vert, computed once in BuildMesh
        private WaterVolume _volume;

        private void Awake()
        {
            _volume = GetComponent<WaterVolume>();
            BuildMesh();
        }

        private void OnValidate()
        {
            // Rebuild in editor when authoring values change so the
            // designer sees the new tessellation immediately.
            if (Application.isPlaying) return;
            if (TryGetComponent<MeshFilter>(out _) && TryGetComponent<WaterVolume>(out _))
            {
                _volume = GetComponent<WaterVolume>();
                BuildMesh();
            }
        }

        private void Update()
        {
            if (_mesh == null || _baseVerts == null || _liveVerts == null) return;

            float time = Time.timeSinceLevelLoad;
            // Using transform.position so a moving water volume (we
            // don't have any, but it's correct) still samples in world
            // space. The base verts are already in local-space XZ; we
            // offset them when sampling and write back local-space Y.
            float originX = transform.position.x;
            float originZ = transform.position.z;
            float baseY = _volume != null ? _volume.SurfaceY : transform.position.y;

            // Crest foam derives from how high a vert sits above the resting
            // surface relative to the configured amplitude. Reading the
            // tweakable once per frame keeps live tuning responsive without
            // burning a lookup per vert.
            float amplitude = Mathf.Max(0.001f, Robogame.Core.Tweakables.Get(Robogame.Core.Tweakables.WaveAmplitude));
            float threshold = _crestFoamThreshold;
            float crestStrength = _crestFoamStrength;

            // Wake foam: pull the latest set of "block straddling the
            // waterline" points from every active BuoyancyController.
            // Snapshotting once per frame keeps the inner vert loop free
            // of allocations / virtual dispatch. The set is typically tiny
            // (one chassis → ~5–40 points), so testing each vert against
            // each point is cheap (4k verts × 30 points = 120k ops/frame).
            float wakeRadius = _wakeFoamRadius;
            float wakeRadiusSqr = wakeRadius * wakeRadius;
            float wakeStrength = _wakeFoamStrength;
            var bouys = BuoyancyController.Active;

            for (int i = 0; i < _baseVerts.Length; i++)
            {
                Vector3 b = _baseVerts[i];
                float worldX = originX + b.x;
                float worldZ = originZ + b.z;
                // SampleHeight returns absolute Y (includes SurfaceY); we
                // want the local offset relative to our transform.
                float surfaceY = _volume != null
                    ? WaterSurface.SampleHeight(_volume, worldX, worldZ, time)
                    : baseY;
                float dy = surfaceY - baseY;
                _liveVerts[i] = new Vector3(b.x, dy, b.z);

                // Crest mask: 0 below the threshold, ramping to 1 at the
                // configured amplitude. Multiply by strength and saturate.
                float crestNorm = (dy / amplitude - threshold) / Mathf.Max(0.001f, 1f - threshold);
                float crest = Mathf.Clamp01(crestNorm) * crestStrength;

                // Wake mask: nearest contact within radius; smooth falloff.
                // We only loop the inner Lists when wakeStrength > 0 to give
                // designers a fast off-switch.
                float wake = 0f;
                if (wakeStrength > 0f && bouys.Count > 0)
                {
                    foreach (var bc in bouys)
                    {
                        if (bc == null) continue;
                        var pts = bc.SurfaceContacts;
                        for (int p = 0, n = pts.Count; p < n; p++)
                        {
                            Vector2 pt = pts[p];
                            float dx = pt.x - worldX;
                            float dz = pt.y - worldZ;
                            float dSqr = dx * dx + dz * dz;
                            if (dSqr >= wakeRadiusSqr) continue;
                            float t = 1f - Mathf.Sqrt(dSqr) / wakeRadius;
                            // Cubic smoothstep so the foam halo doesn't have
                            // a hard outer edge.
                            float falloff = t * t * (3f - 2f * t);
                            float w = falloff * wakeStrength;
                            if (w > wake) wake = w;
                        }
                    }
                }

                // Combine all three foam sources via max-blend. Sticking to
                // max (not sum) keeps the result in [0,1] so the shader
                // never saturates back to white the way it did before the
                // foam mask was authored explicitly.
                float foam = Mathf.Max(_perimeterFoam[i], Mathf.Max(Mathf.Clamp01(crest), Mathf.Clamp01(wake)));
                _liveColors[i] = new Color(foam, 0f, 0f, 1f);
            }

            _mesh.vertices = _liveVerts;
            _mesh.colors = _liveColors;
            if (_recalculateNormals) _mesh.RecalculateNormals();
            // Bounds don't tighten around the wave envelope — recomputing
            // them every frame is wasted work since the surface is always
            // close to flat. We set generous bounds once in BuildMesh.
        }

        // -----------------------------------------------------------------
        // Mesh construction
        // -----------------------------------------------------------------

        private void BuildMesh()
        {
            int N = Mathf.Max(2, _tessellation);
            int verts = (N + 1) * (N + 1);
            int quads = N * N;

            _baseVerts = new Vector3[verts];
            _liveVerts = new Vector3[verts];
            var uvs = new Vector2[verts];
            var tris = new int[quads * 6];
            // Bitgem's stylised water shader reads vertex colour as a foam
            // mask (red = foam edge, black = open water). Default Unity
            // mesh vertex colour is white, which the shader reads as
            // "100% foam" and the surface goes pure white — hence the
            // explicit Color[] write below. URP/Lit fallback ignores this
            // channel so the assignment is harmless either way.
            //
            // We split the foam contribution into two terms:
            //   * _perimeterFoam[i]  — static, distance-to-edge falloff,
            //                          baked once below.
            //   * crest term         — recomputed every frame in Update
            //                          from the live wave height.
            // Update() max-blends the two into _liveColors and pushes the
            // result via Mesh.colors. We allocate the buffers here so the
            // hot path can skip the size check.
            _liveColors = new Color[verts];
            _perimeterFoam = new float[verts];

            float half = _size * 0.5f;
            float step = _size / N;
            float foamWidth = Mathf.Max(0.0001f, _perimeterFoamWidth);

            for (int z = 0, vi = 0; z <= N; z++)
            {
                for (int x = 0; x <= N; x++, vi++)
                {
                    float px = -half + x * step;
                    float pz = -half + z * step;
                    _baseVerts[vi] = new Vector3(px, 0f, pz);
                    _liveVerts[vi] = _baseVerts[vi];
                    uvs[vi] = new Vector2((float)x / N, (float)z / N);

                    // Perimeter foam: distance to the nearest edge of the
                    // square plane, normalised by foamWidth and inverted so
                    // verts at the wall = 1, verts beyond foamWidth = 0.
                    float distToEdge = Mathf.Min(half - Mathf.Abs(px), half - Mathf.Abs(pz));
                    float t = 1f - Mathf.Clamp01(distToEdge / foamWidth);
                    // Smooth the falloff (cubic-ish) so the band doesn't have
                    // a hard inner edge.
                    _perimeterFoam[vi] = t * t * (3f - 2f * t);
                    _liveColors[vi] = new Color(_perimeterFoam[vi], 0f, 0f, 1f);
                }
            }

            for (int z = 0, ti = 0, vi = 0; z < N; z++, vi++)
            {
                for (int x = 0; x < N; x++, ti += 6, vi++)
                {
                    int a = vi;
                    int b = vi + 1;
                    int c = vi + (N + 1);
                    int d = vi + (N + 1) + 1;
                    // Two tris per quad. Wound CCW so the +Y side faces up.
                    tris[ti + 0] = a;
                    tris[ti + 1] = c;
                    tris[ti + 2] = b;
                    tris[ti + 3] = b;
                    tris[ti + 4] = c;
                    tris[ti + 5] = d;
                }
            }

            // Use a fresh mesh per instance — sharing across multiple
            // WaterVolumes (none today, but cheap insurance) would race on
            // the vertex buffer. 32-bit index format keeps us safe past
            // 256-step tessellations.
            _mesh = new Mesh
            {
                name = "WaterSurfaceMesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
            _mesh.MarkDynamic();
            _mesh.vertices = _liveVerts;
            _mesh.uv = uvs;
            _mesh.colors = _liveColors; // perimeter foam baked, crests added in Update
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();

            // Generous bounds so frustum culling never hides the surface
            // when the camera dips just below the average water level.
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(_size, 8f, _size));

            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
        }
    }
}
