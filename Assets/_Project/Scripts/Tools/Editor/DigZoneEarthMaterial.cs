using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Procedurally bakes a tileable stylized dirt texture and the
    /// triplanar <c>Robogame/DigZoneEarth</c> material used by the
    /// diggable voxel terrain (cut faces, tunnel walls, the dug floor).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <see cref="GroundMaterial"/>'s generate-don't-import
    /// discipline: the look is driven from a small palette-derived earth
    /// tone, not a realistic photo texture (docs/ART_DIRECTION.md). The
    /// voxel mesh has no UVs, so the shader samples this texture
    /// triplanar by world position — the texture only has to tile
    /// cleanly, which the <see cref="EdgeMargin"/> seam-safety guarantees.
    /// </para>
    /// <para>
    /// Falls back to a flat earth-tone URP/Lit material if the custom
    /// shader is missing (e.g. a headless build before the shader
    /// imports), so scaffolding never strands the zone bright-pink.
    /// </para>
    /// </remarks>
    public static class DigZoneEarthMaterial
    {
        public  const string GeneratedFolder = "Assets/_Project/Art/Generated";
        public  const string TexturePath     = GeneratedFolder + "/Tex_DirtTile.png";
        public  const string MaterialPath    = WorldPalette.Folder + "/Mat_DigZoneEarth.mat";
        private const string ShaderName      = "Robogame/DigZoneEarth";

        private const int TextureSize = 128;
        private const int EdgeMargin  = 3;
        private const int ClodCount   = 90;   // lighter raised clumps
        private const int PebbleCount = 70;   // darker embedded specks
        private const int Seed        = 0x64_69_72_74; // "dirt"

        // Earthy brown. Not one of the 12 gameplay tokens — terrain
        // surface dressing — but kept deliberately desaturated so it
        // reads as "soil" against the palette greens without fighting
        // the toon ramp.
        private static readonly Color Earth = new Color(0x6B / 255f, 0x4A / 255f, 0x32 / 255f, 1f);

        /// <summary>Build (or refresh) and return the dirt material.</summary>
        public static Material GetOrBuild()
        {
            Texture2D tex = GetOrBuildTexture();

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[DigZoneEarthMaterial] '{ShaderName}' not found — falling back to a " +
                    "flat earth-tone material. The voxel terrain will be untextured until " +
                    "the shader imports.");
                return BuildFallback();
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null && mat.shader != shader)
            {
                AssetDatabase.DeleteAsset(MaterialPath);
                mat = null;
            }
            if (mat == null)
            {
                Directory.CreateDirectory(WorldPalette.Folder);
                mat = new Material(shader) { name = "Mat_DigZoneEarth" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_MapScale", 3.0f);          // one dirt tile every 3 m
            mat.SetFloat("_BlendSharpness", 4.0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        private static Material BuildFallback()
        {
            Shader shader = Shader.Find(WorldPalette.LitShaderName) ?? Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory(WorldPalette.Folder);
                mat = new Material(shader) { name = "Mat_DigZoneEarth" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Earth);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", Earth);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D GetOrBuildTexture()
        {
            EnsureFolder(GeneratedFolder);
            File.WriteAllBytes(TexturePath, BakePng());
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            var ti = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (ti != null)
            {
                bool changed = false;
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
                if (ti.wrapMode    != TextureWrapMode.Repeat)      { ti.wrapMode    = TextureWrapMode.Repeat;       changed = true; }
                if (ti.filterMode  != FilterMode.Bilinear)         { ti.filterMode  = FilterMode.Bilinear;          changed = true; }
                if (!ti.mipmapEnabled)                              { ti.mipmapEnabled = true;                       changed = true; }
                if (!ti.sRGBTexture)                                { ti.sRGBTexture = true;                          changed = true; }
                if (changed) ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        private static byte[] BakePng()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, mipChain: false, linear: false);

            Color baseEarth = Earth;
            Color clod      = Brighten(baseEarth, 0.16f);
            Color pebble    = Darken(baseEarth, 0.22f);
            Color noiseA    = Brighten(baseEarth, 0.05f);
            Color noiseB    = Darken(baseEarth, 0.06f);

            var rng = new System.Random(Seed);
            var px  = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                int roll = rng.Next(0, 100);
                Color c = baseEarth;
                if      (roll < 10) c = noiseA;
                else if (roll < 20) c = noiseB;
                px[y * TextureSize + x] = c;
            }

            for (int i = 0; i < ClodCount; i++)
            {
                int x = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                int y = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                int s = rng.Next(1, 3);
                StampBlob(px, x, y, s, clod);
            }
            for (int i = 0; i < PebbleCount; i++)
            {
                int x = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                int y = rng.Next(EdgeMargin, TextureSize - EdgeMargin);
                px[y * TextureSize + x] = pebble;
            }

            tex.SetPixels(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return bytes;
        }

        private static void StampBlob(Color[] px, int cx, int cy, int radius, Color c)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int x = cx + dx, y = cy + dy;
                if (x < EdgeMargin || x >= TextureSize - EdgeMargin) continue;
                if (y < EdgeMargin || y >= TextureSize - EdgeMargin) continue;
                px[y * TextureSize + x] = c;
            }
        }

        private static Color Brighten(Color c, float a)
            => new Color(Mathf.Clamp01(c.r + a), Mathf.Clamp01(c.g + a), Mathf.Clamp01(c.b + a), 1f);

        private static Color Darken(Color c, float a)
            => new Color(Mathf.Clamp01(c.r - a), Mathf.Clamp01(c.g - a), Mathf.Clamp01(c.b - a), 1f);

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
