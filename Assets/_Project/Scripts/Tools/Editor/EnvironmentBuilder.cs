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

            // Floor — workshop bay. Session 23 expansion pass: 18 → 40
            // half-extent (80 m × 80 m floor, ~5× area) so the build-mode
            // free-cam has room to fly around even a large chassis. The
            // bot also rises (GarageController._hoverHeightCells 7 → 12)
            // to suit the bigger volume.
            const float halfBay = 40f;
            GameObject floor = MakePlane(env.transform, "Floor", Vector3.zero, scale: halfBay / 5f);
            WorldPalette.Apply(floor, WorldPalette.GarageFloor);

            // Walls — taller ring so the larger floor reads as a real
            // bay, not a flat slab. Roofless so the free-cam can swing
            // overhead.
            const float wallH = 12f, wallT = 0.5f;
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
            Vector3 arenaSunEuler = new Vector3(50f, -30f, 0f);
            EnsureCameraAndLight(
                WorldPalette.ArenaClear,
                lightEuler: arenaSunEuler,
                lightColor: new Color(1f, 0.973f, 0.878f, 1f), // #FFF8E0
                lightIntensity: 1.3f,
                useSkybox: true);
            ConfigureAmbient(skyTop: WorldPalette.SkyDay, equator: new Color(0.353f, 0.431f, 0.502f), ground: WorldPalette.Grass * 0.6f);
            EnsureSceneVolume(PostProcessingBuilder.ArenaProfilePath);

            // Polyverse Skies arena skybox — builder falls back to a
            // procedural sky if the package shader isn't found. Sun
            // direction in the sky is the negation of the directional
            // light's forward vector, so the halo on the skybox sits
            // exactly where the shadows on the ground say it should.
            Vector3 arenaSunDir = -(Quaternion.Euler(arenaSunEuler) * Vector3.forward);
            Material sky = SkyboxBuilder.BuildArenaSkybox(arenaSunDir);
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

            // Wall ring + scenic mountain ring via SceneScaffolder.
            // PopulateTestTerrain creates a loose "Terrain" root; we
            // reparent it under Environment for hierarchy consistency.
            SceneScaffolder.PopulateTestTerrain();
            GameObject terrain = GameObject.Find("Terrain");
            if (terrain != null && terrain.transform.parent == null)
                terrain.transform.SetParent(env.transform, worldPositionStays: true);
            TintTerrain();

            // Phase 3 follow-up: drop a small dig zone in the arena corner
            // so the player can drive a drill onto carveable voxel terrain
            // without leaving the main arena. Surface flush with the
            // hilly ground; perimeter wireframe makes the volume visible.
            BuildArenaDigZone(env.transform);
        }

        /// <summary>
        /// Spawn a small dig zone in the arena. 2×1×2 chunks at default
        /// settings = 32 m × 16 m × 32 m. Positioned 60 m off-spawn with
        /// its half-space surface plane sitting flush with the arena
        /// ground (y = 0).
        /// </summary>
        private static void BuildArenaDigZone(Transform envRoot)
        {
            GameObject zoneObj = new GameObject("DigZone");
            zoneObj.transform.SetParent(envRoot, worldPositionStays: false);
            // Surface plane at world y=0 (chunkSide × gridY × 0.5 below transform).
            zoneObj.transform.position = new Vector3(60f, -8f, 60f);
            zoneObj.SetActive(false);   // configure before Awake.

            Robogame.Voxel.DigZone zone = zoneObj.AddComponent<Robogame.Voxel.DigZone>();
            SerializedObject so = new SerializedObject(zone);
            so.FindProperty("_cellSize").floatValue = 0.5f;
            so.FindProperty("_chunkSizeCells").intValue = 32;
            so.FindProperty("_chunkGridSize").vector3IntValue = new Vector3Int(2, 1, 2);
            so.FindProperty("_chunkMaterial").objectReferenceValue = WorldPalette.ArenaGround;
            // The arena dig zone is small (4 chunks total) — well under
            // the LOD-needed triangle threshold. Disable LOD so all
            // chunks mesh at the full resolution; this eliminates the
            // chunk-boundary seams players were seeing from LOD-mismatch
            // transitions when standing at varying distances from the
            // zone. Phase 4c's transition handling closes the
            // perpendicular axis but in-plane spacing still differs at
            // an LOD seam, which reads visibly on a four-chunk grid.
            so.FindProperty("_enableLod").boolValue = false;
            // Session 81: the arena dig zone is a solid cube of dirt the
            // player tunnels into, not a half-space plane on the ground.
            // Initially this read as just a flat plane slightly above
            // the arena floor because the half-space init only emitted
            // the midplane transition; full-solid + outer-layer-exterior
            // gives a watertight 32×16×32m cube with 6 visible faces.
            so.FindProperty("_initFullySolid").boolValue = true;

            // Phase 5 POI authoring stand-in: carve a small chamber
            // inside the cube so the chaser bot has somewhere to live.
            // With the full-solid init the entire zone interior is
            // solid, so the chamber can sit anywhere inside the 16m of
            // cube depth. Place at world (77, 4, 77) — 12m above the
            // cube's bottom (Y=-8), 4m above the arena floor (Y=0),
            // ~4m below the cube's top (Y=8). Cell-center-aligned to
            // pass the majority-Open occupancy threshold.
            //
            // Wired via the _initialBrushes serialized list — applied
            // at DigZone.EnsureInitialised AFTER SDF seeding and BEFORE
            // the occupancy grid is built.
            SerializedProperty brushes = so.FindProperty("_initialBrushes");
            brushes.arraySize = 1;
            SerializedProperty brush = brushes.GetArrayElementAtIndex(0);
            brush.FindPropertyRelative("Kind").enumValueIndex = (int)Robogame.Core.BrushKind.SphereSubtract;
            Vector3 chamberCenter = new Vector3(77f, 5f, 77f);
            brush.FindPropertyRelative("CenterWorld").vector3Value = chamberCenter;
            brush.FindPropertyRelative("EndWorld").vector3Value = chamberCenter;
            brush.FindPropertyRelative("RadiusMeters").floatValue = 2.5f;

            so.ApplyModifiedPropertiesWithoutUndo();

            zoneObj.SetActive(true);

            // Phase 5 visual playtest gate: drop a VoxelChaserBot inside
            // the pre-carved chamber so the OccupancyGrid + A* pipeline
            // has a visible consumer. The bot can't reach the player
            // until the player drills a connection down through the
            // ~4m of solid above the chamber — at which point A* picks
            // up the new tunnel and the bot pursues.
            BuildArenaDigZoneChaser(zoneObj.transform, chamberCenter);
        }

        private static void BuildArenaDigZoneChaser(Transform digZoneRoot, Vector3 chamberCenter)
        {
            // Spawn inside the pre-carved chamber. Slightly offset above
            // chamber centre so the bot sits on the chamber floor in the
            // OpenWithFloor cell rather than dead-centre in the open
            // volume.
            Vector3 spawn = chamberCenter + new Vector3(0f, 0.5f, 0f);

            GameObject botGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            botGo.name = "VoxelChaserBot";
            botGo.transform.SetParent(digZoneRoot.parent, worldPositionStays: true);
            botGo.transform.position = spawn;
            botGo.transform.localScale = Vector3.one * 1.0f;

            // Strip the auto-added SphereCollider — the bot doesn't
            // participate in physics (kinematic movement only).
            Object.DestroyImmediate(botGo.GetComponent<Collider>());

            // Mark visually distinct via the existing palette's "enemy"
            // accent if available; fall back to a tinted instance of
            // ArenaPillar so the sphere doesn't render with the default
            // diffuse-pink missing-material fallback.
            Material enemyMat = WorldPalette.ArenaPillar;
            if (enemyMat != null)
            {
                Material tinted = new Material(enemyMat);
                tinted.color = new Color(0.85f, 0.25f, 0.20f, 1f);
                if (tinted.HasProperty("_BaseColor"))
                    tinted.SetColor("_BaseColor", new Color(0.85f, 0.25f, 0.20f, 1f));
                botGo.GetComponent<MeshRenderer>().sharedMaterial = tinted;
            }

            botGo.AddComponent<Robogame.Gameplay.VoxelChaserBot>();
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
                // Mountains (session 59) — slate stone tone, same
                // material as the wall ring. Distinct prefix so a future
                // dedicated mountain material doesn't have to dig
                // through the Wall_ namespace.
                else if (n.StartsWith("Mountain_")) mat = WorldPalette.ArenaWall;
                else if (n.StartsWith("Wall_")) mat = WorldPalette.ArenaWall;
                WorldPalette.Apply(child.gameObject, mat);
            }
        }

        // -----------------------------------------------------------------
        // Water arena: open ocean sandbox. No obstacles or props — just a
        // submerged floor, a translucent water plane at y=0, and a wall
        // ring so chassis can't drive past the swim window.
        // -----------------------------------------------------------------

        public static void BuildWaterArenaEnvironment()
        {
            GameObject env = ResetEnvRoot();

            // Same lighting/skybox rig as the combat arena — the player
            // should feel like they've stepped sideways into a different
            // map, not a different world. Slightly cooler ambient sells
            // the "open ocean" mood.
            Vector3 waterSunEuler = new Vector3(50f, -30f, 0f);
            EnsureCameraAndLight(
                WorldPalette.WaterArenaClear,
                lightEuler: waterSunEuler,
                lightColor: new Color(1f, 0.973f, 0.878f, 1f),
                lightIntensity: 1.3f,
                useSkybox: true);
            ConfigureAmbient(
                skyTop: WorldPalette.SkyDay,
                equator: new Color(0.28f, 0.40f, 0.50f),
                ground: WorldPalette.WaterDeep * 0.7f);
            EnsureSceneVolume(PostProcessingBuilder.ArenaProfilePath);

            Vector3 waterSunDir = -(Quaternion.Euler(waterSunEuler) * Vector3.forward);
            Material sky = SkyboxBuilder.BuildArenaSkybox(waterSunDir);
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();

            // Authoring constants. Match the 100m half-extent the combat
            // arena uses for the wall ring so HUD margins / camera feel
            // identical when switching maps.
            const float halfExtent = 100f;
            // Walls span from the floor (y = floorY) to wallTop, so a
            // sunken chassis can't slip under them and escape into the
            // void below the arena.
            const float wallTop = 6f;
            const float wallT = 1f;
            // Floor sits exactly 6 BlockGrid cells (1m each) below the
            // water surface. Deep enough that a sunken chassis fully
            // submerges, shallow enough that the floor is still visible
            // through the translucent surface.
            const float floorY = -6f;
            const float surfaceY = 0f;
            // Derived: total wall height + centre Y for primitives.
            const float wallH = wallTop - floorY;          // 12 m
            const float wallCentreY = (wallTop + floorY) * 0.5f; // 0 m

            // Submerged floor: large flat plane sitting well below the
            // water surface so any sunken chassis lands on something
            // visible. (Plane primitive scale*10m = world size.)
            GameObject floor = MakePlane(env.transform, "WaterFloor",
                new Vector3(0f, floorY, 0f),
                scale: halfExtent / 5f);
            WorldPalette.Apply(floor, WorldPalette.WaterFloor);

            // Wall ring — extends from the submerged floor up to wallTop
            // so blocks can't slip under or fly out the top easily.
            // (Plane chassis can still climb out at high enough altitude;
            // 6m of freeboard is enough for stunt clearance, not enough
            // to escape passively.)
            MakeWall(env.transform, "Wall_N",
                new Vector3(0f, wallCentreY,  halfExtent),
                new Vector3(halfExtent * 2f, wallH, wallT));
            MakeWall(env.transform, "Wall_S",
                new Vector3(0f, wallCentreY, -halfExtent),
                new Vector3(halfExtent * 2f, wallH, wallT));
            MakeWall(env.transform, "Wall_E",
                new Vector3( halfExtent, wallCentreY, 0f),
                new Vector3(wallT, wallH, halfExtent * 2f));
            MakeWall(env.transform, "Wall_W",
                new Vector3(-halfExtent, wallCentreY, 0f),
                new Vector3(wallT, wallH, halfExtent * 2f));

            // Water surface: tessellated procedural mesh that animates each
            // frame to match WaterSurface.SampleHeight. The mesh is built
            // by WaterMeshAnimator at runtime so this builder just spawns
            // the host GameObject and stamps the right components on it.
            // No Plane primitive (we'd just delete its mesh anyway).
            GameObject water = new GameObject("Water");
            water.transform.SetParent(env.transform, worldPositionStays: false);
            water.transform.position = new Vector3(0f, surfaceY, 0f);
            water.AddComponent<MeshFilter>();
            water.AddComponent<MeshRenderer>();
            WorldPalette.Apply(water, WorldPalette.WaterMat);
            // WaterVolume is the data marker BuoyancyController resolves;
            // WaterMeshAnimator builds the visible mesh and shares the same
            // SurfaceY by RequireComponent-binding to WaterVolume.
            water.AddComponent<Robogame.Gameplay.WaterVolume>();
            var anim = water.AddComponent<Robogame.Gameplay.WaterMeshAnimator>();
            // Match the wall ring so the mesh fills the bay exactly.
            var animSO = new UnityEditor.SerializedObject(anim);
            animSO.FindProperty("_size").floatValue = halfExtent * 2f;
            animSO.ApplyModifiedPropertiesWithoutUndo();
        }

        // -----------------------------------------------------------------
        // Planet arena: a sphere planet at world origin with custom
        // spherical gravity. v1 sketch \u2014 see
        // [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md)
        // for the full plan and the reasons this is intentionally rough.
        // -----------------------------------------------------------------

        public static void BuildPlanetArenaEnvironment()
        {
            // 2400 m sits comfortably inside the comfort window from
            // SPHERICAL_ARENAS.md §9: ~0.3 °/s camera-up rotation at
            // ground speed, ~2.3° horizon dip, ~19 minute lap. 20% smaller
            // diameter than 3000 m makes the world feel a bit more like a
            // playground without crossing into marble territory.
            const float planetRadius = 1680f;

            GameObject env = ResetEnvRoot();

            // Lighting: same raked-sun rig as the combat arena so the
            // planet's day/night terminator is visible from the spawn pole.
            // CLEAR colour stays solid (no skybox) per the brief.
            Vector3 sunEuler = new Vector3(50f, -30f, 0f);

            // Uniform-color skybox: just a flat clear color on the camera,
            // no Polyverse skybox material. SkyDay (#8CB7E0) is the same
            // tint the arena skybox tops out at, so the planet reads as
            // sitting in "sky" rather than a grey void.
            EnsureCameraAndLight(
                WorldPalette.SkyDay,
                lightEuler: sunEuler,
                lightColor: new Color(1f, 0.973f, 0.878f, 1f),
                lightIntensity: 1.3f,
                useSkybox: false);
            ConfigureAmbient(
                skyTop: WorldPalette.SkyDay,
                equator: new Color(0.353f, 0.431f, 0.502f),
                ground: WorldPalette.Grass * 0.6f);
            EnsureSceneVolume(PostProcessingBuilder.ArenaProfilePath);

            // Strip any skybox the previous scene wired in \u2014 the camera
            // clears to a flat colour by itself.
            RenderSettings.skybox = null;
            DynamicGI.UpdateEnvironment();

            // Camera: position above the north pole looking down-ish so the
            // chassis spawn site is centred when the scene first opens.
            // FollowCamera takes over at runtime.
            GameObject camGO = GameObject.Find("Main Camera");
            if (camGO != null)
            {
                camGO.transform.position = new Vector3(0f, planetRadius + 18f, -10f);
                camGO.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            }

            // Visible + collidable planet mesh. We deliberately do NOT use
            // Unity's primitive Sphere here:
            //   * its mesh is ~80 tris, which reads as "20-sided die" at
            //     1.5 km radius;
            //   * worse, the primitive ships with a SphereCollider on the
            //     mathematically perfect sphere, while the visual faces
            //     dip inward between vertices \u2014 so a chassis lands on
            //     the collider and visibly floats above the visual dips.
            // Instead we generate a subdivision-5 icosphere (20 480 tris,
            // ~22 cm max chord deviation at this radius) and use the SAME
            // mesh for the MeshCollider. Visual triangles == collision
            // triangles, so the float-above-the-dips bug is gone.
            // Smooth normals (vertex normal = unit position) hide the
            // tessellation under shading.
            GameObject planetGO = new GameObject("Planet");
            planetGO.transform.SetParent(env.transform, worldPositionStays: false);
            planetGO.transform.position = Vector3.zero;
            planetGO.transform.localScale = Vector3.one * planetRadius; // unit mesh \u2192 world radius
            // Wire the gravity component FIRST, before any mesh / collider /
            // material work. If a later step throws (PhysX cook on a large
            // MeshCollider, Fluff material rebuild, etc.), the partially
            // built planet still has its PlanetBody so PlanetArenaController
            // can find it on Play, instead of stranding the user with a
            // visible sphere they can't spawn onto.
            var planet = planetGO.AddComponent<Robogame.Gameplay.PlanetBody>();
            var planetSO = new SerializedObject(planet);
            SerializedProperty radiusProp = planetSO.FindProperty("_radius");
            if (radiusProp != null) radiusProp.floatValue = planetRadius;
            planetSO.ApplyModifiedPropertiesWithoutUndo();
            // Subdivision-5 icosphere (20 480 tris, ~36 cm chord deviation
            // at 2400 m radius). Sub-6 looks marginally nicer at the
            // silhouette but the larger static MeshCollider's PhysX cook
            // throws on some Unity versions; the resulting hidden
            // exception was leaving scaffolded scenes without a
            // PlanetBody (PlanetArenaController then fails to find one
            // and the chassis won't spawn). Distance fog (below) does
            // most of the silhouette-smoothing work the extra triangles
            // would have, for free.
            //
            // Terrain: per-vertex radial displacement via PlanetTerrain's
            // noise field. Same sampler is used by the landmark pylons
            // below so they sit on the displaced surface, not the
            // baseline sphere. The displacement is in mesh-local units
            // (radius == 1) so we hand it the radius up front and
            // normalise the result there.
            // Terrain disabled: smooth icosphere only. PlanetTerrain code is
            // kept around (and Settings still passed to BuildPlanetLandmarks)
            // but with zero amplitudes the sampler returns 0 everywhere, so
            // pylons sit on the bare sphere.
            PlanetTerrain.Settings terrain = PlanetTerrain.Settings.Flat(planetRadius);
            Mesh planetMesh = IcosphereBuilder.Build(
                subdivisions: 5,
                name: "PlanetMesh");
            var mf = planetGO.AddComponent<MeshFilter>();
            mf.sharedMesh = planetMesh;
            var mr = planetGO.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            // MeshCollider on the same icosphere. Static (no Rigidbody),
            // non-convex, sub-5 tri count is well within PhysX's comfort
            // zone for a single static collider.
            var planetCol = planetGO.AddComponent<MeshCollider>();
            planetCol.sharedMesh = planetMesh;
            planetCol.convex = false;

            // Try the Fluff shell-grass shader first (the same path the
            // flat arena's ground uses). Fluff projects shells along
            // vertex normals, so it works correctly on a sphere \u2014 every
            // surface point gets its grass blades pointing outward. If
            // the package is missing, FluffGround.ApplyToGround falls
            // back to a procedural tile texture, which we then overwrite
            // with the palette-locked Mat_ArenaGround so the planet at
            // least stays the right shade of green.
            bool fluff = FluffGround.ApplyToGround(planetGO);
            if (!fluff)
            {
                // Procedural tile texture won't tile cleanly on a sphere
                // (UV seam at the poles); replace with the flat lit grass.
                if (mr != null) mr.sharedMaterial = WorldPalette.ArenaGround;
            }

            // Horizon haze. Smooth normals fix the interior shading on a
            // faceted sphere, but the *silhouette* is still polygon-by-
            // polygon — every triangle edge along the limb shows up as
            // a hard line against the flat sky colour. The standard trick
            // (Outer Wilds, BotW, Mario Galaxy, Astroneer) is linear fog
            // tuned so the silhouette fades into the sky a bit before the
            // visible horizon: where geometry meets sky in the framebuffer,
            // fog blends mesh → fog colour, and if fog colour == sky
            // colour the edges dissolve instead of stair-stepping.
            //
            // Tuning: horizon distance from a ground chassis (eye ≈2 m
            // above surface) is √(2·r·h) ≈ 100 m at 2400 m radius. From
            // a plane at 18 m altitude it's ~290 m. We want fog to be
            // negligible up to the *near* horizon and fully opaque well
            // before the chassis can see anything past it, so fog 0–1 km
            // covers both ground and air play without making nearby decor
            // feel hazy.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = WorldPalette.SkyDay;
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 1000f;

            // Landmark pylons. Static decor only \u2014 these aren't combat
            // dummies (a real Robot needs spherical-locomotion AI we
            // haven't built yet, and a kinematic Robot would slide off
            // the cap on frame 1). They exist purely to give the player
            // something to fly toward + figure out what direction they're
            // heading. Replace with actual dummies once Phase A locomotion
            // lands.
            BuildPlanetLandmarks(env.transform, planet, terrain, count: 14);
        }

        /// <summary>
        /// Scatter <paramref name="count"/> brightly-coloured pylons across
        /// the planet's surface using a Fibonacci-sphere distribution.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why Fibonacci sphere:</b> spreading N points by golden-angle
        /// increments around the unit sphere gives near-uniform spacing
        /// without any of the pole-clustering that uniform lat/lon
        /// produces, and is fully deterministic so re-scaffolding lands
        /// pylons in the same spots every time. The placement formula is
        /// the standard one (e.g. <i>Saff &amp; Kuijlaars</i>, 1997):
        /// </para>
        /// <code>
        ///   y = 1 - 2*(i + 0.5)/N
        ///   r = sqrt(1 - y\u00b2)
        ///   \u03c6 = i * \u03c0 * (3 - sqrt(5))   // golden angle
        ///   p = (cos(\u03c6)*r, y, sin(\u03c6)*r)
        /// </code>
        /// <para>
        /// We then skip any sample whose latitude is within ~25\u00b0 of the
        /// north pole (where the chassis spawns) so the player isn't
        /// staring at a pylon at frame 1. Each pylon is built unit-scale
        /// then oriented so its local up matches the surface normal at
        /// that point \u2014 same trick the chassis spawn uses.
        /// </para>
        /// </remarks>
        private static void BuildPlanetLandmarks(
            Transform envRoot,
            Robogame.Gameplay.PlanetBody planet,
            PlanetTerrain.Settings terrain,
            int count)
        {
            if (planet == null || envRoot == null) return;

            GameObject root = new GameObject("Landmarks");
            root.transform.SetParent(envRoot, worldPositionStays: false);

            // Build the four landmark materials once (palette tokens, so
            // they read as authored set dressing rather than test colour).
            // We rotate through them as we walk the Fibonacci sequence so
            // the player can use colour as a coarse "which pylon is that"
            // cue without a minimap.
            Material[] palette = new[]
            {
                WorldPalette.ArenaPillar,  // alert red
                WorldPalette.ArenaRamp,    // hazard orange
                WorldPalette.ArenaBump,    // caution yellow
                WorldPalette.ArenaStair,   // mint green
            };

            // Tunables. Pylon body height scales with planet radius so it
            // stays a few-second drive away on any radius.
            float radius = planet.Radius;
            float bodyHeight = Mathf.Clamp(radius * 0.012f, 12f, 40f); // ~29 m at 2400
            float bodyWidth  = bodyHeight * 0.30f;
            float capRadius  = bodyWidth * 0.95f;
            const float spawnExclusionDeg = 25f;

            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                // Sample the unit sphere.
                float yUnit = 1f - 2f * ((i + 0.5f) / count);
                float rUnit = Mathf.Sqrt(Mathf.Max(0f, 1f - yUnit * yUnit));
                float phi   = i * goldenAngle;
                Vector3 normal = new Vector3(
                    Mathf.Cos(phi) * rUnit,
                    yUnit,
                    Mathf.Sin(phi) * rUnit);

                // Skip the spawn cap. yUnit == cos(latitude from north pole),
                // so cos(25\u00b0) \u2248 0.906 means anything above that latitude
                // band lands too close to the chassis spawn.
                if (yUnit > Mathf.Cos(spawnExclusionDeg * Mathf.Deg2Rad)) continue;

                // Sit on the displaced surface, not the baseline sphere.
                float h = PlanetTerrain.SampleHeight(normal, terrain);
                Vector3 surface = planet.Center + normal * (radius + h);
                BuildPylon(
                    parent: root.transform,
                    surfacePos: surface,
                    surfaceNormal: normal,
                    bodyMat: palette[placed % palette.Length],
                    capMat: WorldPalette.GarageAccent, // bright orange tip on every pylon
                    bodyHeight: bodyHeight,
                    bodyWidth: bodyWidth,
                    capRadius: capRadius,
                    index: placed);
                placed++;
            }
        }

        private static void BuildPylon(
            Transform parent, Vector3 surfacePos, Vector3 surfaceNormal,
            Material bodyMat, Material capMat,
            float bodyHeight, float bodyWidth, float capRadius, int index)
        {
            // Container so the cube + sphere transform together. Identity
            // scale on the parent keeps the children's MeshColliders from
            // inheriting non-uniform scale (which would force a convex
            // baking step and cost cook time).
            GameObject pylon = new GameObject($"Pylon_{index:D2}");
            pylon.transform.SetParent(parent, worldPositionStays: false);

            // Orient: local up == surface normal. Quaternion.LookRotation
            // wants a forward vector; we pick any tangent vector by
            // crossing normal with world-up. If they're parallel (north
            // pole \u2014 already excluded but defensive), fall back to world-X.
            Vector3 tangent = Vector3.Cross(surfaceNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.Cross(surfaceNormal, Vector3.right);
            tangent.Normalize();
            pylon.transform.SetPositionAndRotation(
                surfacePos,
                Quaternion.LookRotation(tangent, surfaceNormal));

            // Body cube. Pivot at base, so we offset up by half-height.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(pylon.transform, worldPositionStays: false);
            body.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(bodyWidth, bodyHeight, bodyWidth);
            var bodyMr = body.GetComponent<MeshRenderer>();
            if (bodyMr != null) bodyMr.sharedMaterial = bodyMat;

            // Cap sphere on top \u2014 universal "this is a marker" reading.
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cap.name = "Cap";
            cap.transform.SetParent(pylon.transform, worldPositionStays: false);
            cap.transform.localPosition = new Vector3(0f, bodyHeight + capRadius * 0.4f, 0f);
            cap.transform.localScale = Vector3.one * (capRadius * 2f);
            var capMr = cap.GetComponent<MeshRenderer>();
            if (capMr != null) capMr.sharedMaterial = capMat;
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

            // Defensive scrub against any Kenney-FBX instances left
            // over from a long-retired Kenney-asset experiment. Pure
            // safety net for stale scenes; the scrub does nothing if no
            // Kenney instances exist.
            ScrubKenneyInstances();

            return new GameObject(EnvRoot);
        }

        /// <summary>
        /// Walk every root GameObject in the active scene and destroy any
        /// subtree containing a MeshFilter whose shared mesh was imported
        /// from a Kenney FBX. Bombproof cleanup for any stale scene that
        /// still references a Kenney mesh (the scaffolder that authored
        /// those is gone; this stays as defence against pre-cleanup
        /// scenes). It doesn't matter what the parent name is, what scale
        /// it has, or how it was nested — if a mesh in the subtree came
        /// from <c>Assets/_Project/Art/ThirdParty/kenney_*</c>, the whole
        /// instance dies.
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
