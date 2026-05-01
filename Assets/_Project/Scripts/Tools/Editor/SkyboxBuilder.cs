using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Authors a Polyverse Skies skybox material for the Arena scene.
    /// Falls back to a flat sky-colour material if the Polyverse shader
    /// can't be resolved (e.g. package not installed yet) so the rest
    /// of the scaffold doesn't blow up.
    /// </summary>
    internal static class SkyboxBuilder
    {
        public const string Folder = "Assets/_Project/Rendering/Skyboxes";
        public const string ArenaSkyboxPath = Folder + "/Skybox_Arena.mat";

        private const string PolyverseShaderName = "BOXOPHOBIC/Polyverse Skies/Standard";

        /// <summary>
        /// Build (or update) the Arena skybox material. Palette tokens
        /// come from <see cref="WorldPalette"/> so re-skinning the
        /// arena means editing one file, not chasing materials.
        /// </summary>
        public static Material BuildArenaSkybox()
        {
            EnsureFolder(Folder);

            Shader shader = Shader.Find(PolyverseShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[Robogame] Polyverse Skies shader '{PolyverseShaderName}' not found. " +
                    "Falling back to a flat-colour skybox material. Re-import the BOXOPHOBIC " +
                    "package or run Boxophobic's setup window if you want the gradient sky.");
                shader = Shader.Find("Skybox/Procedural") ?? Shader.Find("Skybox/6 Sided");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(ArenaSkyboxPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Skybox_Arena" };
                AssetDatabase.CreateAsset(mat, ArenaSkyboxPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Polyverse Skies/Standard has a 2-stop gradient: Equator
            // dominates the upper hemisphere, Ground dominates the lower.
            // We map Equator → SkyDay (the headline blue) and Ground →
            // SkyEquator (paler band that softens into the horizon line).
            // Grass-coloured ground would fight the actual ground plane
            // visible at the edges of the arena, so we keep it sky-toned.
            if (mat.HasProperty("_EquatorColor"))      mat.SetColor("_EquatorColor",      WorldPalette.SkyDay);
            if (mat.HasProperty("_GroundColor"))       mat.SetColor("_GroundColor",       WorldPalette.SkyEquator);
            if (mat.HasProperty("_EquatorHeight"))     mat.SetFloat("_EquatorHeight",     0.5f);
            if (mat.HasProperty("_EquatorSmoothness")) mat.SetFloat("_EquatorSmoothness", 0.6f);
            if (mat.HasProperty("_StarsIntensity"))    mat.SetFloat("_StarsIntensity",    0f);
            if (mat.HasProperty("_SunIntensity"))      mat.SetFloat("_SunIntensity",      0.6f);
            if (mat.HasProperty("_SunColor"))          mat.SetColor("_SunColor",          new Color(1f, 0.97f, 0.88f, 1f));
            if (mat.HasProperty("_CloudsOpacity"))     mat.SetFloat("_CloudsOpacity",     0.4f);

            EditorUtility.SetDirty(mat);
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
    }
}
