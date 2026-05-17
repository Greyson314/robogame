using System.IO;
using Robogame.Voxel;
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
        /// <param name="addCollider">
        /// When true (default) the ground mesh gets its own
        /// <c>MeshCollider</c> so vehicles drive on it. The full-footprint
        /// arena dig zone passes <c>false</c>: the voxel chunks are the
        /// sole ground collider there, so a dug column actually drops the
        /// chassis instead of resting it on an invisible grass-mesh
        /// collider floating over the hole.
        /// </param>
        public static GameObject Build(Transform parent, string name = "Ground", bool addCollider = true)
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

            if (addCollider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }

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

        /// <summary>
        /// Load (or first-run create) the hills settings and project them
        /// onto the runtime <see cref="HeightmapParams"/> the arena dig
        /// zone seeds its diggable surface from. Single call-site for
        /// EnvironmentBuilder so the grass mesh and voxel surface can't
        /// drift apart.
        /// </summary>
        public static HeightmapParams LoadHeightmapParams()
            => ToHeightmapParams(LoadOrCreateSettings());

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
        /// Project the inspector-authored <see cref="HillsSettings"/> onto
        /// the runtime <see cref="HeightmapParams"/> the voxel zone seeds
        /// from. Keeping the conversion here (and routing
        /// <see cref="SampleHeight"/> through the same struct) is what
        /// guarantees the grass mesh and the diggable voxel surface use
        /// byte-identical height math — the alignment risk flagged in
        /// docs/changes/83.
        /// </summary>
        public static HeightmapParams ToHeightmapParams(HillsSettings s)
        {
            return new HeightmapParams
            {
                Enabled       = true,
                NoiseOffset   = s.noiseOffset,
                HillFreqLow   = s.hillFreqLow,
                HillAmpLow    = s.hillAmpLow,
                HillFreqHigh  = s.hillFreqHigh,
                HillAmpHigh   = s.hillAmpHigh,
                FlatRadius    = s.flatRadius,
                RampOuter     = s.rampOuter,
                EdgeFlatStart = s.edgeFlatStart,
                EdgeFlatEnd   = s.edgeFlatEnd,
            };
        }

        /// <summary>
        /// World-space height at <paramref name="x"/>, <paramref name="z"/>.
        /// Delegates to the shared runtime <see cref="HeightmapField"/> so
        /// the baked grass mesh matches the voxel SDF surface exactly.
        /// </summary>
        private static float SampleHeight(float x, float z, HillsSettings s)
            => HeightmapField.Sample(ToHeightmapParams(s), x, z);

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
