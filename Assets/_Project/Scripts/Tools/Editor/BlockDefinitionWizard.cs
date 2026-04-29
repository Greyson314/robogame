using System.IO;
using Robogame.Block;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Creates a small set of canonical <see cref="BlockDefinition"/> assets
    /// for early development. Idempotent — re-running won't overwrite existing assets.
    /// </summary>
    public static class BlockDefinitionWizard
    {
        public const string DefinitionsFolder = "Assets/_Project/ScriptableObjects/BlockDefinitions";

        // Stable IDs — DO NOT change once shipped.
        public const string IdCube = "block.structure.cube";
        public const string IdCpu = "block.cpu.standard";
        public const string IdWheel = "block.movement.wheel";

        [MenuItem("Robogame/Scaffold/Create Test Block Definitions")]
        public static void CreateTestDefinitions()
        {
            EnsureFolder(DefinitionsFolder);

            CreateOrSkip(
                "BlockDef_Cube",
                IdCube,
                "Structure Cube",
                BlockCategory.Structure,
                maxHealth: 100f, mass: 1f, cpuCost: 1);

            CreateOrSkip(
                "BlockDef_Cpu",
                IdCpu,
                "CPU",
                BlockCategory.Cpu,
                maxHealth: 200f, mass: 2f, cpuCost: 0);

            CreateOrSkip(
                "BlockDef_Wheel",
                IdWheel,
                "Wheel",
                BlockCategory.Movement,
                maxHealth: 80f, mass: 1.5f, cpuCost: 25);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Robogame] Test block definitions ready.");
        }

        public static BlockDefinition LoadById(string assetName)
        {
            string path = $"{DefinitionsFolder}/{assetName}.asset";
            return AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
        }

        // -----------------------------------------------------------------

        private static void CreateOrSkip(
            string assetName,
            string stableId,
            string displayName,
            BlockCategory category,
            float maxHealth,
            float mass,
            int cpuCost)
        {
            string path = $"{DefinitionsFolder}/{assetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<BlockDefinition>(path) != null) return;

            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            SerializedObject so = new SerializedObject(def);
            so.FindProperty("_id").stringValue = stableId;
            so.FindProperty("_displayName").stringValue = displayName;
            so.FindProperty("_category").enumValueIndex = (int)category;
            so.FindProperty("_maxHealth").floatValue = maxHealth;
            so.FindProperty("_mass").floatValue = mass;
            so.FindProperty("_cpuCost").intValue = cpuCost;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(def, path);
            Debug.Log($"[Robogame] Created {assetName} -> {path}");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
