// =============================================================================
// Robogame Test Conventions — established session 17, first tests in the project.
//
// NAMING
//   {ClassName}Tests.{MethodOrFeature}_{Scenario}_{ExpectedOutcome}
//   Example: RotorBlockTests.BuildLiftRig_AdoptsFourLateralFoils_WhenRingIsPlaced
//
// LOCATION
//   Assets/_Project/Tests/
//     PlayMode/   — scene-running tests (MonoBehaviours, physics, coroutines)
//       Movement/ — one subfolder per production module
//       Block/
//       Combat/
//       Gameplay/
//     EditMode/   — pure-logic tests (serialization, graph traversal, etc.)
//       Movement/
//       Block/
//       ...
//
// WHEN TO USE PLAYMODE vs EDITMODE
//   PlayMode  : any test that needs FixedUpdate, Rigidbody, Start/OnEnable
//               lifecycle, or multi-frame behaviour. Mark with [UnityTest]
//               and yield WaitForFixedUpdate where timing matters.
//   EditMode  : tests that touch only ScriptableObjects, pure C# helpers,
//               or static utilities. ~10× faster; prefer them when possible.
//
// ASMDEFS
//   Robogame.Tests.PlayMode  → Assets/_Project/Tests/PlayMode/
//   Robogame.Tests.EditMode  → Assets/_Project/Tests/EditMode/
//   Both reference the production assemblies they need (Robogame.Movement,
//   Robogame.Block, Robogame.Core) and the Unity Test Framework GUIDs.
//
// SCAFFOLDING PATTERN
//   Tests spin up minimal GameObjects by hand (see BuildMinimalChassis below).
//   Do NOT depend on scene files or prefabs that might not exist in CI.
//   Each [SetUp] creates fresh GameObjects; [TearDown] destroys them.
//
// INVARIANTS COVERED HERE (session-17 rotor/foil adoption feature)
//   • Lateral spin-plane neighbours are adopted; axial cells are not.
//   • World position of adopted foils matches their placed-cell position.
//   • _velocityRb == hub, _forceTargetRb == chassis after ConfigureRotorMode.
//   • GeneratesLift=false adds zero Rigidbodies (§1.2 zero-baseline-cost).
//   • Bare rotor (no adjacent foils) adopts zero and stays valid.
//
// INVARIANTS COVERED HERE (session-18 kinematic-skip change — Change A)
//   • BuildLiftRig returns early (no hub, no adoption) when chassis.isKinematic.
//   • BuildLiftRig builds the hub normally when chassis is dynamic.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    /// <summary>
    /// Playmode tests for <see cref="RotorBlock"/> adoption behaviour.
    /// Tests are integration-level: they construct a minimal chassis hierarchy
    /// by hand (no prefabs, no scenes) and verify observable outcomes.
    /// </summary>
    public class RotorBlockTests
    {
        // Root of our temporary scene objects. Destroyed in TearDown.
        private GameObject _root;
        // The chassis Rigidbody all blocks push against.
        private Rigidbody _chassisRb;
        // The BlockGrid that owns all blocks.
        private BlockGrid _grid;

        // -----------------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            // Chassis root: has a Rigidbody (dynamic) + BlockGrid.
            _root = new GameObject("TestChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = false;
            _chassisRb.isKinematic = false;
            _grid = _root.AddComponent<BlockGrid>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Create a minimal BlockDefinition ScriptableObject for tests.
        /// No prefab, no material — BlockGrid falls back to a plain Cube primitive.
        /// </summary>
        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            // Access the private backing fields via reflection so tests don't
            // require a serialized asset on disk.
            typeof(BlockDefinition)
                .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, id);
            typeof(BlockDefinition)
                .GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, 100f);
            typeof(BlockDefinition)
                .GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(def, BlockCategory.Movement);
            return def;
        }

        /// <summary>
        /// Place a rotor block at <paramref name="cell"/> and return it.
        /// The caller is responsible for setting GeneratesLift before OnEnable fires.
        /// </summary>
        private RotorBlock PlaceRotor(Vector3Int cell)
        {
            BlockDefinition def = MakeDef("block.rotor.test");
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed for rotor at {cell}");
            RotorBlock rotor = bb.gameObject.AddComponent<RotorBlock>();
            return rotor;
        }

        /// <summary>
        /// Place an AeroSurfaceBlock at <paramref name="cell"/> and return it.
        /// </summary>
        private AeroSurfaceBlock PlaceAero(Vector3Int cell)
        {
            BlockDefinition def = MakeDef("block.aero.test");
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed for aero at {cell}");
            AeroSurfaceBlock aero = bb.gameObject.AddComponent<AeroSurfaceBlock>();
            return aero;
        }

        /// <summary>
        /// Read the private <c>_adoptedFoils</c> list off a <see cref="RotorBlock"/>
        /// via reflection. Returns the count, or -1 if the field is not found.
        /// </summary>
        private static int GetAdoptedFoilCount(RotorBlock rotor)
        {
            FieldInfo fi = typeof(RotorBlock).GetField(
                "_adoptedFoils", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) return -1;
            // The field is List<RotorBlock.AdoptedFoil> (private struct); we
            // reflect on IList to get the Count without needing the struct type.
            var list = fi.GetValue(rotor) as System.Collections.IList;
            return list?.Count ?? -1;
        }

        /// <summary>
        /// Read the private <c>_hubGo</c> field off a <see cref="RotorBlock"/>
        /// via reflection. Returns null if the field is missing or not set.
        /// </summary>
        private static GameObject GetHubGo(RotorBlock rotor)
        {
            FieldInfo fi = typeof(RotorBlock).GetField(
                "_hubGo", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi?.GetValue(rotor) as GameObject;
        }

        /// <summary>
        /// Retrieve the private <c>_velocityRb</c> field from an
        /// <see cref="AeroSurfaceBlock"/> via reflection.
        /// </summary>
        private static Rigidbody GetVelocityRb(AeroSurfaceBlock aero)
        {
            FieldInfo fi = typeof(AeroSurfaceBlock).GetField(
                "_velocityRb", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi?.GetValue(aero) as Rigidbody;
        }

        /// <summary>
        /// Retrieve the private <c>_forceTargetRb</c> field from an
        /// <see cref="AeroSurfaceBlock"/> via reflection.
        /// </summary>
        private static Rigidbody GetForceTargetRb(AeroSurfaceBlock aero)
        {
            FieldInfo fi = typeof(AeroSurfaceBlock).GetField(
                "_forceTargetRb", BindingFlags.NonPublic | BindingFlags.Instance);
            return fi?.GetValue(aero) as Rigidbody;
        }

        // -----------------------------------------------------------------------
        // Tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// R1-regression test (world position).
        /// A rotor at (0,1,0) with four Aero blocks placed at the four lateral
        /// spin-plane cells must adopt all four foils. Each adopted foil's world
        /// position must remain within ε of its placed-cell world position — i.e.
        /// the foil is reparented under the hub but NOT displaced by a full block.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_BuildLiftRig_AdoptsFourLateralAerofoils_PlacedAtSpinPlaneCells()
        {
            // Arrange — place aero foils around the rotor's spin plane first so
            // the grid sees them before the rotor's OnEnable calls AdoptAdjacentAerofoils.
            var lateralCells = new[]
            {
                new Vector3Int( 1, 1, 0),
                new Vector3Int(-1, 1, 0),
                new Vector3Int( 0, 1, 1),
                new Vector3Int( 0, 1,-1),
            };
            var foils = new List<AeroSurfaceBlock>();
            var foilWorldPositions = new List<Vector3>();
            foreach (Vector3Int cell in lateralCells)
            {
                AeroSurfaceBlock aero = PlaceAero(cell);
                foils.Add(aero);
                // Record world position BEFORE adoption (placed-cell position).
                foilWorldPositions.Add(aero.transform.position);
            }

            // The rotor is placed last. Set GeneratesLift=true via the property
            // so the lift rig builds when OnEnable fires.
            // We disable the GO first, set the flag, then re-enable — mirrors
            // what ChassisFactory does (flag flip before SetActive).
            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            rotorBb.gameObject.SetActive(false);
            RotorBlock rotor = rotorBb.gameObject.AddComponent<RotorBlock>();
            // Rebuild the grid's index so it includes the rotor's own cell.
            _grid.RebuildFromChildren();
            // Now flip the lift flag and enable to trigger BuildLiftRig.
            rotor.GeneratesLift = false; // ensure we start from false
            rotorBb.gameObject.SetActive(true);
            rotor.GeneratesLift = true;

            // Let OnEnable and FixedUpdate settle for one frame.
            yield return new WaitForFixedUpdate();

            // Assert adoption count.
            int count = GetAdoptedFoilCount(rotor);
            Assert.AreEqual(4, count,
                $"Expected 4 adopted foils but got {count}. " +
                "Check AdoptAdjacentAerofoils spin-plane cull logic.");

            // Assert world positions didn't shift (R1: 'one block higher' regression).
            const float epsilon = 0.05f; // 5 cm tolerance — well within one cell
            for (int i = 0; i < foils.Count; i++)
            {
                Vector3 before = foilWorldPositions[i];
                Vector3 after  = foils[i] != null ? foils[i].transform.position : Vector3.zero;
                float   dist   = Vector3.Distance(before, after);
                Assert.LessOrEqual(dist, epsilon,
                    $"Foil at lateral cell {lateralCells[i]} moved {dist:F3} m after adoption " +
                    "(expected <= {epsilon} m). worldPositionStays may be broken.");
            }
        }

        /// <summary>
        /// Axial-cull test.
        /// Aero blocks placed directly above (0,2,0) and below (0,0,0) the rotor
        /// lie on the spin axis and must NOT be adopted. The dot-product cull
        /// (threshold 0.9) must exclude them.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_BuildLiftRig_DoesNotAdoptCellsAlongSpinAxis()
        {
            // Arrange — axial foils only; no lateral ones.
            Vector3Int above = new Vector3Int(0, 2, 0);
            Vector3Int below = new Vector3Int(0, 0, 0);
            PlaceAero(above);
            PlaceAero(below);

            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            rotorBb.gameObject.SetActive(false);
            rotorBb.gameObject.AddComponent<RotorBlock>();
            _grid.RebuildFromChildren();
            RotorBlock rotor = rotorBb.GetComponent<RotorBlock>();
            rotorBb.gameObject.SetActive(true);
            rotor.GeneratesLift = true;

            yield return new WaitForFixedUpdate();

            int count = GetAdoptedFoilCount(rotor);
            Assert.AreEqual(0, count,
                $"Expected 0 adopted foils (axial cells should be culled) but got {count}. " +
                "Check spin-axis dot-product cull in AdoptAdjacentAerofoils.");
        }

        /// <summary>
        /// R3-regression test — force target must be chassis, not hub.
        /// After adoption, each foil's _velocityRb must be the hub Rigidbody
        /// and _forceTargetRb must be the chassis Rigidbody. If _forceTargetRb
        /// is null or equals the hub, lift is silently discarded by PhysX.
        /// </summary>
        [UnityTest]
        public IEnumerator AeroSurfaceBlock_ConfigureRotorMode_AfterAdoption_ResolvesForceTargetToChassis()
        {
            // Place one lateral foil.
            AeroSurfaceBlock foil = PlaceAero(new Vector3Int(1, 1, 0));

            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            rotorBb.gameObject.SetActive(false);
            rotorBb.gameObject.AddComponent<RotorBlock>();
            _grid.RebuildFromChildren();
            RotorBlock rotor = rotorBb.GetComponent<RotorBlock>();
            rotorBb.gameObject.SetActive(true);
            rotor.GeneratesLift = true;

            // Give Unity a fixed-update cycle to process the lift-rig build.
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, GetAdoptedFoilCount(rotor),
                "Precondition failed: expected 1 adopted foil.");

            Rigidbody velocityRb    = GetVelocityRb(foil);
            Rigidbody forceTargetRb = GetForceTargetRb(foil);

            // _velocityRb must be the hub (non-null, kinematic).
            Assert.IsNotNull(velocityRb,
                "_velocityRb is null after ConfigureRotorMode — foil can't sample hub velocity.");
            Assert.IsTrue(velocityRb.isKinematic,
                "_velocityRb is not kinematic — expected the hub Rigidbody.");

            // _forceTargetRb must be the chassis (non-null, NOT kinematic, NOT the hub).
            Assert.IsNotNull(forceTargetRb,
                "_forceTargetRb is null after ConfigureRotorMode — lift will be silently dropped (R3).");
            Assert.IsFalse(forceTargetRb.isKinematic,
                "_forceTargetRb is kinematic — lift force is going to the hub, not the chassis (R3).");
            Assert.AreNotSame(velocityRb, forceTargetRb,
                "_velocityRb and _forceTargetRb are the same object — should be hub vs chassis.");
            Assert.AreSame(_chassisRb, forceTargetRb,
                "_forceTargetRb is not the chassis Rigidbody — lift is misrouted (R3).");
        }

        /// <summary>
        /// PHYSICS_PLAN §1.2 zero-baseline-cost contract.
        /// When GeneratesLift is false, BuildLiftRig must not add any Rigidbody
        /// to the scene. Delta of Rigidbody.FindObjectsOfType before/after
        /// enabling the rotor must be zero (only the chassis rb is present).
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_GeneratesLiftFalse_AddsZeroRigidbodies()
        {
            // Arrange.
            int rbCountBefore = Object.FindObjectsByType<Rigidbody>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            // Add lateral foil to make the test meaningful — there IS a foil
            // to adopt, but GeneratesLift=false should skip the whole rig.
            PlaceAero(new Vector3Int(1, 1, 0));
            _grid.RebuildFromChildren();

            // Act — add rotor with GeneratesLift=false (default).
            RotorBlock rotor = rotorBb.gameObject.AddComponent<RotorBlock>();
            // GeneratesLift defaults to false; we don't set it.
            Assert.IsFalse(rotor.GeneratesLift);

            yield return new WaitForFixedUpdate();

            // Assert.
            int rbCountAfter = Object.FindObjectsByType<Rigidbody>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            int delta = rbCountAfter - rbCountBefore;
            Assert.AreEqual(0, delta,
                $"GeneratesLift=false added {delta} Rigidbody(s) to the scene — " +
                "expected zero (PHYSICS_PLAN §1.2 zero-baseline-cost).");
        }

        /// <summary>
        /// Cosmetic-spinner path: a rotor with no adjacent Aero blocks must
        /// build successfully (no exception, no warnings) and adopt zero foils.
        /// This is the supported "tail rotor / decorative spinner" configuration.
        /// </summary>
        [UnityTest]
        public IEnumerator RotorBlock_BareRotor_NoAdjacentFoils_AdoptsZero()
        {
            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            _grid.RebuildFromChildren();

            rotorBb.gameObject.SetActive(false);
            rotorBb.gameObject.AddComponent<RotorBlock>();
            RotorBlock rotor = rotorBb.GetComponent<RotorBlock>();
            rotorBb.gameObject.SetActive(true);
            rotor.GeneratesLift = true;

            yield return new WaitForFixedUpdate();

            // The rig should still build (hub created) but adopt nothing.
            int count = GetAdoptedFoilCount(rotor);
            Assert.AreEqual(0, count,
                $"Bare rotor adopted {count} foils but expected 0 — " +
                "bare rotor must be a valid zero-lift configuration.");

            // Sanity: the rotor is still alive and the root chassis is intact.
            Assert.IsTrue(rotor.isActiveAndEnabled,
                "RotorBlock became inactive after bare-lift-rig build.");
        }

        // -----------------------------------------------------------------------
        // Change A — BuildLiftRig skips hub when chassis is kinematic (session 18)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Change A contract: when the chassis Rigidbody is kinematic,
        /// <c>BuildLiftRig</c> must return early without creating the hub
        /// GameObject. The garage parks the chassis as kinematic for static
        /// display; a lift rig in that state would reparent foils under a
        /// scene-root hub, breaking the garage render path.
        /// Verified by reflection on the private <c>_hubGo</c> field.
        /// </summary>
        [UnityTest]
        public IEnumerator BuildLiftRig_SkipsHub_WhenChassisIsKinematic()
        {
            // Arrange — pin chassis kinematic before the rotor enables.
            _chassisRb.isKinematic = true;

            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            // Place a lateral foil so there IS something to adopt — we want to
            // confirm the skip happens even when foils are present.
            PlaceAero(new Vector3Int(1, 1, 0));
            _grid.RebuildFromChildren();

            rotorBb.gameObject.SetActive(false);
            RotorBlock rotor = rotorBb.gameObject.AddComponent<RotorBlock>();
            rotor.GeneratesLift = true; // set before enable so BuildLiftRig runs
            rotorBb.gameObject.SetActive(true); // fires OnEnable → BuildLiftRig

            yield return new WaitForFixedUpdate();

            // Assert: hub must not have been created.
            GameObject hubGo = GetHubGo(rotor);
            Assert.IsNull(hubGo,
                "_hubGo is non-null after BuildLiftRig with a kinematic chassis — " +
                "the early-return guard is missing or broken (Change A / B1 fix).");

            // Also confirm via scene search: no RotorHub_* GameObject should exist.
            GameObject hubInScene = GameObject.Find($"RotorHub_{rotor.name}");
            Assert.IsNull(hubInScene,
                $"Found 'RotorHub_{rotor.name}' in the scene despite kinematic chassis — " +
                "hub must not be spawned in garage/kinematic mode.");
        }

        /// <summary>
        /// Change A contract (positive case): when the chassis Rigidbody is
        /// dynamic, <c>BuildLiftRig</c> must build the hub as normal.
        /// Mirrors the kinematic test above with <c>isKinematic = false</c>.
        /// </summary>
        [UnityTest]
        public IEnumerator BuildLiftRig_BuildsHub_WhenChassisIsDynamic()
        {
            // Arrange — chassis is dynamic (the SetUp default; stated explicitly
            // here so the intent of this test is unambiguous).
            _chassisRb.isKinematic = false;

            BlockDefinition rotorDef = MakeDef("block.rotor.test");
            BlockBehaviour rotorBb = _grid.PlaceBlock(rotorDef, new Vector3Int(0, 1, 0));
            Assert.IsNotNull(rotorBb);
            _grid.RebuildFromChildren();

            rotorBb.gameObject.SetActive(false);
            RotorBlock rotor = rotorBb.gameObject.AddComponent<RotorBlock>();
            rotor.GeneratesLift = true;
            rotorBb.gameObject.SetActive(true);

            yield return new WaitForFixedUpdate();

            // Assert: hub must have been created.
            GameObject hubGo = GetHubGo(rotor);
            Assert.IsNotNull(hubGo,
                "_hubGo is null after BuildLiftRig with a dynamic chassis — " +
                "hub should be built when the chassis is non-kinematic.");
        }
    }
}
