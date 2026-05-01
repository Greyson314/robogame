using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Locked 12-token palette + cached materials. Single source of
    /// truth for every authored colour in the project. See
    /// <c>docs/ART_DIRECTION.md</c> for the design rationale and the
    /// "if it isn't a token it's wrong" rule.
    /// </summary>
    /// <remarks>
    /// Colour values here are mirrored exactly from the doc so a new
    /// contributor (or a future-AI) can skim either side and trust the
    /// numbers match. The material accessors below build URP-friendly
    /// <see cref="Material"/> assets backed by the tokens. We try MK
    /// Toon first (cel look) and fall back to URP/Lit if the package
    /// is missing — keeps the scaffold running without MK Toon.
    /// </remarks>
    public static class WorldPalette
    {
        public const string Folder = "Assets/_Project/Materials";

        // Preferred shader for environment + chassis materials. MK Toon
        // PBS gives us cel-shaded diffuse + ramps without us authoring
        // anything custom. URP/Lit is the safety net.
        // Public so sibling builders (GroundMaterial, BlockMaterials, etc.)
        // can reuse the same Shader.Find fallback chain without copy-paste.
        public const string ToonShaderName = "MK/Toon/URP/Standard/Physically Based";
        public const string LitShaderName  = "Universal Render Pipeline/Lit";

        // -----------------------------------------------------------------
        // 12-token palette (mirrors docs/ART_DIRECTION.md exactly).
        // -----------------------------------------------------------------

        // Structure & environment
        public static readonly Color Slate       = HexRGB(0x2A, 0x32, 0x3C);
        public static readonly Color SlateLight  = HexRGB(0x52, 0x5B, 0x66);
        public static readonly Color Concrete    = HexRGB(0x3F, 0x43, 0x48);
        public static readonly Color Grass       = HexRGB(0x4D, 0x73, 0x40);
        public static readonly Color SkyDay      = HexRGB(0x8C, 0xB7, 0xE0);
        // Convenience derivative: midpoint between SkyDay and a paler tint
        // used for the Polyverse skies equator band.
        public static readonly Color SkyEquator  = HexRGB(0xB6, 0xCF, 0xE6);

        // Action accents
        public static readonly Color Hazard      = HexRGB(0xF2, 0x8C, 0x1A);
        public static readonly Color Caution     = HexRGB(0xE6, 0xCC, 0x33);
        public static readonly Color Alert       = HexRGB(0xBF, 0x33, 0x3F);

        // Tech / energy
        public static readonly Color Cyan        = HexRGB(0x33, 0xD9, 0xF2);
        public static Color CyanEmit             => Cyan * 4f; // HDR boost
        public static readonly Color Plasma      = HexRGB(0xA1, 0x55, 0xF2);
        public static readonly Color Mint        = HexRGB(0x34, 0xA6, 0x59);

        // UI / chrome
        public static readonly Color UIBg        = HexRGB(0x0F, 0x12, 0x19);
        public static readonly Color UIText      = HexRGB(0xFF, 0xFF, 0xFF);
        public static Color UIDim                => new Color(1f, 1f, 1f, 0.55f);

        // -----------------------------------------------------------------
        // Cached named materials (consumed by EnvironmentBuilder).
        // Smoothness/metallic tuned to match the doc's
        // "Material Vocabulary" table.
        // -----------------------------------------------------------------

        public static Material GarageFloor   => Get("Mat_GarageFloor",   Concrete,    metallic: 0.0f, smoothness: 0.10f);
        public static Material GarageWall    => Get("Mat_GarageWall",    Slate,       metallic: 0.0f, smoothness: 0.15f);
        public static Material GarageAccent  => Get("Mat_GarageAccent",  Hazard,      metallic: 0.2f, smoothness: 0.40f);
        public static Material GaragePodium  => Get("Mat_GaragePodium",  SlateLight,  metallic: 0.4f, smoothness: 0.45f);

        public static Material ArenaGround   => Get("Mat_ArenaGround",   Grass,       metallic: 0.0f, smoothness: 0.05f);
        public static Material ArenaWall     => Get("Mat_ArenaWall",     Slate,       metallic: 0.0f, smoothness: 0.20f);
        public static Material ArenaRamp     => Get("Mat_ArenaRamp",     Hazard,      metallic: 0.1f, smoothness: 0.35f);
        public static Material ArenaBump     => Get("Mat_ArenaBump",     Caution,     metallic: 0.1f, smoothness: 0.35f);
        public static Material ArenaStair    => Get("Mat_ArenaStair",    Mint,        metallic: 0.1f, smoothness: 0.30f);
        public static Material ArenaPillar   => Get("Mat_ArenaPillar",   Alert,       metallic: 0.2f, smoothness: 0.40f);

        // Camera clear colours (used when no skybox is wired up).
        public static readonly Color GarageClear = HexRGB(0x0F, 0x12, 0x19);
        public static Color ArenaClear           => SkyDay;

        // -----------------------------------------------------------------
        // Material factory
        // -----------------------------------------------------------------

        private static Material Get(string assetName, Color color, float metallic, float smoothness, Color? emission = null)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{assetName}.mat";

            Shader shader = Shader.Find(ToonShaderName)
                            ?? Shader.Find(LitShaderName)
                            ?? Shader.Find("Standard");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            ApplyToon(mat, color, metallic, smoothness, emission);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Push palette values into whatever shader the material is
        /// currently using. We probe every plausible property name so
        /// MK Toon, URP/Lit, and Standard all paint correctly without a
        /// per-shader code path.
        /// </summary>
        private static void ApplyToon(Material mat, Color color, float metallic, float smoothness, Color? emission)
        {
            // Base colour — MK Toon uses _AlbedoColor; URP/Lit uses
            // _BaseColor; Standard uses _Color. Set whichever exists.
            if (mat.HasProperty("_AlbedoColor")) mat.SetColor("_AlbedoColor", color);
            if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor",   color);
            if (mat.HasProperty("_Color"))       mat.SetColor("_Color",       color);

            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            if (emission.HasValue)
            {
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            else if (mat.HasProperty("_EmissionColor"))
            {
                // Force-zero emission so re-runs don't leak old values.
                mat.SetColor("_EmissionColor", Color.black);
            }
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

        private static Color HexRGB(int r, int g, int b)
            => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}
