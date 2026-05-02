using System.IO;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Authors a Polyverse Skies skybox material for the Arena scene.
    ///
    /// The previous version of this builder set <c>_EquatorColor</c> /
    /// <c>_GroundColor</c> only and skipped the headline <c>_SkyColor</c>
    /// (the upper hemisphere) — so the sky read as a flat 2-tone wash
    /// with no actual blue dome above the camera. It also never enabled
    /// any of the shader's <c>[Toggle]</c> keywords, so the sun/clouds
    /// features the Polyverse package ships with were dead in the water.
    ///
    /// This rewrite:
    /// 1. Sets the full 3-stop gradient (Sky → Equator → Ground) from
    ///    <see cref="WorldPalette"/> tokens.
    /// 2. Enables the sun layer, points it at the directional light by
    ///    direction vector, and binds the package's <c>Sun 01.png</c>
    ///    halo texture.
    /// 3. Enables a slow-drifting cloud cubemap so the sky has motion
    ///    instead of being a static gradient.
    /// 4. Falls back to <c>Skybox/Procedural</c> if the BOXOPHOBIC
    ///    package is missing so the rest of the scaffold doesn't blow
    ///    up on a fresh clone.
    /// </summary>
    internal static class SkyboxBuilder
    {
        public const string Folder = "Assets/_Project/Rendering/Skyboxes";
        public const string ArenaSkyboxPath = Folder + "/Skybox_Arena.mat";

        private const string PolyverseShaderName = "BOXOPHOBIC/Polyverse Skies/Standard";

        // Polyverse package texture paths — these ship as part of the
        // Polyverse Skies / Core asset and are imported with
        // textureShape=Cube (lat-long auto-cubemap), so they load as
        // Cubemap directly via AssetDatabase.
        private const string SunTexturePath    = "Assets/BOXOPHOBIC/Polyverse Skies/Core/Textures/Suns/Sun 01.png";
        private const string CloudsCubemapPath = "Assets/BOXOPHOBIC/Polyverse Skies/Core/Textures/Clouds/Clouds 03 A.png";

        /// <summary>
        /// Build (or update) the Arena skybox material. Palette tokens
        /// come from <see cref="WorldPalette"/> so re-skinning the
        /// arena means editing one file, not chasing materials.
        /// </summary>
        /// <param name="sunDirectionWorld">
        /// Unit vector from origin pointing TOWARD the sun's position
        /// in the sky (i.e. the negation of the directional light's
        /// forward). Pass <see cref="Vector3.zero"/> to leave the
        /// shader's default sun direction untouched.
        /// </param>
        public static Material BuildArenaSkybox(Vector3 sunDirectionWorld = default)
        {
            EnsureFolder(Folder);

            Shader shader = Shader.Find(PolyverseShaderName);
            bool isPolyverse = shader != null;
            if (!isPolyverse)
            {
                Debug.LogWarning(
                    $"[Robogame] Polyverse Skies shader '{PolyverseShaderName}' not found. " +
                    "Falling back to a procedural skybox material. Re-import the BOXOPHOBIC " +
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

            if (isPolyverse)
                ApplyPolyverseSettings(mat, sunDirectionWorld);
            else
                ApplyProceduralFallback(mat);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Push our palette + feature toggles onto the Polyverse
        /// "Standard" shader. Properties are written defensively
        /// (<c>HasProperty</c> guards) so a future shader update that
        /// removes a knob doesn't crash the builder.
        /// </summary>
        private static void ApplyPolyverseSettings(Material mat, Vector3 sunDirectionWorld)
        {
            // ---- Background ----------------------------------------------
            // Background mode = 0 (Colors). The shader uses a keyword
            // enum, so we have to flip BOTH the float prop AND the
            // shader keyword for the change to take effect at runtime.
            SetEnumKeyword(mat, "_BackgroundMode", 0,
                "_BACKGROUNDMODE_COLORS", "_BACKGROUNDMODE_CUBEMAP", "_BACKGROUNDMODE_COMBINED");

            // 3-stop gradient. _SkyColor dominates the upper hemisphere
            // (everything above _EquatorHeight), _GroundColor dominates
            // the lower. _EquatorHeight is normalised: 0.5 = equator at
            // horizon. Pushing it down to 0.4 buys us a slightly larger
            // sky dome above the camera, which reads better against the
            // 220m ground plane that hides the lower hemisphere.
            //
            // Equator gets a desaturated/lightened SkyDay so the band
            // softens into the sky instead of stamping a hard line.
            // Ground gets a faintly warm horizon haze (Hazard mixed in
            // at 8%) so the rim where sky meets terrain reads as "hot
            // late-afternoon" rather than blue-on-blue.
            if (mat.HasProperty("_SkyColor"))           mat.SetColor("_SkyColor",           WorldPalette.SkyDay);
            if (mat.HasProperty("_EquatorColor"))       mat.SetColor("_EquatorColor",       WorldPalette.SkyEquator);
            if (mat.HasProperty("_GroundColor"))        mat.SetColor("_GroundColor",        Color.Lerp(WorldPalette.SkyEquator, WorldPalette.Hazard, 0.08f));
            if (mat.HasProperty("_EquatorHeight"))      mat.SetFloat("_EquatorHeight",      0.4f);
            if (mat.HasProperty("_EquatorSmoothness"))  mat.SetFloat("_EquatorSmoothness",  0.55f);
            if (mat.HasProperty("_BackgroundExposure")) mat.SetFloat("_BackgroundExposure", 1.0f);

            // ---- Sun -----------------------------------------------------
            // Bind the package's halo texture, point it at the
            // directional light, mid-bright warm tint to match the
            // arena sun colour from EnvironmentBuilder (#FFF8E0).
            Texture2D sunTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SunTexturePath);
            if (sunTex != null && mat.HasProperty("_SunTexture")) mat.SetTexture("_SunTexture", sunTex);
            if (mat.HasProperty("_SunColor"))     mat.SetColor("_SunColor",     new Color(1f, 0.97f, 0.88f, 1f));
            if (mat.HasProperty("_SunSize"))      mat.SetFloat("_SunSize",      0.35f);
            if (mat.HasProperty("_SunIntensity")) mat.SetFloat("_SunIntensity", 1.4f);

            // Local direction mode (1) lets the material own its sun
            // placement — we want the skybox sun to track the same
            // angle as the directional light from EnvironmentBuilder
            // without depending on a Polyverse runtime controller.
            if (mat.HasProperty("_SunDirectionMode")) mat.SetFloat("_SunDirectionMode", 1f);
            if (sunDirectionWorld.sqrMagnitude > 0.0001f && mat.HasProperty("_SunDirection"))
            {
                Vector3 d = sunDirectionWorld.normalized;
                mat.SetVector("_SunDirection", new Vector4(d.x, d.y, d.z, 0f));
            }

            SetToggle(mat, "_EnableSun", true, "_ENABLESUN_ON");

            // ---- Clouds --------------------------------------------------
            // Soft puffy cloud cubemap drifting slowly. Light = sky
            // top so high clouds look lit by the same sky they sit in;
            // shadow = a slightly cooler/darker variant of SkyEquator
            // so the underside reads as "shaded but still hazy" rather
            // than going to black.
            Cubemap cloudsTex = AssetDatabase.LoadAssetAtPath<Cubemap>(CloudsCubemapPath);
            if (cloudsTex != null && mat.HasProperty("_CloudsCubemap")) mat.SetTexture("_CloudsCubemap", cloudsTex);
            if (mat.HasProperty("_CloudsHeight"))      mat.SetFloat("_CloudsHeight",      0.05f);
            if (mat.HasProperty("_CloudsLightColor"))  mat.SetColor("_CloudsLightColor",  Color.Lerp(Color.white, WorldPalette.SkyDay, 0.10f));
            if (mat.HasProperty("_CloudsShadowColor")) mat.SetColor("_CloudsShadowColor", WorldPalette.SkyEquator * 0.85f);

            if (mat.HasProperty("_CloudsRotation"))      mat.SetFloat("_CloudsRotation",      0f);
            if (mat.HasProperty("_CloudsRotationAxis"))  mat.SetVector("_CloudsRotationAxis", new Vector4(0f, 1f, 0f, 0f));
            if (mat.HasProperty("_CloudsRotationSpeed")) mat.SetFloat("_CloudsRotationSpeed", 0.08f); // slow drift

            SetToggle(mat, "_EnableClouds",         cloudsTex != null, "_ENABLECLOUDS_ON");
            SetToggle(mat, "_EnableCloudsRotation", cloudsTex != null, "_ENABLECLOUDSROTATION_ON");

            // ---- Disabled layers (keep them off explicitly) --------------
            // We don't want night-time stars, a moon, or stencilled
            // patterns on a sunny daytime arena. Toggling them off
            // here means re-running BuildArena always lands on the
            // same look even if someone hand-edited the material in
            // the inspector and then re-ran the scaffold.
            SetToggle(mat, "_EnableStars",          false, "_ENABLESTARS_ON");
            SetToggle(mat, "_EnableStarsTwinkling", false, "_ENABLESTARSTWINKLING_ON");
            SetToggle(mat, "_EnableStarsRotation",  false, "_ENABLESTARSROTATION_ON");
            SetToggle(mat, "_EnableMoon",           false, "_ENABLEMOON_ON");
            SetToggle(mat, "_EnablePatternOverlay", false, "_ENABLEPATTERNOVERLAY_ON");
            SetToggle(mat, "_EnableBuiltinFog",     false, "_ENABLEBUILTINFOG_ON");

            // ---- Skybox transform ----------------------------------------
            if (mat.HasProperty("_SkyboxOffset"))   mat.SetFloat("_SkyboxOffset",   0f);
            if (mat.HasProperty("_SkyboxRotation")) mat.SetFloat("_SkyboxRotation", 0f);
        }

        /// <summary>Best-effort palette match on the built-in procedural shader.</summary>
        private static void ApplyProceduralFallback(Material mat)
        {
            if (mat.HasProperty("_SkyTint"))             mat.SetColor("_SkyTint",            WorldPalette.SkyDay);
            if (mat.HasProperty("_GroundColor"))         mat.SetColor("_GroundColor",        WorldPalette.SkyEquator);
            if (mat.HasProperty("_AtmosphereThickness")) mat.SetFloat("_AtmosphereThickness", 1.0f);
            if (mat.HasProperty("_Exposure"))            mat.SetFloat("_Exposure",            1.0f);
        }

        /// <summary>
        /// Flip a [Toggle(KEYWORD)] property — we have to set the float
        /// prop AND enable/disable the shader keyword for the change to
        /// take effect at runtime, since the inspector script that
        /// normally syncs the two doesn't run during scaffolding.
        /// </summary>
        private static void SetToggle(Material mat, string property, bool on, string keyword)
        {
            if (mat.HasProperty(property)) mat.SetFloat(property, on ? 1f : 0f);
            if (on) mat.EnableKeyword(keyword);
            else    mat.DisableKeyword(keyword);
        }

        /// <summary>
        /// Flip a [KeywordEnum(...)] property by writing the float and
        /// rotating which of the mutually-exclusive keywords is active.
        /// </summary>
        private static void SetEnumKeyword(Material mat, string property, int index, params string[] keywords)
        {
            if (mat.HasProperty(property)) mat.SetFloat(property, index);
            for (int i = 0; i < keywords.Length; i++)
            {
                if (i == index) mat.EnableKeyword(keywords[i]);
                else            mat.DisableKeyword(keywords[i]);
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
    }
}
