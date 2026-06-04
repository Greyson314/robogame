using System.Collections;
using NUnit.Framework;
using Robogame.Movement;
using Robogame.Network.Prediction;
using Robogame.Network.Robot;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Network
{
    /// <summary>
    /// ADR-0002 equivalence guard. Pins the load-bearing claim of the
    /// prediction-scene CSP rewrite: re-stepping the chassis as an isolated
    /// mirror — by redirecting the drive subsystems onto the mirror
    /// (<see cref="RobotDrive.SetReplayForceTarget"/>) and stepping it with
    /// <see cref="PhysicsScene.Simulate"/> — reproduces what a global
    /// <see cref="Physics.Simulate"/> would do, to floating-point precision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The off-COM case is the one that matters: an earlier force/torque
    /// transfer via <c>GetAccumulatedForce/Torque</c> moved the chassis but
    /// never turned it, because <c>GetAccumulatedTorque</c> does not surface
    /// the torque an <c>AddForceAtPosition</c> induces. Redirecting the
    /// subsystem so PhysX integrates the force-at-position natively on the
    /// mirror fixes that — this test fails if the redirect path regresses.
    /// </para>
    /// </remarks>
    public sealed class PredictionMirrorTest
    {
        private const float RotTol = 1.0f;

        /// <summary>Configurable test subsystem. <see cref="Offset"/> = 0
        /// pushes through the COM (pure linear); non-zero adds torque via
        /// AddForceAtPosition. Honours the ADR-0002 force-target redirect.</summary>
        private sealed class TestThruster : MonoBehaviour, IDriveSubsystem
        {
            public float Force = 10f;
            public float Offset = 0f;
            public int Order => 0;
            public bool IsOperational => true;

            private Rigidbody _rb;
            private Rigidbody _replayBody;
            private RobotDrive _drive;

            public void SetForceTarget(Rigidbody body) => _replayBody = body;
            private Rigidbody Body => _replayBody != null ? _replayBody : _rb;

            private void Awake()
            {
                _rb = GetComponent<Rigidbody>();
                _drive = GetComponent<RobotDrive>();
            }

            private void OnEnable() => _drive?.Register(this);
            private void OnDisable() => _drive?.Unregister(this);

            public void Tick(in DriveControl control)
            {
                if (Body == null) return;
                Vector3 f = transform.forward * (control.Move.y * Force);
                if (Mathf.Approximately(Offset, 0f))
                    Body.AddForce(f, ForceMode.Force);
                else
                    Body.AddForceAtPosition(f, transform.position + transform.right * Offset, ForceMode.Force);
            }
        }

        [UnityTest]
        public IEnumerator MirrorReplay_LinearForceGravityDrag_MatchesBaseline()
        {
            // Smooth straight-line motion is non-chaotic, so a long horizon is
            // a strong check of the force / velocity / gravity / drag transfer.
            yield return RunEquivalence(force: 50f, offset: 0f, ticks: 50, minBaselineRotationDeg: 0f, posTol: 0.02f);
        }

        [UnityTest]
        public IEnumerator MirrorReplay_OffsetTorque_MatchesBaseline()
        {
            // Off-COM force exercises the torque channel — the thing the old
            // GetAccumulatedTorque transfer could NOT carry. Horizon is short
            // and replay-representative (real replay is 2-3 ticks): sustained
            // off-COM spin is chaotically sensitive across two PhysicsScenes,
            // which is not what production does. minBaselineRotationDeg asserts
            // the baseline actually turned, so a no-torque mirror still fails.
            yield return RunEquivalence(force: 40f, offset: 0.4f, ticks: 6, minBaselineRotationDeg: 3f, posTol: 0.15f);
        }

        private IEnumerator RunEquivalence(float force, float offset, int ticks, float minBaselineRotationDeg, float posTol)
        {
            SimulationMode prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            GameObject go = null;
            Rigidbody mirror = null;
            try
            {
                go = new GameObject("MirrorChassis");
                go.AddComponent<BoxCollider>();
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.linearDamping = 0.2f;
                rb.angularDamping = 2f;
                // Pin COM + inertia explicitly, exactly as production does via
                // Robot.RecalculateAggregates — so the mirror's copied tensor is
                // unambiguously identical to the body being baselined.
                rb.centerOfMass = Vector3.zero;
                rb.inertiaTensor = Vector3.one;
                rb.inertiaTensorRotation = Quaternion.identity;

                NetworkInputSource input = go.AddComponent<NetworkInputSource>();
                RobotDrive drive = go.AddComponent<RobotDrive>();
                drive.AimPointOverride = go.transform.position + Vector3.forward * 30f;
                TestThruster thruster = go.AddComponent<TestThruster>();
                thruster.Force = force;
                thruster.Offset = offset;

                yield return null; // let OnEnable self-register the subsystem

                Vector3 startPos = rb.position;
                Quaternion startRot = rb.rotation;

                // --- Baseline: global Physics.Simulate on the real body. ---
                (Vector3 basePos, Quaternion baseRot) = RunGlobal(rb, input, drive, ticks);

                float baseRotated = Quaternion.Angle(startRot, baseRot);
                if (minBaselineRotationDeg > 0f)
                {
                    Assert.Greater(baseRotated, minBaselineRotationDeg,
                        $"Baseline only rotated {baseRotated:F2}°; the off-COM torque isn't being " +
                        "exercised, so this test can't prove the mirror carries it. Raise force/offset.");
                }

                rb.position = startPos;
                rb.rotation = startRot;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();

                // --- Mirror: isolated PredictionScene via subsystem redirect. ---
                mirror = PredictionScene.CreateMirrorBody(rb);
                Assert.IsNotNull(mirror, "CreateMirrorBody returned null.");
                Assert.IsTrue(PredictionScene.IsCreated, "Prediction scene not created.");

                (Vector3 mirrorPos, Quaternion mirrorRot) =
                    RunMirror(rb, mirror, input, drive, startPos, startRot, ticks);

                float posDrift = Vector3.Distance(basePos, mirrorPos);
                float rotDrift = Quaternion.Angle(baseRot, mirrorRot);

                // Rotation drift is the real discriminator for the off-COM case.
                // A correct redirect tracks the baseline's rotation to within the
                // cross-PhysicsScene float floor (angular integration isn't
                // bit-identical across scene instances — translation is, see the
                // linear case); a mirror that drops the torque (the old transfer
                // bug) drifts by ~the FULL baseline rotation. So the tolerance is
                // relative: well under half the baseline's turn passes, ~all of it
                // fails. Position drift under spin rides the same float floor, so
                // it gets a loose absolute bound. Production replay is 2-3 ticks
                // and the next snapshot corrects any residual drift regardless.
                float rotTolEffective = Mathf.Max(RotTol, 0.4f * baseRotated);
                Assert.Less(rotDrift, rotTolEffective,
                    $"Mirror orientation drifted {rotDrift:F4}° from baseline (it turned " +
                    $"{baseRotated:F2}°; force={force}, offset={offset}). The off-COM torque is not being carried.");
                Assert.Less(posDrift, posTol,
                    $"Mirror position drifted {posDrift:F6} m from the global-simulate baseline " +
                    $"over {ticks} ticks (force={force}, offset={offset}).");
            }
            finally
            {
                if (mirror != null) PredictionScene.ReleaseMirrorBody(mirror);
                if (go != null) Object.Destroy(go);
                Physics.simulationMode = prev;
            }
        }

        private static (Vector3, Quaternion) RunGlobal(Rigidbody rb, NetworkInputSource input, RobotDrive drive, int ticks)
        {
            float dt = Time.fixedDeltaTime;
            for (int tick = 0; tick < ticks; tick++)
            {
                var cmd = new InputCommand { Tick = tick, Move = new Vector2(0f, 1f) };
                input.EnterReplay(in cmd);
                drive.ApplyMovement(cmd.Move, 0f, dt);
                Physics.Simulate(dt);
            }
            input.ExitReplay();
            return (rb.position, rb.rotation);
        }

        // Mirror of NetworkRobotMovement.ReconcileAndReplay's inner loop.
        private static (Vector3, Quaternion) RunMirror(
            Rigidbody rb, Rigidbody mirror, NetworkInputSource input, RobotDrive drive,
            Vector3 startPos, Quaternion startRot, int ticks)
        {
            PhysicsScene predScene = PredictionScene.PhysicsScene;
            float dt = Time.fixedDeltaTime;

            mirror.position = startPos;
            mirror.rotation = startRot;
            mirror.linearVelocity = Vector3.zero;
            mirror.angularVelocity = Vector3.zero;

            drive.SetReplayForceTarget(mirror);
            try
            {
                for (int tick = 0; tick < ticks; tick++)
                {
                    rb.position = mirror.position;
                    rb.rotation = mirror.rotation;
                    Physics.SyncTransforms();

                    var cmd = new InputCommand { Tick = tick, Move = new Vector2(0f, 1f) };
                    input.EnterReplay(in cmd);
                    drive.ApplyMovement(cmd.Move, 0f, dt);
                    predScene.Simulate(dt);
                }
            }
            finally
            {
                drive.SetReplayForceTarget(null);
                input.ExitReplay();
            }
            return (mirror.position, mirror.rotation);
        }
    }
}
