using System;
using Robogame.Movement;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Helpers for loading or authoring tuning ScriptableObject assets used
    /// by the scene scaffolders. Replaces the old <c>SerializedObject</c>
    /// force-write helpers — tuning is now first-class data.
    /// </summary>
    public static class TuningAssets
    {
        public const string TuningFolder = "Assets/_Project/ScriptableObjects/Tuning";

        /// <summary>
        /// Load <typeparamref name="T"/> at <c>{TuningFolder}/{assetName}.asset</c>
        /// or create a fresh instance and persist it. <paramref name="initializer"/>
        /// is called only when a new asset is created (existing ones are left alone
        /// so designer edits stick).
        /// </summary>
        public static T LoadOrCreate<T>(string assetName, Action<T> initializer = null)
            where T : ScriptableObject
        {
            EnsureFolder(TuningFolder);
            string path = $"{TuningFolder}/{assetName}.asset";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            T fresh = ScriptableObject.CreateInstance<T>();
            initializer?.Invoke(fresh);
            AssetDatabase.CreateAsset(fresh, path);
            AssetDatabase.SaveAssets();
            return fresh;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
