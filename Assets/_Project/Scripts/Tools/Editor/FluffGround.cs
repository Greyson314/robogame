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
            // _BaseColor is the ground colour visible BETWEEN blades. Pinned
            // to RGB(173, 255, 147) — a bright handpainted-grass-friendly hue
            // that complements the imported Grass_lighted_up tile texture and
            // keeps the canopy floor reading as "lit grass" rather than the
            // crushed-shadow look of the original FluffGround default.
            Color baseC   = new Color(173f / 255f, 1f, 147f / 255f, 1f);
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
            // 1.7 × 0.85 ≈ 1.45 m of grass at the deepest noise pockets.
            //
            // Shell count: 7 instead of the shader's max of 16. Each shell
            // is a full geometry-shader output triangle; shaving 9 shells
            // off the 28k-tri ground mesh saves ~250k post-geometry tris
            // per frame *per render pass*. Visible difference at our
            // framing is minimal because shape/detail noise + colour
            // gradient do most of the "fluffy" work; the human eye stops
            // resolving individual shells well before 16. Bump back to
            // 12–16 only if the look visibly thins. See
            // docs/PERFORMANCE.md § Fluff for the math.
            mat.SetFloat("_MaximumHeight",         1.7f);
            mat.SetFloat("_ShellCount",            7f);
            // Shape / detail noise scales are normalised against _WorldScale
            // (see comment block on _WorldScale below). The shader samples
            // shape noise at world.xz / (_WorldScale × _ShapeNoiseScale)
            // — so when we drop _WorldScale to tile the ground texture
            // finer, we have to BUMP these scales by the same factor to
            // keep the visible noise patterns the same size. The product
            // _WorldScale × _ShapeNoiseScale ≈ 175 m (one shape-noise tile
            // every ~175 m of world), and _WorldScale × _DetailNoiseScale
            // ≈ 65 m (detail tile every ~65 m).
            mat.SetFloat("_ShapeNoiseScale",       5.5f);
            mat.SetFloat("_ShapeNoiseStrength",    0.15f);
            // Detail noise: this is what carves the per-shell blade
            // silhouettes.
            mat.SetFloat("_DetailNoiseScale",      2.03f);
            mat.SetFloat("_DetailNoiseStrength",   0.65f);
            mat.SetFloat("_GrassDirectionStrength", 1.0f);

            // Noise textures — explicit pins so re-baking can't drift
            // back to the demo's defaults. grass-noise-23 has clean
            // organic blobs (good for shape patches at low strength),
            // grass-noise-14 has fine blade-grain (good detail noise).
            AssignNoiseTexture(mat, "_ShapeNoiseTexture",  "grass-noise-23");
            AssignNoiseTexture(mat, "_DetailNoiseTexture", "grass-noise-14");

            // Ground texture — pinned to the imported Handpainted Grass
            // & Ground "lighted up" tile so the canopy floor between
            // blades reads as real painted grass instead of the Fluff
            // demo's flat colour swatch. Falls back silently if the
            // package is absent (the floor will then sample as white,
            // which the _BaseColor multiply tints to the configured hue).
            AssignAssetTexture(
                mat,
                "_GroundTex",
                "Assets/Handpainted_Grass_and_Ground_Textures/Textures/Grass/Grass_lighted/Grass_lighted_up.png");

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
            // _WorldScale = the world-space size in metres of one tile of
            // _GroundTex / _MainTex. The Fluff shader samples those textures
            // with `uv = positionWS.xz / _WorldScale` (Grass.hlsl line 533),
            // ignoring the material's Tiling/Offset (`_GroundTex_ST`) entirely
            // — Tiling on the inspector does NOTHING for ground colour.
            //
            // Earlier value 65 m made the imported handpainted-grass tile
            // read as huge brush strokes at gameplay framing (camera ~5–10 m
            // off the ground sees one tile every screen-width). 32 m hits a
            // sweet spot for the Handpainted Grass tile: tile is small
            // enough to register as ground variation rather than a single
            // stretched stroke, large enough that obvious texture repeats
            // don't read across the camera frustum.
            //
            // Compensated _ShapeNoiseScale and _DetailNoiseScale above so
            // the shape/detail noise patterns themselves stay visually
            // unchanged. The product (_WorldScale × _ShapeNoiseScale) ≈ 175 m
            // of shape-noise tile and (_WorldScale × _DetailNoiseScale) ≈ 65 m
            // of detail-noise tile, both held constant across the change.
            mat.SetFloat("_WorldScale",                    32f);
            mat.SetFloat("_SurfaceNormalExclusionEnabled", 1f);
            mat.SetFloat("_SurfaceNormalPower",            2f);
            mat.EnableKeyword("_SurfaceNormalExclusionEnabled");

            // Tile warp — Robogame package modification to the Fluff
            // shader (docs/PACKAGE_MODIFICATIONS.md). Reuses the shape +
            // detail noise samples to warp the ground / grass texture
            // UVs, hiding the regular tile grid as the imported
            // Handpainted Grass tile is otherwise visibly repeating at
            // gameplay framing. 0.5 is a moderate setting; bump to ~1.0
            // for more visible wobble, drop to 0 to disable.
            mat.SetFloat("_TileWarpStrength",              0.5f);

            // Cast shadows OFF on the grass plane. The MeshRenderer's
            // shadowCastingMode (set further down to ShadowCastingMode.Off)
            // is the ACTUAL gate — Fluff's _CastShadowsEnabled material
            // float is UI-only and not wired into the shader's runtime
            // branches (verified in Grass.hlsl). Setting these floats to
            // 0 mirrors the renderer state in the material inspector so
            // a future maintainer doesn't see a misleading "Cast Shadows
            // ✓" tick on a grass plane that doesn't actually cast.
            mat.SetFloat("_CastShadows",                   0f);
            mat.SetFloat("_CastShadowsEnabled",            0f);

            // Fade distance window. Two thresholds, both critical for perf:
            //   _FadeStartDistance — within this radius every triangle's
            //                         geometry shader emits 16 shells AND
            //                         6 fin layers. Past it, only shells.
            //   _MaximumDistance   — past this, the grass shader skips
            //                         shells entirely and emits only the
            //                         base mesh.
            // Earlier values (150 / 220) put almost the entire 220 × 220 m
            // hills mesh in the most expensive band, since the player camera
            // is rarely more than 10 m off the ground. Shrinking the fade
            // window drops the geometry-shader workload by 4–10× depending
            // on camera height, with no visible quality change at gameplay
            // framing — the screen-space grass density inside ~80 m looks
            // identical (you literally cannot resolve individual blades on
            // grass that's > 80 m away). See docs/PERFORMANCE.md § Fluff.
            mat.SetFloat("_FadeStartDistance", 22f);
            mat.SetFloat("_MaximumDistance",   85f);

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

        /// <summary>
        /// Pins a project-asset texture (full asset-DB path) onto a
        /// material property. Used for the ground-tile texture which lives
        /// outside the Fluff package's noise folder. Silent no-op if the
        /// asset is missing — the property keeps whatever it had.
        /// </summary>
        private static void AssignAssetTexture(Material mat, string propertyName, string assetPath)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null)
            {
                Debug.LogWarning($"[FluffGround] Texture not found at '{assetPath}'. Leaving '{propertyName}' as-is.");
                return;
            }
            mat.SetTexture(propertyName, tex);
        }
    }
}
