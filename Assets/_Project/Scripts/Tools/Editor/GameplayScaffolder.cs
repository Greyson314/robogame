using System.Collections.Generic;
using System.IO;
using Robogame.Block;
using Robogame.Core;
using Robogame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Pass A scaffolder: creates the data assets (block definition
    /// library, default blueprints) and builds the Bootstrap / Garage /
    /// Arena scenes wired for the runtime state machine.
    /// </summary>
    /// <remarks>
    /// Idempotent menu items live under <c>Robogame/Scaffold/Gameplay/...</c>.
    /// The legacy "Build Test Robot/Plane/Dummy" entries in
    /// <see cref="SceneScaffolder"/> are unaffected — they still drop a
    /// hand-wired chassis directly into the Garage scene for fast iteration.
    /// </remarks>
    public static class GameplayScaffolder
    {
        private const string MenuRoot = "Robogame/Scaffold/Gameplay/";
        private const string SoFolder = "Assets/_Project/ScriptableObjects";
        private const string BlueprintFolder = SoFolder + "/Blueprints";
        private const string LibraryAssetPath = SoFolder + "/BlockDefinitionLibrary.asset";
        private const string DefaultGroundPath = BlueprintFolder + "/Blueprint_DefaultGround.asset";
        private const string DefaultPlanePath = BlueprintFolder + "/Blueprint_DefaultPlane.asset";
        private const string DefaultBuggyPath = BlueprintFolder + "/Blueprint_DefaultBuggy.asset";
        private const string CombatDummyPath = BlueprintFolder + "/Blueprint_CombatDummy.asset";

        // -----------------------------------------------------------------
        // Data assets
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "1 — Populate Block Definition Library")]
        public static BlockDefinitionLibrary PopulateBlockDefinitionLibrary()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            EnsureFolder(SoFolder);

            BlockDefinitionLibrary lib = LoadOrCreateAsset<BlockDefinitionLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                Debug.LogError($"[Robogame] Could not load or create BlockDefinitionLibrary at {LibraryAssetPath}.");
                return null;
            }

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BlockDefinition));
            var defs = new List<BlockDefinition>(guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BlockDefinition def = AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
                if (def != null) defs.Add(def);
            }

            lib.SetDefinitions(defs.ToArray());
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] BlockDefinitionLibrary populated with {defs.Count} entries (asset persisted: {AssetDatabase.Contains(lib)}).");
            return lib;
        }

        [MenuItem(MenuRoot + "2 — Create Default Blueprints")]
        public static void CreateDefaultBlueprintsMenu() => CreateDefaultBlueprints();

        public static ChassisBlueprint CreateDefaultBlueprints()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            EnsureFolder(BlueprintFolder);

            ChassisBlueprint ground = CreateOrUpdateBlueprint(DefaultGroundPath, "Tank", ChassisKind.Ground, BuildGroundEntries());
            CreateOrUpdateBlueprint(DefaultPlanePath, "Plane", ChassisKind.Plane, BuildPlaneEntries());
            CreateOrUpdateBlueprint(DefaultBuggyPath, "Buggy", ChassisKind.Ground, BuildBuggyEntries());
            CreateOrUpdateBlueprint(CombatDummyPath, "Combat Dummy", ChassisKind.Ground, BuildDummyEntries());
            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] Default blueprints created (ground asset persisted: {AssetDatabase.Contains(ground)}).");
            return ground;
        }

        private static ChassisBlueprint.Entry[] BuildGroundEntries()
        {
            // Mirrors RobotLayouts.PopulateTestRobot: 3×6 chassis with
            // CPU centre, weapon on top, wheels at corners + mid.
            var list = new List<ChassisBlueprint.Entry>();
            const int xMin = -1, xMax = 1, zMin = -2, zMax = 3;
            for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
            {
                bool isCpu = (x == 0 && z == 0);
                bool isWheel = (x == xMin || x == xMax) && (z == zMin || z == 0 || z == zMax);
                if (isCpu || isWheel) continue;
                list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(x, 0, z)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 1, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(xMin, 0, zMax)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(xMax, 0, zMax)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMin, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMax, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMin, 0, zMin)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMax, 0, zMin)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildPlaneEntries()
        {
            // Mirrors RobotLayouts.PopulateTestPlane: thruster-rear fuselage,
            // 4-segment main wings, tailplane and vertical fin.
            var list = new List<ChassisBlueprint.Entry>();
            list.Add(new ChassisBlueprint.Entry(BlockIds.Thruster, new Vector3Int(0, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0, -2)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu,      new Vector3Int(0, 0,  0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  2)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon,   new Vector3Int(0, 1,  3)));
            for (int x = 1; x <= 4; x++)
            {
                list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( x, 0, 0)));
                list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-x, 0, 0)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 2, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-2, 0, 1)));
            // Tailplane: all four are lifting surfaces. Wings far from COM
            // no longer cause a constant pitching moment because
            // AeroSurfaceBlock now scales lift with angle of attack, so the
            // tail self-trims with the rest of the wing.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 1, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-1, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 2, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-2, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 0, 1, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int( 0, 2, -3)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildBuggyEntries()
        {
            // Compact 2-wide × 3-long × 2-tall buggy: smaller and lighter
            // than the tank, no top armour. CPU at centre, weapon mounted
            // up top, steering wheels at the front, drive wheels at the rear.
            var list = new List<ChassisBlueprint.Entry>();
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(-1, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(1, 0, 0)));
            // Front: steering wheels and a nose block.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(-1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(1, 0, 1)));
            // Rear: drive wheels and a tail block.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(-1, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(1, 0, -1)));
            // Roll cage / weapon mount.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 1, 0)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildDummyEntries()
        {
            // 2×2×2 cube minus one corner that becomes the CPU.
            var list = new List<ChassisBlueprint.Entry>();
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                if (x == 0 && y == 1 && z == 0) continue;
                list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(x, y, z)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 1, 0)));
            return list.ToArray();
        }

        private static ChassisBlueprint CreateOrUpdateBlueprint(
            string path, string displayName, ChassisKind kind, ChassisBlueprint.Entry[] entries)
        {
            ChassisBlueprint bp = LoadOrCreateAsset<ChassisBlueprint>(path);
            if (bp == null)
            {
                Debug.LogError($"[Robogame] Could not load or create blueprint at {path}.");
                return null;
            }
            bp.DisplayName = displayName;
            bp.Kind = kind;
            bp.SetEntries(entries);
            EditorUtility.SetDirty(bp);
            return bp;
        }

        // -----------------------------------------------------------------
        // Scenes
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "3 — Build Bootstrap (Pass A)")]
        public static void BuildBootstrapPassA()
        {
            BlockDefinitionLibrary lib = PopulateBlockDefinitionLibrary();
            ChassisBlueprint defaultBp = CreateDefaultBlueprints();
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);

            if (lib == null || !AssetDatabase.Contains(lib))
                Debug.LogError($"[Robogame] BuildBootstrapPassA: library is null or not a persistent asset (path: {LibraryAssetPath}).");
            if (defaultBp == null || !AssetDatabase.Contains(defaultBp))
                Debug.LogError($"[Robogame] BuildBootstrapPassA: default blueprint is null or not a persistent asset (path: {DefaultGroundPath}).");
            if (actions == null)
                Debug.LogWarning($"[Robogame] BuildBootstrapPassA: input actions asset not found at {ScaffoldUtils.InputActionsAsset}.");

            ScaffoldUtils.OpenScene(ScaffoldUtils.BootstrapScene);

            GameObject bootstrap = ScaffoldUtils.GetOrCreate("Bootstrap");
            var boot = bootstrap.GetComponent<GameBootstrap>();
            if (boot == null) boot = bootstrap.AddComponent<GameBootstrap>();
            SerializedObject bootSO = new SerializedObject(boot);
            bootSO.FindProperty("_firstScene").stringValue = "Garage";
            bootSO.FindProperty("_persistAcrossScenes").boolValue = true;
            bootSO.ApplyModifiedPropertiesWithoutUndo();

            var state = bootstrap.GetComponent<GameStateController>();
            if (state == null) state = bootstrap.AddComponent<GameStateController>();

            // Re-load the assets by path RIGHT BEFORE assignment. Anything
            // that triggered AssetDatabase.Refresh between asset creation
            // and this point (BlockDefinitionWizard does, OpenScene can)
            // will have invalidated the C# refs we captured earlier
            // (Unity's "fake null"). Loading by path here is cheap and
            // guarantees we hand SerializedProperty a live, persistent ref.
            BlockDefinitionLibrary libLive = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            ChassisBlueprint defaultBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultGroundPath);
            ChassisBlueprint planeBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultPlanePath);
            ChassisBlueprint buggyBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultBuggyPath);
            InputActionAsset actionsLive = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);

            if (libLive == null)
                Debug.LogError($"[Robogame] BuildBootstrapPassA: library load FAILED at assignment time (path: {LibraryAssetPath}).");
            if (defaultBpLive == null)
                Debug.LogError($"[Robogame] BuildBootstrapPassA: default blueprint load FAILED at assignment time (path: {DefaultGroundPath}).");

            SerializedObject stateSO = new SerializedObject(state);
            stateSO.FindProperty("_library").objectReferenceValue = libLive;
            stateSO.FindProperty("_defaultBlueprint").objectReferenceValue = defaultBpLive;
            stateSO.FindProperty("_inputActions").objectReferenceValue = actionsLive;

            // Populate the HUD-facing preset list (Tank / Plane / Buggy).
            SerializedProperty presets = stateSO.FindProperty("_presetBlueprints");
            if (presets != null)
            {
                presets.arraySize = 3;
                presets.GetArrayElementAtIndex(0).objectReferenceValue = defaultBpLive;
                presets.GetArrayElementAtIndex(1).objectReferenceValue = planeBpLive;
                presets.GetArrayElementAtIndex(2).objectReferenceValue = buggyBpLive;
            }
            stateSO.ApplyModifiedPropertiesWithoutUndo();

            // Confirm the values stuck before saving — catches the case where
            // the GameStateController script changed and old serialised data
            // doesn't bind to the new fields.
            stateSO.Update();
            bool libOK    = stateSO.FindProperty("_library").objectReferenceValue != null;
            bool bpOK     = stateSO.FindProperty("_defaultBlueprint").objectReferenceValue != null;
            bool inputOK  = stateSO.FindProperty("_inputActions").objectReferenceValue != null;
            EditorUtility.SetDirty(state);
            EditorSceneManager.MarkSceneDirty(state.gameObject.scene);

            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built Bootstrap.unity (Pass A). " +
                      $"GameStateController wired: library={libOK}, defaultBlueprint={bpOK}, inputActions={inputOK}.");
        }

        [MenuItem(MenuRoot + "4 — Build Garage Scene (Pass A)")]
        public static void BuildGaragePassA()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            EnvironmentBuilder.BuildGarageEnvironment();
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Plane");

            GameObject controller = ScaffoldUtils.GetOrCreate("GarageController");
            if (controller.GetComponent<GarageController>() == null)
                controller.AddComponent<GarageController>();
            if (controller.GetComponent<SceneTransitionHud>() == null)
                controller.AddComponent<SceneTransitionHud>();

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Garage.unity (Pass A).");
        }

        [MenuItem(MenuRoot + "5 — Build Arena Scene (Pass A)")]
        public static void BuildArenaPassA()
        {
            ChassisBlueprint dummyBp = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(CombatDummyPath);

            // Create the Arena scene file if missing.
            if (!File.Exists(ScaffoldUtils.ArenaScene))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, ScaffoldUtils.ArenaScene);
            }
            ScaffoldUtils.OpenScene(ScaffoldUtils.ArenaScene);

            EnvironmentBuilder.BuildArenaEnvironment();
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");

            GameObject controller = ScaffoldUtils.GetOrCreate("ArenaController");
            ArenaController arena = controller.GetComponent<ArenaController>();
            if (arena == null) arena = controller.AddComponent<ArenaController>();
            if (controller.GetComponent<SceneTransitionHud>() == null)
                controller.AddComponent<SceneTransitionHud>();

            if (dummyBp != null)
            {
                SerializedObject so = new SerializedObject(arena);
                SerializedProperty prop = so.FindProperty("_dummyBlueprint");
                if (prop != null) prop.objectReferenceValue = dummyBp;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Arena.unity (Pass A).");
        }

        [MenuItem(MenuRoot + "Build All Pass A")]
        public static void BuildAllPassA()
        {
            BuildBootstrapPassA();
            BuildArenaPassA();
            BuildGaragePassA();
            BuildSettingsConfigurator.SyncSceneList();

            // Leave Bootstrap.unity open so pressing Play actually exercises
            // the state machine. (BuildGaragePassA was the last to run and
            // would otherwise leave Garage open, which spawns nothing
            // because GameStateController only exists in Bootstrap.)
            ScaffoldUtils.OpenScene(ScaffoldUtils.BootstrapScene);
            Debug.Log("[Robogame] Pass A scaffold complete. Bootstrap.unity is open — press Play.");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Robust load-or-create for a ScriptableObject asset at <paramref name="path"/>.
        /// Handles the failure mode where the file exists on disk but
        /// <see cref="AssetDatabase.LoadAssetAtPath{T}"/> returns null because
        /// the asset's serialised script type can't bind (stale GUID, type
        /// moved between assemblies, etc.) by deleting the orphan and
        /// recreating fresh. Returns null only if the create itself fails.
        /// </summary>
        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            // First try the typed load.
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            // Maybe the script binding is fine but the typed cast failed for
            // some reason — try the untyped path next.
            UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main is T cast) return cast;

            // File is there but the Type doesn't match what we want — nuke it.
            if (File.Exists(path) || main != null)
            {
                Debug.LogWarning($"[Robogame] LoadOrCreateAsset: existing asset at '{path}' could not be loaded as {typeof(T).Name} (got {(main == null ? "null" : main.GetType().Name)}). Recreating.");
                AssetDatabase.DeleteAsset(path);
            }

            T inst = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(inst, path);
            AssetDatabase.SaveAssets();
            return inst;
        }
    }
}
