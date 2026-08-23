// =============================================================================
// HoverPadAuthorityTests — pins invariant #11 for the per-hover-pad drive
// force migration: HoverDriveSubsystem.Tick moves forward thrust + yaw
// torque from a single COM-applied force to per-pad AddForceAtPosition,
// mirroring HoverBladeBlock's existing per-pad LIFT force. See
// GroundDriveWheelAuthorityTests for the wheeled-drive twin of this pin,
// and docs/invariants.md #11.
//
// WHY THIS MATTERS: HoverBladeBlock already applies LIFT per-pad, at each
// pad's own world position — losing a corner pad already tips the chassis
// today. But HoverDriveSubsystem's forward thrust + yaw torque are still
// applied once at the chassis centre of mass, so losing a pad does NOT
// change how hard or where the chassis pushes itself forward. Once thrust
// is distributed across the surviving pads at their own positions, losing
// one of four symmetric corner pads breaks a 2-2 left/right thrust split
// into 2-1, which must cost control under throttle — on top of (not
// instead of) the pre-existing lift-asymmetry tip.
//
// METRIC CHOICE: full angular-velocity MAGNITUDE (world space), not just
// yaw. A missing corner pad couples roll/pitch (lift loss) with yaw
// (thrust-split loss), and HoverDriveSubsystem has no active self-righting
// (unlike GroundDriveSubsystem — see its own class doc: "lift force at
// each blade attach point naturally rights the chassis" is a PASSIVE
// effect that a destroyed pad removes, not an active correction). Pinning
// a single axis would be fragile; total rotation rate is the robust
// "is this chassis still under control" signal invariant #11 cares about.
//
// FOOTPRINT-SHIFT NOTE: a hover pad's force point is NOT its grid cell —
// HoverBladeBlock shifts it by +(N-1)/2 in both x and z from the anchor
// cell (its "near corner") to the footprint's true centre, unconditionally
// (not mirrored per corner). To get four pads whose ACTUAL force points
// form a symmetric square about the chassis origin, the anchor cells below
// are chosen by inverting that shift, not by eyeballing a symmetric-looking
// set of grid coordinates — see the worked comment on BuildSymmetricRig.
//
// PATTERN: pad construction mirrors HoverBladeBlockTests (PlaceBlock +
// SetDims + AddComponent<HoverBladeBlock>, ground as a separate primitive);
// Robot + RobotDrive wiring mirrors FoilControlTests / this session's
// GroundDriveWheelAuthorityTests so Robot.RecalculateAggregates gives the
// rig a real mass-weighted COM/inertia tensor. Connectivity: as in the
// wheeled twin, Robot auto-detaches any block unreachable from the CPU the
// frame after a removal, so every pad needs a face-adjacent path back to
// the CPU through lightweight "spine" cubes (mass negligible vs. the pads,
// since the two spines aren't symmetric in cube COUNT — see below).
//
// THRESHOLDS ARE BEST-EFFORT, more so than the wheeled twin: hover has no
// active uprighting, so a destroyed corner may settle into a new tilted
// equilibrium rather than a clean small rotation signal, and this subagent
// cannot launch Unity to calibrate against the real sim. Tune margins (and
// possibly DriveSteps, if the asymmetric rig tips further than expected)
// once the implementation lands if this proves flaky.
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
    public class HoverPadAuthorityTests
    {
        private GameObject _root;
        private Rigidbody _chassisRb;
        private BlockGrid _grid;
        private Robot _robot;
        private RobotDrive _robotDrive;
        private HoverDriveSubsystem _hoverDrive;
        private GameObject _ground;

        private BlockBehaviour _padFrontLeft;
        private BlockBehaviour _padFrontRight;
        private BlockBehaviour _padBackLeft;
        private BlockBehaviour _padBackRight;

        private const int SettleSteps   = 60; // underdamped spring (session-99 tuning) needs several periods to settle
        private const int ResettleSteps = 20; // extra settle after a pad is destroyed
        private const int DriveSteps    = 40; // ~0.8 s — short on purpose: hover has no active uprighting, so a
                                               // destroyed-corner rig can tip further the longer it keeps running

        private const float SymmetricRotationThreshold = 0.08f; // rad/s, near-zero bar (generous for hover's settle bounce)
        private const float RotationMargin              = 0.15f; // rad/s, "measurably larger" bar for the asymmetric rig

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

        // Ground plane whose top sits at the pads' resting target-altitude
        // gap (2.5 m, HoverBladeTuningConfig.Default.TargetAltitude) below
        // the pad origins, so the rig starts near its hover equilibrium
        // instead of launching from a large initial spring error.
        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -3.0f, 0f); // top at y = -2.5
            _ground.transform.localScale = new Vector3(50f, 1f, 50f);
        }

        // Four size-2 hover pads whose ACTUAL force points sit at
        // (+-1.5, *, +-1.5) — a symmetric square about the CPU at the
        // origin — plus one CPU and five negligible-mass "spine" cubes for
        // grid connectivity (see class header).
        //
        // Worked anchor math: HoverBladeBlock's force point = anchor cell +
        // (0.5, 0, 0.5) for a size-2 pad (unconditional half-cell shift
        // toward +x/+z, mount-up zeroed). Solving anchor = centre - (0.5,0.5)
        // for the four desired centres:
        //   centre ( 1.5,  1.5) -> anchor ( 1, 1)  [front-right]
        //   centre ( 1.5, -1.5) -> anchor ( 1, -2) [back-right]
        //   centre (-1.5,  1.5) -> anchor (-2, 1)  [front-left]
        //   centre (-1.5, -1.5) -> anchor (-2, -2) [back-left]
        // The anchors look asymmetric as raw integers (1 vs -2, not +-1) —
        // that is the point: it is what makes the actual FORCE points
        // symmetric once the shift is applied. Using naively "symmetric"
        // anchors like (+-1, +-1) instead would silently offset all four
        // force points by a uniform (+0.5, +0.5) from the CPU, producing a
        // constant roll/pitch bias even in the "symmetric" rig.
        private void BuildSymmetricRig()
        {
            _root = new GameObject("HoverAuthorityChassis");
            _chassisRb = _root.AddComponent<Rigidbody>();
            _chassisRb.useGravity = true; // real weight-vs-lift equilibrium
            _chassisRb.isKinematic = false;
            _grid = _root.AddComponent<BlockGrid>();
            _robot = _root.AddComponent<Robot>();
            _robotDrive = _root.AddComponent<RobotDrive>();
            _robotDrive.Scheme = ControlScheme.Ground; // no blueprint on a hand-built grid

            BlockDefinition cpu   = MakeDef(BlockIds.Cpu, BlockCategory.Cpu, 10f);
            BlockDefinition spine = MakeDef(BlockIds.Cube, BlockCategory.Structure, 0.01f); // negligible: see class header
            BlockDefinition pad   = MakeDef(BlockIds.HoverBlade, BlockCategory.Movement, 5f);

            _grid.PlaceBlock(cpu, Vector3Int.zero);

            // Right spine: CPU -> (1,0,0) -> front-right pad; (1,0,0) -> (1,0,-1) -> back-right pad.
            _grid.PlaceBlock(spine, new Vector3Int(1, 0, 0));
            _grid.PlaceBlock(spine, new Vector3Int(1, 0, -1));
            // Left spine: CPU -> (-1,0,0) -> (-2,0,0) -> front-left pad; (-2,0,0) -> (-2,0,-1) -> back-left pad.
            _grid.PlaceBlock(spine, new Vector3Int(-1, 0, 0));
            _grid.PlaceBlock(spine, new Vector3Int(-2, 0, 0));
            _grid.PlaceBlock(spine, new Vector3Int(-2, 0, -1));

            _padFrontRight = PlaceHoverPad(pad, new Vector3Int( 1, 0,  1));
            _padBackRight  = PlaceHoverPad(pad, new Vector3Int( 1, 0, -2));
            _padFrontLeft  = PlaceHoverPad(pad, new Vector3Int(-2, 0,  1));
            _padBackLeft   = PlaceHoverPad(pad, new Vector3Int(-2, 0, -2));

            _hoverDrive = _root.AddComponent<HoverDriveSubsystem>();
            _robot.RecalculateAggregates();
        }

        private BlockBehaviour PlaceHoverPad(BlockDefinition def, Vector3Int cell, int size = 2)
        {
            BlockBehaviour bb = _grid.PlaceBlock(def, cell);
            Assert.IsNotNull(bb, $"PlaceBlock failed for hover pad at {cell}");
            bb.SetDims(new Vector3(size, 0f, 0f)); // must happen before HoverBladeBlock is added — see HoverBladeBlockTests
            bb.gameObject.AddComponent<HoverBladeBlock>();
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

        private float RotationRate => _chassisRb.angularVelocity.magnitude;

        [UnityTest]
        public IEnumerator ForwardThrust_SymmetricPadLayout_ProducesNearZeroAttitudeBias()
        {
            // Baseline / regression guard: must hold both BEFORE and AFTER
            // the per-pad thrust migration — a healthy symmetric hover bot
            // must not develop uncommanded rotation just because propulsion
            // now acts at each pad's own position instead of the COM.
            CreateGround();
            BuildSymmetricRig();
            yield return Settle(SettleSteps);
            yield return Settle(ResettleSteps);
            yield return DriveForward(DriveSteps);

            float rotationRate = RotationRate;
            Assert.Less(rotationRate, SymmetricRotationThreshold,
                $"A symmetric 4-pad hover rig under full forward thrust must not develop uncommanded " +
                $"rotation; got {rotationRate:F4} rad/s. Per-pad thrust application must not introduce " +
                "asymmetry on a healthy bot.");
        }

        [UnityTest]
        public IEnumerator ForwardThrust_OneFrontPadDestroyed_AttitudeBiasExceedsSymmetricBaseline()
        {
            // Baseline: identical rig, nothing destroyed.
            CreateGround();
            BuildSymmetricRig();
            yield return Settle(SettleSteps);
            yield return Settle(ResettleSteps);
            yield return DriveForward(DriveSteps);
            float symmetricRate = RotationRate;
            Assert.Less(symmetricRate, SymmetricRotationThreshold,
                $"Sanity check failed: symmetric baseline rotation {symmetricRate:F4} rad/s is not near " +
                "zero; the comparison below is meaningless until this holds.");
            Object.Destroy(_root);
            yield return null;

            // Same rig, front-right pad destroyed via the production damage
            // path: TakeDamage -> BlockBehaviour.Destroyed -> BlockGrid.RemoveBlock
            // -> BlockRemoving, the same event HoverDriveSubsystem uses to drop a
            // pad from its live set — the same "TakeDamage(overkill) is the
            // production-equivalent path" idiom HoverBladeBlockTests uses.
            BuildSymmetricRig();
            yield return Settle(SettleSteps);
            _padFrontRight.TakeDamage(9999f);
            yield return Settle(ResettleSteps);
            yield return DriveForward(DriveSteps);
            float asymmetricRate = RotationRate;

            Assert.Greater(asymmetricRate, symmetricRate + RotationMargin,
                $"Losing one of four symmetric hover pads must cost control: the surviving pads no longer " +
                $"split lift OR (once implemented) forward thrust evenly, which must rotate the chassis. " +
                $"Got asymmetric={asymmetricRate:F4} rad/s vs symmetric={symmetricRate:F4} rad/s " +
                $"(need > symmetric + {RotationMargin:F2}).");
        }
    }
}
