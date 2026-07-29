using System.IO;
using Robogame.Block;
using Robogame.Combat;
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
        public const string WeaponDefinitionsFolder = "Assets/_Project/ScriptableObjects/WeaponDefinitions";

        public static void CreateTestDefinitions()
        {
            EnsureFolder(DefinitionsFolder);
            EnsureFolder(WeaponDefinitionsFolder);

            // Author the per-kind component-data SOs FIRST so the
            // BlockDefinition writes below can reference live assets.
            // Spin-up/overheat literals (session 155): 5→12 shots/s over
            // 1.2 s, 4 s unbroken sustain to lockout, 2.5 s cooldown.
            WeaponDefinition smgDef = CreateOrUpdateWeaponDefinition(
                "Weapon_Smg", fireRate: 12f, muzzleSpeed: 80f, spreadDeg: 1.2f, damage: 25f, recoil: 5f,
                minFireRate: 5f, spinUpSeconds: 1.2f, spinDownSeconds: 0.8f,
                overheatSeconds: 4f, overheatCooldownSeconds: 2.5f);
            BombDefinition bombDef = CreateOrUpdateBombDefinition(
                "Bomb_Default", dropInterval: 1.2f, damage: 80f, radius: 18f, initialSpeed: 2f);
            // TRACE[LOG-127]: cannon buff 60 -> 110 (survey-driven). The
            // CreateOrUpdate* helpers re-stamp EXISTING assets, so these
            // literals must track balance changes or re-runs revert them
            // (bit session 131 three times before being traced here).
            CannonDefinition cannonDef = CreateOrUpdateCannonDefinition(
                "Cannon_Default",
                fireInterval: 0.85f, muzzleSpeed: 80f, damage: 110f,
                ballRadius: 0.28f, recoil: 28f, ballMass: 5f);
            MortarDefinition mortarDef = CreateOrUpdateMortarDefinition(
                "Mortar_Default",
                fireInterval: 2.2f, muzzleSpeed: 34f, damage: 90f,
                splashRadius: 9f, recoil: 22f, knockback: 55f, shellRadius: 0.3f);

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
            // Wing (session 140): the animated bat-wing aero part. Bigger
            // authored footprint than the foil (~2 cells span) → heavier,
            // tougher, pricier. Same AeroSurfaceBlock lift path.
            CreateOrUpdate("BlockDef_Wing",       BlockIds.Wing,       "Wing",           BlockCategory.Movement,  maxHealth:  70f, mass: 1.2f, cpuCost: 18, tint: w);
            CreateOrUpdate("BlockDef_Rudder",     BlockIds.Rudder,     "Rudder",         BlockCategory.Movement,  maxHealth:  60f, mass: 0.8f, cpuCost: 15, tint: w);
            CreateOrUpdate("BlockDef_Weapon",     BlockIds.Weapon,     "Hitscan Gun",    BlockCategory.Weapon,    maxHealth:  60f, mass: 1.5f, cpuCost: 20, tint: w, componentData: smgDef);
            CreateOrUpdate("BlockDef_BombBay",    BlockIds.BombBay,    "Bomb Bay",       BlockCategory.Weapon,    maxHealth: 110f, mass: 3.0f, cpuCost: 40, tint: w, componentData: bombDef);
            // Cannon: pirate-themed gravity-projectile weapon. Slower
            // fire rate than SMG (~1 shot/sec), heavier per-hit damage,
            // gravity-affected so the player has to lead targets.
            // Heavier than the SMG block so it reads as "real artillery"
            // when placed on a chassis.
            CreateOrUpdate("BlockDef_Cannon",     BlockIds.Cannon,     "Cannon",         BlockCategory.Weapon,    maxHealth:  90f, mass: 3.5f, cpuCost: 35, tint: w, componentData: cannonDef);
            // Mortar (session 108): top-mounted indirect-fire artillery.
            // Lobs an explosive shell on a camera-offset ballistic arc.
            // Heavier than the SMG, on par with the cannon. Must mount on a
            // top face (BlockConnectivity top-mount rule).
            CreateOrUpdate("BlockDef_Mortar",     BlockIds.Mortar,     "Mortar",         BlockCategory.Weapon,    maxHealth: 100f, mass: 3.2f, cpuCost: 38, tint: w, componentData: mortarDef);
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
            // docs/subsystems/physics.md §3. Hook is light + sharp (high damage
            // per kJ, low mass means modest KE per swing). Mace is heavy +
            // blunt (low damage per kJ, high mass means big KE per swing).
            // The mass differential is the gameplay differentiator; share
            // the dmg/kJ tweakable so balance changes hit both at once.
            // Tip blocks scale up in session 22 so they actually wrap /
            // smack chassis-scale targets. Hook's J-shape is ~2 m tall
            // with a 1.5 m mouth (fits a 1 m chassis cell); mace's ball
            // is 1 m diameter. Mass scales with envelope volume — both
            // bumped roughly proportional, preserving the 3.3× hook→mace
            // mass ratio that drives the kinetic-energy differential.
            CreateOrUpdate("BlockDef_Hook",       BlockIds.Hook,       "Rope Hook",      BlockCategory.Weapon,    maxHealth: 120f, mass: 0.5f, cpuCost: 18, tint: w);
            CreateOrUpdate("BlockDef_Mace",       BlockIds.Mace,       "Rope Mace",      BlockCategory.Weapon,    maxHealth: 180f, mass: 5.0f, cpuCost: 28, tint: w);
            // Magnet (session 59): heavier than hook, lighter than
            // mace. Mid-cost CPU budget. Damage is small (DamagePerKj 0.8
            // on the component side); the value comes from the pull
            // field, not contact.
            CreateOrUpdate("BlockDef_Magnet",     BlockIds.Magnet,     "Rope Magnet",    BlockCategory.Weapon,    maxHealth: 150f, mass: 3.0f, cpuCost: 24, tint: w);
            // Grapple magnet (session 61): standalone single-shot
            // launcher. Heavier than the SMG; spends its action
            // budget on the rope + tip projectile rather than ammo.
            CreateOrUpdate("BlockDef_GrappleMagnet", BlockIds.GrappleMagnet, "Grapple Magnet", BlockCategory.Weapon, maxHealth: 140f, mass: 4.5f, cpuCost: 45, tint: w);
            // Drill (Phase 3b): carves voxel dig zones. Moderate mass +
            // mid-cost CPU. No componentData yet — drill radius + emit
            // rate live on the DrillBlock MonoBehaviour as SerializeFields.
            CreateOrUpdate("BlockDef_Drill",      BlockIds.Drill,      "Drill",          BlockCategory.Weapon,    maxHealth: 130f, mass: 3.5f, cpuCost: 32, tint: w);
            // Modules (session 105): each is its own destructible carrier whose
            // block type IS its ability (ModuleKinds maps id ↔ kind). Per-module
            // power rides the blueprint ConfigValue (no componentData SO — tuning
            // lives in ModuleTuning). Up to ModuleBudget.MaxModules per chassis.
            // Spring keeps its shipped id but is a Module now (a grounded launch).
            CreateOrUpdate("BlockDef_Spring",      BlockIds.Spring,      "Spring",       BlockCategory.Module, maxHealth:  80f, mass: 1.8f, cpuCost: 20, tint: w);
            CreateOrUpdate("BlockDef_ModuleEmp",   BlockIds.ModuleEmp,   "EMP Burst",    BlockCategory.Module, maxHealth:  90f, mass: 2.0f, cpuCost: 30, tint: w);
            CreateOrUpdate("BlockDef_ModuleBlink", BlockIds.ModuleBlink, "Blink",        BlockCategory.Module, maxHealth:  90f, mass: 2.0f, cpuCost: 30, tint: w);
            CreateOrUpdate("BlockDef_ModuleShield",BlockIds.ModuleShield,"Disc Shield",  BlockCategory.Module, maxHealth:  90f, mass: 2.0f, cpuCost: 30, tint: w);
            CreateOrUpdate("BlockDef_ModuleSmoke", BlockIds.ModuleSmoke, "Smoke",        BlockCategory.Module, maxHealth:  90f, mass: 2.0f, cpuCost: 25, tint: w);
            CreateOrUpdate("BlockDef_ModuleInvis", BlockIds.ModuleInvis, "Cloak",        BlockCategory.Module, maxHealth:  90f, mass: 2.0f, cpuCost: 35, tint: w);
            // Mines (session 108): drops a ground proximity mine that detonates
            // when an enemy drives over it. Cheap-ish carrier; power (centre
            // damage) + cooldown ride ModuleTuning like the other modules.
            CreateOrUpdate("BlockDef_ModuleMines", BlockIds.ModuleMines, "Mines",        BlockCategory.Module, maxHealth:  90f, mass: 2.2f, cpuCost: 28, tint: w);
            // Wave-1 prototype suite (session 155, docs/research/ea-block-triage.md).
            // All literals are first-pass placeholders pending live playtest.
            // Counterweight: dense trim ballast — mass is the whole feature.
            // Feather: huge-but-light fragile bulk; visual oversize deferred to
            // scalable-parts Phase 4, so for now it's a normal cell that weighs
            // almost nothing and pops if you sneeze at it.
            CreateOrUpdate("BlockDef_Gyro",          BlockIds.Gyro,          "Gyro",          BlockCategory.Movement,  maxHealth:  70f, mass: 2.0f,  cpuCost: 30, tint: w);
            CreateOrUpdate("BlockDef_Pogo",          BlockIds.Pogo,          "Pogo",          BlockCategory.Movement,  maxHealth:  70f, mass: 1.5f,  cpuCost: 20, tint: w);
            CreateOrUpdate("BlockDef_Counterweight", BlockIds.Counterweight, "Counterweight", BlockCategory.Structure, maxHealth: 150f, mass: 8f,    cpuCost: 5,  tint: w);
            CreateOrUpdate("BlockDef_Feather",       BlockIds.Feather,       "Feather Block", BlockCategory.Structure, maxHealth:  30f, mass: 0.15f, cpuCost: 2,  tint: w);
            CreateOrUpdate("BlockDef_SpikeArmor",    BlockIds.SpikeArmor,    "Spike Armor",   BlockCategory.Structure, maxHealth: 120f, mass: 1.5f,  cpuCost: 8,  tint: w);
            CreateOrUpdate("BlockDef_WedgeArmor",    BlockIds.WedgeArmor,    "Wedge Armor",   BlockCategory.Structure, maxHealth: 110f, mass: 1.2f,  cpuCost: 6,  tint: w);
            CreateOrUpdate("BlockDef_Fuse",          BlockIds.Fuse,          "Fuse",          BlockCategory.Structure, maxHealth:  80f, mass: 1.0f,  cpuCost: 6,  tint: w);

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

        // -----------------------------------------------------------------

        private static void CreateOrUpdate(
            string assetName,
            string stableId,
            string displayName,
            BlockCategory category,
            float maxHealth,
            float mass,
            int cpuCost,
            Color tint,
            ScriptableObject componentData = null)
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

            // Component data (kind-specific stat blob: WeaponDefinition,
            // BombDefinition, …). The wizard passes a live SO ref; the
            // serialized property writes by ObjectReference so the
            // BlockDefinition.asset on disk carries the GUID.
            SerializedProperty cdProp = so.FindProperty("_componentData");
            if (cdProp != null)
            {
                cdProp.objectReferenceValue = componentData;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);

            if (created) Debug.Log($"[Robogame] Created {assetName} -> {path}");
        }

        // -----------------------------------------------------------------
        // Per-kind component-data assets (WeaponDefinition / BombDefinition)
        // -----------------------------------------------------------------

        private static WeaponDefinition CreateOrUpdateWeaponDefinition(
            string assetName, float fireRate, float muzzleSpeed, float spreadDeg, float damage, float recoil,
            float minFireRate, float spinUpSeconds, float spinDownSeconds,
            float overheatSeconds, float overheatCooldownSeconds)
        {
            string path = $"{WeaponDefinitionsFolder}/{assetName}.asset";
            WeaponDefinition def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(def, path);
                Debug.Log($"[Robogame] Created {assetName} -> {path}");
            }
            SerializedObject so = new SerializedObject(def);
            so.FindProperty("_fireRate").floatValue      = fireRate;
            so.FindProperty("_muzzleSpeed").floatValue   = muzzleSpeed;
            so.FindProperty("_spreadDeg").floatValue     = spreadDeg;
            so.FindProperty("_damage").floatValue        = damage;
            so.FindProperty("_recoilImpulse").floatValue = recoil;
            so.FindProperty("_minFireRate").floatValue             = minFireRate;
            so.FindProperty("_spinUpSeconds").floatValue           = spinUpSeconds;
            so.FindProperty("_spinDownSeconds").floatValue         = spinDownSeconds;
            so.FindProperty("_overheatSeconds").floatValue         = overheatSeconds;
            so.FindProperty("_overheatCooldownSeconds").floatValue = overheatCooldownSeconds;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        private static BombDefinition CreateOrUpdateBombDefinition(
            string assetName, float dropInterval, float damage, float radius, float initialSpeed)
        {
            string path = $"{WeaponDefinitionsFolder}/{assetName}.asset";
            BombDefinition def = AssetDatabase.LoadAssetAtPath<BombDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<BombDefinition>();
                AssetDatabase.CreateAsset(def, path);
                Debug.Log($"[Robogame] Created {assetName} -> {path}");
            }
            SerializedObject so = new SerializedObject(def);
            so.FindProperty("_dropInterval").floatValue = dropInterval;
            so.FindProperty("_damage").floatValue       = damage;
            so.FindProperty("_radius").floatValue       = radius;
            so.FindProperty("_initialSpeed").floatValue = initialSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        private static CannonDefinition CreateOrUpdateCannonDefinition(
            string assetName, float fireInterval, float muzzleSpeed, float damage,
            float ballRadius, float recoil, float ballMass)
        {
            string path = $"{WeaponDefinitionsFolder}/{assetName}.asset";
            CannonDefinition def = AssetDatabase.LoadAssetAtPath<CannonDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<CannonDefinition>();
                AssetDatabase.CreateAsset(def, path);
                Debug.Log($"[Robogame] Created {assetName} -> {path}");
            }
            SerializedObject so = new SerializedObject(def);
            so.FindProperty("_fireInterval").floatValue   = fireInterval;
            so.FindProperty("_muzzleSpeed").floatValue    = muzzleSpeed;
            so.FindProperty("_damage").floatValue         = damage;
            so.FindProperty("_ballRadius").floatValue     = ballRadius;
            so.FindProperty("_recoilImpulse").floatValue  = recoil;
            so.FindProperty("_ballMass").floatValue       = ballMass;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
        }

        private static MortarDefinition CreateOrUpdateMortarDefinition(
            string assetName, float fireInterval, float muzzleSpeed, float damage,
            float splashRadius, float recoil, float knockback, float shellRadius)
        {
            string path = $"{WeaponDefinitionsFolder}/{assetName}.asset";
            MortarDefinition def = AssetDatabase.LoadAssetAtPath<MortarDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MortarDefinition>();
                AssetDatabase.CreateAsset(def, path);
                Debug.Log($"[Robogame] Created {assetName} -> {path}");
            }
            SerializedObject so = new SerializedObject(def);
            so.FindProperty("_fireInterval").floatValue     = fireInterval;
            so.FindProperty("_muzzleSpeed").floatValue      = muzzleSpeed;
            so.FindProperty("_damage").floatValue           = damage;
            so.FindProperty("_splashRadius").floatValue     = splashRadius;
            so.FindProperty("_recoilImpulse").floatValue    = recoil;
            so.FindProperty("_knockbackImpulse").floatValue = knockback;
            so.FindProperty("_shellRadius").floatValue      = shellRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return def;
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
