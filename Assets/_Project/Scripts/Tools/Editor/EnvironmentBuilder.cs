using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Drops the static decor (ground, walls, props, lighting, camera clear
    /// colour) for each named scene. Garage and Arena get visually distinct
    /// rigs so the player has an obvious sense of "where am I".
    /// </summary>
    /// <remarks>
    /// Idempotent: each call destroys the previous "Environment" parent
    /// before rebuilding, so re-running never duplicates geometry.
    /// </remarks>
    internal static class EnvironmentBuilder
    {
        private const string EnvRoot = "Environment";

        // -----------------------------------------------------------------
        // Garage: small enclosed bay with a turntable podium for the chassis.
        // -----------------------------------------------------------------

        public static void BuildGarageEnvironment()
        {
            // Garage: warm dusk-workshop. Numbers from ART_DIRECTION.md § Lighting Rules.
            EnsureCameraAndLight(
                WorldPalette.GarageClear,
                lightEuler: new Vector3(45f, 30f, 0f),
                lightColor: new Color(1f, 0.878f, 0.690f, 1f), // #FFE0B0
                lightIntensity: 1.0f,
                useSkybox: false);
            ConfigureAmbient(skyTop: WorldPalette.GarageClear, equator: new Color(0.165f, 0.125f, 0.153f), ground: new Color(0.06f, 0.05f, 0.06f));
            EnsureSceneVolume(PostProcessingBuilder.GarageProfilePath);
            // No skybox in the garage — pure dark clear matches the
            // "only the chassis is bright" mood.
            RenderSettings.skybox = null;
            DynamicGI.UpdateEnvironment();

            GameObject env = ResetEnvRoot();

            // Floor — small, dark.
            const float halfBay = 9f;
            GameObject floor = MakePlane(env.transform, "Floor", Vector3.zero, scale: halfBay / 5f);
            WorldPalette.Apply(floor, WorldPalette.GarageFloor);

            // Walls — short box ring, no roof so the camera can swing around.
            const float wallH = 4f, wallT = 0.5f;
            MakeWall(env.transform, "Wall_N", new Vector3(0f, wallH * 0.5f,  halfBay), new Vector3(halfBay * 2f, wallH, wallT));
            MakeWall(env.transform, "Wall_S", new Vector3(0f, wallH * 0.5f, -halfBay), new Vector3(halfBay * 2f, wallH, wallT));
            MakeWall(env.transform, "Wall_E", new Vector3( halfBay, wallH * 0.5f, 0f), new Vector3(wallT, wallH, halfBay * 2f));
            MakeWall(env.transform, "Wall_W", new Vector3(-halfBay, wallH * 0.5f, 0f), new Vector3(wallT, wallH, halfBay * 2f));

            // Hazard stripes — flat boxes flush with the floor, framing the spawn pad.
            const float padHalf = 3f, stripeT = 0.04f, stripeW = 0.4f;
            MakeAccent(env.transform, "Stripe_N", new Vector3(0f, stripeT, padHalf),  new Vector3(padHalf * 2f, stripeT, stripeW));
            MakeAccent(env.transform, "Stripe_S", new Vector3(0f, stripeT, -padHalf), new Vector3(padHalf * 2f, stripeT, stripeW));
            MakeAccent(env.transform, "Stripe_E", new Vector3( padHalf, stripeT, 0f), new Vector3(stripeW, stripeT, padHalf * 2f));
            MakeAccent(env.transform, "Stripe_W", new Vector3(-padHalf, stripeT, 0f), new Vector3(stripeW, stripeT, padHalf * 2f));

            // Podium — short cylinder beneath the spawn point so the chassis
            // looks staged rather than dropped.
            GameObject podium = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            podium.name = "Podium";
            podium.transform.SetParent(env.transform, worldPositionStays: false);
            podium.transform.position = new Vector3(0f, 0.05f, 0f);
            podium.transform.localScale = new Vector3(2.4f, 0.05f, 2.4f);
            WorldPalette.Apply(podium, WorldPalette.GaragePodium);
        }

        // -----------------------------------------------------------------
        // Arena: large open field + the obstacle course.
        // -----------------------------------------------------------------

        public static void BuildArenaEnvironment()
        {
            // Single shared clean-slate primitive: kills the previous
            // Environment root, any stale loose Ground/Terrain, and any
            // Kenney mesh-kit instances left over from prior experiments.
            // Both Garage and Arena go through ResetEnvRoot — keeps the
            // "how do we reset a scene?" answer in exactly one place.
            GameObject env = ResetEnvRoot();

            // Arena: bright, raked sun, cool ambient. Numbers from ART_DIRECTION.md.
            EnsureCameraAndLight(
                WorldPalette.ArenaClear,
                lightEuler: new Vector3(50f, -30f, 0f),
                lightColor: new Color(1f, 0.973f, 0.878f, 1f), // #FFF8E0
                lightIntensity: 1.3f,
                useSkybox: true);
            ConfigureAmbient(skyTop: WorldPalette.SkyDay, equator: new Color(0.353f, 0.431f, 0.502f), ground: WorldPalette.Grass * 0.6f);
            EnsureSceneVolume(PostProcessingBuilder.ArenaProfilePath);

            // Polyverse Skies arena skybox — builder falls back to a
            // procedural sky if the package shader isn't found.
            Material sky = SkyboxBuilder.BuildArenaSkybox();
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();

            // Ground: subdivided 220m mesh with gentle Perlin hills
            // (HillsGround). Central spawn zone is flat so the obstacle
            // course doesn't fight the topology, and the boundary ramps
            // back to flat by ±100m so the wall ring sits on level
            // ground. FluffGround then swaps the ground's material for
            // OccaSoftware Fluff's shell-based grass shader (palette-
            // tinted), or falls back to GroundMaterial's procedural tile
            // texture if the package is missing. Both layers are
            // palette-locked to WorldPalette.Grass.
            GameObject ground = HillsGround.Build(env.transform, "Ground");
            FluffGround.ApplyToGround(ground);

            // Re-use the primitive obstacle course populator + palette
            // tint. PopulateTestTerrain creates a loose "Terrain" root;
            // we reparent it under Environment for hierarchy consistency.
            // (We previously experimented with Kenney mesh kits here —
            // see ArenaBuilder.cs / KenneyKit.cs — but rolled back to
            // geometric primitives until the asset pipeline is sorted.
            // ART_DIRECTION.md § "Verifying authored size".)
            SceneScaffolder.PopulateTestTerrain();
            GameObject terrain = GameObject.Find("Terrain");
            if (terrain != null && terrain.transform.parent == null)
                terrain.transform.SetParent(env.transform, worldPositionStays: true);
            TintTerrain();
        }

        private static void TintTerrain()
        {
            GameObject terrain = GameObject.Find("Terrain");
            if (terrain == null) return;

            foreach (Transform child in terrain.transform)
            {
                string n = child.name;
                Material mat = WorldPalette.ArenaWall; // default fallback
                if (n.StartsWith("Ramp_")) mat = WorldPalette.ArenaRamp;
                else if (n.StartsWith("Bump_")) mat = WorldPalette.ArenaBump;
                else if (n.StartsWith("Stair_")) mat = WorldPalette.ArenaStair;
                else if (n.StartsWith("Pillar_")) mat = WorldPalette.ArenaPillar;
                else if (n.StartsWith("Wall_")) mat = WorldPalette.ArenaWall;
                WorldPalette.Apply(child.gameObject, mat);
            }
        }

        // -----------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Canonical clean-slate primitive for every scene builder.
        /// Destroys the previous <c>Environment</c> root, any stray
        /// loose <c>Ground</c> / <c>Terrain</c> from earlier scaffolds,
        /// and any subtree containing a Kenney FBX mesh (see
        /// <see cref="ScrubKenneyInstances"/>). Returns a fresh empty
        /// <c>Environment</c> GameObject that the caller should parent
        /// every piece of static decor under.
        /// </summary>
        /// <remarks>
        /// Best practice: <b>do not</b> reach past this and find/destroy
        /// individual GameObjects from a scene-specific builder — add
        /// the new cleanup case here so all scenes inherit it. Likewise,
        /// every static piece of decor a builder creates should land
        /// under the returned env transform, never loose at scene root.
        /// That keeps the hierarchy readable and makes the next cleanup
        /// trivial.
        /// </remarks>
        private static GameObject ResetEnvRoot()
        {
            GameObject existing = GameObject.Find(EnvRoot);
            if (existing != null) Object.DestroyImmediate(existing);

            // Stale loose decor from prior scaffolds (pre-refactor scenes
            // had Ground / Terrain at scene root rather than under env).
            GameObject oldGround = GameObject.Find("Ground");
            if (oldGround != null) Object.DestroyImmediate(oldGround);
            GameObject oldTerrain = GameObject.Find("Terrain");
            if (oldTerrain != null) Object.DestroyImmediate(oldTerrain);

            // Kenney mesh-kit instances from the short ArenaBuilder
            // experiment. Bombproof scrub: any subtree containing a
            // mesh imported from Assets/_Project/Art/ThirdParty/kenney_*
            // dies, regardless of name or parenting.
            ScrubKenneyInstances();

            return new GameObject(EnvRoot);
        }

        /// <summary>
        /// Walk every root GameObject in the active scene and destroy any
        /// subtree containing a MeshFilter whose shared mesh was imported
        /// from a Kenney FBX. This is the bombproof cleanup for the
        /// short-lived ArenaBuilder experiment: it doesn't matter what
        /// the parent name is, what scale it has, or how it was nested —
        /// if a mesh in the subtree came from <c>Assets/_Project/Art/
        /// ThirdParty/kenney_*</c>, the whole instance dies.
        /// </summary>
        /// <remarks>
        /// Cheap (one AssetDatabase.GetAssetPath per unique mesh,
        /// cached). Idempotent. Safe to call when no Kenney instances
        /// exist — early-outs after the scene scan.
        /// </remarks>
        private static void ScrubKenneyInstances()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            var roots = scene.GetRootGameObjects();
            int killed = 0;

            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                if (SubtreeContainsKenneyMesh(root))
                {
                    Object.DestroyImmediate(root);
                    killed++;
                }
            }

            if (killed > 0)
                Debug.Log($"[Robogame] EnvironmentBuilder: scrubbed {killed} Kenney-sourced root(s) from {scene.name}.");
        }

        private static bool SubtreeContainsKenneyMesh(GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            foreach (var mf in filters)
            {
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                string path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path)) continue;

                // Match anything imported from one of our Kenney kits.
                if (path.Contains("/kenney_", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static GameObject MakePlane(Transform parent, string name, Vector3 pos, float scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(scale, 1f, scale);
            return go;
        }

        private static void MakeWall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            WorldPalette.Apply(go, WorldPalette.GarageWall);
        }

        private static void MakeAccent(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            // Strip collider — these are decorative stripes.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            WorldPalette.Apply(go, WorldPalette.GarageAccent);
        }

        private static void EnsureCameraAndLight(Color clearColor, Vector3 lightEuler, Color lightColor, float lightIntensity, bool useSkybox)
        {
            GameObject camGO = GameObject.Find("Main Camera");
            if (camGO == null)
            {
                camGO = new GameObject("Main Camera");
                camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
                camGO.tag = "MainCamera";
            }
            camGO.transform.position = new Vector3(0f, 8f, -10f);
            camGO.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            Camera cam = camGO.GetComponent<Camera>();
            // If a skybox is wired we MUST clear to Skybox or the camera
            // never samples it. Garage has no skybox so it stays solid.
            cam.clearFlags = useSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = clearColor;

            GameObject light = ScaffoldUtils.GetOrCreate("Directional Light");
            var lightComp = light.GetComponent<Light>();
            if (lightComp == null) lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.color = lightColor;
            lightComp.intensity = lightIntensity;
            lightComp.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(lightEuler);
        }

        // -----------------------------------------------------------------
        // Ambient + Volume hookups
        // -----------------------------------------------------------------

        /// <summary>
        /// Switch ambient mode to trilight (sky/equator/ground) and push
        /// the supplied palette colours. Trilight is dirt-cheap and gives
        /// us a stylized fake-bounce we'd otherwise need a probe for.
        /// </summary>
        private static void ConfigureAmbient(Color skyTop, Color equator, Color ground)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyTop;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = 1f;
        }

        /// <summary>
        /// Drop a global <see cref="Volume"/> into the scene wired to the
        /// supplied profile path. Loads the profile by path right before
        /// assignment to dodge the AssetDatabase fake-null pattern
        /// documented in CHANGES.md.
        /// </summary>
        private static void EnsureSceneVolume(string profilePath)
        {
            GameObject volGO = ScaffoldUtils.GetOrCreate("PostProcess Volume");
            Volume vol = volGO.GetComponent<Volume>();
            if (vol == null) vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.weight = 1f;

            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profilePath);
            if (profile == null)
            {
                Debug.LogWarning(
                    $"[Robogame] EnsureSceneVolume: profile not found at {profilePath}. " +
                    "Run PostProcessingBuilder.BuildAll() (Pass A scaffolder hooks this for you).");
                return;
            }
            vol.sharedProfile = profile;
        }
    }
}
