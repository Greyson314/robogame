using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Imports & exposes the Kenney Pattern Pack PNGs as proper tileable
    /// Texture2D assets, then assigns a starter pattern per block category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern pack ships as black-on-white 2D pattern tiles. We use
    /// them as MK Toon <c>_AlbedoMap</c> overlays — the multiplier nature
    /// of the map means black "etches" the line darker and white passes
    /// through the palette tint. That's exactly the panel-grooves look
    /// we want without ever shipping photo-PBR detail that would fight
    /// the cel shader.
    /// </para>
    /// <para>
    /// <strong>How to iterate:</strong> the only thing you change is the
    /// integers in <see cref="Picks"/> below. Open the pattern previews
    /// at <c>Assets/_Project/Art/ThirdParty/kenney_pattern-pack/PNG/Default/</c>
    /// (Unity will show thumbnails after the first import), pick numbers
    /// you like, drop them in here, then run
    /// <c>Robogame → Scaffold → Build All Pass A</c> again.
    /// </para>
    /// </remarks>
    public static class BlockTextures
    {
        // -----------------------------------------------------------------
        // Per-category pattern picks. Numbers refer to pattern_NN.png in
        // the Kenney pack. Patterns 01-84 are available. Pick by gut:
        // dots/grids for Structure, chevrons/stripes for hero blocks,
        // lines for wheels/aero. Iterate freely.
        // -----------------------------------------------------------------
        public struct Picks
        {
            // Bulk-volume canvas. Subtle is good.
            public const int Structure   = 1;

            // Cpu — wants something tech / circuit-y.
            public const int Cpu         = 55;

            // Weapon — wants hazard / aggressive.
            public const int Weapon      = 35;

            // Thruster — wants directional / striped.
            public const int Thruster    = 25;

            // Wheel tire — wants tread-like horizontal lines.
            public const int WheelTire   = 12;

            // Wheel hub — wants radial / spoke-ish.
            public const int WheelHub    = 70;

            // Aero / wing — clean stripes.
            public const int Aero        = 18;
        }

        // Tiling per face (each block face is ~1m). 3 means the pattern
        // repeats 3 times across the cube — fine grain without aliasing.
        public const float DefaultTiling = 3f;

        // _AlbedoMapIntensity: 1 = full pattern, 0 = pattern invisible.
        // Around 0.5–0.7 keeps the palette colour dominant while the
        // pattern still reads as surface detail rather than decal.
        public const float DefaultIntensity = 0.65f;

        private const string PatternFolder =
            "Assets/_Project/Art/ThirdParty/kenney_pattern-pack/PNG/Default";

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Force the import settings on the whole pattern pack so they're
        /// proper tileable repeat textures (Repeat wrap, bilinear, sRGB,
        /// no compression so the pattern lines stay crisp at tile seams).
        /// Idempotent — safe to call every Pass A build.
        /// </summary>
        public static void EnsureImportSettings()
        {
            if (!AssetDatabase.IsValidFolder(PatternFolder))
            {
                Debug.LogWarning($"[Robogame] BlockTextures: pattern folder not found at {PatternFolder}. " +
                                 "Drop the Kenney pattern pack there or update the path constant.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PatternFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool changed = false;

                if (importer.textureType != TextureImporterType.Default)
                {
                    importer.textureType = TextureImporterType.Default;
                    changed = true;
                }
                if (importer.wrapMode != TextureWrapMode.Repeat)
                {
                    importer.wrapMode = TextureWrapMode.Repeat;
                    changed = true;
                }
                if (importer.filterMode != FilterMode.Bilinear)
                {
                    importer.filterMode = FilterMode.Bilinear;
                    changed = true;
                }
                if (importer.sRGBTexture != true)
                {
                    importer.sRGBTexture = true;
                    changed = true;
                }
                if (importer.mipmapEnabled != true)
                {
                    importer.mipmapEnabled = true;
                    changed = true;
                }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    // Patterns are 1-bit-ish geometry — block compression
                    // chunks them up. Uncompressed is cheap (these are tiny).
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }
                if (importer.maxTextureSize > 512)
                {
                    importer.maxTextureSize = 512;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>Load pattern_NN.png as a Texture2D, or null if missing.</summary>
        public static Texture2D LoadPattern(int index)
        {
            string fileName = $"pattern_{index:00}.png";
            string path = Path.Combine(PatternFolder, fileName).Replace('\\', '/');
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
