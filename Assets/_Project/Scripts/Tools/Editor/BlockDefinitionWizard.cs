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

        public static void CreateTestDefinitions()
        {
            EnsureFolder(DefinitionsFolder);

            // Phase 1+2: every block reads through a shared, palette-backed
            // MK Toon material. Build them BEFORE the definitions so the
            // wizard can wire the references in a single SerializedObject pass.
            BlockMaterials.BuildAll();

            // Tints are all white now: the per-category MK Toon material
            // (BlockMaterials.ForBlockId) carries the authored colour. Tint
            // remains a multiplicative MPB override on top of that — keep
            // it white so we don't double-darken the material's hue.
            Color w = Color.white;
            CreateOrUpdate("BlockDef_Cube",       BlockIds.Cube,       "Structure Cube", BlockCategory.Structure, maxHealth: 100f, mass: 1f,   cpuCost: 1,  tint: w);
            CreateOrUpdate("BlockDef_Cpu",        BlockIds.Cpu,        "CPU",            BlockCategory.Cpu,       maxHealth: 200f, mass: 2f,   cpuCost: 0,  tint: w);
            CreateOrUpdate("BlockDef_Wheel",      BlockIds.Wheel,      "Drive Wheel",    BlockCategory.Movement,  maxHealth:  80f, mass: 1.5f, cpuCost: 25, tint: w);
            CreateOrUpdate("BlockDef_WheelSteer", BlockIds.WheelSteer, "Steer Wheel",    BlockCategory.Movement,  maxHealth:  80f, mass: 1.5f, cpuCost: 25, tint: w);
            CreateOrUpdate("BlockDef_Thruster",   BlockIds.Thruster,   "Thruster",       BlockCategory.Movement,  maxHealth:  70f, mass: 2f,   cpuCost: 30, tint: w);
            CreateOrUpdate("BlockDef_Aero",       BlockIds.Aero,       "Wing Section",   BlockCategory.Movement,  maxHealth:  50f, mass: 0.6f, cpuCost: 10, tint: w);
            CreateOrUpdate("BlockDef_AeroFin",    BlockIds.AeroFin,    "Tail Fin",       BlockCategory.Movement,  maxHealth:  50f, mass: 0.5f, cpuCost: 8,  tint: w);
            CreateOrUpdate("BlockDef_Rudder",     BlockIds.Rudder,     "Rudder",         BlockCategory.Movement,  maxHealth:  60f, mass: 0.8f, cpuCost: 15, tint: w);
            CreateOrUpdate("BlockDef_Weapon",     BlockIds.Weapon,     "Hitscan Gun",    BlockCategory.Weapon,    maxHealth:  60f, mass: 1.5f, cpuCost: 20, tint: w);
            CreateOrUpdate("BlockDef_BombBay",    BlockIds.BombBay,    "Bomb Bay",       BlockCategory.Weapon,    maxHealth: 110f, mass: 3.0f, cpuCost: 40, tint: w);
            // Rope is a Cosmetic free-body block: dangles a jointed
            // chain below the host cell. Cheap CPU + low mass so a
            // builder can hang one off any chassis without rebalancing.
            CreateOrUpdate("BlockDef_Rope",       BlockIds.Rope,       "Rope",           BlockCategory.Cosmetic,  maxHealth:  40f, mass: 0.4f, cpuCost: 5,  tint: w);
            // Rotor is a Cosmetic spinning block. Hosts an optional ring
            // of ropes radiating from its hub — the helicopter / chained
            // flail use case. Slightly heftier than a rope (it carries
            // a kinematic hub plus its rope ring) but still well below
            // structural mass.
            CreateOrUpdate("BlockDef_Rotor",      BlockIds.Rotor,      "Rotor",          BlockCategory.Cosmetic,  maxHealth:  60f, mass: 0.6f, cpuCost: 10, tint: w);
            // Hook + Mace tip blocks. Both adopt onto a rope's tip
            // segment at game-start and deal contact damage per
            // docs/PHYSICS_PLAN.md §3. Hook is light + sharp (high damage
            // per kJ, low mass means modest KE per swing). Mace is heavy +
            // blunt (low damage per kJ, high mass means big KE per swing).
            // The mass differential is the gameplay differentiator; share
            // the dmg/kJ tweakable so balance changes hit both at once.
            CreateOrUpdate("BlockDef_Hook",       BlockIds.Hook,       "Rope Hook",      BlockCategory.Weapon,    maxHealth:  60f, mass: 0.5f, cpuCost: 12, tint: w);
            CreateOrUpdate("BlockDef_Mace",       BlockIds.Mace,       "Rope Mace",      BlockCategory.Weapon,    maxHealth:  90f, mass: 2.0f, cpuCost: 18, tint: w);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Robogame] Test block definitions ready.");
        }

        /// <summary>Load a definition by its asset filename (without extension).</summary>
        public static BlockDefinition LoadByAssetName(string assetName)
        {
            string path = $"{DefinitionsFolder}/{assetName}.asset";
            return AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
        }

        /// <summary>Legacy alias kept for source compatibility.</summary>
        public static BlockDefinition LoadById(string assetName) => LoadByAssetName(assetName);

        // -----------------------------------------------------------------

        private static void CreateOrUpdate(
            string assetName,
            string stableId,
            string displayName,
            BlockCategory category,
            float maxHealth,
            float mass,
            int cpuCost,
            Color tint)
        {
            string path = $"{DefinitionsFolder}/{assetName}.asset";
            BlockDefinition def = AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
            bool created = false;
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<BlockDefinition>();
                AssetDatabase.CreateAsset(def, path);
                created = true;
            }

            SerializedObject so = new SerializedObject(def);
            so.FindProperty("_id").stringValue = stableId;
            so.FindProperty("_displayName").stringValue = displayName;
            so.FindProperty("_category").enumValueIndex = (int)category;
            so.FindProperty("_maxHealth").floatValue = maxHealth;
            so.FindProperty("_mass").floatValue = mass;
            so.FindProperty("_cpuCost").intValue = cpuCost;
            SerializedProperty tintProp = so.FindProperty("_tintColor");
            if (tintProp != null) tintProp.colorValue = tint;

            // Per-category material reference. Loaded by path right before
            // assignment to dodge the AssetDatabase fake-null pattern
            // documented in CHANGES.md.
            SerializedProperty matProp = so.FindProperty("_material");
            if (matProp != null)
            {
                Material categoryMat = BlockMaterials.ForBlockId(stableId, category);
                if (categoryMat != null) matProp.objectReferenceValue = categoryMat;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);

            if (created) Debug.Log($"[Robogame] Created {assetName} -> {path}");
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
