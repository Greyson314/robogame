using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Points weapon + block definitions at the inventor-aesthetic FBX
    /// models (session 132). Idempotent: run it any number of times;
    /// it only writes when a reference actually changes. Deliberately
    /// NOT part of BlockDefinitionWizard — that wizard re-stamps stats
    /// on existing assets (131 finding), and art wiring must never
    /// ride along with a stats reset.
    /// </summary>
    // TRACE[LOG-132]: inventor model wiring — weapons via the
    // WeaponModelRig contract, blocks via BlockDefinition._visualModel.
    public static class InventorModelWiring
    {
        private const string WeaponsDir = "Assets/_Project/Art/Models/Weapons/";
        private const string BlocksDir = "Assets/_Project/Art/Models/Blocks/Inv/";
        private const string DefsDir = "Assets/_Project/ScriptableObjects/";

        [MenuItem("Robogame/Art/Wire Inventor Models")]
        public static void Wire()
        {
            int changed = 0;

            // Turreted / static weapons: same three serialized fields on
            // WeaponStatsDefinition (BombDefinition inherits them).
            // Model origin = block bottom (gear base) -> offset -0.5;
            // the bomb bay is authored around the block centre -> 0.
            changed += WireWeapon("WeaponDefinitions/Weapon_Smg.asset",
                                  "SMG_Inv.fbx", new Vector3(0f, -0.5f, 0f));
            changed += WireWeapon("WeaponDefinitions/Cannon_Default.asset",
                                  "Cannon_Inv.fbx", new Vector3(0f, -0.5f, 0f));
            changed += WireWeapon("WeaponDefinitions/Mortar_Default.asset",
                                  "Mortar_Inv.fbx", new Vector3(0f, -0.5f, 0f));
            changed += WireWeapon("WeaponDefinitions/Bomb_Default.asset",
                                  "BombBay_Inv.fbx", Vector3.zero);

            // Blocks on the BlockDefinition visual-model path. Wheel model
            // is authored at 1 m diameter; WheelBlock scales by physics
            // radius itself, so definition scale stays 1.
            changed += WireBlock("BlockDefinitions/BlockDef_Wheel.asset",
                                 "Wheel_Inv.fbx");
            changed += WireBlock("BlockDefinitions/BlockDef_WheelSteer.asset",
                                 "Wheel_Inv.fbx");

            if (changed > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[InventorModelWiring] Done — {changed} definition(s) updated.");
        }

        private static int WireWeapon(string defRelPath, string fbxName, Vector3 offset)
        {
            return Apply(DefsDir + defRelPath, WeaponsDir + fbxName,
                         "_turretModel", "_turretModelScale", "_turretModelOffset",
                         1f, offset);
        }

        private static int WireBlock(string defRelPath, string fbxName)
        {
            return Apply(DefsDir + defRelPath, BlocksDir + fbxName,
                         "_visualModel", "_visualModelScale", "_visualModelOffset",
                         1f, Vector3.zero);
        }

        private static int Apply(string defPath, string fbxPath,
                                 string modelProp, string scaleProp,
                                 string offsetProp, float scale, Vector3 offset)
        {
            var def = AssetDatabase.LoadAssetAtPath<ScriptableObject>(defPath);
            if (def == null)
            {
                Debug.LogWarning($"[InventorModelWiring] Missing definition: {defPath}");
                return 0;
            }
            // Re-load the model by path right before assignment —
            // AssetDatabase.Refresh invalidates stale C# refs.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null)
            {
                Debug.LogWarning($"[InventorModelWiring] Missing model: {fbxPath}");
                return 0;
            }

            var so = new SerializedObject(def);
            SerializedProperty pModel = so.FindProperty(modelProp);
            SerializedProperty pScale = so.FindProperty(scaleProp);
            SerializedProperty pOffset = so.FindProperty(offsetProp);
            if (pModel == null || pScale == null || pOffset == null)
            {
                Debug.LogWarning($"[InventorModelWiring] {defPath}: missing serialized " +
                                 $"field(s) {modelProp}/{scaleProp}/{offsetProp} — skipped.");
                return 0;
            }

            bool dirty = pModel.objectReferenceValue != model
                         || !Mathf.Approximately(pScale.floatValue, scale)
                         || pOffset.vector3Value != offset;
            if (!dirty) return 0;

            pModel.objectReferenceValue = model;
            pScale.floatValue = scale;
            pOffset.vector3Value = offset;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            Debug.Log($"[InventorModelWiring] {defPath} -> {fbxPath}");
            return 1;
        }
    }
}
