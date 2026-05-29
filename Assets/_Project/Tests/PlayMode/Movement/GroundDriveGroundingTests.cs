// =============================================================================
// Playmode test for the GroundDriveSubsystem ground-contact gate (session 104
// follow-up): wheels may only put down drive force + steering yaw while at
// least one wheel is touching ground. A wheels-only chassis must NOT be able
// to accelerate or turn in mid-air (e.g. after a spring jump).
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
    public class GroundDriveGroundingTests
    {
        private GameObject _root;
        private Rigidbody _chassisRb;
        private BlockGrid _grid;
        private GroundDriveSubsystem _drive;
        private GameObject _ground;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = false;   // isolate drive force from gravity
            _chassisRb.isKinematic = false;
            _chassisRb.mass = 1f;
            _chassisRb.linearDamping = 0f;
            _grid = _root.AddComponent<BlockGrid>();

            // One wheel in the hierarchy so AnyWheelGrounded() has something
            // to poll; GroundDriveSubsystem seeds it in OnEnable.
            BlockDefinition wheelDef = MakeDef(BlockIds.Wheel);
            BlockBehaviour bb = _grid.PlaceBlock(wheelDef, Vector3Int.zero);
            bb.gameObject.AddComponent<WheelBlock>();

            _drive = _root.AddComponent<GroundDriveSubsystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_ground != null) Object.Destroy(_ground);
        }

        private static BlockDefinition MakeDef(string id)
        {
            BlockDefinition def = ScriptableObject.CreateInstance<BlockDefinition>();
            typeof(BlockDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, id);
            typeof(BlockDefinition).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, 100f);
            typeof(BlockDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(def, BlockCategory.Movement);
            return def;
        }

        private static DriveControl ForwardInput() =>
            new DriveControl(new Vector2(0f, 1f), vertical: 0f, fireHeld: false,
                             aimPoint: Vector3.zero, dt: Time.fixedDeltaTime, speedMultiplier: 1f);

        [UnityTest]
        public IEnumerator GroundDrive_NoWheelGrounded_AppliesNoDriveForce()
        {
            // No ground anywhere → every wheel raycast misses → IsGrounded
            // false. Forward input over several steps must produce no momentum.
            for (int i = 0; i < 6; i++)
            {
                _drive.Tick(ForwardInput());
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(_chassisRb.linearVelocity.magnitude, 0.01f,
                $"Wheels-only chassis in mid-air must not accelerate; got {_chassisRb.linearVelocity}.");
        }

        [UnityTest]
        public IEnumerator GroundDrive_NoWheelGrounded_CanStillSteer()
        {
            // Turning in the air IS allowed (intentional middle ground) — only
            // linear drive is gated. A turn input aloft must still yaw.
            for (int i = 0; i < 6; i++)
            {
                _drive.Tick(new DriveControl(new Vector2(1f, 0f), vertical: 0f, fireHeld: false,
                                             aimPoint: Vector3.zero, dt: Time.fixedDeltaTime, speedMultiplier: 1f));
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(Mathf.Abs(_chassisRb.angularVelocity.y), 0.05f,
                $"Air-steering must still yaw the chassis; got angVel {_chassisRb.angularVelocity}.");
        }

        [UnityTest]
        public IEnumerator GroundDrive_JumpImpulse_DoesNotFireInAir()
        {
            // The wheel "hop" is grounded-only: re-hopping mid-air would let a
            // bot fly forever by spamming Space. No ground → no hop.
            for (int i = 0; i < 4; i++)
            {
                _drive.Tick(new DriveControl(Vector2.zero, vertical: 1f, fireHeld: false,
                                             aimPoint: Vector3.zero, dt: Time.fixedDeltaTime, speedMultiplier: 1f));
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(_chassisRb.linearVelocity.magnitude, 0.01f,
                $"Wheel hop must NOT fire airborne; got velocity {_chassisRb.linearVelocity}.");
        }

        [UnityTest]
        public IEnumerator GroundDrive_JumpImpulse_FiresWhenGrounded()
        {
            // With a wheel on the ground, the hop fires (upward velocity).
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -2.0f, 0f); // top ≈ -1.5, within wheel cast
            _ground.transform.localScale = new Vector3(50f, 1f, 50f);

            yield return new WaitForFixedUpdate(); // let the wheel register ground

            _drive.Tick(new DriveControl(Vector2.zero, vertical: 1f, fireHeld: false,
                                         aimPoint: Vector3.zero, dt: Time.fixedDeltaTime, speedMultiplier: 1f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Greater(_chassisRb.linearVelocity.y, 1f,
                $"Grounded hop must apply upward velocity; got {_chassisRb.linearVelocity}.");
        }

        [UnityTest]
        public IEnumerator GroundDrive_WheelGrounded_DrivesForward()
        {
            // Ground plane just under the wheel → IsGrounded true → the same
            // forward input must now build +Z velocity. (Positive control so
            // the air test above isn't vacuously passing.)
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            // Top at y ≈ -1.5: within the wheel's 1.65 m cast (restLength 1.15
            // + radius 0.5) but near rest, so suspension stays gentle and the
            // wheel doesn't launch itself out of contact during the test.
            _ground.transform.position = new Vector3(0f, -2.0f, 0f);
            _ground.transform.localScale = new Vector3(50f, 1f, 50f);

            // Let the wheel's suspension raycast register ground first.
            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 10; i++)
            {
                _drive.Tick(ForwardInput());
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(_chassisRb.linearVelocity.z, 0.1f,
                $"A grounded chassis must drive forward (+Z); got {_chassisRb.linearVelocity}.");
        }
    }
}
