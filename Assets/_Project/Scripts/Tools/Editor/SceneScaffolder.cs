using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Player;
using Robogame.Robots;
using Robogame.UI;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor menu commands that build out our standard scenes from scratch.
    /// </summary>
    /// <remarks>
    /// Idempotent where possible: re-running a scaffold on an already-built
    /// scene should be a no-op (objects are looked up by name and reused).
    /// </remarks>
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

            // Configure to load the Garage scene next.
            SerializedObject so = new SerializedObject(component);
            so.FindProperty("_firstScene").stringValue = "Garage";
            so.FindProperty("_persistAcrossScenes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Bootstrap.unity.");
        }

        // -----------------------------------------------------------------
        // Garage test scene
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Garage")]
        public static void BuildTestGarage()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            // Ground
            GameObject ground = ScaffoldUtils.GetOrCreate(
                "Ground",
                () => GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(5f, 1f, 5f);

            // Player cube
            GameObject player = ScaffoldUtils.GetOrCreate(
                "Player",
                () => GameObject.CreatePrimitive(PrimitiveType.Cube));
            player.transform.position = new Vector3(0f, 1f, 0f);
            player.transform.localScale = Vector3.one;

            EnsureComponent<Rigidbody>(player);
            EnsureComponent<GroundDrive>(player);

            var input = EnsureComponent<PlayerInputHandler>(player);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);
            if (actions == null)
            {
                Debug.LogWarning(
                    $"[Robogame] Input actions asset not found at {ScaffoldUtils.InputActionsAsset}. " +
                    "PlayerInputHandler will be added but un-wired.");
            }
            else
            {
                SerializedObject so = new SerializedObject(input);
                so.FindProperty("_actions").objectReferenceValue = actions;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EnsureComponent<PlayerController>(player);

            // Camera
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

            // Light (Unity primitives don't bring their own).
            GameObject light = ScaffoldUtils.GetOrCreate("Directional Light");
            var lightComp = light.GetComponent<Light>();
            if (lightComp == null) lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Garage.unity test scene.");
        }

        // -----------------------------------------------------------------
        // Test robot (multi-block) — replaces the simple cube
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Robot")]
        public static void BuildTestRobot()
        {
            // Make sure block definitions exist before we try to use them.
            BlockDefinitionWizard.CreateTestDefinitions();

            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            // Ground + camera + light reuse the simple-garage helpers.
            EnsureGround();
            EnsureCamera();
            EnsureLight();

            // Remove the simple "Player" cube if it's still hanging around from
            // the earlier scaffold, since the robot replaces it.
            GameObject legacyPlayer = GameObject.Find("Player");
            if (legacyPlayer != null && legacyPlayer.GetComponent<Robot>() == null)
            {
                Object.DestroyImmediate(legacyPlayer);
            }

            GameObject robotGO = ScaffoldUtils.GetOrCreate("Robot");
            Robot robot = PopulateTestRobot(robotGO);

            EnsureDevHud();

            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built Garage.unity with test robot. " +
                      $"Blocks: {robot.BlockCount}, CPU: {robot.TotalCpu}, Mass: {robot.TotalBlockMass}");
        }

        /// <summary>
        /// Populate <paramref name="robotGO"/> with the canonical test-robot layout.
        /// Safe to call in play mode (no scene open/save).
        /// </summary>
        public static Robot PopulateTestRobot(GameObject robotGO)
        {
            BlockDefinitionWizard.CreateTestDefinitions();

            robotGO.transform.position = new Vector3(0f, 1.5f, 0f);
            robotGO.transform.rotation = Quaternion.identity;

            EnsureComponent<Rigidbody>(robotGO);
            EnsureComponent<BlockGrid>(robotGO);
            var robot = EnsureComponent<Robot>(robotGO);
            EnsureComponent<GroundDrive>(robotGO);

            var input = EnsureComponent<PlayerInputHandler>(robotGO);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);
            if (actions != null)
            {
                SerializedObject so = new SerializedObject(input);
                so.FindProperty("_actions").objectReferenceValue = actions;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EnsureComponent<PlayerController>(robotGO);

            BlockGrid grid = robotGO.GetComponent<BlockGrid>();
            grid.Clear();

            BlockDefinition cube = BlockDefinitionWizard.LoadById("BlockDef_Cube");
            BlockDefinition cpu = BlockDefinitionWizard.LoadById("BlockDef_Cpu");
            BlockDefinition wheel = BlockDefinitionWizard.LoadById("BlockDef_Wheel");

            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                grid.PlaceBlock(cube, new Vector3Int(x, 0, z));
            }
            grid.PlaceBlock(cpu, new Vector3Int(0, 1, 0));
            grid.PlaceBlock(cube, new Vector3Int(0, 1, 1));
            grid.PlaceBlock(wheel, new Vector3Int(-1, 0, -1));
            grid.PlaceBlock(wheel, new Vector3Int( 1, 0, -1));
            grid.PlaceBlock(wheel, new Vector3Int(-1, 0,  1));
            grid.PlaceBlock(wheel, new Vector3Int( 1, 0,  1));

            EnsureMuzzleAndGun(robotGO);
            BindFollowCameraTo(robotGO.transform);

            robot.RecalculateAggregates();
            return robot;
        }

        // -----------------------------------------------------------------
        // Combat dummy — stationary target for weapons testing
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Combat Dummy")]
        public static void BuildCombatDummy()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);
            EnsureGround(); EnsureCamera(); EnsureLight();

            GameObject dummyGO = ScaffoldUtils.GetOrCreate("CombatDummy");
            Robot dummy = PopulateCombatDummy(dummyGO);

            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built combat dummy. Blocks: {dummy.BlockCount}, Mass: {dummy.TotalBlockMass}");
        }

        /// <summary>Populate <paramref name="dummyGO"/> with the canonical combat-dummy layout.</summary>
        public static Robot PopulateCombatDummy(GameObject dummyGO)
        {
            BlockDefinitionWizard.CreateTestDefinitions();

            dummyGO.transform.position = new Vector3(8f, 1.5f, 0f);
            dummyGO.transform.rotation = Quaternion.identity;

            var rb = EnsureComponent<Rigidbody>(dummyGO);
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            EnsureComponent<BlockGrid>(dummyGO);
            var dummy = EnsureComponent<Robot>(dummyGO);

            BlockGrid grid = dummyGO.GetComponent<BlockGrid>();
            grid.Clear();

            BlockDefinition cube = BlockDefinitionWizard.LoadById("BlockDef_Cube");
            BlockDefinition cpu = BlockDefinitionWizard.LoadById("BlockDef_Cpu");

            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                grid.PlaceBlock(cube, new Vector3Int(x, y, z));
            }
            grid.RemoveBlock(new Vector3Int(0, 1, 0));
            grid.PlaceBlock(cpu, new Vector3Int(0, 1, 0));

            dummy.RecalculateAggregates();
            return dummy;
        }

        // -----------------------------------------------------------------
        // Build everything + sync build profiles
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

        private static void EnsureGround()
        {
            GameObject ground = ScaffoldUtils.GetOrCreate(
                "Ground",
                () => GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
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

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static void EnsureMuzzleAndGun(GameObject robotGO)
        {
            Transform existing = robotGO.transform.Find("Muzzle");
            GameObject muzzleGO = existing != null ? existing.gameObject : new GameObject("Muzzle");
            muzzleGO.transform.SetParent(robotGO.transform, worldPositionStays: false);
            // Place at the front of the chassis, slightly above the deck.
            muzzleGO.transform.localPosition = new Vector3(0f, 1f, 1.5f);
            muzzleGO.transform.localRotation = Quaternion.identity;

            HitscanGun gun = robotGO.GetComponent<HitscanGun>();
            if (gun == null) gun = robotGO.AddComponent<HitscanGun>();
            SerializedObject so = new SerializedObject(gun);
            so.FindProperty("_muzzle").objectReferenceValue = muzzleGO.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindFollowCameraTo(Transform target)
        {
            GameObject cam = GameObject.Find("Main Camera");
            if (cam == null) return;
            FollowCamera follow = cam.GetComponent<FollowCamera>();
            if (follow == null) follow = cam.AddComponent<FollowCamera>();
            SerializedObject so = new SerializedObject(follow);
            so.FindProperty("_target").objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDevHud()
        {
            GameObject hudGO = ScaffoldUtils.GetOrCreate("DevHud");
            DevHud hud = hudGO.GetComponent<DevHud>();
            if (hud == null) hudGO.AddComponent<DevHud>();
        }
    }
}
