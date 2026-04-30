using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Player;
using Robogame.Robots;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor menu commands that build out our standard scenes from scratch.
    /// Block layouts live in <see cref="RobotLayouts"/>; per-component
    /// wiring lives in <see cref="ScaffoldHelpers"/>; tuning data lives in
    /// ScriptableObject assets created via <see cref="TuningAssets"/>.
    /// </summary>
    public static class SceneScaffolder
    {
        private const string MenuRoot = "Robogame/Scaffold/";

        // -----------------------------------------------------------------
        // Bootstrap scene
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Bootstrap Scene")]
        public static void BuildBootstrap()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.BootstrapScene);

            GameObject bootstrap = ScaffoldUtils.GetOrCreate("Bootstrap");
            var component = bootstrap.GetComponent<GameBootstrap>();
            if (component == null) component = bootstrap.AddComponent<GameBootstrap>();

            SerializedObject so = new SerializedObject(component);
            so.FindProperty("_firstScene").stringValue = "Garage";
            so.FindProperty("_persistAcrossScenes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Bootstrap.unity.");
        }

        // -----------------------------------------------------------------
        // Simple garage (legacy single-cube player)
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Garage")]
        public static void BuildTestGarage()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            EnsureGround();
            EnsureCamera();
            EnsureLight();

            GameObject player = ScaffoldUtils.GetOrCreate(
                "Player",
                () => GameObject.CreatePrimitive(PrimitiveType.Cube));
            player.transform.position = new Vector3(0f, 1f, 0f);
            player.transform.localScale = Vector3.one;

            ScaffoldHelpers.EnsureComponent<Rigidbody>(player);
            ScaffoldHelpers.EnsureComponent<RobotDrive>(player);
            ScaffoldHelpers.EnsureComponent<GroundDriveSubsystem>(player);
            ScaffoldHelpers.WirePlayerInput(player);
            ScaffoldHelpers.EnsureComponent<PlayerController>(player);

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Garage.unity test scene.");
        }

        // -----------------------------------------------------------------
        // Test robot
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Robot")]
        public static void BuildTestRobot()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            EnsureGround();
            EnsureCamera();
            EnsureLight();
            PopulateTestTerrain();

            ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");

            GameObject robotGO = ScaffoldUtils.GetOrCreate("Robot");
            Robot robot = RobotLayouts.PopulateTestRobot(robotGO);

            ScaffoldHelpers.EnsureDevHud();
            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built Garage.unity with test robot. " +
                      $"Blocks: {robot.BlockCount}, CPU: {robot.TotalCpu}, Mass: {robot.TotalBlockMass}");
        }

        // -----------------------------------------------------------------
        // Test plane
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Plane")]
        public static void BuildTestPlane()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            EnsureGround();
            EnsureCamera();
            EnsureLight();
            PopulateTestTerrain();

            ScaffoldHelpers.ClearPlayerChassis(keepName: "Plane");

            GameObject planeGO = ScaffoldUtils.GetOrCreate("Plane");
            Robot plane = RobotLayouts.PopulateTestPlane(planeGO);

            ScaffoldHelpers.EnsureDevHud();
            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built Garage.unity with test plane. " +
                      $"Blocks: {plane.BlockCount}, CPU: {plane.TotalCpu}, Mass: {plane.TotalBlockMass}");
        }

        // -----------------------------------------------------------------
        // Combat dummy
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Combat Dummy")]
        public static void BuildCombatDummy()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            EnsureGround();
            EnsureCamera();
            EnsureLight();

            GameObject dummyGO = ScaffoldUtils.GetOrCreate("CombatDummy");
            Robot dummy = RobotLayouts.PopulateCombatDummy(dummyGO);

            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built combat dummy. Blocks: {dummy.BlockCount}, Mass: {dummy.TotalBlockMass}");
        }

        // -----------------------------------------------------------------
        // Build all
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build All && Configure")]
        public static void BuildAll()
        {
            BuildBootstrap();
            BuildTestRobot();
            BuildCombatDummy();
            BuildSettingsConfigurator.SyncSceneList();
            Debug.Log("[Robogame] Scaffold complete. Open Bootstrap.unity and press Play.");
        }

        // -----------------------------------------------------------------
        // Test terrain
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Terrain")]
        public static void BuildTestTerrain()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);
            EnsureGround();
            EnsureCamera();
            EnsureLight();
            PopulateTestTerrain();
            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Test terrain built.");
        }

        /// <summary>
        /// Drop a small obstacle course around the origin. Idempotent:
        /// nukes the previous "Terrain" parent and rebuilds.
        /// </summary>
        public static void PopulateTestTerrain()
        {
            GameObject root = GameObject.Find("Terrain");
            if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("Terrain");

            // Ramps at four pitches, fan out behind the spawn (-Z).
            float[] pitches = { 8f, 15f, 25f, 35f };
            for (int i = 0; i < pitches.Length; i++)
            {
                float ang = pitches[i];
                Vector3 pos = new Vector3(-12f + i * 6f, 0f, -16f);
                MakeRamp(root.transform, pos, ang, size: new Vector3(4f, 0.5f, 8f), name: $"Ramp_{ang:00}deg");
            }

            // Bump strip directly ahead (+Z).
            for (int i = 0; i < 6; i++)
            {
                MakeBox(root.transform,
                    pos: new Vector3(-3f + i * 1.2f, 0.15f, 12f),
                    size: new Vector3(1f, 0.3f, 0.6f),
                    name: $"Bump_{i}");
            }

            // Stair step on the right (+X).
            for (int i = 0; i < 4; i++)
            {
                float h = 0.4f * (i + 1);
                MakeBox(root.transform,
                    pos: new Vector3(15f, h * 0.5f, -2f + i * 1.2f),
                    size: new Vector3(4f, h, 1.2f),
                    name: $"Stair_{i}");
            }

            // Free-standing pillars.
            MakeBox(root.transform, new Vector3(-8f, 1.5f, 6f), new Vector3(1f, 3f, 1f), "Pillar_A");
            MakeBox(root.transform, new Vector3(-6f, 1.5f, 9f), new Vector3(1f, 3f, 1f), "Pillar_B");
            MakeBox(root.transform, new Vector3(10f, 1.5f, 8f), new Vector3(1f, 3f, 1f), "Pillar_C");

            // Boundary wall ring. Pushed well beyond the obstacle course
            // so the arena reads as a large open field with the course in
            // the centre. Walls are taller too so flyers don't trivially
            // clear them.
            const float arenaHalf = 100f;
            const float wallH = 4f, wallT = 1f;
            MakeBox(root.transform, new Vector3(0f, wallH * 0.5f,  arenaHalf), new Vector3(arenaHalf * 2f, wallH, wallT), "Wall_N");
            MakeBox(root.transform, new Vector3(0f, wallH * 0.5f, -arenaHalf), new Vector3(arenaHalf * 2f, wallH, wallT), "Wall_S");
            MakeBox(root.transform, new Vector3( arenaHalf, wallH * 0.5f, 0f), new Vector3(wallT, wallH, arenaHalf * 2f), "Wall_E");
            MakeBox(root.transform, new Vector3(-arenaHalf, wallH * 0.5f, 0f), new Vector3(wallT, wallH, arenaHalf * 2f), "Wall_W");
        }

        // -----------------------------------------------------------------
        // Scene-element helpers
        // -----------------------------------------------------------------

        private static void EnsureGround()
        {
            // Unity's built-in Plane is 10m on a side at scale 1, so scale
            // 22 = 220m square. That comfortably contains the 200m arena
            // wall ring with a bit of slack on each side.
            GameObject ground = ScaffoldUtils.GetOrCreate(
                "Ground",
                () => GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(22f, 1f, 22f);
        }

        private static void EnsureCamera()
        {
            GameObject cam = GameObject.Find("Main Camera");
            if (cam == null)
            {
                cam = new GameObject("Main Camera");
                cam.AddComponent<Camera>();
                cam.AddComponent<AudioListener>();
                cam.tag = "MainCamera";
            }
            cam.transform.position = new Vector3(0f, 8f, -10f);
            cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
        }

        private static void EnsureLight()
        {
            GameObject light = ScaffoldUtils.GetOrCreate("Directional Light");
            var lightComp = light.GetComponent<Light>();
            if (lightComp == null) lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject MakeBox(Transform parent, Vector3 pos, Vector3 size, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            return go;
        }

        private static GameObject MakeRamp(Transform parent, Vector3 pos, float pitchDeg, Vector3 size, string name)
        {
            GameObject go = MakeBox(parent, pos, size, name);
            float halfH = size.y * 0.5f;
            float halfL = size.z * 0.5f;
            go.transform.rotation = Quaternion.Euler(-pitchDeg, 0f, 0f);
            float lift = Mathf.Sin(pitchDeg * Mathf.Deg2Rad) * halfL + Mathf.Cos(pitchDeg * Mathf.Deg2Rad) * halfH;
            go.transform.position = new Vector3(pos.x, lift, pos.z);
            return go;
        }
    }
}
