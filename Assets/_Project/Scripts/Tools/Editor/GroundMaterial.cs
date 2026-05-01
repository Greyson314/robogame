using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Procedurally bakes a tileable stylized grass texture and a matching
    /// MK-Toon-friendly material, then applies it to the arena ground plane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why generate rather than import: the art direction
    /// (<c>docs/ART_DIRECTION.md</c>) bans realistic grass textures and
    /// keeps every colour palette-locked. A small procedural texture lets
    /// us drive the look entirely from <see cref="WorldPalette.Grass"/>
    /// with no third-party dependency or scaling traps.
    /// </para>
    /// <para>
    /// Tileability is guaranteed by skipping the outermost
    /// <see cref="EdgeMargin"/> pixels when stamping blade marks, so
    /// nothing crosses the wrap seam. The result is meant to be tiled
    /// dozens of times across the 220 m arena plane (see
    /// <see cref="ApplyToGround"/>).
    /// </para>
    /// </remarks>
    public static class GroundMaterial
    {
        // -----------------------------------------------------------------
        // Asset paths
        // -----------------------------------------------------------------

        public const string GeneratedFolder = "Assets/_Project/Art/Generated";
        public const string TexturePath     = GeneratedFolder + "/Tex_GrassTile.png";
        public const string MaterialPath    = WorldPalette.Folder + "/Mat_ArenaGrass.mat";

        // -----------------------------------------------------------------
        // Authoring constants
        // -----------------------------------------------------------------

        private const int   TextureSize    = 128;
        private const int   EdgeMargin     = 3;     // tile-seam safety
        private const int   BladeCount     = 110;   // bright vertical highlights
        private const int   ShadowCount    = 60;    // dark single-pixel specks
        private const int   BladeMinHeight = 2;
        private const int   BladeMaxHeight = 4;
        private const int   Seed           = 0x6e_61_72_67; // "garn"

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Build (or refresh) the grass texture + material and assign it
        /// to <paramref name="ground"/>'s <see cref="MeshRenderer"/>. The
        /// material tiles <paramref name="tilesPerSide"/> times across
        /// each axis of the plane — pick this so 1 tile reads as a
        /// natural patch of grass at gameplay camera distance.
        /// </summary>
        public static void ApplyToGround(GameObject ground, int tilesPerSide = 30)
        {
            if (ground == null) return;

            Texture2D tex = GetOrBuildTexture();
            Material  mat = GetOrBuildMaterial(tex, tilesPerSide);

            var mr = ground.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        // -----------------------------------------------------------------
        // Texture baker
        // -----------------------------------------------------------------

        private static Texture2D GetOrBuildTexture()
        {
            EnsureFolder(GeneratedFolder);

            // Re-bake on every Pass A run. Cheap (128×128, ~50 ms) and
            // guarantees the asset matches the current palette token.
            byte[] png = BakePng();
            File.WriteAllBytes(TexturePath, png);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            // Pin importer settings: bilinear (cel look stays soft at
            // distance via mips), repeat wrap (so tiling works), no
            // sRGB game on a colour map (it's already sRGB authored).
            var ti = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (ti != null)
            {
                bool changed = false;
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
                if (ti.wrapMode    != TextureWrapMode.Repeat)      { ti.wrapMode    = TextureWrapMode.Repeat;       changed = true; }
                if (ti.filterMode  != FilterMode.Bilinear)         { ti.filterMode  = FilterMode.Bilinear;          changed = true; }
                if (!ti.mipmapEnabled)                              { ti.mipmapEnabled = true;                       changed = true; }
                if (!ti.sRGBTexture)                                { ti.sRGBTexture = true;                          changed = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ)
                {
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    changed = true;
                }
                if (changed) ti.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        private static byte[] BakePng()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: false, linear: false);

            Color baseGreen   = WorldPalette.Grass;
            Color highlight   = Brighten(baseGreen, 0.22f);
            Color shadow      = Darken(baseGreen,   0.30f);
            Color microNoiseA = Brighten(baseGreen, 0.06f);
            Color microNoiseB = Darken(baseGreen,   0.08f);

            // Pass 1 — base + per-pixel low-amplitude noise (kills the
            // dead-flat look when tiled across 220 m).
            var rng = new System.Random(Seed);
            var px  = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                int   roll = rng.Next(0, 100);
                Color c    = baseGreen;
                if      (roll <  8) c = microNoiseA;
                else if (roll < 16) c = microNoiseB;
                px[y * TextureSize + x] = c;
            }

            // Pass 2 — scatter brighter "blade" highlights (2–4 px tall
            // vertical strokes). Skip the outer EdgeMargin pixels so the
            // tile wraps cleanly with no visible seam.
            for (int i = 0; i < BladeCount; i++)
            {
                int x = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                int y = rng.Next(EdgeMargin, TextureSize - EdgeMargin - BladeMaxHeight);
                int h = rng.Next(BladeMinHeight, BladeMaxHeight + 1);
                for (int k = 0; k < h; k++)
                    px[(y + k) * TextureSize + x] = highlight;
            }

            // Pass 3 — scatter darker single-pixel specks for "between
            // blades" shadow texture.
            for (int i = 0; i < ShadowCount; i++)
            {
                int x = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                int y = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                px[y * TextureSize + x] = shadow;
            }

            tex.SetPixels(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return bytes;
        }

        // -----------------------------------------------------------------
        // Material baker
        // -----------------------------------------------------------------

        private static Material GetOrBuildMaterial(Texture2D tex, int tilesPerSide)
        {
            Shader shader = Shader.Find(WorldPalette.ToonShaderName)
                            ?? Shader.Find(WorldPalette.LitShaderName)
                            ?? Shader.Find("Standard");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Mat_ArenaGrass" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            // Push base colour first (so the texture multiplies against
            // the palette token, not pure white) — MK Toon multiplies
            // _AlbedoColor by _AlbedoMap.
            if (mat.HasProperty("_AlbedoColor")) mat.SetColor("_AlbedoColor", Color.white);
            if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor",   Color.white);
            if (mat.HasProperty("_Color"))       mat.SetColor("_Color",       Color.white);

            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);

            // Texture slot — MK Toon uses _AlbedoMap, URP/Lit uses
            // _BaseMap, Standard uses _MainTex.
            SetTextureIfPresent(mat, "_AlbedoMap", tex, tilesPerSide);
            SetTextureIfPresent(mat, "_BaseMap",   tex, tilesPerSide);
            SetTextureIfPresent(mat, "_MainTex",   tex, tilesPerSide);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetTextureIfPresent(Material mat, string prop, Texture2D tex, int tilesPerSide)
        {
            if (!mat.HasProperty(prop)) return;
            mat.SetTexture(prop, tex);
            mat.SetTextureScale(prop,  new Vector2(tilesPerSide, tilesPerSide));
            mat.SetTextureOffset(prop, Vector2.zero);
        }

        // -----------------------------------------------------------------
        // Colour helpers
        // -----------------------------------------------------------------

        private static Color Brighten(Color c, float amt)
            => new Color(Mathf.Clamp01(c.r + amt), Mathf.Clamp01(c.g + amt), Mathf.Clamp01(c.b + amt), c.a);

        private static Color Darken(Color c, float amt)
            => new Color(Mathf.Clamp01(c.r - amt), Mathf.Clamp01(c.g - amt), Mathf.Clamp01(c.b - amt), c.a);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf   = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
