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
            // radius itself, so definition scale stays 1. Component-driven
            // (isStatic false): WheelBlock owns the instance under Spin.
            changed += WireBlock("BlockDefinitions/BlockDef_Wheel.asset",
                                 "Wheel_Inv.fbx", isStatic: false);
            changed += WireBlock("BlockDefinitions/BlockDef_WheelSteer.asset",
                                 "Wheel_Inv.fbx", isStatic: false);

            // Static visuals: no moving parts — BlockBehaviour attaches the
            // model generically at placement. Ends the red-cube fallback for
            // these ids everywhere (garage + arena, all spawn paths).
            // Drill: baked Rx-90 so the auger runs down the dig axis; the
            // -0.4 block-local Y offset seats the drive collar against the
            // hull (the drill cell sits one ahead of the front cube).
            changed += WireBlock("BlockDefinitions/BlockDef_Drill.asset",
                                 "Drill_Inv.fbx", isStatic: true,
                                 offset: new Vector3(0f, -0.4f, 0f));
            changed += WireBlock("BlockDefinitions/BlockDef_Rope.asset",
                                 "Rope_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_Spring.asset",
                                 "Spring_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_Hook.asset",
                                 "TipHook_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_Mace.asset",
                                 "TipMace_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_Magnet.asset",
                                 "TipMagnet_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleEmp.asset",
                                 "ModuleEmp_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleRepair.asset",
                                 "ModuleRepair_Inv.fbx", isStatic: true);
            // CPU: the capybara command cockpit (1x1x2 — head rises into
            // the cell above; origin at bottom-cell centre so offset 0).
            // CpuBlockMarker sees the model and keeps only its pulsing
            // light — no beacon mast through the pilot.
            changed += WireBlock("BlockDefinitions/BlockDef_Cpu.asset",
                                 "CapyCube_Inv.fbx", isStatic: true);
            // Remaining module family (session 133 studies).
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleBlink.asset",
                                 "ModuleBlink_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleShield.asset",
                                 "ModuleShield_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleSmoke.asset",
                                 "ModuleSmoke_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleInvis.asset",
                                 "ModuleInvis_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_ModuleMines.asset",
                                 "ModuleMines_Inv.fbx", isStatic: true);
            // Plain foil: component-driven — AeroSurfaceBlock hangs the
            // model under its Wing rig with an inverse-defaults child
            // scale (authored at FoilDefaults dims).
            changed += WireBlock("BlockDefinitions/BlockDef_Aero.asset",
                                 "Foil_Inv.fbx", isStatic: false);
            // Fin binds AeroSurfaceBlock too — same WingModel rig.
            changed += WireBlock("BlockDefinitions/BlockDef_AeroFin.asset",
                                 "Fin_Inv.fbx", isStatic: false);
            // Rudder blade never rotates (steering is force-only) and the
            // thruster's moving parts are the flame/plume, which stay on
            // the procedural rig — both take the generic static path;
            // their components suppress the procedural slab/cube.
            changed += WireBlock("BlockDefinitions/BlockDef_Rudder.asset",
                                 "Rudder_Inv.fbx", isStatic: true);
            changed += WireBlock("BlockDefinitions/BlockDef_Thruster.asset",
                                 "Thruster_Inv.fbx", isStatic: true);

            if (changed > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[InventorModelWiring] Done — {changed} definition(s) updated.");
        }

        private static int WireWeapon(string defRelPath, string fbxName, Vector3 offset)
        {
            return Apply(DefsDir + defRelPath, WeaponsDir + fbxName,
                         "_turretModel", "_turretModelScale", "_turretModelOffset",
                         1f, offset);
        }

        private static int WireBlock(string defRelPath, string fbxName, bool isStatic,
                                     Vector3 offset = default)
        {
            return Apply(DefsDir + defRelPath, BlocksDir + fbxName,
                         "_visualModel", "_visualModelScale", "_visualModelOffset",
                         1f, offset, "_visualModelStatic", isStatic);
        }

        private static int Apply(string defPath, string fbxPath,
                                 string modelProp, string scaleProp,
                                 string offsetProp, float scale, Vector3 offset,
                                 string boolProp = null, bool boolValue = false)
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

            SerializedProperty pBool = null;
            if (boolProp != null)
            {
                pBool = so.FindProperty(boolProp);
                if (pBool == null)
                {
                    Debug.LogWarning($"[InventorModelWiring] {defPath}: missing serialized field {boolProp} — skipped.");
                    return 0;
                }
            }

            bool dirty = pModel.objectReferenceValue != model
                         || !Mathf.Approximately(pScale.floatValue, scale)
                         || pOffset.vector3Value != offset
                         || (pBool != null && pBool.boolValue != boolValue);
            if (!dirty) return 0;

            pModel.objectReferenceValue = model;
            pScale.floatValue = scale;
            pOffset.vector3Value = offset;
            if (pBool != null) pBool.boolValue = boolValue;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            Debug.Log($"[InventorModelWiring] {defPath} -> {fbxPath}");
            return 1;
        }
    }
}
