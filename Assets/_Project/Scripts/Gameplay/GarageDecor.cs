using UnityEngine;
using UnityEngine.Rendering;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Builds the garage's "bubble shield in space" look at runtime — a
    /// panel-textured circular platform with a hazard-stripe rim and cyan
    /// glow trim, the translucent shield dome, blinking rim beacons, a
    /// rotating holo build-pad ring, drifting dust motes, and a slowly
    /// tumbling asteroid field outside the bubble. Applied from code (not
    /// the scene file) so the look survives scene reverts — see
    /// <see cref="GarageController"/>. Idempotent: every piece is
    /// get-or-create by name, then its transform/material state is enforced
    /// either way, so stale serialized copies get corrected rather than
    /// skipped.
    /// </summary>
    public static class GarageDecor
    {
        // Palette tokens (docs/subsystems/art-direction.md § Palette).
        // WorldPalette is editor-only (Tools asmdef), so the runtime decor
        // mirrors the hexes it needs.
        // TRACE[DOC:art-direction§Palette]: every decor color below is a
        // palette token — don't introduce new tints here.
        private static readonly Color Concrete = new Color32(0x3F, 0x43, 0x48, 0xFF);
        private static readonly Color Slate = new Color32(0x2A, 0x32, 0x3C, 0xFF);
        private static readonly Color SlateLight = new Color32(0x52, 0x5B, 0x66, 0xFF);
        private static readonly Color Hazard = new Color32(0xF2, 0x8C, 0x1A, 0xFF);
        private static readonly Color Cyan = new Color32(0x33, 0xD9, 0xF2, 0xFF);

        // Session 121 liveliness pass: platform r~35 / bubble r~45, down
        // from r~75 / r~85 — both read cavernous around a <5 m bot.
        private const float PlatformScale = 70f;
        private const float BubbleScale = 90f;
        private const int BeaconCount = 8;
        private const float BeaconRadius = 32f;

        /// <summary>Apply the full decor pass. Safe to call on every garage load.</summary>
        public static void Apply()
        {
            GameObject env = GameObject.Find("Environment");
            Transform envT = env != null ? env.transform : null;

            // The animator owns every runtime-created material/texture so
            // repeat garage visits don't leak instances.
            GarageAmbience ambience = EnsureAmbience(env);

            ApplyLightingAndSky(ambience);
            if (envT == null) return;

            Material floorMat = HideBay(envT);
            Vector3 center = envT.Find("Podium") is Transform podium ? podium.position : Vector3.zero;

            BuildPlatform(envT, center, floorMat, ambience);
            BuildBubble(envT, center, ambience);
            BuildBeacons(envT, center, ambience);
            BuildHoloRing(envT, center, ambience);
            BuildDust(envT, center, ambience);
            BuildAsteroids(envT, center, floorMat, ambience);
        }

        private static GarageAmbience EnsureAmbience(GameObject env)
        {
            GameObject host = env != null ? env : GameObject.Find("GarageAmbience");
            if (host == null) host = new GameObject("GarageAmbience");
            GarageAmbience a = host.GetComponent<GarageAmbience>();
            if (a == null) a = host.AddComponent<GarageAmbience>();
            return a;
        }

        // -----------------------------------------------------------------
        // Sky / lighting
        // -----------------------------------------------------------------

        private static void ApplyLightingAndSky(GarageAmbience ambience)
        {
            // Night-sky skybox. Instantiated so the slow _Rotation drift the
            // ambience drives doesn't dirty the shared asset.
            Material sky = Resources.Load<Material>("Garage/Skybox_GarageSpace");
            if (sky != null)
            {
                Material instance = new Material(sky) { name = sky.name + " (Garage)" };
                // Session 121 feedback: the sky read too dark — push the stars
                // up from code so the values survive material-asset reverts.
                if (instance.HasProperty("_Exposure")) instance.SetFloat("_Exposure", 1.25f);
                if (instance.HasProperty("_Tint")) instance.SetColor("_Tint", new Color(0.32f, 0.35f, 0.45f));
                RenderSettings.skybox = instance;
                ambience.SkyboxMaterial = instance;
                ambience.Owned.Add(instance);
                DynamicGI.UpdateEnvironment();
            }

            // No atmosphere out here — kill the distance fog that greys out
            // the platform edge + horizon, so the dark night sky reads cleanly.
            RenderSettings.fog = false;

            // A touch of night: dim, slightly-blue flat ambient so the scene
            // reads dark instead of skybox-washed grey. The directional light
            // still keys the bot brightly enough to build by. Lifted in
            // session 121 — the original 0.13/0.15/0.20 left the platform and
            // asteroids near-invisible.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.21f, 0.24f, 0.31f);

            // The walled bay cleared the camera to solid black (no sky needed
            // when enclosed). Now that it's open, switch every camera to draw
            // the skybox — otherwise RenderSettings.skybox never shows and the
            // additive bubble reads as a solid glowing ball over black. Use
            // FindObjectsByType (not Camera.allCameras, which is empty this
            // early in Start before any camera has rendered a frame).
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
        }

        // -----------------------------------------------------------------
        // Bay teardown
        // -----------------------------------------------------------------

        /// <summary>Hide the old walled bay; returns the square floor's material for reuse.</summary>
        private static Material HideBay(Transform envT)
        {
            Material floorMat = null;
            foreach (Transform c in envT)
            {
                if (c.name.StartsWith("Wall_") || c.name.StartsWith("Stripe_"))
                    c.gameObject.SetActive(false);
                else if (c.name == "Floor")
                {
                    Renderer fr = c.GetComponent<Renderer>();
                    if (fr != null) floorMat = fr.sharedMaterial;
                    c.gameObject.SetActive(false);
                }
            }
            return floorMat;
        }

        // -----------------------------------------------------------------
        // Platform + rim
        // -----------------------------------------------------------------

        private static void BuildPlatform(Transform envT, Vector3 center, Material floorMat, GarageAmbience ambience)
        {
            // Main disc: panel-grid texture over the Concrete floor token.
            Transform platform = GetOrCreate(envT, "Platform", PrimitiveType.Cylinder);
            platform.localPosition = new Vector3(center.x, -0.3f, center.z);
            platform.localScale = new Vector3(PlatformScale, 0.3f, PlatformScale); // top face at y=0
            SetMaterial(platform, PlatformMaterial(floorMat, ambience));

            // Hazard-stripe rim step just below the top surface — the
            // "workshop where dangerous things get built" accent.
            Transform hazard = GetOrCreate(envT, "PlatformRimHazard", PrimitiveType.Cylinder);
            hazard.localPosition = new Vector3(center.x, -0.34f, center.z);
            hazard.localScale = new Vector3(PlatformScale + 3f, 0.26f, PlatformScale + 3f);
            SetMaterial(hazard, SolidMaterial("GarageRimHazard", floorMat, Hazard, ambience));

            // Cyan energy trim under the hazard step (Tron accent — emissive
            // vocabulary on an otherwise-dark surface).
            Transform glow = GetOrCreate(envT, "PlatformRimGlow", PrimitiveType.Cylinder);
            glow.localPosition = new Vector3(center.x, -0.45f, center.z);
            glow.localScale = new Vector3(PlatformScale + 5.5f, 0.16f, PlatformScale + 5.5f);
            SetMaterial(glow, ShieldMaterial("GarageRimGlow", Cyan, baseAlpha: 0.45f, rimIntensity: 1.6f, ambience));
        }

        /// <summary>
        /// Panel-grid texture on plain URP/Lit. Deliberately NOT a clone of
        /// the MK Toon floor material: MK Toon only samples its albedo map
        /// behind a shader-feature keyword, so a runtime clone renders base
        /// color only (verified in Play — the disc came out white). URP/Lit
        /// always samples <c>_BaseMap</c>; the texture carries the palette.
        /// </summary>
        private static Material PlatformMaterial(Material floorMat, GarageAmbience ambience)
        {
            Texture2D panels = BuildPanelTexture();
            ambience.Owned.Add(panels);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(lit) { name = "GaragePlatformPanels" };
            TrySetMainTexture(mat, panels, tiling: 18f);
            TrySetColor(mat, Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f); // matte floor (art-direction § Material Vocabulary)
            ambience.Owned.Add(mat);
            return mat;
        }

        /// <summary>
        /// 4×4-panel grid tile: Concrete panels with slight per-panel value
        /// jitter, separated by Slate seam lines. Palette-pure procedural
        /// "texture" — the art direction forbids imported realistic surfaces.
        /// </summary>
        private static Texture2D BuildPanelTexture()
        {
            const int size = 128;
            const int cell = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = "GaragePlatformPanelsTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool seam = (x % cell) < 2 || (y % cell) < 2;
                    Color c;
                    if (seam)
                    {
                        c = Slate;
                    }
                    else
                    {
                        // Deterministic per-panel plate variety between the
                        // Concrete and SlateLight tokens (brighter than pure
                        // Concrete — session 121 feedback: the grid was
                        // invisible under the dim night lighting).
                        int px = x / cell, py = y / cell;
                        int h = (px * 73856093) ^ (py * 19349663);
                        float t = ((h >> 8) & 0xFF) / 255f;
                        c = Color.Lerp(Concrete, SlateLight, 0.25f + 0.75f * t);
                    }
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true);
            return tex;
        }

        // -----------------------------------------------------------------
        // Bubble
        // -----------------------------------------------------------------

        private static void BuildBubble(Transform envT, Vector3 center, GarageAmbience ambience)
        {
            Transform bubble = GetOrCreate(envT, "ShieldBubble", PrimitiveType.Sphere);
            bubble.localPosition = new Vector3(center.x, 3f, center.z);
            bubble.localScale = Vector3.one * BubbleScale;
            Material bubbleMat = Resources.Load<Material>("Garage/Mat_ShieldBubble");
            if (bubbleMat != null) SetMaterial(bubble, bubbleMat);
        }

        // -----------------------------------------------------------------
        // Rim beacons (mast + glowing tip + a few real lights)
        // -----------------------------------------------------------------

        private static void BuildBeacons(Transform envT, Vector3 center, GarageAmbience ambience)
        {
            Material mastMat = SolidMaterial("GarageBeaconMast", null, SlateLight, ambience);
            Material tipMat = ShieldMaterial("GarageBeaconTip", Cyan, baseAlpha: 0.35f, rimIntensity: 1.8f, ambience);

            var tips = new Renderer[BeaconCount];
            var lights = new Light[BeaconCount];
            var phases = new float[BeaconCount];

            for (int i = 0; i < BeaconCount; i++)
            {
                float ang = i * (Mathf.PI * 2f / BeaconCount);
                var pos = new Vector3(
                    center.x + Mathf.Cos(ang) * BeaconRadius,
                    0f,
                    center.z + Mathf.Sin(ang) * BeaconRadius);

                Transform mast = GetOrCreate(envT, $"Beacon_{i}", PrimitiveType.Cylinder);
                mast.localPosition = pos + new Vector3(0f, 0.9f, 0f);
                mast.localScale = new Vector3(0.12f, 0.9f, 0.12f);
                SetMaterial(mast, mastMat);

                Transform tip = GetOrCreate(mast, "Tip", PrimitiveType.Sphere);
                // Local space: parent is scaled (0.12, 0.9, 0.12), so undo it
                // per-axis to keep the 0.4 m tip spherical.
                tip.localScale = new Vector3(0.4f / 0.12f, 0.4f / 0.9f, 0.4f / 0.12f);
                tip.localPosition = new Vector3(0f, 1.2f, 0f); // world y ≈ 1.98, just atop the 1.8 m mast
                SetMaterial(tip, tipMat);
                tips[i] = tip.GetComponent<Renderer>();
                phases[i] = i * 0.9f;

                // Real lights on every other beacon only — small cyan pools on
                // the platform edge, cheap enough for URP forward limits.
                if (i % 2 == 0)
                {
                    Light l = mast.GetComponent<Light>();
                    if (l == null) l = mast.gameObject.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = Cyan;
                    l.range = 9f;
                    l.intensity = 1f;
                    l.shadows = LightShadows.None;
                    lights[i] = l;
                }
            }

            ambience.BeaconTips = tips;
            ambience.BeaconLights = lights;
            ambience.BeaconPhases = phases;
        }

        // -----------------------------------------------------------------
        // Holo build-pad ring
        // -----------------------------------------------------------------

        private static void BuildHoloRing(Transform envT, Vector3 center, GarageAmbience ambience)
        {
            Transform ring = GetOrCreate(envT, "HoloRing", PrimitiveType.Cylinder);
            ring.localPosition = new Vector3(center.x, 0.18f, center.z);
            ring.localScale = new Vector3(13f, 0.05f, 13f);
            SetMaterial(ring, ShieldMaterial("GarageHoloRing", Cyan, baseAlpha: 0.22f, rimIntensity: 1.2f, ambience));
            ambience.HoloRing = ring;
        }

        // -----------------------------------------------------------------
        // Dust motes
        // -----------------------------------------------------------------

        private static void BuildDust(Transform envT, Vector3 center, GarageAmbience ambience)
        {
            Transform existing = envT.Find("GarageDust");
            if (existing != null) return; // particle config is one-shot; recreate only when absent

            var go = new GameObject("GarageDust");
            go.transform.SetParent(envT, false);
            go.transform.localPosition = new Vector3(center.x, 10f, center.z);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true; // bubble is already dusty when the garage fades in
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            // Sparse / subtle / transparent (session 121 feedback) — the
            // first pass read as cyan confetti.
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f);
            main.maxParticles = 128;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 4f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(50f, 20f, 50f); // stays inside the r~45 bubble

            // Fade in/out so motes never pop.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            // Gentle wander so the drift reads organic, not laminar.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.08f;
            noise.scrollSpeed = 0.04f;

            ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = LightProbeUsage.Off;
            rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Additive unlit squares — hard-edged, palette-locked, same
            // runtime-material idiom as VfxSpawner's billboard pool.
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Particles/Standard Unlit")
                            ?? Shader.Find("Sprites/Default");
            Material dustMat = new Material(shader) { name = "GarageDust" };
            if (dustMat.HasProperty("_Surface")) dustMat.SetFloat("_Surface", 1f); // transparent
            if (dustMat.HasProperty("_Blend")) dustMat.SetFloat("_Blend", 1f);     // additive
            if (dustMat.HasProperty("_BaseColor")) dustMat.SetColor("_BaseColor", Color.white);
            rend.material = dustMat;
            ambience.Owned.Add(dustMat);

            ps.Play(withChildren: true);
        }

        // -----------------------------------------------------------------
        // Asteroid field (outside the bubble)
        // -----------------------------------------------------------------

        private static void BuildAsteroids(Transform envT, Vector3 center, Material floorMat, GarageAmbience ambience)
        {
            const int clusterCount = 7;
            Transform pivot = envT.Find("AsteroidField");
            bool fresh = pivot == null;
            if (fresh)
            {
                var go = new GameObject("AsteroidField");
                pivot = go.transform;
                pivot.SetParent(envT, false);
                pivot.localPosition = new Vector3(center.x, 0f, center.z);
            }

            var clusters = new Transform[clusterCount];
            var axes = new Vector3[clusterCount];
            var speeds = new float[clusterCount];

            // Deterministic layout — same field every visit, no per-session
            // surprise compositions.
            var rng = new System.Random(9341);
            // SlateLight (not Slate): the darker token vanished against the
            // night sky under the dim key light (session 121 feedback).
            Material rockMat = SolidMaterial("GarageAsteroid", floorMat, SlateLight, ambience);

            for (int i = 0; i < clusterCount; i++)
            {
                string name = $"Asteroids_{i}";
                Transform cluster = pivot.Find(name);
                if (cluster == null)
                {
                    var go = new GameObject(name);
                    cluster = go.transform;
                    cluster.SetParent(pivot, false);

                    float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    float dist = Mathf.Lerp(105f, 220f, (float)rng.NextDouble());
                    float height = Mathf.Lerp(-30f, 70f, (float)rng.NextDouble());
                    cluster.localPosition = new Vector3(Mathf.Cos(ang) * dist, height, Mathf.Sin(ang) * dist);

                    // 2–4 overlapping slate cubes per cluster — chunky voxel
                    // rocks that match the game's blocky silhouette language.
                    int rocks = 2 + rng.Next(3);
                    for (int r = 0; r < rocks; r++)
                    {
                        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.name = $"Rock_{r}";
                        Object.Destroy(cube.GetComponent<Collider>());
                        cube.transform.SetParent(cluster, false);
                        float s = Mathf.Lerp(8f, 20f, (float)rng.NextDouble());
                        cube.transform.localScale = new Vector3(
                            s,
                            s * Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble()),
                            s * Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble()));
                        cube.transform.localPosition = new Vector3(
                            (float)(rng.NextDouble() - 0.5) * s * 1.4f,
                            (float)(rng.NextDouble() - 0.5) * s * 1.0f,
                            (float)(rng.NextDouble() - 0.5) * s * 1.4f);
                        cube.transform.localRotation = Quaternion.Euler(
                            (float)rng.NextDouble() * 360f,
                            (float)rng.NextDouble() * 360f,
                            (float)rng.NextDouble() * 360f);
                        var mr = cube.GetComponent<MeshRenderer>();
                        mr.sharedMaterial = rockMat;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }

                clusters[i] = cluster;
                axes[i] = new Vector3(
                    (float)(rng.NextDouble() - 0.5),
                    (float)(rng.NextDouble() - 0.5),
                    (float)(rng.NextDouble() - 0.5)).normalized;
                speeds[i] = Mathf.Lerp(1.5f, 5f, (float)rng.NextDouble());
            }

            ambience.AsteroidPivot = pivot;
            ambience.AsteroidClusters = clusters;
            ambience.ClusterTumbleAxes = axes;
            ambience.ClusterTumbleDegPerSec = speeds;
        }

        // -----------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------

        /// <summary>Find a named child or create it from a primitive (collider stripped, shadows off).</summary>
        private static Transform GetOrCreate(Transform parent, string name, PrimitiveType primitive)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;

            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            go.transform.SetParent(parent, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        private static void SetMaterial(Transform t, Material mat)
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }

        /// <summary>
        /// Opaque palette-tinted material. Clones <paramref name="baseMat"/>
        /// (usually the MK Toon bay floor material) when available so the
        /// decor shades like the rest of the world; URP/Lit otherwise.
        /// </summary>
        private static Material SolidMaterial(string name, Material baseMat, Color color, GarageAmbience ambience)
        {
            Material mat;
            if (baseMat != null)
            {
                mat = new Material(baseMat) { name = name };
            }
            else
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(lit) { name = name };
            }
            TrySetColor(mat, color);
            ambience.Owned.Add(mat);
            return mat;
        }

        /// <summary>
        /// Additive glow material on the owned Robogame/ShieldBubble shader
        /// (loaded via the bubble material so the variant ships with the
        /// build). Used for every emissive accent so glow stays one idiom.
        /// </summary>
        private static Material ShieldMaterial(string name, Color color, float baseAlpha, float rimIntensity, GarageAmbience ambience)
        {
            Material baseMat = Resources.Load<Material>("Garage/Mat_ShieldBubble");
            Material mat;
            if (baseMat != null)
            {
                mat = new Material(baseMat) { name = name };
            }
            else
            {
                Shader sh = Shader.Find("Robogame/ShieldBubble") ?? Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(sh) { name = name };
            }
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_BaseAlpha")) mat.SetFloat("_BaseAlpha", baseAlpha);
            if (mat.HasProperty("_RimIntensity")) mat.SetFloat("_RimIntensity", rimIntensity);
            if (mat.HasProperty("_RimPower")) mat.SetFloat("_RimPower", 1.4f);
            ambience.Owned.Add(mat);
            return mat;
        }

        /// <summary>Set the main texture + tiling across the MK Toon / URP/Lit / legacy property names.</summary>
        private static bool TrySetMainTexture(Material mat, Texture2D tex, float tiling)
        {
            string[] names = { "_AlbedoMap", "_BaseMap", "_MainTex" };
            foreach (string n in names)
            {
                if (!mat.HasProperty(n)) continue;
                mat.SetTexture(n, tex);
                mat.SetTextureScale(n, new Vector2(tiling, tiling));
                return true;
            }
            return false;
        }

        /// <summary>Tint across the MK Toon / URP/Lit / legacy color property names.</summary>
        private static void TrySetColor(Material mat, Color color)
        {
            if (mat.HasProperty("_AlbedoColor")) mat.SetColor("_AlbedoColor", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }
    }
}
