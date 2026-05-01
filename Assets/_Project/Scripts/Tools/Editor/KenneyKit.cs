using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Loader + import-settings enforcer for the Kenney FBX kits living
    /// under <c>Assets/_Project/Art/ThirdParty/</c>. Both kits use the same
    /// trick: every model UV-maps into a single shared <c>colormap.png</c>
    /// palette texture, so once the colormap is imported correctly every
    /// FBX picks up its colours for free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why an explicit helper rather than relying on Unity's auto-import:
    /// these Kenney FBXs declare their internal unit as centimetres,
    /// so Unity's default FBX importer multiplies every vertex by 100
    /// (a 0.5 m crate becomes 50 m). We force <c>useFileScale=false</c>
    /// and <c>globalScale=1.0</c> so a crate lands at its authored
    /// 0.5 m, a block-grass at 1 m, etc. Same idea with the colormap —
    /// point-filtered keeps the cel look, bilinear muddies it.
    /// </para>
    /// <para>
    /// The <see cref="Load(string,string)"/> entry point loads the FBX as
    /// an asset reference (a "model prefab") which we then
    /// <c>PrefabUtility.InstantiatePrefab</c> into the scene — that keeps
    /// the green prefab outline in the hierarchy and lets us re-bake the
    /// arena from scratch without leaking instances.
    /// </para>
    /// </remarks>
    public static class KenneyKit
    {
        // -----------------------------------------------------------------
        // Kit roots
        // -----------------------------------------------------------------

        public const string IndustrialRoot =
            "Assets/_Project/Art/ThirdParty/kenney_city-kit-industrial_1.0/Models/FBX format";

        public const string PlatformerRoot =
            "Assets/_Project/Art/ThirdParty/kenney_platformer-kit/Models/FBX format";

        /// <summary>
        /// FBX scale factor we force on every Kenney model. Both kits
        /// are authored at scene scale in metres (verified by reading
        /// the OBJ siblings: crate ≈ 0.5 m, block-grass ≈ 1.0 m,
        /// tree-pine ≈ 2.0 m, building-a ≈ 2.1 × 1.5 × 1.2 m). Default
        /// FBX import would multiply by the file's <c>UnitScaleFactor</c>
        /// (cm → 100×) which is what made everything come in giant; we
        /// prevent that by setting <c>useFileScale = false</c> below and
        /// keeping <c>globalScale = 1.0</c>.
        /// </summary>
        public const float KenneyFbxScale = 1.0f;

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Force scale factor + colormap import settings on every Kenney
        /// FBX/PNG. Idempotent. Call once per Pass A build.
        /// </summary>
        public static void EnsureImportSettings()
        {
            EnforceFbxKit(IndustrialRoot);
            EnforceFbxKit(PlatformerRoot);
        }

        /// <summary>
        /// Load a Kenney model by file name (without extension) from the
        /// industrial kit. Returns the GameObject reference suitable for
        /// passing to <see cref="PrefabUtility.InstantiatePrefab(Object)"/>.
        /// </summary>
        public static GameObject Industrial(string nameNoExt)
            => Load(IndustrialRoot, nameNoExt);

        /// <summary>Load a Kenney model from the platformer kit.</summary>
        public static GameObject Platformer(string nameNoExt)
            => Load(PlatformerRoot, nameNoExt);

        /// <summary>
        /// Instantiate a model prefab into the scene under
        /// <paramref name="parent"/> at <paramref name="position"/> with
        /// optional <paramref name="rotation"/> and uniform
        /// <paramref name="scale"/>. Returns the spawned GameObject (or
        /// null if the asset is missing — caller should handle).
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position,
                                       Quaternion rotation = default, float scale = 1f)
        {
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (go == null) return null;

            go.transform.position = position;
            go.transform.rotation = rotation == default ? Quaternion.identity : rotation;
            if (!Mathf.Approximately(scale, 1f))
                go.transform.localScale = Vector3.one * scale;

            return go;
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private static GameObject Load(string root, string nameNoExt)
        {
            string path = Path.Combine(root, nameNoExt + ".fbx").Replace('\\', '/');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[Robogame] KenneyKit: model not found at {path}.");
            }
            return prefab;
        }

        private static void EnforceFbxKit(string root)
        {
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning($"[Robogame] KenneyKit: kit root not found at {root}.");
                return;
            }

            // FBX models — scale factor 1, no rig, materials extracted to
            // the colormap automatically.
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { root });
            foreach (string guid in fbxGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;

                bool changed = false;

                // Scale: vertex coords in these FBXs are already in
                // metres (verified vs the .obj siblings). Default FBX
                // import would also multiply by the file's UnitScale
                // (cm = 100×), which is what made props giant. Force
                // globalScale=1 + useFileScale=false to land at the
                // authored size.
                if (!Mathf.Approximately(importer.globalScale, KenneyFbxScale))
                {
                    importer.globalScale = KenneyFbxScale;
                    changed = true;
                }
                if (importer.useFileScale)
                {
                    importer.useFileScale = false;
                    changed = true;
                }

                // No need for rig / animation on static props.
                if (importer.animationType != ModelImporterAnimationType.None)
                {
                    importer.animationType = ModelImporterAnimationType.None;
                    changed = true;
                }
                if (importer.importAnimation)
                {
                    importer.importAnimation = false;
                    changed = true;
                }

                // Use embedded materials (they reference the shared
                // colormap by relative path — Unity resolves this fine).
                if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    changed = true;
                }

                if (changed) importer.SaveAndReimport();
            }

            // Colormap PNG — point-filter to preserve crisp colour boundaries
            // (these are sub-100px palette atlases; bilinear blurs them).
            string[] pngGuids = AssetDatabase.FindAssets("t:Texture2D colormap", new[] { root });
            foreach (string guid in pngGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("colormap.png")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool changed = false;

                if (importer.filterMode != FilterMode.Point)
                {
                    importer.filterMode = FilterMode.Point;
                    changed = true;
                }
                if (importer.wrapMode != TextureWrapMode.Clamp)
                {
                    importer.wrapMode = TextureWrapMode.Clamp;
                    changed = true;
                }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }

                if (changed) importer.SaveAndReimport();
            }
        }
    }
}
