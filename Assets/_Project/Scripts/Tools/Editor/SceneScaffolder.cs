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
            PopulateTestTerrain();

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
            var drive = EnsureComponent<GroundDrive>(robotGO);
            // Force the tuning values so old serialised drives don't keep stale numbers.
            ApplyDriveTuning(drive,
                acceleration: 26.25f, maxSpeed: 13.5f, turnRate: 7.5f,
                linearDamping: 0.2f, angularDamping: 2f);

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
            BlockDefinition wheelSteer = BlockDefinitionWizard.LoadById("BlockDef_WheelSteer");
            BlockDefinition weapon = BlockDefinitionWizard.LoadById("BlockDef_Weapon");

            // Mount + binders must exist BEFORE we place blocks so their
            // BlockPlaced subscriptions catch the new arrivals.
            EnsureWeaponMountAndBinder(robotGO);
            EnsureWheelBinder(robotGO);

            // Chassis: 3 wide (x ∈ [-1,1]) × 6 long (z ∈ [-2,3]).
            // Wheel cells: x = ±1 at z = -2, 0, 3 (front, mid, rear pairs).
            // CPU sits at the centre cell (0,0,0); turret rides on top.
            const int xMin = -1, xMax = 1;
            const int zMin = -2, zMax = 3;

            for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
            {
                bool isCpu = (x == 0 && z == 0);
                bool isWheel = (x == xMin || x == xMax) && (z == zMin || z == 0 || z == zMax);
                if (isCpu || isWheel) continue;
                grid.PlaceBlock(cube, new Vector3Int(x, 0, z));
            }
            grid.PlaceBlock(cpu, new Vector3Int(0, 0, 0));
            grid.PlaceBlock(weapon, new Vector3Int(0, 1, 0));

            // Front (z = zMax) = steer; mid + rear = drive.
            grid.PlaceBlock(wheelSteer, new Vector3Int(xMin, 0, zMax));
            grid.PlaceBlock(wheelSteer, new Vector3Int(xMax, 0, zMax));
            grid.PlaceBlock(wheel,      new Vector3Int(xMin, 0, 0));
            grid.PlaceBlock(wheel,      new Vector3Int(xMax, 0, 0));
            grid.PlaceBlock(wheel,      new Vector3Int(xMin, 0, zMin));
            grid.PlaceBlock(wheel,      new Vector3Int(xMax, 0, zMin));

            // Tear down the legacy root-level HitscanGun + Muzzle if present
            // (older scaffolds left these behind).
            RemoveLegacyRootGun(robotGO);

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

        // -----------------------------------------------------------------
        // Test terrain — ramps, bumps, walls for driving feel
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build Test Terrain")]
        public static void BuildTestTerrain()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);
            EnsureGround(); EnsureCamera(); EnsureLight();
            PopulateTestTerrain();
            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Test terrain built.");
        }

        /// <summary>
        /// Drop a small obstacle course around the origin: ramps of varying
        /// pitch, a bump strip, a stair-step, and a low boundary wall ring.
        /// Idempotent: nukes the previous "Terrain" parent and rebuilds.
        /// </summary>
        public static void PopulateTestTerrain()
        {
            // Wipe and rebuild under one parent so it's easy to clear.
            GameObject root = GameObject.Find("Terrain");
            if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("Terrain");

            // --- Ramps at four pitches, fan out behind the spawn (-Z).
            float[] pitches = { 8f, 15f, 25f, 35f };
            for (int i = 0; i < pitches.Length; i++)
            {
                float ang = pitches[i];
                Vector3 pos = new Vector3(-12f + i * 6f, 0f, -16f);
                MakeRamp(root.transform, pos, ang, size: new Vector3(4f, 0.5f, 8f),
                    name: $"Ramp_{ang:00}deg");
            }

            // --- Bump strip: small evenly-spaced humps directly ahead (+Z).
            for (int i = 0; i < 6; i++)
            {
                MakeBox(root.transform,
                    pos: new Vector3(-3f + i * 1.2f, 0.15f, 12f),
                    size: new Vector3(1f, 0.3f, 0.6f),
                    name: $"Bump_{i}");
            }

            // --- Stair step (4 steps) on the right (+X).
            for (int i = 0; i < 4; i++)
            {
                float h = 0.4f * (i + 1);
                MakeBox(root.transform,
                    pos: new Vector3(15f, h * 0.5f, -2f + i * 1.2f),
                    size: new Vector3(4f, h, 1.2f),
                    name: $"Stair_{i}");
            }

            // --- A couple of free-standing pillars for cover / aim targets.
            MakeBox(root.transform, new Vector3(-8f, 1.5f, 6f), new Vector3(1f, 3f, 1f), "Pillar_A");
            MakeBox(root.transform, new Vector3(-6f, 1.5f, 9f), new Vector3(1f, 3f, 1f), "Pillar_B");
            MakeBox(root.transform, new Vector3(10f, 1.5f, 8f), new Vector3(1f, 3f, 1f), "Pillar_C");

            // --- Boundary wall ring so the bot can't fall off the plane.
            const float arenaHalf = 24f;
            const float wallH = 1.2f, wallT = 0.5f;
            MakeBox(root.transform, new Vector3(0f, wallH * 0.5f,  arenaHalf), new Vector3(arenaHalf * 2f, wallH, wallT), "Wall_N");
            MakeBox(root.transform, new Vector3(0f, wallH * 0.5f, -arenaHalf), new Vector3(arenaHalf * 2f, wallH, wallT), "Wall_S");
            MakeBox(root.transform, new Vector3( arenaHalf, wallH * 0.5f, 0f), new Vector3(wallT, wallH, arenaHalf * 2f), "Wall_E");
            MakeBox(root.transform, new Vector3(-arenaHalf, wallH * 0.5f, 0f), new Vector3(wallT, wallH, arenaHalf * 2f), "Wall_W");
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
            // Lift so the low end sits on the ground.
            float halfH = size.y * 0.5f;
            float halfL = size.z * 0.5f;
            go.transform.rotation = Quaternion.Euler(-pitchDeg, 0f, 0f);
            // After tilting, the low corner dips below 0 — push the ramp up.
            float lift = Mathf.Sin(pitchDeg * Mathf.Deg2Rad) * halfL + Mathf.Cos(pitchDeg * Mathf.Deg2Rad) * halfH;
            go.transform.position = new Vector3(pos.x, lift, pos.z);
            return go;
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

        private static void EnsureWeaponMountAndBinder(GameObject robotGO)
        {
            // Mount lives as a child so its rotation is independent of the chassis.
            Transform mountT = robotGO.transform.Find("WeaponMount");
            GameObject mountGO = mountT != null ? mountT.gameObject : new GameObject("WeaponMount");
            mountGO.transform.SetParent(robotGO.transform, worldPositionStays: false);
            mountGO.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            mountGO.transform.localRotation = Quaternion.identity;
            if (mountGO.GetComponent<WeaponMount>() == null) mountGO.AddComponent<WeaponMount>();

            RobotWeaponBinder binder = robotGO.GetComponent<RobotWeaponBinder>();
            if (binder == null) binder = robotGO.AddComponent<RobotWeaponBinder>();
            SerializedObject so = new SerializedObject(binder);
            so.FindProperty("_mount").objectReferenceValue = mountGO.GetComponent<WeaponMount>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureWheelBinder(GameObject robotGO)
        {
            if (robotGO.GetComponent<RobotWheelBinder>() == null)
                robotGO.AddComponent<RobotWheelBinder>();
        }

        /// <summary>
        /// Force-write GroundDrive tuning onto an instance. Defaults set on
        /// the field declaration only apply the FIRST time the component is
        /// added — every subsequent rebuild kept whatever was serialised
        /// before. This makes the scaffolder authoritative.
        /// </summary>
        private static void ApplyDriveTuning(
            GroundDrive drive,
            float acceleration, float maxSpeed, float turnRate,
            float linearDamping, float angularDamping)
        {
            if (drive == null) return;
            SerializedObject so = new SerializedObject(drive);
            so.FindProperty("_acceleration").floatValue = acceleration;
            so.FindProperty("_maxSpeed").floatValue = maxSpeed;
            so.FindProperty("_turnRate").floatValue = turnRate;
            SerializedProperty linProp = so.FindProperty("_groundedLinearDamping");
            if (linProp != null) linProp.floatValue = linearDamping;
            SerializedProperty angProp = so.FindProperty("_groundedAngularDamping");
            if (angProp != null) angProp.floatValue = angularDamping;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemoveLegacyRootGun(GameObject robotGO)
        {
            HitscanGun rootGun = robotGO.GetComponent<HitscanGun>();
            if (rootGun != null) Object.DestroyImmediate(rootGun);

            Transform muzzle = robotGO.transform.Find("Muzzle");
            if (muzzle != null) Object.DestroyImmediate(muzzle.gameObject);
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
