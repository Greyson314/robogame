// =============================================================================
// GroundDriveWheelAuthorityTests — pins invariant #11 for the per-wheel drive
// force migration: GroundDriveSubsystem.Tick moves forward drive force from
// a single COM-applied force to per-wheel AddForceAtPosition, mirroring the
// suspension force WheelBlock already applies per-wheel today.
//
// WHY THIS MATTERS: right now, forward drive force is applied once at the
// chassis centre of mass — wheel COUNT and POSITION only gate whether drive
// force is allowed at all (ProbeGround's any-wheel-grounded check), never
// how much torque it produces or where. That means a bot missing every
// wheel on one side drives exactly as straight as a healthy one, as long as
// one wheel on the other side still touches ground — a hole in invariant
// #11 ("size + position of parts must matter", docs/invariants.md; see
// ADR-0009's aero-foil precedent, pinned for planes by FoilControlTests).
// Once drive force is applied at each wheel's own contact point, a lopsided
// wheel loss must show up as real yaw / lateral drift, exactly like losing
// a wing does for a plane.
//
// PATTERN: hand-built grid + primitive ground plane + reflection MakeDef
// helper, mirroring GroundDriveGroundingTests / GroundDriveSlopeTests. Adds
// Robot + RobotDrive (FoilControlTests' wiring) so Robot.RecalculateAggregates
// gives the rig a real mass-weighted COM + inertia tensor — required for a
// lopsided wheel loss to show up as a genuine physical asymmetry rather than
// just a force-magnitude change at a fixed COM. Driven through
// RobotDrive.ApplyMovement (the real per-frame player path), not
// GroundDriveSubsystem.Tick directly.
//
// CONNECTIVITY NOTE: Robot auto-detaches any block BFS-unreachable from the
// CPU as debris the frame after a removal (Robot.RunConnectivityNextFrame).
// Wheels are placed one cell diagonally off the CPU, which is NOT
// face-adjacent, so each side gets one small "spine" cube directly
// face-adjacent to the CPU and to both of that side's wheels — without it,
// destroying a wheel would orphan (and silently detach) every other wheel
// on the rig, not just the intended two.
//
// THRESHOLDS ARE BEST-EFFORT: derived from the force/torque algebra (see the
// per-test comments), not from running the sim — this subagent cannot launch
// Unity. Tune margins against the real sim once the implementation lands if
// this proves flaky.
// =============================================================================

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
    public class GroundDriveWheelAuthorityTests
    {
        private GameObject _root;
        private Rigidbody _chassisRb;
        private BlockGrid _grid;
        private Robot _robot;
        private RobotDrive _robotDrive;
        private GroundDriveSubsystem _groundDrive;
        private GameObject _ground;

        // Corner wheel references from the most recent BuildSymmetricRig
        // call, so a test can selectively destroy one side's pair.
        private BlockBehaviour _wheelFrontLeft;
        private BlockBehaviour _wheelFrontRight;
        private BlockBehaviour _wheelBackLeft;
        private BlockBehaviour _wheelBackRight;

        private const int SettleSteps   = 30; // let suspension compress to equilibrium under gravity
        private const int ResettleSteps = 20; // extra settle after a wheel is destroyed (removal + Robot's
                                               // deferred RecalculateAggregates + remaining suspension re-balance)
        private const int DriveSteps    = 75; // ~1.5 s @ the project's 50 Hz fixed step

        // "Near zero" bar for the symmetric rig's yaw rate / drift. Generous
        // on purpose: these gate a real physics change, not a tuned
        // production constant — see the class header.
        private const float SymmetricYawThreshold   = 0.05f; // rad/s
        private const float SymmetricDriftThreshold = 0.5f;  // metres (world X) over the drive window

        // How much MORE yaw the asymmetric rig must show than the symmetric
        // baseline to count as "measurably larger" — comfortably above
        // floating-point / settle noise, comfortably below the yaw a real
        // per-wheel force split should produce once implemented (losing 2
        // of 4 wheels on one side leaves the remaining pair pushing forward
        // from a full cell off-axis, an unambiguous torque today's COM-only
        // force can't produce at all).
        private const float YawMargin = 0.15f; // rad/s

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_ground != null) Object.Destroy(_ground);
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

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            // Top at y = -1.5: within the wheel's 1.65 m suspension cast
            // (restLength 1.15 + radius 0.5), same geometry as
            // GroundDriveGroundingTests / GroundDriveSlopeTests.
            _ground.transform.position = new Vector3(0f, -2.0f, 0f);
            _ground.transform.localScale = new Vector3(50f, 1f, 50f);
        }

        // Four drive wheels at the corners of a 2x2-cell track (+-1 cell in
        // both x and z), one CPU at the centre, and one small spine cube per
        // side purely to keep the grid connected (see class header). Wheels
        // have no per-instance footprint shift (unlike hover blades), so
        // their grid cell IS their force-application point — no extra
        // geometry math needed to keep this layout genuinely symmetric.
        // Robot.RecalculateAggregates gives the rigidbody a real
        // mass-weighted COM + inertia tensor from this layout.
        private void BuildSymmetricRig()
        {
            _root = new GameObject("WheelAuthorityChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = true; // real weight-on-suspension equilibrium (LOG-154 tuning assumes this)
            _chassisRb.isKinematic = false;
            _grid = _root.AddComponent<BlockGrid>();
            _robot = _root.AddComponent<Robot>();
            _robotDrive = _root.AddComponent<RobotDrive>();
            _robotDrive.Scheme = ControlScheme.Ground; // no blueprint on a hand-built grid

            BlockDefinition cpu   = MakeDef(BlockIds.Cpu, BlockCategory.Cpu, 10f);
            BlockDefinition spine = MakeDef(BlockIds.Cube, BlockCategory.Structure, 1f);
            BlockDefinition wheel = MakeDef(BlockIds.Wheel, BlockCategory.Movement, 5f);

            _grid.PlaceBlock(cpu, Vector3Int.zero);
            // Spine cubes: face-adjacent to the CPU (dx = +-1) AND to both of
            // their side's wheels (dz = +-1 from the spine) — placed
            // symmetrically (same mass, mirrored x) so they don't bias COM.
            _grid.PlaceBlock(spine, new Vector3Int(1, 0, 0));
            _grid.PlaceBlock(spine, new Vector3Int(-1, 0, 0));

            _wheelFrontLeft  = PlaceWheel(wheel, new Vector3Int(-1, 0,  1));
            _wheelFrontRight = PlaceWheel(wheel, new Vector3Int( 1, 0,  1));
            _wheelBackLeft   = PlaceWheel(wheel, new Vector3Int(-1, 0, -1));
            _wheelBackRight  = PlaceWheel(wheel, new Vector3Int( 1, 0, -1));

            _groundDrive = _root.AddComponent<GroundDriveSubsystem>();
            _robot.RecalculateAggregates();
        }

        private BlockBehaviour PlaceWheel(BlockDefinition def, Vector3Int cell)
        {
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed for wheel at {cell}");
            bb.gameObject.AddComponent<WheelBlock>();
            return bb;
        }

        private IEnumerator Settle(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                _robotDrive.ApplyMovement(Vector2.zero, 0f, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
        }

        private IEnumerator DriveForward(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                _robotDrive.ApplyMovement(new Vector2(0f, 1f), 0f, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator ForwardThrottle_SymmetricWheelLayout_ProducesNearZeroYawAndDrift()
        {
            // Baseline / regression guard: this must hold both BEFORE and
            // AFTER the per-wheel force migration — a healthy symmetric bot
            // must never develop uncommanded yaw just because propulsion
            // now acts at each wheel's contact point instead of the COM.
            CreateGround();
            BuildSymmetricRig();
            yield return Settle(SettleSteps);
            yield return DriveForward(DriveSteps);

            float yawRate = Mathf.Abs(_chassisRb.angularVelocity.y);
            float lateralDrift = Mathf.Abs(_chassisRb.position.x);

            Assert.Less(yawRate, SymmetricYawThreshold,
                $"A symmetric 4-wheel rig under full forward throttle must not develop yaw on its own; " +
                $"got {yawRate:F4} rad/s. Per-wheel force application must not introduce asymmetry on a " +
                "healthy bot.");
            Assert.Less(lateralDrift, SymmetricDriftThreshold,
                $"A symmetric rig driving straight must not drift sideways; got x={_chassisRb.position.x:F3} m " +
                $"after {DriveSteps} steps.");
        }

        [UnityTest]
        public IEnumerator ForwardThrottle_OneSideWheelsDestroyed_YawsMoreThanSymmetricBaseline()
        {
            // Baseline: identical rig, nothing destroyed.
            CreateGround();
            BuildSymmetricRig();
            yield return Settle(SettleSteps);
            yield return Settle(ResettleSteps); // match the asymmetric leg's total settle time
            yield return DriveForward(DriveSteps);
            float symmetricYaw = Mathf.Abs(_chassisRb.angularVelocity.y);
            Assert.Less(symmetricYaw, SymmetricYawThreshold,
                $"Sanity check failed: symmetric baseline yaw {symmetricYaw:F4} rad/s is not near zero; " +
                "the comparison below is meaningless until this holds.");
            Object.Destroy(_root);
            yield return null;

            // Same rig, both LEFT-side wheels destroyed via the production
            // damage path: TakeDamage -> BlockBehaviour.Destroyed ->
            // BlockGrid.RemoveBlock -> BlockRemoving, the same event
            // GroundDriveSubsystem uses to drop a wheel from its live set.
            // ("TakeDamage(overkill) is the production-equivalent path" —
            // same idiom HoverBladeBlockTests uses for its destroyed-blade
            // test.)
            BuildSymmetricRig();
            yield return Settle(SettleSteps);
            _wheelFrontLeft.TakeDamage(9999f);
            _wheelBackLeft.TakeDamage(9999f);
            yield return Settle(ResettleSteps);
            yield return DriveForward(DriveSteps);
            float asymmetricYaw = Mathf.Abs(_chassisRb.angularVelocity.y);

            Assert.Greater(asymmetricYaw, symmetricYaw + YawMargin,
                $"Losing both LEFT wheels must cost straight-line control: forward drive force now only " +
                $"acts on the surviving RIGHT-side wheels, off to one side of the chassis, which must yaw " +
                $"it. Got asymmetric={asymmetricYaw:F4} rad/s vs symmetric={symmetricYaw:F4} rad/s " +
                $"(need > symmetric + {YawMargin:F2}).");
        }
    }
}
