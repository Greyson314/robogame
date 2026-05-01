using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Wires OccaSoftware's <c>Fluff</c> shell-based grass shader onto the
    /// arena ground plane. Far simpler than the previous GrassFlow path:
    /// Fluff is a single shader (<c>OccaSoftware/Fluff/Grass</c>) that
    /// replaces the ground's material — no extra renderer component, no
    /// chunk grid, no per-blade meshes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fluff ships as a UPM package at
    /// <c>Packages/com.occasoftware.fluff/</c>. The shader expects several
    /// noise / direction / wind textures, so we follow the same
    /// "clone the shipped sample" rule we use everywhere else (see
    /// <c>docs/ART_DIRECTION.md § Verifying authored size</c>): we
    /// duplicate <c>Samples/Demo/Materials/Grass.mat</c> and override
    /// only the two colour properties — <c>_TopColor</c> (grass tint) and
    /// <c>_BaseColor</c> (ground tint visible between blades) — to lock
    /// the look to <see cref="WorldPalette.Grass"/>. Authoring a fresh
    /// material from the shader is the same cargo-cult mistake that bit
    /// us with GrassFlow's keyword toggles.
    /// </para>
    /// <para>
    /// We don't compile-couple to the package: <c>Shader.Find</c> on the
    /// shader name and <c>AssetDatabase.LoadAssetAtPath</c> on the sample
    /// material both return null cleanly if Fluff is removed, and we fall
    /// back to <see cref="GroundMaterial.ApplyToGround"/>'s procedural
    /// tile texture. Note: this package has no asmdef on the consuming
    /// side either — there's nothing to reference — so reflection isn't
    /// needed. (The package's <c>RenderInteractiveGrass</c> component
    /// only matters if we want player-driven grass deflection later, and
    /// that's a separate Phase 2+ task.)
    /// </para>
    /// </remarks>
    public static class FluffGround
    {
        // -----------------------------------------------------------------
        // Asset paths
        // -----------------------------------------------------------------

        public  const string MaterialPath  = WorldPalette.Folder + "/Mat_ArenaFluff.mat";
        private const string SourceMatPath = "Packages/com.occasoftware.fluff/Samples/Demo/Materials/Grass.mat";
        private const string ShaderName    = "OccaSoftware/Fluff/Grass";

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Try to assign a palette-tinted Fluff grass material to
        /// <paramref name="ground"/>'s <see cref="MeshRenderer"/>. Returns
        /// <c>true</c> if Fluff was found and wired, <c>false</c> if we
        /// fell back to the procedural <see cref="GroundMaterial"/>.
        /// </summary>
        public static bool ApplyToGround(GameObject ground)
        {
            if (ground == null) return false;

            Shader fluff = Shader.Find(ShaderName);
            if (fluff == null)
            {
                Debug.LogWarning(
                    "[FluffGround] Fluff package not found (shader '" + ShaderName +
                    "' missing) — falling back to procedural GroundMaterial. " +
                    "Re-import Fluff from Package Manager to enable shell-based grass.");
                GroundMaterial.ApplyToGround(ground, tilesPerSide: 30);
                return false;
            }

            Material mat = GetOrBuildMaterial(fluff);
            if (mat == null)
            {
                Debug.LogWarning(
                    "[FluffGround] Could not locate Fluff's sample 'Grass.mat' " +
                    "to clone (path: " + SourceMatPath + "). Falling back to " +
                    "procedural GroundMaterial.");
                GroundMaterial.ApplyToGround(ground, tilesPerSide: 30);
                return false;
            }

            var mr = ground.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Debug.LogWarning("[FluffGround] Ground has no MeshRenderer; skipping.");
                return false;
            }

            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // grass shader handles shells; no need to cast from the base plane
            mr.receiveShadows = true;

            EditorUtility.SetDirty(mr);
            return true;
        }

        // -----------------------------------------------------------------
        // Material baker
        // -----------------------------------------------------------------

        private static Material GetOrBuildMaterial(Shader fluff)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceMatPath);
            if (source == null) return null;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            // If the existing asset was authored against an older shader
            // version (or against a different shader entirely after a
            // package swap), nuke and re-clone — cheaper than migrating
            // keywords across shader versions.
            if (mat != null && mat.shader != source.shader)
            {
                AssetDatabase.DeleteAsset(MaterialPath);
                mat = null;
            }

            if (mat == null)
            {
                Directory.CreateDirectory(WorldPalette.Folder);
                mat = new Material(source) { name = "Mat_ArenaFluff" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            // Idempotent re-tint + tuning. We override colour and a small
            // set of shape/wind floats; everything else (textures,
            // keywords, shell count, fade distances) inherits from the
            // shipped sample.
            //
            // Colour:
            //   _TopColor       — grass tint at the shell tips. Slightly
            //                     brightened above WorldPalette.Grass so
            //                     the cel look reads as "lit" rather than
            //                     flat under the toon ramp.
            //   _BaseColor      — ground tint visible between blades.
            //                     Darker derivative so the shells read
            //                     as floating above the ground.
            //   _GrassTintColor — sample ships this red as a "remember
            //                     to set me" marker. Reset to white so
            //                     interactivity-cut doesn't recolour us.
            //   _WindColor      — extra tip highlight when wind hits.
            //                     Lock to a light derivative of grass.
            //
            // Shape (height + density of shells):
            //   _MaximumHeight  — sample is 0.4 (very low carpet). Bump
            //                     to 1.0 so blades stand up clearly at
            //                     gameplay camera distance.
            //   _ShellCount     — keep at 16 (max). Density of layers.
            //   _ShapeNoiseStrength / _DetailNoiseStrength — keep sample
            //                     values; these drive blade silhouette.
            //
            // Wind (chill the cauldron):
            //   _WindMainStrength       — main wave amplitude.
            //   _WindPulseStrength      — gust amplitude.
            //   _WindTurbulenceStrength — high-freq jitter amplitude.
            //   Sample defaults are all 0.3-1.0; we drop them by ~5x so
            //   the grass sways subtly instead of breathing in unison.
            //   _WindPulseFrequency / _WindTurbulenceSpeed also slowed.
            Color top   = Brighten(WorldPalette.Grass, 0.18f);
            Color baseC = new Color(top.r * 0.45f, top.g * 0.45f, top.b * 0.45f, 1f);
            Color wind  = Brighten(WorldPalette.Grass, 0.35f);

            mat.SetColor("_TopColor",       top);
            mat.SetColor("_BaseColor",      baseC);
            mat.SetColor("_GrassTintColor", Color.white);
            mat.SetColor("_WindColor",      wind);

            // Shape — taller, stylized, wavy.
            //
            // Tuning targets the OccaSoftware tutorial's "calm meadow"
            // recommendations:
            //   • Maximum height in [0.25, 1.0] — anything taller exposes
            //     shell-step banding when shadows fall through the grass.
            //   • Shell count high (16 max) — low counts produce visible
            //     layer striping.
            //   • Shape + detail noise *moderate* — cranking strength
            //     produces "interesting but stylized" results that read as
            //     noisy rather than wavy.
            //
            // How Fluff varies height: each shell pass discards fragments
            // where the shape-noise sample falls below the shell's height
            // threshold. So `_ShapeNoiseStrength` is the height delta
            // between bald and full and `_ShapeNoiseScale` is the
            // frequency of those carvings — smaller scale = larger
            // patches of tall vs. short grass (broad rolling waves).
            mat.SetFloat("_MaximumHeight",         0.85f);
            mat.SetFloat("_ShellCount",            16f);
            mat.SetFloat("_ShapeNoiseScale",       0.35f);
            mat.SetFloat("_ShapeNoiseStrength",    0.55f);
            mat.SetFloat("_DetailNoiseScale",      4.0f);
            mat.SetFloat("_DetailNoiseStrength",   0.30f);
            mat.SetFloat("_GrassDirectionStrength", 1.0f);

            // Fins on — they fill the silhouette gap when viewing the
            // grass from the side (third-person camera arc). Top-down-only
            // games can leave this off; we don't have that luxury.
            mat.SetFloat("_FinsEnabled",   1f);
            mat.SetFloat("_ShellsEnabled", 1f);
            mat.EnableKeyword("_FinsEnabled");
            mat.EnableKeyword("_ShellsEnabled");

            // Sampling — world-space is the tutorial's recommendation for
            // any flat terrain that uses surface-normal exclusion (so
            // grass doesn't sprout horizontally off cliff edges). UV
            // sampling (1) only makes sense on UV-mapped meshes without
            // the normal exclusion.
            mat.SetFloat("_TextureSamplingMethod",         0f);
            mat.SetFloat("_SurfaceNormalExclusionEnabled", 1f);
            mat.SetFloat("_SurfaceNormalPower",            2f);
            mat.EnableKeyword("_SurfaceNormalExclusionEnabled");

            // Fade distance — high so the grass reads bright and edge-free
            // out to the horizon. Drop these to 10–20 if profiling shows
            // grass is the GPU bottleneck.
            mat.SetFloat("_FadeStartDistance", 150f);
            mat.SetFloat("_MaximumDistance",   220f);

            // Wind — calmer.
            mat.SetFloat("_WindMainStrength",       0.06f);
            mat.SetFloat("_WindPulseStrength",      0.05f);
            mat.SetFloat("_WindPulseFrequency",     1.5f);
            mat.SetFloat("_WindTurbulenceStrength", 0.04f);
            mat.SetFloat("_WindTurbulenceSpeed",    0.4f);
            mat.SetFloat("_WindTurbulenceScale",    1.0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        private static Color Brighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + (1f - c.r) * amount),
                Mathf.Clamp01(c.g + (1f - c.g) * amount),
                Mathf.Clamp01(c.b + (1f - c.b) * amount),
                1f);
        }
    }
}
