using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Builds the arena ground as a procedurally-displaced subdivided
    /// mesh — gentle Perlin-noise hills, with a flat spawn zone in the
    /// middle and flat boundary at the edges so neither the obstacle
    /// course nor the wall ring fights the topology.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authoring knobs live in <see cref="HillsSettings"/>, an asset at
    /// <c>Assets/_Project/ScriptableObjects/HillsSettings.asset</c>.
    /// First build creates that asset with sensible defaults; after
    /// that you edit the asset in the inspector and re-run Build Arena
    /// Pass A to rebake the mesh.
    /// </para>
    /// <para>
    /// Why a sculpted mesh instead of Unity Terrain: Terrain comes with
    /// its own renderer / texturing / detail-mesh stack, none of which
    /// integrates cleanly with Fluff (Fluff just wants a mesh with a
    /// material). A subdivided plane gives us the same look at a
    /// fraction of the complexity, and Fluff's world-space sampling +
    /// surface-normal exclusion mean the grass automatically follows
    /// the curvature without us doing anything special.
    /// </para>
    /// <para>
    /// Mesh is baked once per <c>Build</c> call and saved as a real
    /// asset under <c>Assets/_Project/Art/Generated/</c>. That way the
    /// scene file references a stable GUID, version control is happy,
    /// and we don't burn the cost of regenerating tens of thousands of
    /// vertices on every scene load.
    /// </para>
    /// </remarks>
    public static class HillsGround
    {
        // -----------------------------------------------------------------
        // Asset paths
        // -----------------------------------------------------------------

        public  const string GeneratedFolder = "Assets/_Project/Art/Generated";
        public  const string MeshPath        = GeneratedFolder + "/Mesh_ArenaHills.asset";
        public  const string SettingsFolder  = "Assets/_Project/ScriptableObjects";
        public  const string SettingsPath    = SettingsFolder + "/HillsSettings.asset";

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Build (or reuse) the hills mesh asset and instantiate it as a
        /// child of <paramref name="parent"/>. Adds a <c>MeshCollider</c>
        /// (sharing the same mesh) so vehicles can drive on it. Returns
        /// the GameObject so the caller can wire grass / palette / etc.
        /// </summary>
        public static GameObject Build(Transform parent, string name = "Ground")
        {
            HillsSettings settings = LoadOrCreateSettings();
            Mesh mesh = GetOrBuildMesh(settings);

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = Vector3.zero;
            go.transform.localScale = Vector3.one;
            go.isStatic = true;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            // Material is assigned by the caller (FluffGround). Drop a
            // sensible default so the mesh isn't bright pink in the
            // editor for the half-second between AddComponent and the
            // FluffGround call.
            mr.sharedMaterial = WorldPalette.ArenaGround;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            return go;
        }

        /// <summary>
        /// Rebake <c>Mesh_ArenaHills.asset</c> in place from the current
        /// <see cref="HillsSettings"/> values. Every Ground GameObject in
        /// every scene that references the mesh updates immediately —
        /// callers don't need to re-run <c>Build Arena Pass A</c>.
        /// </summary>
        /// <remarks>
        /// Wired from <see cref="HillsSettingsEditor"/>'s "Rebake hills
        /// mesh" button.
        /// </remarks>
        public static void RebakeMesh()
        {
            HillsSettings settings = LoadOrCreateSettings();
            Mesh mesh = GetOrBuildMesh(settings);

            // MeshCollider caches the cooked physics mesh; nudging the
            // sharedMesh reference forces every collider in the scene to
            // re-cook against the new geometry.
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
            {
                if (mc.sharedMesh == mesh)
                {
                    mc.sharedMesh = null;
                    mc.sharedMesh = mesh;
                }
            }

            Debug.Log($"[Robogame] HillsGround: rebaked {MeshPath} ({mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris).");
        }

        // -----------------------------------------------------------------
        // Settings loader
        // -----------------------------------------------------------------

        private static HillsSettings LoadOrCreateSettings()
        {
            EnsureFolder(SettingsFolder);
            HillsSettings s = AssetDatabase.LoadAssetAtPath<HillsSettings>(SettingsPath);
            if (s != null) return s;

            // First-run bootstrap: stamp the asset with the same defaults
            // we used when the values were hard-coded constants. Future
            // edits happen in the inspector.
            s = ScriptableObject.CreateInstance<HillsSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] HillsGround: created default {SettingsPath}. Edit it in the Project window to tune the hills.");
            return s;
        }

        // -----------------------------------------------------------------
        // Mesh baker
        // -----------------------------------------------------------------

        private static Mesh GetOrBuildMesh(HillsSettings s)
        {
            EnsureFolder(GeneratedFolder);

            // Always re-bake on Pass A. Cheap (tens of ms) and guarantees
            // the mesh matches the current settings asset.
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Mesh_ArenaHills" };
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }

            BakeInto(mesh, s);

            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static void BakeInto(Mesh mesh, HillsSettings s)
        {
            // 16-bit indices cap at 65 535 verts; 251×251 = 63 001 fits.
            int   res    = Mathf.Clamp(s.resolution, 33, 251);
            float size   = Mathf.Max(10f, s.size);
            int   vCount = res * res;
            float half   = size * 0.5f;
            float step   = size / (res - 1);

            var verts   = new Vector3[vCount];
            var uvs     = new Vector2[vCount];
            var normals = new Vector3[vCount];

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = -half + x * step;
                float wz = -half + z * step;

                float wy = SampleHeight(wx, wz, s);
                int   i  = z * res + x;
                verts[i]   = new Vector3(wx, wy, wz);
                uvs[i]     = new Vector2((float)x / (res - 1), (float)z / (res - 1));
                normals[i] = Vector3.up; // overwritten by RecalculateNormals
            }

            // Two triangles per quad, wound CCW so the surface faces +Y.
            int qCount = (res - 1) * (res - 1);
            var tris   = new int[qCount * 6];
            int t      = 0;
            for (int z = 0; z < res - 1; z++)
            for (int x = 0; x < res - 1; x++)
            {
                int i00 = z * res + x;
                int i10 = i00 + 1;
                int i01 = i00 + res;
                int i11 = i01 + 1;
                tris[t++] = i00; tris[t++] = i01; tris[t++] = i11;
                tris[t++] = i00; tris[t++] = i11; tris[t++] = i10;
            }

            mesh.Clear();
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.normals   = normals;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// World-space height at <paramref name="x"/>, <paramref name="z"/>.
        /// Two-octave Perlin noise centred on zero, modulated by a
        /// central-flat falloff and an edge-flat falloff.
        /// </summary>
        private static float SampleHeight(float x, float z, HillsSettings s)
        {
            // Two-octave Perlin, both centred on 0 (Mathf.PerlinNoise is
            // [0,1] so we shift by -0.5 first).
            float n1 = Mathf.PerlinNoise((x + s.noiseOffset.x) * s.hillFreqLow,
                                          (z + s.noiseOffset.y) * s.hillFreqLow) - 0.5f;
            float n2 = Mathf.PerlinNoise((x - s.noiseOffset.y) * s.hillFreqHigh,
                                          (z + s.noiseOffset.x) * s.hillFreqHigh) - 0.5f;
            float h = n1 * s.hillAmpLow * 2f + n2 * s.hillAmpHigh * 2f;

            // Distance from origin in the XZ plane.
            float r = Mathf.Sqrt(x * x + z * z);

            // Inner falloff: 0 inside the spawn zone, ramps to 1 by rampOuter.
            float inner = Smoothstep(s.flatRadius, s.rampOuter, r);

            // Outer falloff: 1 in the playable region, ramps back to 0 by
            // edgeFlatEnd so boundary walls sit on flat ground.
            float outer = 1f - Smoothstep(s.edgeFlatStart, s.edgeFlatEnd, r);

            return h * inner * outer;
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-5f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        // -----------------------------------------------------------------
        // Folder helper
        // -----------------------------------------------------------------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
