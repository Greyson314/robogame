// =============================================================================
// Playmode tests for GroundDriveSubsystem's slope handling (LOG-154).
// Raycast suspension carries no contact friction, so before the parking
// brake an idle bot on a hill kept any in-plane velocity forever and slid
// until a chassis cube snagged terrain. These tests encode the two fixes:
//   1. Idle on a slope → in-plane creep is braked to a stop (no slide).
//   2. Throttle uphill → drive force acts along the slope plane, so the
//      chassis actually climbs (gains height) instead of plowing into
//      the hill face with a horizontal push.
// =============================================================================

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Robogame.Block;
using Robogame.Movement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Movement
{
    public class GroundDriveSlopeTests
    {
        private GameObject _root;
        private Rigidbody _chassisRb;
        private BlockGrid _grid;
        private GroundDriveSubsystem _drive;
        private GameObject _slope;

        private const float SlopeDeg = 25f;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = true;    // gravity is the antagonist here
            _chassisRb.isKinematic = false;
            // Chassis-scale mass, NOT the grounding tests' 1 kg: the wheel's
            // spring/damper constants (600 N/m, 220 N·s/m) are tuned for a
            // multi-block chassis. On a 1 kg body they make a superball
            // that bounces down the ramp mostly airborne, which starves the
            // grounded-gated brake of contact frames.
            _chassisRb.mass = 30f;
            _chassisRb.linearDamping = 0f;
            _grid = _root.AddComponent<BlockGrid>();

            BlockDefinition wheelDef = MakeDef(BlockIds.Wheel);
            BlockBehaviour bb = _grid.PlaceBlock(wheelDef, Vector3Int.zero);
            bb.gameObject.AddComponent<WheelBlock>();

            _drive = _root.AddComponent<GroundDriveSubsystem>();

            // 25° ramp rising toward +Z, top surface ~1.5 m under the wheel
            // origin — inside the suspension cast (restLength 1.15 + radius
            // 0.5) like the flat-ground grounding tests.
            _slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _slope.name = "TestSlope";
            _slope.transform.position = new Vector3(0f, -2.0f, 0f);
            _slope.transform.rotation = Quaternion.Euler(-SlopeDeg, 0f, 0f);
            _slope.transform.localScale = new Vector3(50f, 1f, 50f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_slope != null) Object.Destroy(_slope);
        }

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, BlockCategory.Movement);
            return def;
        }

        private static DriveControl Idle() =>
            new DriveControl(Vector2.zero, vertical: 0f, fireHeld: false,
                             aimPoint: Vector3.zero, dt: Time.fixedDeltaTime, speedMultiplier: 1f);

        private static DriveControl Forward() =>
            new DriveControl(new Vector2(0f, 1f), vertical: 0f, fireHeld: false,
                             aimPoint: Vector3.zero, dt: Time.fixedDeltaTime, speedMultiplier: 1f);

        [UnityTest]
        public IEnumerator IdleOnSlope_DownhillCreep_IsBrakedToAStop()
        {
            // Let the suspension register the ramp, then shove the chassis
            // downhill. Nothing in the old code damped in-plane motion
            // (raycast suspension has no friction), so the shove persisted
            // indefinitely — the "slides until it snags" failure. The
            // parking brake must bleed it to a stop; once stopped, the
            // vertical suspension holds the slope by construction.
            yield return new WaitForFixedUpdate();
            _chassisRb.linearVelocity = new Vector3(0f, 0f, -2f);   // downhill = -Z

            for (int i = 0; i < 90; i++)
            {
                _drive.Tick(Idle());
                yield return new WaitForFixedUpdate();
            }

            Vector3 v = _chassisRb.linearVelocity;
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            Assert.Less(horizontal.magnitude, 0.3f,
                $"Idle chassis on a {SlopeDeg}° slope must brake to a hold; still moving at {v}.");
        }

        [UnityTest]
        public IEnumerator ThrottleUphill_ClimbsAlongTheSlope()
        {
            // Drive force must act in the slope plane: forward throttle on
            // a ramp rising toward +Z has to produce both forward AND
            // upward velocity (the old horizontal push wasted sin(θ) of it
            // plowing into the hill face).
            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 15; i++)
            {
                _drive.Tick(Forward());
                yield return new WaitForFixedUpdate();
            }

            // The instantaneous v.y/v.z ratio oscillates well below the
            // geometric tan(25°): the chassis rides a vertical spring, so
            // climb height arrives through spring lag and brief lift-offs
            // rather than as a clean slope-parallel velocity. Assert the
            // intent directly instead: sustained forward motion AND a
            // clearly positive climb rate (suspension jitter is < 0.2).
            Vector3 v = _chassisRb.linearVelocity;
            Assert.Greater(v.z, 2f,
                $"Throttle uphill must move the chassis forward; got {v}.");
            Assert.Greater(v.y, 0.4f,
                $"Climbing a {SlopeDeg}° ramp must gain height under throttle " +
                $"(slope-plane drive, LOG-154); got {v}.");
        }
    }
}
