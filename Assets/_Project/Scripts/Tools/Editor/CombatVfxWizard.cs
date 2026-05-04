using System.IO;
using Robogame.Combat;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Creates / refreshes <see cref="CombatVfxLibrary"/> at
    /// <c>Assets/_Project/Resources/CombatVfxLibrary.asset</c> and binds
    /// it to the Cartoon FX Remaster prefabs we ship with. Idempotent.
    /// </summary>
    /// <remarks>
    /// Lives in the Editor asmdef so the runtime <see cref="CombatVfxLibrary"/>
    /// stays free of <see cref="UnityEditor"/> calls. Run once after
    /// importing the CFXR pack; afterwards the library asset is just a
    /// regular ScriptableObject any code can <c>Resources.Load</c>.
    /// </remarks>
    public static class CombatVfxWizard
    {
        public const string LibraryFolder = "Assets/_Project/Resources";
        public const string LibraryAssetPath = LibraryFolder + "/CombatVfxLibrary.asset";

        // Default VFX picks. CFXR Explosion 1 is the classic punchy
        // chunk-and-shockwave used in the CFXR demo scene; swap for
        // "CFXR3 Fire Explosion B" or "CFXR2 WW Explosion" if that
        // reads better in-game.
        private const string BombExplosionPrefabPath =
            "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Explosions/CFXR Explosion 1.prefab";

        public static CombatVfxLibrary CreateOrUpdate()
        {
            EnsureFolder(LibraryFolder);

            CombatVfxLibrary lib = AssetDatabase.LoadAssetAtPath<CombatVfxLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<CombatVfxLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryAssetPath);
            }

            GameObject explosion = AssetDatabase.LoadAssetAtPath<GameObject>(BombExplosionPrefabPath);
            if (explosion == null)
            {
                Debug.LogWarning($"[Robogame] CombatVfxWizard: bomb explosion prefab not found at {BombExplosionPrefabPath}. " +
                                 "The library was created but bombs will fall back to no-vfx.");
            }

            SerializedObject so = new SerializedObject(lib);
            SerializedProperty bombProp = so.FindProperty("_bombExplosion");
            if (bombProp != null) bombProp.objectReferenceValue = explosion;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Robogame] CombatVfxLibrary ready (bomb VFX bound: {explosion != null}).");
            return lib;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
