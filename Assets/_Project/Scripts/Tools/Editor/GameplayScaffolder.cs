using System.Collections.Generic;
using System.IO;
using Robogame.Block;
using Robogame.Core;
using Robogame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Pass A scaffolder: creates the data assets (block definition
    /// library, default blueprints) and builds the Bootstrap / Garage /
    /// Arena scenes wired for the runtime state machine.
    /// </summary>
    /// <remarks>
    /// Idempotent menu items live under <c>Robogame/Scaffold/Gameplay/...</c>.
    /// The legacy "Build Test Robot/Plane/Dummy" entries in
    /// <see cref="SceneScaffolder"/> are unaffected — they still drop a
    /// hand-wired chassis directly into the Garage scene for fast iteration.
    /// </remarks>
    public static class GameplayScaffolder
    {
        private const string SoFolder = "Assets/_Project/ScriptableObjects";
        private const string BlueprintFolder = SoFolder + "/Blueprints";
        private const string LibraryAssetPath = SoFolder + "/BlockDefinitionLibrary.asset";
        private const string DefaultGroundPath = BlueprintFolder + "/Blueprint_DefaultGround.asset";
        private const string DefaultPlanePath = BlueprintFolder + "/Blueprint_DefaultPlane.asset";
        // Session 61: retired the Buggy preset; replaced its slot with
        // the Grappler — a plane mounting a grapple-magnet weapon.
        private const string DefaultGrapplerPath = BlueprintFolder + "/Blueprint_DefaultGrappler.asset";
        private const string DefaultBoatPath = BlueprintFolder + "/Blueprint_DefaultBoat.asset";
        private const string DefaultBomberPath = BlueprintFolder + "/Blueprint_DefaultBomber.asset";
        private const string DefaultPropPlanePath = BlueprintFolder + "/Blueprint_DefaultPropPlane.asset";
        private const string DefaultHelicopterPath = BlueprintFolder + "/Blueprint_DefaultHelicopter.asset";
        private const string CombatDummyPath = BlueprintFolder + "/Blueprint_CombatDummy.asset";
        private const string StressTowerPath = BlueprintFolder + "/Blueprint_StressRotorTower.asset";
        private const string ArchDummyPath = BlueprintFolder + "/Blueprint_ArchDummy.asset";
        private const string StressRopeTowerPath = BlueprintFolder + "/Blueprint_StressRopeTower.asset";

        // -----------------------------------------------------------------
        // Data assets
        // -----------------------------------------------------------------

        public static BlockDefinitionLibrary PopulateBlockDefinitionLibrary()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            EnsureFolder(SoFolder);

            BlockDefinitionLibrary lib = LoadOrCreateAsset<BlockDefinitionLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                Debug.LogError($"[Robogame] Could not load or create BlockDefinitionLibrary at {LibraryAssetPath}.");
                return null;
            }

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BlockDefinition));
            var defs = new List<BlockDefinition>(guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                BlockDefinition def = AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
                if (def != null) defs.Add(def);
            }

            lib.SetDefinitions(defs.ToArray());
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] BlockDefinitionLibrary populated with {defs.Count} entries (asset persisted: {AssetDatabase.Contains(lib)}).");
            return lib;
        }

        // CreateDefaultBlueprintsMenu was the menu wrapper; menu hidden,
        // but BuildAllPassA still calls CreateDefaultBlueprints directly.

        public static ChassisBlueprint CreateDefaultBlueprints()
        {
            BlockDefinitionWizard.CreateTestDefinitions();
            EnsureFolder(BlueprintFolder);

            // Load the library by path so every preset script can resolve
            // BlockDefinitions for ScriptedChassisBuilder. The library was
            // populated immediately before us in BuildAllPassA / BuildAll;
            // load-by-path here is robust against AssetDatabase.Refresh
            // invalidations that might have stale C# refs from elsewhere.
            BlockDefinitionLibrary lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                Debug.LogError(
                    $"[Robogame] CreateDefaultBlueprints: BlockDefinitionLibrary load FAILED at {LibraryAssetPath}. " +
                    "Run Robogame > Scaffold > Gameplay > Populate Block Definition Library first.");
                return null;
            }

            ChassisBlueprint ground = CreateOrUpdateBlueprint(DefaultGroundPath, BuildGroundPlan(lib));
            CreateOrUpdateBlueprint(DefaultPlanePath, BuildPlanePlan(lib));
            CreateOrUpdateBlueprint(DefaultGrapplerPath, BuildGrapplerPlan(lib));
            CreateOrUpdateBlueprint(DefaultBoatPath,  BuildBoatPlan(lib));
            CreateOrUpdateBlueprint(DefaultBomberPath, BuildBomberPlan(lib));
            // Propeller plane: same wing/tail layout as the standard plane
            // but propulsion comes from a forward-facing rotor + 4-blade
            // propeller ring at the nose instead of a thruster at the tail.
            // RotorsGenerateLift = true flips the prop's foils into thrust
            // mode (force along the rotor's spin axis = chassis +Z).
            CreateOrUpdateBlueprint(DefaultPropPlanePath, BuildPropPlanePlan(lib));
            // Helicopter: rotor on top of cabin generates lift via
            // RotorsGenerateLift. Ground-kind so it spawns on the pad;
            // forward-flight input is a separate session.
            CreateOrUpdateBlueprint(DefaultHelicopterPath, BuildHelicopterPlan(lib));
            CreateOrUpdateBlueprint(CombatDummyPath, BuildCombatDummyPlan(lib));
            // Stress-test target: tall column of rotors. Spawn-gated by
            // Stress.RotorTower tweakable in the settings panel.
            CreateOrUpdateBlueprint(StressTowerPath, BuildStressTowerPlan(lib));
            // Verlet rope profiling target (PHYSICS_PLAN § 2 trigger #1):
            // 5 rotor levels × 4 ropes ringing each rotor's mech cell =
            // 20 chains × 8 segs = 160 particles for the Verlet sim.
            CreateOrUpdateBlueprint(StressRopeTowerPath, BuildStressRopeTowerPlan(lib));
            // Arch dummy: pillars + beam, sized so the J-hook's 1.5 m mouth
            // scoops the top beam cleanly.
            CreateOrUpdateBlueprint(ArchDummyPath, BuildArchDummyPlan(lib));
            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] Default blueprints created via ScriptedChassisBuilder (ground asset persisted: {AssetDatabase.Contains(ground)}).");
            return ground;
        }

        private static BlueprintPlan BuildGroundPlan(BlockDefinitionLibrary lib)
        {
            // 3-wide × 6-long floor (x ∈ [-1,1], z ∈ [-2,3]), CPU at centre,
            // weapon on top, six wheels side-mounted on the outermost cubes.
            // Build order is CPU → outward so every Place call has a placed
            // host on its mount face — the same constraint a player faces
            // building this from scratch in the garage.
            //
            // Cube Up directions point back toward a placed neighbor; cubes
            // are visually symmetric so the choice is arbitrary as long as
            // PlacementRules' host-exists check passes.
            var sb = ScriptedChassisBuilder.Create("Tank", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Central z-axis from CPU outward (north then south). Up =
                // step direction so each cube hosts on the previous one.
                Vector3Int forwardStep = new Vector3Int(0, 0, 1);
                Vector3Int backStep    = new Vector3Int(0, 0, -1);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  1), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  2), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  3), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -1), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -2), backStep);
                // Side strips mirrored across X. Each x=1 cube mounts on the
                // x=0 column placed above; mirror produces the x=-1 cube
                // mounting on the corresponding x=0 cell.
                Vector3Int rightStep = new Vector3Int(1, 0, 0);
                sb.MirrorX(b => b
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  0), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  1), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  2), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  3), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0, -1), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0, -2), rightStep));
                // Top weapon on CPU's +Y face.
                sb.Place(BlockIds.Weapon, new Vector3Int(0, 1, 0), Vector3Int.up);
                // Wheels: side-mount stem extends outward from the
                // outermost cube. Each wheel's host = (±1, 0, z); up =
                // ±X. Mirrored across X for the opposite side.
                sb.MirrorX(b => b
                    .Place(BlockIds.WheelSteer, new Vector3Int(2, 0,  3), rightStep)
                    .Place(BlockIds.Wheel,      new Vector3Int(2, 0,  0), rightStep)
                    .Place(BlockIds.Wheel,      new Vector3Int(2, 0, -2), rightStep));
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildPlanePlan(BlockDefinitionLibrary lib)
        {
            // Plane: side-mount wings + canards + tail stabs, top-mount
            // vertical fin on a riser, tail-hanging rope+hook for the
            // contact-damage hot path. Thruster sits on top of the tail
            // spine end with up=+Y so ThrusterBlock's `transform.forward`
            // resolves to chassis +Z (forward push). With up != +Y,
            // OrientationFromUp's fwdSeed fallback rotates the forward
            // axis sideways — see ThrusterBlock.Tick.
            //
            // Layout (forward = +Z):
            //   - Spine cubes z=-3..3 with CPU at z=0.
            //   - Top weapon at the nose (0, 1, 3).
            //   - Thruster on top of the tail end: (0, 1, -3) up=+Y.
            //   - Main wing pair: side-mounted on the CPU, span 4.
            //   - Forward canards: side-mounted on (0,0,1), span 2.
            //   - Tail stabilisers: side-mounted on (0,0,-2), span 2.
            //   - Vertical fin: top-mounted on a riser above (0,0,-2), span 2.
            //   - Tail rope+hook on -Y face of (0,0,-2).
            Vector3 wingDims  = new Vector3(4f, 0.08f, 0.9f);
            Vector3 stabDims  = new Vector3(2f, 0.08f, 0.7f);
            Vector3 finDims   = new Vector3(2f, 0.08f, 0.9f);
            Vector3Int upRight = new Vector3Int( 1, 0, 0);
            Vector3Int upTop   = new Vector3Int( 0, 1, 0);
            Vector3Int upDown  = new Vector3Int( 0,-1, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Plane", ChassisKind.Plane, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Forward spine (z=1..3), then aft spine (z=-1..-3). Each
                // cube hosts on the previous one along the spine.
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  1), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  2), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  3), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -1), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -2), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -3), backStep);
                // Thruster on +Y face of the tail-end cube. up=+Y is the
                // only orientation that gives ThrusterBlock the correct
                // forward axis (chassis +Z) for push direction.
                sb.Place(BlockIds.Thruster, new Vector3Int(0, 1, -3), upTop);
                // Top weapon at the nose.
                sb.Place(BlockIds.Weapon, new Vector3Int(0, 1, 3), upTop);
                // Main wings + canards + tail stabs — mirrored.
                sb.MirrorX(b => b
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  0), upRight, wingDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  1), upRight, stabDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0, -2), upRight, stabDims));
                // Vertical fin: riser cube on top of (0,0,-2), then the
                // fin top-mounted on the riser.
                sb.Place(BlockIds.Cube,    new Vector3Int(0, 1, -2), upTop);
                sb.Place(BlockIds.AeroFin, new Vector3Int(0, 2, -2), upTop, finDims);
                // Rope+hook hanging off the -Y face of the tail-boom cube
                // (0,0,-2). Rope's mount-up = -Y (chain dangles downward);
                // hook lands at ropeCell + lengthCells * up.
                sb.RopeWithHook(new Vector3Int(0, -1, -2), upDown, lengthCells: 1);
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildGrapplerPlan(BlockDefinitionLibrary lib)
        {
            // Plane variant kitted for utility play: same airframe as the
            // default Plane (spine, wings, canards, tail stabs, fin), but
            // sports a Grapple Magnet on the nose instead of an SMG and
            // *two* thrusters so the player has the extra acceleration
            // they need to drag a heavy target around on the rope.
            //
            // No tail hook — the grapple magnet is the only swung tool
            // on this preset. (Hook + grapple together is a viable
            // future preset.)
            Vector3 wingDims  = new Vector3(4f, 0.08f, 0.9f);
            Vector3 stabDims  = new Vector3(2f, 0.08f, 0.7f);
            Vector3 finDims   = new Vector3(2f, 0.08f, 0.9f);
            Vector3Int upRight = new Vector3Int( 1, 0, 0);
            Vector3Int upTop   = new Vector3Int( 0, 1, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Grappler", ChassisKind.Plane, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  1), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  2), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  3), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -1), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -2), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -3), backStep);
                // Twin thrusters along the spine top, both with up=+Y.
                // up=+Y is the only orientation that lets ThrusterBlock
                // resolve its forward axis to chassis +Z (see the rant
                // in BuildPlanePlan). That means each thruster's host
                // must be one cell BELOW it on the spine — no -Y face
                // mount is available because there's no block under the
                // tail cube. So we stack two thrusters at (0,1,-3) and
                // (0,1,-1), hosted by spine cubes (0,0,-3) and (0,0,-1).
                // Twice the forward thrust, no airframe restructure.
                sb.Place(BlockIds.Thruster, new Vector3Int(0, 1, -3), upTop);
                sb.Place(BlockIds.Thruster, new Vector3Int(0, 1, -1), upTop);
                // Grapple magnet on the bottom-rear face of the tail-
                // boom cube (0, 0, -2). Same mount as the default
                // Plane's tail rope+hook, on purpose: apples-to-apples
                // comparison between this fired-from-a-gun grapple and
                // a chassis-attached rope+magnet on the same airframe
                // position. up=-Y means the block's local +Y axis points
                // downward, so the yoke + barrel will hang under the
                // tail rather than rise above the nose.
                Vector3Int upDown = new Vector3Int(0, -1, 0);
                sb.Place(BlockIds.GrappleMagnet, new Vector3Int(0, -1, -2), upDown);
                // Same wing kit as the default plane: main wings on the
                // CPU, canards forward, stabs back.
                sb.MirrorX(b => b
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  0), upRight, wingDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  1), upRight, stabDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0, -2), upRight, stabDims));
                // Vertical fin: riser cube on top of (0,0,-2), fin on top.
                sb.Place(BlockIds.Cube,    new Vector3Int(0, 1, -2), upTop);
                sb.Place(BlockIds.AeroFin, new Vector3Int(0, 2, -2), upTop, finDims);
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildBoatPlan(BlockDefinitionLibrary lib)
        {
            // 5w × 7d flat hull, CPU at centre, rear thruster on deck,
            // rudder below stern, bow gun up front. Buoyancy math sits at
            // ~94% submerged with default water tweakables (density=4,
            // displacement=0.30) → 39 kg vs 412 N at full submersion.
            const int xMin = -2, xMax = 2;
            const int zMin = -3, zMax = 3;
            Vector3Int rightStep = new Vector3Int(1, 0, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0, -1);
            Vector3Int upDown      = new Vector3Int(0, -1, 0);

            var sb = ScriptedChassisBuilder.Create("Boat", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Central z-axis from CPU outward to the bow / stern.
                for (int z = 1; z <= zMax; z++)
                    sb.Place(BlockIds.Cube, new Vector3Int(0, 0, z), forwardStep);
                for (int z = -1; z >= zMin; z--)
                    sb.Place(BlockIds.Cube, new Vector3Int(0, 0, z), backStep);
                // Side strips: each x-step mirrored, each row along z fans
                // out from the x=0 column. Build x=1 first, then x=2; for
                // each x, sweep z from 0 outward.
                for (int x = 1; x <= xMax; x++)
                {
                    int xLocal = x;
                    sb.MirrorX(b =>
                    {
                        b.Place(BlockIds.Cube, new Vector3Int(xLocal, 0, 0), rightStep);
                        for (int z = 1; z <= zMax; z++)
                            b.Place(BlockIds.Cube, new Vector3Int(xLocal, 0, z), rightStep);
                        for (int z = -1; z >= zMin; z--)
                            b.Place(BlockIds.Cube, new Vector3Int(xLocal, 0, z), rightStep);
                    });
                }
                // Rear thruster on top of stern (above water line).
                sb.Place(BlockIds.Thruster, new Vector3Int(0, 1, zMin), Vector3Int.up);
                // Rudder hangs below stern.
                sb.Place(BlockIds.Rudder, new Vector3Int(0, -1, zMin), upDown);
                // Bow gun.
                sb.Place(BlockIds.Weapon, new Vector3Int(0, 1, zMax), Vector3Int.up);
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildBomberPlan(BlockDefinitionLibrary lib)
        {
            // Bomber: same wing/tail skeleton as the plane but with a
            // bomb bay slung under the CPU and a wider main wing (span 5).
            // Thruster on top of tail spine with up=+Y so the forward axis
            // resolves correctly — see plane's note on ThrusterBlock.
            Vector3 wingDims = new Vector3(5f, 0.08f, 1.0f);
            Vector3 stabDims = new Vector3(2f, 0.08f, 0.7f);
            Vector3 finDims  = new Vector3(2f, 0.08f, 0.9f);
            Vector3Int upRight = new Vector3Int( 1, 0, 0);
            Vector3Int upTop   = new Vector3Int( 0, 1, 0);
            Vector3Int upDown  = new Vector3Int( 0,-1, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Bomber", ChassisKind.Plane, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Forward + aft spine.
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  1), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  2), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  3), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -1), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -2), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -3), backStep);
                sb.Place(BlockIds.Thruster, new Vector3Int(0, 1, -3), upTop);
                // Bomb bay on CPU's -Y face.
                sb.Place(BlockIds.BombBay, new Vector3Int(0, -1, 0), upDown);
                // Wings + canards + tail stabs mirrored.
                sb.MirrorX(b => b
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  0), upRight, wingDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  1), upRight, stabDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0, -2), upRight, stabDims));
                // Vertical fin riser + fin.
                sb.Place(BlockIds.Cube,    new Vector3Int(0, 1, -2), upTop);
                sb.Place(BlockIds.AeroFin, new Vector3Int(0, 2, -2), upTop, finDims);
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildPropPlanePlan(BlockDefinitionLibrary lib)
        {
            // Prop plane: plane skeleton with a forward-facing nose rotor
            // instead of a rear thruster. spinAxis=+Z; the foil ring at
            // (0,0,5) and ±x / ±y produces forward thrust when
            // RotorsGenerateLift is set on the blueprint.
            Vector3 wingDims = new Vector3(4f, 0.08f, 0.9f);
            Vector3 stabDims = new Vector3(2f, 0.08f, 0.7f);
            Vector3 finDims  = new Vector3(2f, 0.08f, 0.9f);
            Vector3Int upRight = new Vector3Int( 1, 0, 0);
            Vector3Int upTop   = new Vector3Int( 0, 1, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Prop Plane", ChassisKind.Plane, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  1), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  2), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  3), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -1), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -2), backStep);
                sb.Place(BlockIds.Weapon, new Vector3Int(0, 1, 0), Vector3Int.up);
                sb.MirrorX(b => b
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  0), upRight, wingDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0,  1), upRight, stabDims)
                    .Place(BlockIds.Aero, new Vector3Int(1, 0, -2), upRight, stabDims));
                sb.Place(BlockIds.Cube,    new Vector3Int(0, 1, -2), upTop);
                sb.Place(BlockIds.AeroFin, new Vector3Int(0, 2, -2), upTop, finDims);
                // Nose rotor with 4-foil propeller ring. spinAxis = +Z
                // → rotor hosts on the +Z face of (0,0,3) (nose spine cap);
                // auto-companion drops the mechanism cube at (0,0,5); foils
                // ring at (±1,0,5) and (0,±1,5).
                sb.RotorWithFoils(new Vector3Int(0, 0, 4), forwardStep);
                sb.RotorsGenerateLift(true);
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildHelicopterPlan(BlockDefinitionLibrary lib)
        {
            // Larger helicopter sandbox (~38 cells). Authored as a sequence
            // of player-style placements — every cube hosts on a placed
            // neighbor, every weapon / rotor / foil has a face-adjacent
            // cabin or mechanism cube to mount on.
            //
            // Plan side profile (z increases forward, +X = right):
            //
            //                A       y=3 mechanism cell + foil ring
            //             A  M  A    (foils are the absolute topmost blocks)
            //                A
            //                R       y=2 rotor stem (host cell of RotorBlock)
            //          # # # # #     y=1 cabin roof (3w × 4d slab)
            //          # # C # #     y=0 cabin floor (CPU at centre)
            //        G # # # # # G   y=0 outboard hardpoint guns at |x|=2
            //          # # # # #
            //                #       y=0 tail boom (4 cells, x=0 only)
            //                #
            //                #
            //                F       y=1 vertical tail fin at the boom tip
            //
            // Session 23 design note: no cosmetic tail rotor (asymmetric
            // mass creates off-diagonal inertia moments that fight
            // RobotDrive's center-of-mass override).
            Vector3Int upTop      = new Vector3Int(0, 1, 0);
            Vector3Int rightStep  = new Vector3Int(1, 0, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Helicopter", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Central spine outward from CPU (forward then aft).
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  1), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  2), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0,  3), forwardStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -1), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -2), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -3), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -4), backStep);
                sb.Place(BlockIds.Cube, new Vector3Int(0, 0, -5), backStep);
                // Cabin sides at |x|=1, z=-1..2. Each x=1 cube mounts on
                // the corresponding x=0 cell (rightStep up); mirror builds
                // the -X side simultaneously.
                sb.MirrorX(b => b
                    .Place(BlockIds.Cube, new Vector3Int(1, 0, -1), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  0), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  1), rightStep)
                    .Place(BlockIds.Cube, new Vector3Int(1, 0,  2), rightStep));
                // Outboard hardpoint guns at |x|=2, z=0 — mount on the
                // cabin side cubes at |x|=1 (rightStep up).
                sb.MirrorX(b => b.Place(BlockIds.Weapon, new Vector3Int(2, 0, 0), rightStep));
                // Cabin roof at y=1, z=-1..2, x=-1..1. Every cell hosts on
                // its y=0 counterpart (placed above). Up = +Y for every roof
                // cell — Box default works because all the floor cells are
                // there.
                sb.Box(BlockIds.Cube, new Vector3Int(-1, 1, -1), new Vector3Int(1, 1, 2));
                // Vertical tail fin on top of the boom tip cube (0,0,-5).
                sb.Place(BlockIds.AeroFin, new Vector3Int(0, 1, -5), upTop);
                // Main rotor at (0, 2, 0) above the cabin roof. The rotor
                // hosts on (0, 1, 0) (a roof cell); auto-companion drops
                // the mechanism cube at (0, 3, 0); the four foils ring
                // around it.
                sb.RotorWithFoils(new Vector3Int(0, 2, 0));
                sb.RotorsGenerateLift(true);
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildCombatDummyPlan(BlockDefinitionLibrary lib)
        {
            // Solid 5×5×6 fortress with a CPU head one cell above the
            // roof centre. The CPU sits at (0, 6, 0); every body cell must
            // be CPU-reachable, so we build top-down from a temp top cell
            // adjacent to the CPU.
            //
            // Key constraint: CPU is at (0, 6, 0), so the layer at y=5 must
            // contain the cell (0, 5, 0) (CPU-adjacent). We place that
            // first, then expand the y=5 layer outward, then drop to y=4,
            // etc. Each cube's Up points back toward a placed neighbor.
            const int half = 2;
            const int height = 6;
            Vector3Int upDown = new Vector3Int(0, -1, 0);
            Vector3Int rightStep = new Vector3Int(1, 0, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Combat Dummy", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, new Vector3Int(0, height, 0));
                // For each Y from height-1 down to 0, fill the 5×5 layer
                // starting at (0, y, 0) which is adjacent to either the
                // CPU (top layer) or the (0, y+1, 0) cell we just placed.
                for (int y = height - 1; y >= 0; y--)
                {
                    int yLocal = y;
                    // (0, y, 0) — mounts on (0, y+1, 0) above (or CPU at top).
                    sb.Place(BlockIds.Cube, new Vector3Int(0, yLocal, 0), upDown);
                    // Central z-axis at this Y, growing out from (0, y, 0).
                    for (int z = 1; z <= half; z++)
                        sb.Place(BlockIds.Cube, new Vector3Int(0, yLocal, z), forwardStep);
                    for (int z = -1; z >= -half; z--)
                        sb.Place(BlockIds.Cube, new Vector3Int(0, yLocal, z), backStep);
                    // Side strips at this Y, mirrored across X.
                    for (int x = 1; x <= half; x++)
                    {
                        int xLocal = x;
                        sb.MirrorX(b =>
                        {
                            b.Place(BlockIds.Cube, new Vector3Int(xLocal, yLocal, 0), rightStep);
                            for (int z = 1; z <= half; z++)
                                b.Place(BlockIds.Cube, new Vector3Int(xLocal, yLocal, z), rightStep);
                            for (int z = -1; z >= -half; z--)
                                b.Place(BlockIds.Cube, new Vector3Int(xLocal, yLocal, z), rightStep);
                        });
                    }
                }
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildArchDummyPlan(BlockDefinitionLibrary lib)
        {
            // Pillars + top beam. CPU sits in the middle of the beam at
            // (0, 7, 0). Build order: CPU → beam outward → pillars from
            // beam ends downward. Each cube hosts on the previous step
            // along the row direction.
            Vector3Int rightStep = new Vector3Int(1, 0, 0);
            Vector3Int leftStep  = new Vector3Int(-1, 0, 0);
            Vector3Int upDown    = new Vector3Int(0, -1, 0);

            var sb = ScriptedChassisBuilder.Create("Arch Dummy", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, new Vector3Int(0, 7, 0));
                // Beam — outward from CPU along ±X.
                sb.Place(BlockIds.Cube, new Vector3Int( 1, 7, 0), rightStep);
                sb.Place(BlockIds.Cube, new Vector3Int( 2, 7, 0), rightStep);
                sb.Place(BlockIds.Cube, new Vector3Int(-1, 7, 0), leftStep);
                sb.Place(BlockIds.Cube, new Vector3Int(-2, 7, 0), leftStep);
                // Pillars — downward from beam ends. Each pillar cell hosts
                // on the cell above (the beam end first, then the prior
                // pillar cell).
                for (int y = 6; y >= 0; y--)
                {
                    sb.Place(BlockIds.Cube, new Vector3Int( 2, y, 0), upDown);
                    sb.Place(BlockIds.Cube, new Vector3Int(-2, y, 0), upDown);
                }
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildStressRopeTowerPlan(BlockDefinitionLibrary lib)
        {
            // PHYSICS_PLAN § 2 stress profile. Each rotor's auto-companion
            // cube lands at (0, y+spinAxis, 0) — since spinAxis=+Y the
            // rotor at y=1 puts its mechanism cube at y=2. So if we use
            // BlockIds.Cube at "even" Y, those would conflict with
            // mechanism cubes. Instead we let auto-companion handle the
            // even cells and only place rotors at odd Y.
            //
            // For each rotor level (y=1,3,5,7,9), four ropes ring the
            // mechanism cube (the cell at y+1). Each rope's mount-up
            // points outward from the mechanism cube. RotorBlock's
            // BuildLiftRig won't adopt these as foils (rope mount face
            // is allowed but rope is not aero — see BlockConnectivity).
            Vector3Int rightStep = new Vector3Int(1, 0, 0);
            Vector3Int leftStep  = new Vector3Int(-1, 0, 0);
            Vector3Int forwardStep = new Vector3Int(0, 0, 1);
            Vector3Int backStep    = new Vector3Int(0, 0,-1);

            var sb = ScriptedChassisBuilder.Create("Stress Rope Tower", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                // Rotors at odd Y. Each rotor's auto-companion drops the
                // mechanism cube one cell above (so y=1 rotor → cube at
                // y=2, which serves as the host for the y=3 rotor).
                // First rotor needs a structural host at y=0 (CPU works:
                // CPU's +Y face hosts a rotor with up=+Y).
                for (int y = 1; y < 10; y += 2)
                {
                    sb.Place(BlockIds.Rotor, new Vector3Int(0, y, 0), Vector3Int.up);
                    // Four ropes ringed around the mechanism cube at y+1.
                    int mech = y + 1;
                    sb.Place(BlockIds.Rope, new Vector3Int( 1, mech, 0), rightStep);
                    sb.Place(BlockIds.Rope, new Vector3Int(-1, mech, 0), leftStep);
                    sb.Place(BlockIds.Rope, new Vector3Int( 0, mech, 1), forwardStep);
                    sb.Place(BlockIds.Rope, new Vector3Int( 0, mech,-1), backStep);
                }
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        private static BlueprintPlan BuildStressTowerPlan(BlockDefinitionLibrary lib)
        {
            // Spinning-rotor visual stress test. Same layout pattern as the
            // rope tower but without the rope rings — pure rotor + mechanism
            // cube column.
            var sb = ScriptedChassisBuilder.Create("Stress Rotor Tower", ChassisKind.Ground, lib);
            try
            {
                sb.Place(BlockIds.Cpu, 0, 0, 0);
                for (int y = 1; y < 10; y += 2)
                {
                    sb.Place(BlockIds.Rotor, new Vector3Int(0, y, 0), Vector3Int.up);
                    // Auto-companion cube at y+1 is placed by BuildSession;
                    // no explicit Place needed.
                }
                return sb.Build();
            }
            finally { sb.Dispose(); }
        }

        /// <summary>
        /// Persist a scripted <see cref="BlueprintPlan"/> to the on-disk
        /// asset at <paramref name="path"/>. Hard-fails (throws) on any
        /// validation error — defaults that wouldn't pass the same rules
        /// the player faces in the garage are bugs in the build script,
        /// not warnings to swallow. ScriptedChassisBuilder already threw
        /// on rejected placements; this is the second-pass full-blueprint
        /// validator (CPU connectivity, swept-overlap, pitch limits, ...).
        /// </summary>
        private static ChassisBlueprint CreateOrUpdateBlueprint(string path, BlueprintPlan plan)
        {
            ChassisBlueprint bp = LoadOrCreateAsset<ChassisBlueprint>(path);
            if (bp == null)
            {
                Debug.LogError($"[Robogame] Could not load or create blueprint at {path}.");
                return null;
            }

            // Validate BEFORE writing so a broken plan can't poison the
            // on-disk asset. The library-aware overload catches host-face
            // rejections that the positions-only path misses.
            BlockDefinitionLibrary lib = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan, lib);
            if (!result.IsValid)
            {
                throw new System.InvalidOperationException(
                    $"[Robogame] Blueprint '{plan.DisplayName}' (path={path}) failed validation:\n{result}\n" +
                    "Fix the scripted build before retrying — defaults must pass the same rules as user-built bots.");
            }
            if (result.Warnings.Count > 0)
            {
                Debug.Log(
                    $"[Robogame] Blueprint '{plan.DisplayName}' validated with warnings:\n{result}",
                    bp);
            }

            bp.DisplayName = plan.DisplayName;
            bp.Kind = plan.Kind;
            bp.SetEntries(plan.Entries);
            bp.RotorsGenerateLift = plan.RotorsGenerateLift;
            EditorUtility.SetDirty(bp);
            return bp;
        }

        // -----------------------------------------------------------------
        // Scenes
        // -----------------------------------------------------------------

        public static void BuildBootstrapPassA()
        {
            BlockDefinitionLibrary lib = PopulateBlockDefinitionLibrary();
            ChassisBlueprint defaultBp = CreateDefaultBlueprints();
            // Combat VFX library: lives in Resources/, references the
            // Cartoon FX Remaster prefabs. Building it as part of Pass A
            // means a fresh clone has working bomb explosions out of the
            // box — no separate menu trip required.
            CombatVfxWizard.CreateOrUpdate();
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);

            if (lib == null || !AssetDatabase.Contains(lib))
                Debug.LogError($"[Robogame] BuildBootstrapPassA: library is null or not a persistent asset (path: {LibraryAssetPath}).");
            if (defaultBp == null || !AssetDatabase.Contains(defaultBp))
                Debug.LogError($"[Robogame] BuildBootstrapPassA: default blueprint is null or not a persistent asset (path: {DefaultGroundPath}).");
            if (actions == null)
                Debug.LogWarning($"[Robogame] BuildBootstrapPassA: input actions asset not found at {ScaffoldUtils.InputActionsAsset}.");

            ScaffoldUtils.OpenScene(ScaffoldUtils.BootstrapScene);

            GameObject bootstrap = ScaffoldUtils.GetOrCreate("Bootstrap");
            var boot = bootstrap.GetComponent<GameBootstrap>();
            if (boot == null) boot = bootstrap.AddComponent<GameBootstrap>();
            SerializedObject bootSO = new SerializedObject(boot);
            bootSO.FindProperty("_firstScene").stringValue = "MainMenu";
            bootSO.FindProperty("_persistAcrossScenes").boolValue = true;
            bootSO.ApplyModifiedPropertiesWithoutUndo();

            var state = bootstrap.GetComponent<GameStateController>();
            if (state == null) state = bootstrap.AddComponent<GameStateController>();

            // Settings HUD lives on the persistent Bootstrap object so a
            // single instance survives scene transitions (Esc to toggle).
            if (bootstrap.GetComponent<SettingsHud>() == null)
                bootstrap.AddComponent<SettingsHud>();

            // Re-load the assets by path RIGHT BEFORE assignment. Anything
            // that triggered AssetDatabase.Refresh between asset creation
            // and this point (BlockDefinitionWizard does, OpenScene can)
            // will have invalidated the C# refs we captured earlier
            // (Unity's "fake null"). Loading by path here is cheap and
            // guarantees we hand SerializedProperty a live, persistent ref.
            BlockDefinitionLibrary libLive = AssetDatabase.LoadAssetAtPath<BlockDefinitionLibrary>(LibraryAssetPath);
            ChassisBlueprint defaultBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultGroundPath);
            ChassisBlueprint planeBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultPlanePath);
            ChassisBlueprint grapplerBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultGrapplerPath);
            ChassisBlueprint boatBpLive  = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultBoatPath);
            ChassisBlueprint bomberBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultBomberPath);
            ChassisBlueprint propPlaneBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultPropPlanePath);
            ChassisBlueprint helicopterBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultHelicopterPath);
            InputActionAsset actionsLive = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);

            if (libLive == null)
                Debug.LogError($"[Robogame] BuildBootstrapPassA: library load FAILED at assignment time (path: {LibraryAssetPath}).");
            if (defaultBpLive == null)
                Debug.LogError($"[Robogame] BuildBootstrapPassA: default blueprint load FAILED at assignment time (path: {DefaultGroundPath}).");

            SerializedObject stateSO = new SerializedObject(state);
            stateSO.FindProperty("_library").objectReferenceValue = libLive;
            stateSO.FindProperty("_defaultBlueprint").objectReferenceValue = defaultBpLive;
            stateSO.FindProperty("_inputActions").objectReferenceValue = actionsLive;

            // Populate the HUD-facing preset list (Tank / Plane / Grappler / Boat / Bomber / Prop Plane / Helicopter).
            // Session 61: replaced Buggy slot with Grappler (utility plane).
            SerializedProperty presets = stateSO.FindProperty("_presetBlueprints");
            if (presets != null)
            {
                presets.arraySize = 7;
                presets.GetArrayElementAtIndex(0).objectReferenceValue = defaultBpLive;
                presets.GetArrayElementAtIndex(1).objectReferenceValue = planeBpLive;
                presets.GetArrayElementAtIndex(2).objectReferenceValue = grapplerBpLive;
                presets.GetArrayElementAtIndex(3).objectReferenceValue = boatBpLive;
                presets.GetArrayElementAtIndex(4).objectReferenceValue = bomberBpLive;
                presets.GetArrayElementAtIndex(5).objectReferenceValue = propPlaneBpLive;
                presets.GetArrayElementAtIndex(6).objectReferenceValue = helicopterBpLive;
            }
            stateSO.ApplyModifiedPropertiesWithoutUndo();

            // Confirm the values stuck before saving — catches the case where
            // the GameStateController script changed and old serialised data
            // doesn't bind to the new fields.
            stateSO.Update();
            bool libOK    = stateSO.FindProperty("_library").objectReferenceValue != null;
            bool bpOK     = stateSO.FindProperty("_defaultBlueprint").objectReferenceValue != null;
            bool inputOK  = stateSO.FindProperty("_inputActions").objectReferenceValue != null;
            EditorUtility.SetDirty(state);
            EditorSceneManager.MarkSceneDirty(state.gameObject.scene);

            ScaffoldUtils.SaveActiveScene();
            Debug.Log($"[Robogame] Built Bootstrap.unity (Pass A). " +
                      $"GameStateController wired: library={libOK}, defaultBlueprint={bpOK}, inputActions={inputOK}.");
        }

        public static void BuildMainMenuPassA()
        {
            // Create the MainMenu scene file if missing. The menu is light
            // weight — empty scene + a single GameObject with the
            // MainMenuController, which builds its own Canvas in Awake.
            if (!File.Exists(ScaffoldUtils.MainMenuScene))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, ScaffoldUtils.MainMenuScene);
            }
            ScaffoldUtils.OpenScene(ScaffoldUtils.MainMenuScene);

            // Drop a controller GameObject if not already present. The
            // controller subscribes to no scene-level data — it's pure UI
            // and a SceneManager.LoadScene call.
            GameObject controller = ScaffoldUtils.GetOrCreate("MainMenuController");
            if (controller.GetComponent<MainMenuController>() == null)
                controller.AddComponent<MainMenuController>();

            // A camera is required for any scene; without it, Unity logs
            // a "no main camera" warning every frame. Plain solid-colour
            // camera is enough — the menu draws via UGUI overlay anyway.
            GameObject camGO = ScaffoldUtils.GetOrCreate("Main Camera",
                () => new GameObject("Main Camera"));
            Camera cam = camGO.GetComponent<Camera>();
            if (cam == null) cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.tag = "MainCamera";

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built MainMenu.unity (Pass A).");
        }

        public static void BuildGaragePassA()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);

            EnvironmentBuilder.BuildGarageEnvironment();
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Plane");

            GameObject controller = ScaffoldUtils.GetOrCreate("GarageController");
            if (controller.GetComponent<GarageController>() == null)
                controller.AddComponent<GarageController>();
            if (controller.GetComponent<SceneTransitionHud>() == null)
                controller.AddComponent<SceneTransitionHud>();

            // Force the hover height onto the GarageController in the
            // saved scene. AddComponent only seeds the C# default at
            // first creation; existing scenes carry their old serialised
            // value, so a code-side default bump (7 → 12 in session 23)
            // doesn't propagate without an explicit SerializedObject
            // write here. Same pattern BuildArenaPassA uses for the
            // dummy / barbell positions.
            GarageController gc = controller.GetComponent<GarageController>();
            if (gc != null)
            {
                SerializedObject gcSO = new SerializedObject(gc);
                SerializedProperty hoverProp = gcSO.FindProperty("_hoverHeightCells");
                if (hoverProp != null) hoverProp.floatValue = 12f;
                gcSO.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(gc);
            }

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Garage.unity (Pass A).");
        }

        public static void BuildArenaPassA()
        {
            // Create the Arena scene file if missing.
            if (!File.Exists(ScaffoldUtils.ArenaScene))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, ScaffoldUtils.ArenaScene);
            }
            ScaffoldUtils.OpenScene(ScaffoldUtils.ArenaScene);

            EnvironmentBuilder.BuildArenaEnvironment();
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");

            GameObject controller = ScaffoldUtils.GetOrCreate("ArenaController");
            ArenaController arena = controller.GetComponent<ArenaController>();
            if (arena == null) arena = controller.AddComponent<ArenaController>();
            if (controller.GetComponent<SceneTransitionHud>() == null)
                controller.AddComponent<SceneTransitionHud>();

            // Load the blueprint asset RIGHT BEFORE the SerializedObject write.
            // OpenScene above can trigger AssetDatabase.Refresh which invalidates
            // any C# refs captured earlier (Unity's "fake null"). Loading by path
            // here guarantees a live persistent ref that SerializedProperty will
            // actually keep. (Same pattern used in BuildBootstrapPassA.)
            ChassisBlueprint dummyBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(CombatDummyPath);
            ChassisBlueprint stressBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(StressTowerPath);
            ChassisBlueprint archBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(ArchDummyPath);
            if (dummyBpLive == null)
            {
                Debug.LogError(
                    $"[Robogame] BuildArenaPassA: combat dummy blueprint load FAILED at " +
                    $"assignment time (path: {CombatDummyPath}). The scene's ArenaController " +
                    "will have no dummy. Run step 2 (Create Default Blueprints) first.");
            }
            else
            {
                SerializedObject so = new SerializedObject(arena);
                SerializedProperty prop = so.FindProperty("_dummyBlueprint");
                if (prop != null) prop.objectReferenceValue = dummyBpLive;

                // Push the position too so bumping the default in code
                // actually reaches the saved scene asset on re-scaffold.
                SerializedProperty posProp = so.FindProperty("_dummyPosition");
                if (posProp != null) posProp.vector3Value = new Vector3(0f, 0.5f, 18f);

                // Stress tower blueprint — optional, only spawned when
                // the Tweakable toggle is on. Wire it here so a fresh
                // scaffold doesn't require a separate menu trip.
                SerializedProperty stressProp = so.FindProperty("_stressTowerBlueprint");
                if (stressProp != null) stressProp.objectReferenceValue = stressBpLive;
                SerializedProperty stressPosProp = so.FindProperty("_stressTowerPosition");
                if (stressPosProp != null) stressPosProp.vector3Value = new Vector3(40f, 0.5f, 18f);

                // Arch dummy: hookable target for the new tip blocks
                // (replaces the dumbbell). Spawn off to the player's
                // left so the existing combat dummy stays dead-ahead.
                // Y=0.5 puts the bottom of the pillars at world Y=0.5
                // (resting on the ground rather than floating).
                SerializedProperty archProp = so.FindProperty("_archBlueprint");
                if (archProp != null) archProp.objectReferenceValue = archBpLive;
                SerializedProperty archPosProp = so.FindProperty("_archPosition");
                if (archPosProp != null) archPosProp.vector3Value = new Vector3(-25f, 0.5f, 18f);

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(arena);
                EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);

                bool wired = so.FindProperty("_dummyBlueprint").objectReferenceValue != null;
                Debug.Log($"[Robogame] BuildArenaPassA: ArenaController dummy wired = {wired}.");
            }

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built Arena.unity (Pass A).");
        }

        public static void BuildWaterArenaPassA()
        {
            // Create the WaterArena scene file if missing.
            if (!File.Exists(ScaffoldUtils.WaterArenaScene))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, ScaffoldUtils.WaterArenaScene);
            }
            ScaffoldUtils.OpenScene(ScaffoldUtils.WaterArenaScene);

            EnvironmentBuilder.BuildWaterArenaEnvironment();
            ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");

            GameObject controller = ScaffoldUtils.GetOrCreate("WaterArenaController");
            if (controller.GetComponent<WaterArenaController>() == null)
                controller.AddComponent<WaterArenaController>();
            if (controller.GetComponent<SceneTransitionHud>() == null)
                controller.AddComponent<SceneTransitionHud>();

            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Built WaterArena.unity (Pass A).");
        }

        public static void BuildPlanetArenaPassA()
        {
            // Create the PlanetArena scene file if missing.
            if (!File.Exists(ScaffoldUtils.PlanetArenaScene))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, ScaffoldUtils.PlanetArenaScene);
            }
            ScaffoldUtils.OpenScene(ScaffoldUtils.PlanetArenaScene);

            // Wrap in try/finally so the scene gets saved (and marked dirty)
            // even if BuildPlanetArenaEnvironment throws partway through.
            // Without this, a transient build failure leaves disk state
            // wedged: the saved scene reflects whatever the prior pass
            // wrote, the user re-runs Build, the still-open scene appears
            // unchanged, and the runtime error sticks around. Marking the
            // scene dirty + saving forces every successful step to land
            // on disk before the next attempt.
            System.Exception caught = null;
            try
            {
                EnvironmentBuilder.BuildPlanetArenaEnvironment();
                ScaffoldHelpers.ClearPlayerChassis(keepName: "Robot");

                GameObject controller = ScaffoldUtils.GetOrCreate("PlanetArenaController");
                if (controller.GetComponent<PlanetArenaController>() == null)
                    controller.AddComponent<PlanetArenaController>();
                if (controller.GetComponent<SceneTransitionHud>() == null)
                    controller.AddComponent<SceneTransitionHud>();
            }
            catch (System.Exception ex)
            {
                caught = ex;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            ScaffoldUtils.SaveActiveScene();

            if (caught != null)
            {
                Debug.LogError(
                    "[Robogame] BuildPlanetArenaPassA: scaffold threw " +
                    "partway through. Saved whatever was built so far. " +
                    "Re-run after fixing the underlying error: " + caught,
                    null);
                throw caught; // surface to the menu invoker so red bar shows
            }
            Debug.Log("[Robogame] Built PlanetArena.unity (Pass A).");
        }

        public static void BuildAllPassA()
        {
            // Treat this as a true catch-all: run every numbered menu step
            // explicitly, in order, instead of relying on transitive calls
            // from BuildBootstrapPassA. That way the user can invoke this
            // entry alone after touching any data-asset or material code
            // and trust the entire Pass A surface is rebuilt.
            //
            // Order matters: data assets first (definitions → blueprints),
            // then rendering data (post profiles, skybox, outline feature,
            // block materials), then scenes that consume both.

            // Step 1 + Step 2: data assets. EnvironmentBuilder /
            // BootstrapBuilder load these by path so they must exist
            // before any scene is opened.
            PopulateBlockDefinitionLibrary();
            CreateDefaultBlueprints();

            // Rendering data. EnvironmentBuilder loads post-profile and
            // skybox assets by path at scene-build time, so they have to
            // be authored on disk before any BuildXxxPassA runs.
            PostProcessingBuilder.BuildAll();
            SkyboxBuilder.BuildArenaSkybox();
            // Phase 2: register MK Toon's per-object outline renderer feature
            // on the URP renderer asset. The outline pass uses a custom
            // LightMode tag and is dead without the feature dispatching it.
            OutlineRendererFeatureWiring.EnsureOutlineFeatureOnRenderers();
            // Phase 2: refresh the per-category block materials in place.
            // Idempotent — flips shaders / properties on the existing .mat
            // assets so iterating on BlockMaterials.cs doesn't require the
            // user to also re-run the BlockDefinition wizard.
            BlockMaterials.BuildAll();
            AssetDatabase.SaveAssets();

            BuildBootstrapPassA();
            BuildMainMenuPassA();
            BuildArenaPassA();
            BuildWaterArenaPassA();
            BuildPlanetArenaPassA();
            BuildGaragePassA();
            BuildSettingsConfigurator.SyncSceneList();

            // Leave Bootstrap.unity open so pressing Play actually exercises
            // the state machine. (BuildGaragePassA was the last to run and
            // would otherwise leave Garage open, which spawns nothing
            // because GameStateController only exists in Bootstrap.)
            ScaffoldUtils.OpenScene(ScaffoldUtils.BootstrapScene);
            Debug.Log("[Robogame] Pass A scaffold complete. Bootstrap.unity is open — press Play.");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Robust load-or-create for a ScriptableObject asset at <paramref name="path"/>.
        /// Handles the failure mode where the file exists on disk but
        /// <see cref="AssetDatabase.LoadAssetAtPath{T}"/> returns null because
        /// the asset's serialised script type can't bind (stale GUID, type
        /// moved between assemblies, etc.) by deleting the orphan and
        /// recreating fresh. Returns null only if the create itself fails.
        /// </summary>
        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            // First try the typed load.
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            // Maybe the script binding is fine but the typed cast failed for
            // some reason — try the untyped path next.
            UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main is T cast) return cast;

            // File is there but the Type doesn't match what we want — nuke it.
            if (File.Exists(path) || main != null)
            {
                Debug.LogWarning($"[Robogame] LoadOrCreateAsset: existing asset at '{path}' could not be loaded as {typeof(T).Name} (got {(main == null ? "null" : main.GetType().Name)}). Recreating.");
                AssetDatabase.DeleteAsset(path);
            }

            T inst = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(inst, path);
            AssetDatabase.SaveAssets();
            return inst;
        }
    }
}
