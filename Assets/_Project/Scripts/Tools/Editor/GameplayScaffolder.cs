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
        private const string DefaultBuggyPath = BlueprintFolder + "/Blueprint_DefaultBuggy.asset";
        private const string DefaultBoatPath = BlueprintFolder + "/Blueprint_DefaultBoat.asset";
        private const string DefaultBomberPath = BlueprintFolder + "/Blueprint_DefaultBomber.asset";
        private const string DefaultHelicopterPath = BlueprintFolder + "/Blueprint_DefaultHelicopter.asset";
        private const string CombatDummyPath = BlueprintFolder + "/Blueprint_CombatDummy.asset";
        private const string StressTowerPath = BlueprintFolder + "/Blueprint_StressRotorTower.asset";
        private const string BarbellDummyPath = BlueprintFolder + "/Blueprint_BarbellDummy.asset";

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

            ChassisBlueprint ground = CreateOrUpdateBlueprint(DefaultGroundPath, "Tank", ChassisKind.Ground, BuildGroundEntries());
            CreateOrUpdateBlueprint(DefaultPlanePath, "Plane", ChassisKind.Plane, BuildPlaneEntries());
            CreateOrUpdateBlueprint(DefaultBuggyPath, "Buggy", ChassisKind.Ground, BuildBuggyEntries());
            CreateOrUpdateBlueprint(DefaultBoatPath,  "Boat",  ChassisKind.Ground, BuildBoatEntries());
            CreateOrUpdateBlueprint(DefaultBomberPath, "Bomber", ChassisKind.Plane, BuildBomberEntries());
            // Helicopter: simple T-shaped sandbox chassis — short fuselage,
            // long tail boom, vertical fin, rotor on top of the CPU. The
            // rotor block flips into lift mode (kinematic hub + ring of
            // aerofoil blades) via RotorsGenerateLift; ChassisFactory.Build
            // sets the flag on every RotorBlock after placement. No
            // thruster — forward flight via player tilt input is a
            // separate session. Ground-kind for now (no plane-spawn
            // forward velocity); revisit once helicopter input lands.
            CreateOrUpdateBlueprint(DefaultHelicopterPath, "Helicopter", ChassisKind.Ground, BuildHelicopterEntries(), rotorsGenerateLift: true);
            CreateOrUpdateBlueprint(CombatDummyPath, "Combat Dummy", ChassisKind.Ground, BuildDummyEntries());
            // Stress-test target: a tall column of rotors, each carrying
            // a default ring of 4 ropes. The arena controller spawns this
            // when the Stress.RotorTower tweakable is on; a dev session
            // drags the slider to 1, the tower appears, the Profiler
            // shows the truth about how the kinematic-hub trick scales.
            CreateOrUpdateBlueprint(StressTowerPath, "Stress Rotor Tower", ChassisKind.Ground, BuildStressTowerEntries());
            // Barbell dummy: two large mass cubes joined by a long rod.
            // Used to test the new Hook / Mace tip blocks (PHYSICS_PLAN
            // §3) — the player can hook onto a bell, swing the chassis
            // around, or wallop the rod with a mace and watch
            // momentum-impact damage chew through the structure.
            CreateOrUpdateBlueprint(BarbellDummyPath, "Barbell Dummy", ChassisKind.Ground, BuildBarbellDummyEntries());
            AssetDatabase.SaveAssets();
            Debug.Log($"[Robogame] Default blueprints created (ground asset persisted: {AssetDatabase.Contains(ground)}).");
            return ground;
        }

        private static ChassisBlueprint.Entry[] BuildGroundEntries()
        {
            // Mirrors RobotLayouts.PopulateTestRobot: 3×6 chassis with
            // CPU centre, weapon on top, wheels at corners + mid.
            var list = new List<ChassisBlueprint.Entry>();
            const int xMin = -1, xMax = 1, zMin = -2, zMax = 3;
            for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
            {
                bool isCpu = (x == 0 && z == 0);
                bool isWheel = (x == xMin || x == xMax) && (z == zMin || z == 0 || z == zMax);
                if (isCpu || isWheel) continue;
                list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(x, 0, z)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 1, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(xMin, 0, zMax)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(xMax, 0, zMax)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMin, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMax, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMin, 0, zMin)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(xMax, 0, zMin)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildPlaneEntries()
        {
            // Mirrors RobotLayouts.PopulateTestPlane: thruster-rear fuselage,
            // 4-segment main wings, tailplane and vertical fin.
            var list = new List<ChassisBlueprint.Entry>();
            list.Add(new ChassisBlueprint.Entry(BlockIds.Thruster, new Vector3Int(0, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0, -2)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu,      new Vector3Int(0, 0,  0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  2)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon,   new Vector3Int(0, 1,  3)));
            for (int x = 1; x <= 4; x++)
            {
                list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( x, 0, 0)));
                list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-x, 0, 0)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 2, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-2, 0, 1)));
            // Tailplane: all four are lifting surfaces. Wings far from COM
            // no longer cause a constant pitching moment because
            // AeroSurfaceBlock now scales lift with angle of attack, so the
            // tail self-trims with the rest of the wing.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 1, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-1, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 2, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-2, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,    new Vector3Int( 0, 1, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.AeroFin, new Vector3Int( 0, 2, -3)));
            // Rope tail: free-body cosmetic chain hanging off the bottom
            // of the thruster cell. Anchored under (0,0,-3), so it stays
            // connected to the chassis via the y-axis neighbour for the
            // CPU-connectivity check.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Rope,    new Vector3Int( 0, -1, -3)));
            // Tail rotor: spinning hub on top of the rear fuselage at
            // (0,1,-2). Connected to the cube at (0,0,-2) via the y-axis
            // neighbour. Lives one cell forward of the vertical fin so
            // the rotor visual doesn't intersect the fin. RotorBlock is
            // cosmetic-only — no ropes, no lift — so this is just a
            // spinning visual on the tail. Add a Rope cell adjacent if
            // you want a chain hanging off the rotor.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Rotor,   new Vector3Int( 0,  1, -2)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildBuggyEntries()
        {
            // Compact 2-wide × 3-long × 2-tall buggy: smaller and lighter
            // than the tank, no top armour. CPU at centre, weapon mounted
            // up top, steering wheels at the front, drive wheels at the rear.
            var list = new List<ChassisBlueprint.Entry>();
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(-1, 0, 0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(1, 0, 0)));
            // Front: steering wheels and a nose block.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(-1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.WheelSteer, new Vector3Int(1, 0, 1)));
            // Rear: drive wheels and a tail block.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(0, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(-1, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Wheel, new Vector3Int(1, 0, -1)));
            // Roll cage / weapon mount.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 1, 0)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildBoatEntries()
        {
            // Sandbox boat for the water arena: a wide 5×7 flat hull
            // (35 cells of displacement) with a CPU at the centre, a single
            // rear thruster on the deck, and a weapon up front. Designed
            // around the default water tweakables (density=4, displacement=0.30):
            // total mass ≈ 39 kg vs buoyancy ≈ 412 N at full submersion, so
            // it settles ≈94% submerged — visible freeboard, room to bob.
            // Bump Water.Density above ~6 in Settings if you want it to ride higher.
            //
            // Steering note: this is a Ground-kind chassis with no wheels,
            // so movement is thruster-only (W = forward, no native turn).
            // That's intentional for a v1 sandbox; rudder/keel block lands
            // in a follow-up if we want true boat control.
            var list = new List<ChassisBlueprint.Entry>();
            const int xMin = -2, xMax = 2;
            const int zMin = -3, zMax = 3;

            // Flat hull: 5 wide × 7 long × 1 tall. CPU replaces the centre
            // cube so the brain has the same buoyancy contribution as the
            // structure block it displaces.
            for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
            {
                if (x == 0 && z == 0) continue; // CPU goes here
                list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(x, 0, z)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)));

            // Single rear thruster on top of the deck — keeps the prop
            // above water so it doesn't constantly drag.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Thruster, new Vector3Int(0, 1, zMin)));
            // Rudder hangs below the stern (y=-1) where a real boat's
            // blade would sit. Speed-scaled yaw torque — W to push,
            // A/D to turn. Adds buoyancy + mass below COM, which also
            // helps the boat self-right.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Rudder, new Vector3Int(0, -1, zMin)));
            // Bow gun for sandbox target practice.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Weapon, new Vector3Int(0, 1, zMax)));

            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildBomberEntries()
        {
            // Plane variant with a bomb bay slung underneath the fuselage
            // instead of a top-mounted hitscan gun. Slightly beefier
            // structure and a wider wing for the extra bomb-bay mass; no
            // forward weapon, so this thing is a true ground-attack
            // platform — fly over targets and hold Fire to pickle bombs.
            //
            // Layout summary (forward = +Z):
            //   - Thruster at z=-3.
            //   - Fuselage cubes z=-2..+3 (one cube longer at the nose
            //     than the standard plane to balance the bomb-bay weight).
            //   - CPU at origin.
            //   - Bomb bay at (0, -1, 0) — directly under the CPU.
            //   - Main wings: 4 segments per side at z=0 + 2 inboard at
            //     z=1 (matches the default plane).
            //   - Tailplane + vertical fin identical to the plane.
            var list = new List<ChassisBlueprint.Entry>();
            list.Add(new ChassisBlueprint.Entry(BlockIds.Thruster, new Vector3Int(0, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0, -2)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0, -1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu,      new Vector3Int(0, 0,  0)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  2)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,     new Vector3Int(0, 0,  3)));

            // Bomb bay underneath the CPU. Drops bombs straight down so a
            // CPU-overhead release stays on top of the chassis silhouette.
            list.Add(new ChassisBlueprint.Entry(BlockIds.BombBay,  new Vector3Int(0, -1, 0)));

            // Main wings.
            for (int x = 1; x <= 4; x++)
            {
                list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( x, 0, 0)));
                list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-x, 0, 0)));
            }
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-1, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 2, 0, 1)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-2, 0, 1)));

            // Tailplane + vertical fin.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 1, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-1, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int( 2, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Aero, new Vector3Int(-2, 0, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cube,    new Vector3Int( 0, 1, -3)));
            list.Add(new ChassisBlueprint.Entry(BlockIds.AeroFin, new Vector3Int( 0, 2, -3)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildHelicopterEntries()
        {
            // Larger helicopter sandbox — ~38 cells, roughly 4× the size of
            // the previous T-shaped placeholder. Built via BlueprintBuilder
            // so the layout reads top-down as a description of the chassis
            // rather than a coordinate dump.
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
            //                  T     y=0 tail rotor on +X face of the boom
            //          # # # # #
            //                #       y=0 tail boom (4 cells, x=0 only)
            //                #
            //                #
            //                F       y=1 vertical tail fin at the boom tip
            //
            // Scope cuts: ChassisKind.Ground (arena spawn on the pad, not
            // 18 m up) and RotorsGenerateLift = true (set externally in
            // CreateOrUpdateBlueprint). Dual side guns share the chassis
            // WeaponMount; both fire toward the player's aim point each
            // press, giving the chassis a visible muzzle on each side.
            return BlueprintBuilder.Create("Helicopter", ChassisKind.Ground)
                // CPU at the cabin centre.
                .Block(BlockIds.Cpu, 0, 0, 0)
                // Central column along Z: tail boom (-5..-1) and forward
                // fuselage (1..3). z=0 holds the CPU (already placed).
                .Row(BlockIds.Cube, new Vector3Int(0, 0, -5), new Vector3Int(0, 0, -1))
                .Row(BlockIds.Cube, new Vector3Int(0, 0,  1), new Vector3Int(0, 0,  3))
                // Cabin sides at |x|=1, z=-1..2. Mirrored across X so
                // the layout reads as one half + symmetry.
                .MirrorX(b => b
                    .Block(BlockIds.Cube, 1, 0, -1)
                    .Block(BlockIds.Cube, 1, 0,  0)
                    .Block(BlockIds.Cube, 1, 0,  1)
                    .Block(BlockIds.Cube, 1, 0,  2))
                // Outboard hardpoint guns at |x|=2, z=0. Each connects to
                // the cabin via face-adjacency through (±1, 0, 0).
                .MirrorX(b => b.Block(BlockIds.Weapon, 2, 0, 0))
                // Cabin roof: 3-wide × 4-deep slab at y=1, z=-1..2.
                .Box(BlockIds.Cube, new Vector3Int(-1, 1, -1), new Vector3Int(1, 1, 2))
                // Vertical tail fin on top of the boom tip.
                .Block(BlockIds.AeroFin, 0, 1, -5)
                // Tail rotor: bare cosmetic spinner on the +X face of the
                // boom segment at (0,0,-4). Spin axis is +X (its mechanism
                // cell would be (2,0,-4) but no foils are placed there, so
                // adoption finds zero and the rotor stays a pure visual).
                .RotorBare(new Vector3Int(1, 0, -4), spinAxis: Vector3Int.right)
                // Main rotor: stem at (0,2,0) on top of the cabin roof.
                // RotorWithFoils() also drops the invisible mechanism cube
                // and the four foils ringed around it at y=3 — those are
                // the absolute topmost cells on the chassis.
                .RotorWithFoils(new Vector3Int(0, 2, 0))
                .Build()
                .Entries;
        }

        private static ChassisBlueprint.Entry[] BuildDummyEntries()
        {
            // Big fortress dummy: 5w × 5d × 6h solid cube body with a CPU
            // "head" sticking up from the top-centre. Solid (not hollow) so
            // splash damage has long chains of connected blocks to chew
            // through — makes propagation visually obvious. Centred on the
            // x/z origin so positioning the dummy GameObject feels natural.
            var list = new List<ChassisBlueprint.Entry>();
            const int half = 2;       // → 5 wide / deep (-2..2)
            const int height = 6;     // body cubes y=0..5
            for (int x = -half; x <= half; x++)
            for (int z = -half; z <= half; z++)
            for (int y = 0; y < height; y++)
            {
                list.Add(new ChassisBlueprint.Entry(BlockIds.Cube, new Vector3Int(x, y, z)));
            }
            // CPU pokes up one cell above the centre of the roof so a sniper
            // can decapitate from far away, but splash testing still has a
            // chunky body to walk damage through.
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, height, 0)));
            return list.ToArray();
        }

        private static ChassisBlueprint.Entry[] BuildBarbellDummyEntries()
        {
            // Barbell-shaped target dummy:
            //   - End mass A: 3×3×3 cube cluster at z = -7..-5
            //   - Rod:        1×1 column along x=y=0 from z = -4..4 (CPU at z=0)
            //   - End mass B: 3×3×3 cube cluster at z = 5..7
            //
            // ~12.6 m long along Z, 3 m thick at the bells. Plenty of
            // surface area for hooks to bite into and a long rod to
            // smack with a mace.
            return BlueprintBuilder.Create("Barbell Dummy", ChassisKind.Ground)
                .Block(BlockIds.Cpu, 0, 0, 0)
                // Rod: x=0, y=0, z=-4..-1 and z=1..4 (CPU at z=0).
                .Row(BlockIds.Cube, new Vector3Int(0, 0, -4), new Vector3Int(0, 0, -1))
                .Row(BlockIds.Cube, new Vector3Int(0, 0,  1), new Vector3Int(0, 0,  4))
                // End mass A: 3×3×3 box at z=-7..-5.
                .Box(BlockIds.Cube, new Vector3Int(-1, -1, -7), new Vector3Int(1, 1, -5))
                // End mass B: 3×3×3 box at z=5..7.
                .Box(BlockIds.Cube, new Vector3Int(-1, -1,  5), new Vector3Int(1, 1,  7))
                .Build()
                .Entries;
        }

        private static ChassisBlueprint.Entry[] BuildStressTowerEntries()
        {
            // Spinning-rotor tower used to visually stress-test multiple
            // rotors on one chassis. A 1×1 column 10 cells tall with
            // rotors at every odd y. The rotor block is now cosmetic
            // (no Rigidbody, no ropes) so this loadout no longer
            // exercises the joint solver — it's a sanity-check that
            // many rotors at high RPM stay smooth and that the
            // StressRotorTowerRpm override path still drives every
            // rotor's spin rate. The original "80 dynamic rbs" stress
            // test moves to the rope tower in a follow-up session
            // (rotor + adjacent rope blocks); see PHYSICS_PLAN.md §2.
            //
            // CPU lives at the bottom so the rotors above all pass the
            // CPU-connectivity check trivially (every cell is in a
            // single y-axis chain back to (0,0,0)).
            var list = new List<ChassisBlueprint.Entry>();
            list.Add(new ChassisBlueprint.Entry(BlockIds.Cpu, new Vector3Int(0, 0, 0)));
            for (int y = 1; y < 10; y++)
            {
                bool isRotorLevel = (y % 2 == 1); // 1, 3, 5, 7, 9
                list.Add(new ChassisBlueprint.Entry(
                    isRotorLevel ? BlockIds.Rotor : BlockIds.Cube,
                    new Vector3Int(0, y, 0)));
            }
            return list.ToArray();
        }

        private static ChassisBlueprint CreateOrUpdateBlueprint(
            string path, string displayName, ChassisKind kind, ChassisBlueprint.Entry[] entries,
            bool rotorsGenerateLift = false)
        {
            ChassisBlueprint bp = LoadOrCreateAsset<ChassisBlueprint>(path);
            if (bp == null)
            {
                Debug.LogError($"[Robogame] Could not load or create blueprint at {path}.");
                return null;
            }
            bp.DisplayName = displayName;
            bp.Kind = kind;
            bp.SetEntries(entries);
            bp.RotorsGenerateLift = rotorsGenerateLift;
            EditorUtility.SetDirty(bp);

            // Run BlueprintValidator at scaffold time so a broken preset
            // surfaces in the Console immediately, not at game start.
            // Errors degrade to warnings here (the asset still saves) so
            // the user can investigate without the whole Build All Pass A
            // bailing out — but the warning is loud and clickable.
            BlueprintPlan plan = new BlueprintPlan(displayName, kind, entries, rotorsGenerateLift);
            BlueprintValidationResult result = BlueprintValidator.Validate(plan);
            if (!result.IsValid)
            {
                Debug.LogWarning(
                    $"[Robogame] Blueprint '{displayName}' has validation errors:\n{result}",
                    bp);
            }
            else if (result.Warnings.Count > 0)
            {
                Debug.Log(
                    $"[Robogame] Blueprint '{displayName}' validated with warnings:\n{result}",
                    bp);
            }
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
            bootSO.FindProperty("_firstScene").stringValue = "Garage";
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
            ChassisBlueprint buggyBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultBuggyPath);
            ChassisBlueprint boatBpLive  = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultBoatPath);
            ChassisBlueprint bomberBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(DefaultBomberPath);
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

            // Populate the HUD-facing preset list (Tank / Plane / Buggy / Boat / Bomber / Helicopter).
            SerializedProperty presets = stateSO.FindProperty("_presetBlueprints");
            if (presets != null)
            {
                presets.arraySize = 6;
                presets.GetArrayElementAtIndex(0).objectReferenceValue = defaultBpLive;
                presets.GetArrayElementAtIndex(1).objectReferenceValue = planeBpLive;
                presets.GetArrayElementAtIndex(2).objectReferenceValue = buggyBpLive;
                presets.GetArrayElementAtIndex(3).objectReferenceValue = boatBpLive;
                presets.GetArrayElementAtIndex(4).objectReferenceValue = bomberBpLive;
                presets.GetArrayElementAtIndex(5).objectReferenceValue = helicopterBpLive;
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
            ChassisBlueprint barbellBpLive = AssetDatabase.LoadAssetAtPath<ChassisBlueprint>(BarbellDummyPath);
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

                // Barbell dummy: hookable + smackable target for the new
                // tip blocks. Spawn position is off to the player's left
                // so the existing combat dummy stays dead-ahead.
                SerializedProperty barbellProp = so.FindProperty("_barbellBlueprint");
                if (barbellProp != null) barbellProp.objectReferenceValue = barbellBpLive;
                SerializedProperty barbellPosProp = so.FindProperty("_barbellPosition");
                if (barbellPosProp != null) barbellPosProp.vector3Value = new Vector3(-25f, 1.5f, 18f);

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
