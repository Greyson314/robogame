// FoilControlTests — ADR-0009 / invariant #11.
//
// A plane's pitch and roll now come from its foils deflecting as control
// surfaces, each by its own position relative to the CoM. These tests pin
// the three things the user named as load-bearing for the game's fun:
//   1. Space pitches the nose UP through the foils (sign of the whole chain).
//   2. A/D banks the right way.
//   3. Losing one wing costs control: a symmetric plane flies straight with
//      no input, the same plane minus its right wing rolls on its own.
// Hand-built grid (no presets, no scene), RobotDrive driven directly so the
// only moving part under test is the foil control path.

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public sealed class FoilControlTests
    {
        private GameObject _root;
        private Rigidbody _rb;
        private RobotDrive _drive;

        private const float Airspeed = 30f;
        private const int Steps = 20;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        private static BlockDefinition MakeDef(string id, BlockCategory cat, float mass)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, cat);
            typeof(BlockDefinition).GetField("_mass", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, mass);
            return def;
        }

        // Spine z=-2..2 with main wings at z=0 and tail stabs at z=-2, the
        // default-plane shape in miniature. Foils get AeroSurfaceBlock
        // exactly the way RobotAeroBinder wires them (Vertical = true).
        private void BuildPlane(bool withRightWing, float wingIncidenceDeg)
        {
            _root = new GameObject("FoilControlPlane");
            _rb = _root.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            BlockGrid grid = _root.AddComponent<BlockGrid>();
            Robot robot = _root.AddComponent<Robot>();
            _drive = _root.AddComponent<RobotDrive>();
            _drive.Scheme = ControlScheme.Plane; // no blueprint on a hand-built grid

            BlockDefinition cube = MakeDef(BlockIds.Cube, BlockCategory.Structure, 1f);
            BlockDefinition cpu  = MakeDef(BlockIds.Cpu, BlockCategory.Cpu, 2f);
            BlockDefinition aero = MakeDef(BlockIds.Aero, BlockCategory.Movement, 0.6f);

            grid.PlaceBlock(cpu, new Vector3Int(0, 0, 0), Vector3Int.up);
            for (int z = -2; z <= 2; z++)
                if (z != 0) grid.PlaceBlock(cube, new Vector3Int(0, 0, z), Vector3Int.up);

            Vector3 wingDims = new Vector3(4f, 0.08f, 0.9f);
            Vector3 stabDims = new Vector3(2f, 0.08f, 0.7f);
            Vector3Int right = new Vector3Int(1, 0, 0), left = new Vector3Int(-1, 0, 0);

            PlaceFoil(grid, aero, new Vector3Int(-1, 0, 0), left, wingDims, wingIncidenceDeg);
            if (withRightWing) PlaceFoil(grid, aero, new Vector3Int(1, 0, 0), right, wingDims, wingIncidenceDeg);
            PlaceFoil(grid, aero, new Vector3Int(-1, 0, -2), left, stabDims, 0f);
            PlaceFoil(grid, aero, new Vector3Int(1, 0, -2), right, stabDims, 0f);

            robot.RecalculateAggregates();
        }

        private static void PlaceFoil(BlockGrid grid, BlockDefinition def, Vector3Int cell, Vector3Int up, Vector3 dims, float worldPitchDeg)
        {
            float localPitch = BlockOrientation.NormalizePitchForUp(def, worldPitchDeg, up);
            BlockBehaviour bb = grid.PlaceBlock(def, cell, up, dims, localPitch);
            Assert.IsNotNull(bb, $"PlaceBlock failed at {cell}");
            AeroSurfaceBlock foil = bb.gameObject.AddComponent<AeroSurfaceBlock>();
            foil.Vertical = true;
        }

        // Hold airspeed along the nose so airflow AoA stays ~0 and the only
        // torque source is the control deflection (plus lift asymmetry when
        // incidence is dialled in).
        private IEnumerator Fly(Vector2 move, float vertical, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                _rb.linearVelocity = _root.transform.forward * Airspeed;
                _drive.ApplyMovement(move, vertical, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
        }

        private Vector3 LocalOmega => _root.transform.InverseTransformDirection(_rb.angularVelocity);

        [UnityTest]
        public IEnumerator PitchUpDemand_RotatesNoseUp_ThroughFoils()
        {
            BuildPlane(withRightWing: true, wingIncidenceDeg: 0f);
            yield return Fly(Vector2.zero, vertical: 1f, Steps);
            // Nose up = negative rotation about chassis +X.
            Assert.Less(LocalOmega.x, -0.05f,
                $"Space (pitch +1) must pitch the nose UP via the foils; got local omega.x={LocalOmega.x:F3} rad/s.");
        }

        [UnityTest]
        public IEnumerator RollRightDemand_BanksRight_ThroughFoils()
        {
            BuildPlane(withRightWing: true, wingIncidenceDeg: 0f);
            yield return Fly(new Vector2(1f, 0f), vertical: 0f, Steps);
            // Bank right = negative rotation about chassis +Z.
            Assert.Less(LocalOmega.z, -0.05f,
                $"D (roll +1) must bank RIGHT via the ailerons; got local omega.z={LocalOmega.z:F3} rad/s.");
        }

        [UnityTest]
        public IEnumerator LosingAWing_CostsRollControl()
        {
            // Invariant #11: geometry must matter. Same incidence on both
            // wings → symmetric lift → no roll with hands off. Take the right
            // wing away and the surviving left wing's lift rolls the plane.
            BuildPlane(withRightWing: true, wingIncidenceDeg: 3f);
            yield return Fly(Vector2.zero, 0f, Steps);
            float symmetricRoll = Mathf.Abs(LocalOmega.z);
            Object.Destroy(_root);
            yield return null;

            BuildPlane(withRightWing: false, wingIncidenceDeg: 3f);
            yield return Fly(Vector2.zero, 0f, Steps);
            float oneWingRoll = Mathf.Abs(LocalOmega.z);

            Assert.Less(symmetricRoll, 0.05f,
                $"A symmetric plane with hands off must not roll; got {symmetricRoll:F3} rad/s.");
            Assert.Greater(oneWingRoll, symmetricRoll + 0.2f,
                $"Shooting off a wing must produce an uncommanded roll; got {oneWingRoll:F3} vs {symmetricRoll:F3} rad/s.");
        }

        // Spine + one wing pair only (no stabs), span parametrised — the
        // minimal rig for measuring pure aileron roll torque.
        private void BuildBareWingBot(float wingSpan)
        {
            _root = new GameObject("FoilLeverBot");
            _rb = _root.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            BlockGrid grid = _root.AddComponent<BlockGrid>();
            Robot robot = _root.AddComponent<Robot>();
            _drive = _root.AddComponent<RobotDrive>();
            _drive.Scheme = ControlScheme.Plane;

            BlockDefinition cube = MakeDef(BlockIds.Cube, BlockCategory.Structure, 1f);
            BlockDefinition cpu  = MakeDef(BlockIds.Cpu, BlockCategory.Cpu, 2f);
            BlockDefinition aero = MakeDef(BlockIds.Aero, BlockCategory.Movement, 0.6f);

            grid.PlaceBlock(cpu, new Vector3Int(0, 0, 0), Vector3Int.up);
            for (int z = -2; z <= 2; z++)
                if (z != 0) grid.PlaceBlock(cube, new Vector3Int(0, 0, z), Vector3Int.up);

            Vector3 dims = new Vector3(wingSpan, 0.08f, 0.9f);
            PlaceFoil(grid, aero, new Vector3Int(-1, 0, 0), new Vector3Int(-1, 0, 0), dims, 0f);
            PlaceFoil(grid, aero, new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 0), dims, 0f);
            robot.RecalculateAggregates();
        }

        [UnityTest]
        public IEnumerator WiderWing_GainsRollLever_NotJustArea()
        {
            // Lift acts at the foil's geometric centre (session 168): a 4×
            // span wing on the same mount lifts 4× AND levers from 2.5× the
            // arm, so its roll torque is ~10× a stock wing's. If the force
            // point ever snaps back to the mount cell, the ratio collapses
            // to area alone (~4×) and tinkering with span stops mattering
            // for roll. One physics step from rest so aero damping (ω×r)
            // hasn't fed back yet: torque = I_zz · ω_z / dt, dt cancels.
            BuildBareWingBot(1f);
            yield return Fly(new Vector2(1f, 0f), 0f, steps: 1);
            float torqueSpan1 = Mathf.Abs(_rb.inertiaTensor.z * LocalOmega.z);
            Object.Destroy(_root);
            yield return null;

            BuildBareWingBot(4f);
            yield return Fly(new Vector2(1f, 0f), 0f, steps: 1);
            float torqueSpan4 = Mathf.Abs(_rb.inertiaTensor.z * LocalOmega.z);

            Assert.Greater(torqueSpan1, 0f, "Span-1 wings must produce some roll torque.");
            float ratio = torqueSpan4 / torqueSpan1;
            Assert.Greater(ratio, 6f,
                $"4× span must out-torque 1× by area × lever (≈10×), not area alone (≈4×); got {ratio:F1}×.");
            Assert.Less(ratio, 14f,
                $"Roll torque ratio {ratio:F1}× exceeds area × lever — lever double-counted?");
        }

        [UnityTest]
        public IEnumerator NoChassisLevelPlaneController_IsAttached()
        {
            // The retired PlaneControlSubsystem type is gone from the build;
            // this guards the assembler path from ever growing a replacement
            // "torque on the chassis because a wing exists" component.
            BuildPlane(withRightWing: true, wingIncidenceDeg: 0f);
            yield return null;
            foreach (IDriveSubsystem s in _root.GetComponents<IDriveSubsystem>())
            {
                Assert.IsFalse(s.GetType().Name.Contains("PlaneControl"),
                    $"Chassis-level plane controller {s.GetType().Name} is retired (ADR-0009).");
            }
        }
    }
}
