using Robogame.Block;
using Robogame.Combat;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Player;
using Robogame.Robots;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Authored block layouts for the canonical test chassis: ground robot,
    /// plane, and stationary combat dummy. Pure data + placement; scene
    /// composition (ground/light/camera) lives in <see cref="SceneScaffolder"/>.
    /// </summary>
    public static class RobotLayouts
    {
        // -----------------------------------------------------------------
        // Tank-style ground robot
        // -----------------------------------------------------------------

        /// <summary>Populate <paramref name="robotGO"/> with the canonical test-robot layout.</summary>
        public static Robot PopulateTestRobot(GameObject robotGO)
        {
            BlockDefinitionWizard.CreateTestDefinitions();

            robotGO.transform.position = new Vector3(0f, 1.5f, 0f);
            robotGO.transform.rotation = Quaternion.identity;

            ScaffoldHelpers.EnsureComponent<Rigidbody>(robotGO);
            ScaffoldHelpers.EnsureComponent<BlockGrid>(robotGO);
            var robot = ScaffoldHelpers.EnsureComponent<Robot>(robotGO);
            var drive = ScaffoldHelpers.EnsureComponent<RobotDrive>(robotGO);
            ScaffoldHelpers.EnsureComponent<GroundDriveSubsystem>(robotGO);

            // Tuning profiles: data-driven, no force-writes needed.
            ScaffoldHelpers.AssignTuning(drive, "_tuning",
                TuningAssets.LoadOrCreate<ChassisTuning>("ChassisTuning_Ground"));
            ScaffoldHelpers.AssignTuning(robotGO.GetComponent<GroundDriveSubsystem>(), "_tuning",
                TuningAssets.LoadOrCreate<GroundDriveTuning>("GroundDriveTuning_Default"));

            ScaffoldHelpers.WirePlayerInput(robotGO);
            ScaffoldHelpers.EnsureComponent<PlayerController>(robotGO);

            BlockGrid grid = robotGO.GetComponent<BlockGrid>();
            grid.Clear();

            BlockDefinition cube       = BlockDefinitionWizard.LoadByAssetName("BlockDef_Cube");
            BlockDefinition cpu        = BlockDefinitionWizard.LoadByAssetName("BlockDef_Cpu");
            BlockDefinition wheel      = BlockDefinitionWizard.LoadByAssetName("BlockDef_Wheel");
            BlockDefinition wheelSteer = BlockDefinitionWizard.LoadByAssetName("BlockDef_WheelSteer");
            BlockDefinition weapon     = BlockDefinitionWizard.LoadByAssetName("BlockDef_Weapon");

            // Mount + binders BEFORE placement so BlockPlaced fires correctly.
            ScaffoldHelpers.EnsureWeaponMountAndBinder(robotGO);
            ScaffoldHelpers.EnsureWheelBinder(robotGO);

            // Chassis: 3 wide × 6 long. CPU centre, turret on top, wheels at corners + mid.
            const int xMin = -1, xMax = 1, zMin = -2, zMax = 3;
            for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
            {
                bool isCpu = (x == 0 && z == 0);
                bool isWheel = (x == xMin || x == xMax) && (z == zMin || z == 0 || z == zMax);
                if (isCpu || isWheel) continue;
                grid.PlaceBlock(cube, new Vector3Int(x, 0, z));
            }
            grid.PlaceBlock(cpu, new Vector3Int(0, 0, 0));
            grid.PlaceBlock(weapon, new Vector3Int(0, 1, 0));

            grid.PlaceBlock(wheelSteer, new Vector3Int(xMin, 0, zMax));
            grid.PlaceBlock(wheelSteer, new Vector3Int(xMax, 0, zMax));
            grid.PlaceBlock(wheel,      new Vector3Int(xMin, 0, 0));
            grid.PlaceBlock(wheel,      new Vector3Int(xMax, 0, 0));
            grid.PlaceBlock(wheel,      new Vector3Int(xMin, 0, zMin));
            grid.PlaceBlock(wheel,      new Vector3Int(xMax, 0, zMin));

            ScaffoldHelpers.RemoveLegacyRootGun(robotGO);
            ScaffoldHelpers.BindFollowCameraTo(robotGO.transform);

            robot.RecalculateAggregates();
            return robot;
        }

        // -----------------------------------------------------------------
        // Test plane
        // -----------------------------------------------------------------

        /// <summary>Populate <paramref name="planeGO"/> with the canonical test-plane layout.</summary>
        public static Robot PopulateTestPlane(GameObject planeGO)
        {
            BlockDefinitionWizard.CreateTestDefinitions();

            planeGO.transform.position = new Vector3(0f, 18f, -14f);
            planeGO.transform.rotation = Quaternion.identity;

            var rb = ScaffoldHelpers.EnsureComponent<Rigidbody>(planeGO);
            rb.useGravity = true;

            ScaffoldHelpers.EnsureComponent<BlockGrid>(planeGO);
            var robot = ScaffoldHelpers.EnsureComponent<Robot>(planeGO);
            var drive = ScaffoldHelpers.EnsureComponent<RobotDrive>(planeGO);
            var planeCtrl = ScaffoldHelpers.EnsureComponent<PlaneControlSubsystem>(planeGO);

            ScaffoldHelpers.AssignTuning(drive, "_tuning",
                TuningAssets.LoadOrCreate<ChassisTuning>("ChassisTuning_Plane",
                    asset =>
                    {
                        asset.CenterOfMassOffset = Vector3.zero;
                        asset.LinearDamping = 0.05f;
                        asset.AngularDamping = 0.5f;
                    }));
            ScaffoldHelpers.AssignTuning(planeCtrl, "_tuning",
                TuningAssets.LoadOrCreate<PlaneControlTuning>("PlaneControlTuning_Default"));

            ScaffoldHelpers.WirePlayerInput(planeGO);
            ScaffoldHelpers.EnsureComponent<PlayerController>(planeGO);

            BlockGrid grid = planeGO.GetComponent<BlockGrid>();
            grid.Clear();

            BlockDefinition cube     = BlockDefinitionWizard.LoadByAssetName("BlockDef_Cube");
            BlockDefinition cpu      = BlockDefinitionWizard.LoadByAssetName("BlockDef_Cpu");
            BlockDefinition weapon   = BlockDefinitionWizard.LoadByAssetName("BlockDef_Weapon");
            BlockDefinition thruster = BlockDefinitionWizard.LoadByAssetName("BlockDef_Thruster");
            BlockDefinition aero     = BlockDefinitionWizard.LoadByAssetName("BlockDef_Aero");
            BlockDefinition aeroFin  = BlockDefinitionWizard.LoadByAssetName("BlockDef_AeroFin");

            ScaffoldHelpers.EnsureWeaponMountAndBinder(planeGO);
            ScaffoldHelpers.EnsureAeroBinder(planeGO);

            // Fuselage along Z (forward = +Z).
            grid.PlaceBlock(thruster, new Vector3Int( 0, 0, -3));
            grid.PlaceBlock(cube,     new Vector3Int( 0, 0, -2));
            grid.PlaceBlock(cube,     new Vector3Int( 0, 0, -1));
            grid.PlaceBlock(cpu,      new Vector3Int( 0, 0,  0));
            grid.PlaceBlock(cube,     new Vector3Int( 0, 0,  1));
            grid.PlaceBlock(cube,     new Vector3Int( 0, 0,  2));
            grid.PlaceBlock(cube,     new Vector3Int( 0, 0,  3));
            grid.PlaceBlock(weapon,   new Vector3Int( 0, 1,  3));

            // Main wings: 4 segments each side at z = 0, plus inner segments at z=1.
            for (int x = 1; x <= 4; x++)
            {
                grid.PlaceBlock(aero, new Vector3Int( x, 0, 0));
                grid.PlaceBlock(aero, new Vector3Int(-x, 0, 0));
            }
            grid.PlaceBlock(aero, new Vector3Int( 1, 0,  1));
            grid.PlaceBlock(aero, new Vector3Int(-1, 0,  1));
            grid.PlaceBlock(aero, new Vector3Int( 2, 0,  1));
            grid.PlaceBlock(aero, new Vector3Int(-2, 0,  1));

            // Tail: horizontal stabilisers + vertical fin.
            grid.PlaceBlock(aero, new Vector3Int( 1, 0, -3));
            grid.PlaceBlock(aero, new Vector3Int(-1, 0, -3));
            grid.PlaceBlock(aero, new Vector3Int( 2, 0, -3));
            grid.PlaceBlock(aero, new Vector3Int(-2, 0, -3));
            grid.PlaceBlock(cube,    new Vector3Int( 0, 1, -3));
            grid.PlaceBlock(aeroFin, new Vector3Int( 0, 2, -3));

            // Apply thruster tuning to every thruster instance.
            ThrusterTuning thrusterTuning = TuningAssets.LoadOrCreate<ThrusterTuning>("ThrusterTuning_Default");
            foreach (ThrusterBlock t in planeGO.GetComponentsInChildren<ThrusterBlock>(true))
            {
                ScaffoldHelpers.AssignTuning(t, "_tuning", thrusterTuning);
            }

            ScaffoldHelpers.RemoveLegacyRootGun(planeGO);
            ScaffoldHelpers.BindFollowCameraTo(planeGO.transform);

            robot.RecalculateAggregates();

            // Launch into cruise so we don't have to taxi.
            rb.linearVelocity = planeGO.transform.forward * 14f;
            return robot;
        }

        // -----------------------------------------------------------------
        // Combat dummy
        // -----------------------------------------------------------------

        /// <summary>Populate <paramref name="dummyGO"/> with a stationary 2x2x2 target.</summary>
        public static Robot PopulateCombatDummy(GameObject dummyGO)
        {
            BlockDefinitionWizard.CreateTestDefinitions();

            dummyGO.transform.position = new Vector3(8f, 1.5f, 0f);
            dummyGO.transform.rotation = Quaternion.identity;

            var rb = ScaffoldHelpers.EnsureComponent<Rigidbody>(dummyGO);
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            ScaffoldHelpers.EnsureComponent<BlockGrid>(dummyGO);
            var dummy = ScaffoldHelpers.EnsureComponent<Robot>(dummyGO);

            BlockGrid grid = dummyGO.GetComponent<BlockGrid>();
            grid.Clear();

            BlockDefinition cube = BlockDefinitionWizard.LoadByAssetName("BlockDef_Cube");
            BlockDefinition cpu  = BlockDefinitionWizard.LoadByAssetName("BlockDef_Cpu");

            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                grid.PlaceBlock(cube, new Vector3Int(x, y, z));
            }
            grid.RemoveBlock(new Vector3Int(0, 1, 0));
            grid.PlaceBlock(cpu, new Vector3Int(0, 1, 0));

            dummy.RecalculateAggregates();
            return dummy;
        }
    }
}
