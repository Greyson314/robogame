using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Common helpers for Editor scaffolding scripts.
    /// </summary>
    internal static class ScaffoldUtils
    {
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public const string BootstrapScene = ScenesFolder + "/Bootstrap.unity";
        public const string GarageScene = ScenesFolder + "/Garage.unity";
        public const string ArenaScene = ScenesFolder + "/Arena.unity";

        public const string InputActionsAsset = "Assets/InputSystem_Actions.inputactions";

        /// <summary>
        /// Open the scene at <paramref name="path"/>, prompting the user to save
        /// any modifications to the currently-open scene first.
        /// </summary>
        public static UnityEngine.SceneManagement.Scene OpenScene(string path)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new System.OperationCanceledException("User cancelled scene save.");
            }
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        /// <summary>Save the currently active scene.</summary>
        public static void SaveActiveScene()
        {
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }

        /// <summary>Find an existing GameObject in the active scene by name, or null.</summary>
        public static GameObject Find(string name) => GameObject.Find(name);

        /// <summary>
        /// Get-or-create a GameObject by name in the active scene. Optionally provide
        /// a factory for the case where it must be created (e.g. <c>GameObject.CreatePrimitive</c>).
        /// </summary>
        public static GameObject GetOrCreate(string name, System.Func<GameObject> factory = null)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null) return existing;
            GameObject created = factory != null ? factory() : new GameObject(name);
            created.name = name;
            return created;
        }
    }
}
