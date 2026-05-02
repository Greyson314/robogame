using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor-only factory that produces <c>Mat_Water.mat</c> backed by
    /// the Bitgem Stylised Water URP shader graph
    /// (<c>Assets/Bitgem/StylisedWater/URP/Shaders/WaterVolume-URP.shadergraph</c>).
    /// </summary>
    /// <remarks>
    /// <para>Strategy: clone Bitgem's <c>example-water-01.mat</c> verbatim so
    /// all of its tuned floats (foam width/noise, depth scale/power, refraction
    /// strength, bump strength, glossiness, scroll speed, detail noise) carry
    /// over for free. We then override only:</para>
    /// <list type="bullet">
    ///   <item>Shallow + deep tints → <see cref="WorldPalette.WaterSurface"/> /
    ///   <see cref="WorldPalette.WaterDeep"/>.</item>
    ///   <item><c>_WaveScale</c>, <c>_WaveSpeed</c>, <c>_WaveFrequency</c> → 0.
    ///   The Bitgem shader has built-in vertex displacement that would fight
    ///   our CPU-side <see cref="Robogame.Gameplay.WaterMeshAnimator"/>; the
    ///   animator is authoritative because <c>BuoyancyController</c> samples
    ///   the same animated mesh, so visual + physics stay in lockstep.</item>
    /// </list>
    /// <para>If Bitgem ever uninstalls the demo material or shader graph,
    /// <see cref="GetOrBuild"/> returns <c>null</c> and <see cref="WorldPalette.WaterMat"/>
    /// falls back to the original URP/Lit translucent teal so builds don't break.</para>
    /// </remarks>
    public static class BitgemWaterMaterial
    {
        public const string MaterialPath  = WorldPalette.Folder + "/Mat_Water.mat";
        private const string SourceMatPath = "Assets/Bitgem/StylisedWater/URP/Materials/example-water-01.mat";
        private const string ShaderPath    = "Assets/Bitgem/StylisedWater/URP/Shaders/WaterVolume-URP.shadergraph";

        // Property reference names. Wave knobs use friendly OverrideReferenceName
        // values from the shader graph; colors fall through to the hex
        // DefaultReferenceName because the graph doesn't override them.
        // (Confirmed by reading WaterVolume-URP.shadergraph directly.)
        private const string PropShallowColor = "Color_F01C36BF"; // _ShallowColor
        private const string PropDeepColor    = "Color_7D9A58EC"; // _DeepColor
        private const string PropWaveScale    = "_WaveScale";
        private const string PropWaveSpeed    = "_WaveSpeed";
        private const string PropWaveFreq     = "_WaveFrequency";
        // Surface-detail knobs (per the shader graph property dump):
        //   Vector1_244B0600 = _ScrollSpeed   — UV scroll on the normal map.
        //                                       Demo ships at 1.2 which races
        //                                       against our slow Gerstner swell
        //                                       (looks like a fast current).
        //   Vector1_46E42935 = _DetailStrength — how hard the second-octave
        //                                       normal contributes. Demo 0.25.
        //   Vector1_B9F56378 = _BumpStrength   — overall normal-map influence.
        //                                       Demo 0.35.
        private const string PropScrollSpeed   = "Vector1_244B0600";
        private const string PropDetailStrength = "Vector1_46E42935";
        private const string PropBumpStrength   = "Vector1_B9F56378";

        /// <summary>
        /// Returns the cached <c>Mat_Water.mat</c>, cloning + tinting the
        /// Bitgem demo material the first time (or whenever the asset has
        /// drifted off the expected shader). Returns <c>null</c> if the
        /// Bitgem package is missing — caller should fall back.
        /// </summary>
        public static Material GetOrBuild()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) return null;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            // Clone-from-source on first build, or whenever the asset somehow
            // ended up on a different shader (e.g. previous URP/Lit fallback
            // material still on disk from before Bitgem was installed).
            if (mat == null || mat.shader != shader)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceMatPath);
                if (source == null) return null;

                EnsureFolder(WorldPalette.Folder);
                if (mat != null)
                {
                    AssetDatabase.DeleteAsset(MaterialPath);
                }

                // CopyAsset preserves every Vector1_* / Texture2D_* the demo
                // ships with, so we inherit foam/depth/refraction tuning.
                AssetDatabase.CopyAsset(SourceMatPath, MaterialPath);
                mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (mat == null) return null;
                mat.name = "Mat_Water";
            }

            ApplyOverrides(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ApplyOverrides(Material mat)
        {
            // Tints — keep the demo's shallow alpha (~0.30) so depth fade
            // still reads. Deep color goes opaque; the shader uses it as a
            // far-distance absorption tint, not a literal vertex alpha.
            Color shallow = WorldPalette.WaterSurface;
            shallow.a = 0.30f;
            mat.SetColor(PropShallowColor, shallow);

            Color deep = WorldPalette.WaterDeep;
            deep.a = 1.0f;
            mat.SetColor(PropDeepColor, deep);

            // Disable Bitgem's GPU vertex displacement — WaterMeshAnimator
            // owns the surface geometry. Leaving these non-zero double-animates
            // the verts (visual lifts above where buoyancy thinks the surface is).
            mat.SetFloat(PropWaveScale, 0f);
            mat.SetFloat(PropWaveSpeed, 0f);
            mat.SetFloat(PropWaveFreq,  0f);

            // Calm the normal-map scroll. The demo's 1.2 reads as a racing
            // current on top of our slow Gerstner swell; 0.15 is a lazy drift
            // that complements the ~15 s wave period. Detail + bump strengths
            // pulled down so micro-ripples don't fight the macro shape.
            mat.SetFloat(PropScrollSpeed,    0.15f);
            mat.SetFloat(PropDetailStrength, 0.12f);
            mat.SetFloat(PropBumpStrength,   0.20f);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
