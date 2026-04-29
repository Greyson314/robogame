using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Player;

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
        // Build everything + sync build profiles
        // -----------------------------------------------------------------

        [MenuItem(MenuRoot + "Build All && Configure")]
        public static void BuildAll()
        {
            BuildBootstrap();
            BuildTestGarage();
            BuildSettingsConfigurator.SyncSceneList();
            Debug.Log("[Robogame] Scaffold complete. Open Bootstrap.unity and press Play.");
        }

        // -----------------------------------------------------------------

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }
    }
}
