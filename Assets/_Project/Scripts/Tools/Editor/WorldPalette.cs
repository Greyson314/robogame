using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Small library of named, cached URP/Lit materials used by scene
    /// scaffolders. Materials are persisted as project assets in
    /// <see cref="Folder"/> so the editor doesn't churn new instances on
    /// every rebuild and the inspector shows a stable reference.
    /// </summary>
    /// <remarks>
    /// All colours are deliberately punchy primaries — placeholder art is
    /// supposed to read at a glance, not be tasteful.
    /// </remarks>
    public static class WorldPalette
    {
        public const string Folder = "Assets/_Project/Materials";

        // Garage interior
        public static Material GarageFloor    => Get("Mat_GarageFloor",    new Color(0.18f, 0.18f, 0.22f));
        public static Material GarageWall     => Get("Mat_GarageWall",     new Color(0.32f, 0.34f, 0.38f));
        public static Material GarageAccent   => Get("Mat_GarageAccent",   new Color(0.95f, 0.55f, 0.10f)); // hazard orange
        public static Material GaragePodium   => Get("Mat_GaragePodium",   new Color(0.55f, 0.40f, 0.25f));

        // Arena
        public static Material ArenaGround    => Get("Mat_ArenaGround",    new Color(0.30f, 0.45f, 0.25f)); // grass
        public static Material ArenaWall      => Get("Mat_ArenaWall",      new Color(0.25f, 0.30f, 0.45f)); // slate blue
        public static Material ArenaRamp      => Get("Mat_ArenaRamp",      new Color(0.85f, 0.50f, 0.20f)); // orange
        public static Material ArenaBump      => Get("Mat_ArenaBump",      new Color(0.90f, 0.80f, 0.20f)); // yellow
        public static Material ArenaStair     => Get("Mat_ArenaStair",     new Color(0.20f, 0.65f, 0.35f)); // green
        public static Material ArenaPillar    => Get("Mat_ArenaPillar",    new Color(0.75f, 0.20f, 0.25f)); // red

        // Skybox / camera clear colours (picked, not assets).
        public static readonly Color GarageClear = new Color(0.06f, 0.07f, 0.09f);
        public static readonly Color ArenaClear  = new Color(0.55f, 0.72f, 0.88f); // soft sky

        // -----------------------------------------------------------------

        private static Material Get(string assetName, Color color)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{assetName}.mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                // Repaint in case we tweaked the palette since last build.
                if (existing.HasProperty("_BaseColor")) existing.SetColor("_BaseColor", color);
                if (existing.HasProperty("_Color")) existing.SetColor("_Color", color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = assetName };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>Apply a material to a primitive's <see cref="MeshRenderer"/> if present.</summary>
        public static void Apply(GameObject go, Material mat)
        {
            if (go == null || mat == null) return;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }
    }
}
