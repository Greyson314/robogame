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
            //
            // Why these two colors carry so much weight: in the fragment
            // (`Grass.hlsl` line ~691) the shader does
            //   albedo = lerp(_BaseColor * _GroundTex,
            //                 _TopColor  * _MainTex, sqrt(height));
            // i.e. the per-pixel colour is a *vertical* gradient driven
            // by which shell the fragment came from. The OccaSoftware
            // demo's signature "fluffy layered" look is just that
            // gradient with high contrast: HDR-bright tips + crushed
            // bases. Both colour properties are tagged [HDR] in the
            // shader so values > 1.0 are valid (and required to get the
            // sunlit-tip glow Michael shows in the trailer). We were
            // previously running ~2.2× contrast (top × 0.45) which read
            // as "smooth gradient," not "individual shells." Pushing to
            // ~7× contrast + HDR top gives the canopy-shadow stacking.
            Color baseHue = Brighten(WorldPalette.Grass, 0.18f);
            Color top     = new Color(baseHue.r * 1.55f, baseHue.g * 1.55f, baseHue.b * 1.55f, 1f); // HDR tip (~1.55× radiometric)
            Color baseC   = new Color(baseHue.r * 0.22f, baseHue.g * 0.22f, baseHue.b * 0.22f, 1f); // crushed canopy floor
            Color wind    = Brighten(WorldPalette.Grass, 0.35f);

            mat.SetColor("_TopColor",       top);
            mat.SetColor("_BaseColor",      baseC);
            mat.SetColor("_GrassTintColor", Color.white);
            mat.SetColor("_WindColor",      wind);

            // Shape — taller, stylized, wavy.
            //
            // Tuning targets the OccaSoftware tutorial's "calm meadow"
            // recommendations *adjusted for our framing*. The tutorial
            // assumes a hand-held camera ~1 m above a 10 m plane; our
            // FollowCamera sits ~7-9 m above the chassis (distance 12 m,
            // height 2 m, pitch ~18°) over a 220 m field. Authored grass
            // height in meters projects to a much smaller screen-space
            // band at our framing — the tutorial's "0.25-1.0 m" range
            // reads as a flat painted texture for us. We compensate by
            // bumping `_MaximumHeight` and pulling `_ShapeNoiseStrength`
            // down so less of that height is carved away (otherwise the
            // grass looks tall in patches and bald everywhere else).
            //
            // How Fluff varies height: each shell pass discards fragments
            // where the shape-noise sample falls below the shell's height
            // threshold. So `_ShapeNoiseStrength` is the height delta
            // between bald and full and `_ShapeNoiseScale` is the
            // frequency of those carvings.
            //
            // Tiling math: one shape-noise tile covers
            //   tileMeters = _WorldScale / _ShapeNoiseScale
            // and visible repeats == frustumWidth / tileMeters. With
            // WorldScale 8 and ShapeNoiseScale 0.35, that's ~23 m tiles
            // — a third-person camera sees ~3 of them across the field
            // and the eye picks out the repeats as soft boxes.
            //
            // We pick the *opposite* direction from the tutorial defaults:
            // tiny shape tiles + low strength. That reads as high-
            // frequency natural density variation rather than "biome
            // patches," so repeats become invisible — every square meter
            // has its own tiny irregularity. Combined with a large
            // _WorldScale (65 m), the noise textures themselves don't
            // visibly tile within a single screen.
            //
            // Effective tip height ≈ _MaximumHeight × (1 - _ShapeNoiseStrength).
            // 1.2 × 0.85 ≈ 1.0 m of grass at the deepest noise pockets.
            mat.SetFloat("_MaximumHeight",         1.2f);
            mat.SetFloat("_ShellCount",            16f);
            mat.SetFloat("_ShapeNoiseScale",       2.7f);
            mat.SetFloat("_ShapeNoiseStrength",    0.15f);
            // Detail noise: this is what carves the per-shell blade
            // silhouettes.
            mat.SetFloat("_DetailNoiseScale",      1.0f);
            mat.SetFloat("_DetailNoiseStrength",   0.65f);
            mat.SetFloat("_GrassDirectionStrength", 1.0f);

            // Noise textures — explicit pins so re-baking can't drift
            // back to the demo's defaults. grass-noise-23 has clean
            // organic blobs (good for shape patches at low strength),
            // grass-noise-14 has fine blade-grain (good detail noise).
            AssignNoiseTexture(mat, "_ShapeNoiseTexture",  "grass-noise-23");
            AssignNoiseTexture(mat, "_DetailNoiseTexture", "grass-noise-14");

            // Fins on — they fill the silhouette gap when viewing the
            // grass from the side (third-person camera arc). Top-down-only
            // games can leave this off; we don't have that luxury.
            mat.SetFloat("_FinsEnabled",   1f);
            mat.SetFloat("_ShellsEnabled", 1f);
            mat.EnableKeyword("_FinsEnabled");
            mat.EnableKeyword("_ShellsEnabled");

            // Sampling — World-space (1), not UV (0). Earlier comment in
            // this file claimed 0 was world-space; the shader actually
            // does the opposite (Grass.hlsl ~line 531 — `if(method==1)
            // uv = positionWS.xz * _InvWorldScale`). UV sampling is fine
            // on a tiny demo plane but on our 220 m hills the per-quad
            // UVs stretch and warp across the displaced mesh, which
            // makes the noise smear and the layers blur into a single
            // gradient. World-space sampling locks the noise to actual
            // meters of terrain — same blade density everywhere, no
            // distortion on slopes.
            //
            // _WorldScale is the *size in meters that one tile of the
            // shape/detail noise covers*. Larger _WorldScale stretches
            // the noise tiles wider, so the textures themselves don't
            // visibly repeat across the visible strip. Combined with a
            // small _ShapeNoiseScale this still gives high-frequency
            // surface variation; the ratio (_WorldScale / _NoiseScale)
            // is what reads as the on-screen patch size. 65 m landed
            // nicely after manual tuning at our FollowCamera framing.
            mat.SetFloat("_TextureSamplingMethod",         1f);
            mat.SetFloat("_WorldScale",                    65f);
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

        // -----------------------------------------------------------------
        // Noise texture pinning
        // -----------------------------------------------------------------

        /// <summary>
        /// Assigns one of Fluff's shipped noise PNGs (e.g. "grass-noise-23")
        /// to the given material property. Loads from the package's
        /// <c>Runtime/Textures/Noise/</c> folder so re-baking can't drift
        /// back to whatever the demo material had last.
        /// </summary>
        private static void AssignNoiseTexture(Material mat, string propertyName, string textureBaseName)
        {
            const string NoiseFolder = "Packages/com.occasoftware.fluff/Runtime/Textures/Noise/";
            string path = NoiseFolder + textureBaseName + ".png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogWarning($"[FluffGround] Noise texture not found at '{path}'. Leaving '{propertyName}' as-is.");
                return;
            }
            mat.SetTexture(propertyName, tex);
        }
    }
}
