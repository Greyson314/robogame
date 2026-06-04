using System.Collections;
using NUnit.Framework;
using Robogame.Movement;
using Robogame.Network.Robot;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Network
{
    /// <summary>
    /// Phase-3.6 determinism guard (NETCODE_PLAN §16). Pins the load-bearing
    /// claim of Phase 3.5: replaying an identical <see cref="InputCommand"/>
    /// stream through <see cref="NetworkInputSource.EnterReplay"/> +
    /// <see cref="RobotDrive.ApplyMovement"/> + <see cref="Physics.Simulate"/>
    /// twice must produce poses that drift less than the 0.5 m/s budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The replay is correct by construction today, but a future change that
    /// silently introduces time-of-day, <c>Random.value</c>, or per-frame
    /// allocation into the drive-dispatch path would break Phase 3.5
    /// reconciliation. This test fires first — before MPPM smoke would
    /// notice — because it pins the property at the drive layer, no NGO
    /// session required.
    /// </para>
    /// <para>
    /// The chassis is the minimum that exercises <c>RobotDrive.ApplyMovement</c>
    /// end-to-end: a Rigidbody + RobotDrive + NetworkInputSource +
    /// <see cref="ForwardThruster"/> (one test-only IDriveSubsystem that turns
    /// <c>Move.y</c> into a constant chassis-forward force). No real blocks,
    /// no blueprint, no scene assets — so the test stays portable and fast.
    /// </para>
    /// </remarks>
    public sealed class PredictionDeterminismTest
    {
        // 50 fixed ticks at 50 Hz = 1 simulated second; the §16 budget is
        // worded "0.5 m / second of identical input."
        private const int Ticks = 50;
        private const float DriftBudgetMetres = 0.5f;

        /// <summary>Minimum IDriveSubsystem — turns <c>Move.y</c> into a
        /// constant chassis-forward force so the rig actually moves and the
        /// determinism property is observable in <see cref="Rigidbody.position"/>.</summary>
        private sealed class ForwardThruster : MonoBehaviour, IDriveSubsystem
        {
            public int Order => 0;
            public bool IsOperational => true;

            private Rigidbody _rb;
            private RobotDrive _drive;

            private void Awake()
            {
                _rb = GetComponent<Rigidbody>();
                _drive = GetComponent<RobotDrive>();
            }

            private void OnEnable() => _drive?.Register(this);
            private void OnDisable() => _drive?.Unregister(this);

            // This test never redirects; no-op satisfies the interface.
            public void SetForceTarget(Rigidbody body) { }

            public void Tick(in DriveControl control)
            {
                if (_rb == null) return;
                _rb.AddForce(transform.forward * (control.Move.y * 50f),
                    ForceMode.Force);
            }
        }

        [UnityTest]
        public IEnumerator Replay_IdenticalInput_DriftsLessThanBudget()
        {
            // Stepped physics so Physics.Simulate(dt) is the only integrator;
            // otherwise FixedUpdate auto-sim would double-tick the chassis.
            SimulationMode prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            GameObject go = null;
            try
            {
                go = new GameObject("DeterminismChassis");
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearDamping = 0f;
                rb.angularDamping = 0f;

                NetworkInputSource input = go.AddComponent<NetworkInputSource>();
                RobotDrive drive = go.AddComponent<RobotDrive>();
                // Suppress the camera-ray aim path so a missing Camera.main
                // / Mouse in the test runner can't introduce a per-tick
                // raycast and become a hidden non-determinism source.
                drive.AimPointOverride = go.transform.position + Vector3.forward * 30f;
                go.AddComponent<ForwardThruster>();

                // Let Awake/OnEnable settle so the subsystem self-registers.
                yield return null;

                Vector3 startPos = rb.position;
                Quaternion startRot = rb.rotation;

                Vector3 poseA = RunReplay(rb, input, drive);

                rb.position = startPos;
                rb.rotation = startRot;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();

                Vector3 poseB = RunReplay(rb, input, drive);

                float drift = Vector3.Distance(poseA, poseB);
                Assert.Less(drift, DriftBudgetMetres,
                    $"Replay drifted {drift:F6} m over {Ticks * Time.fixedDeltaTime:F2} s of identical input. " +
                    $"Budget: {DriftBudgetMetres} m / s. A regression here usually means non-determinism " +
                    "leaked into RobotDrive / NetworkInputSource / a drive subsystem.");
            }
            finally
            {
                if (go != null) Object.Destroy(go);
                Physics.simulationMode = prev;
            }
        }

        private static Vector3 RunReplay(Rigidbody rb, NetworkInputSource input, RobotDrive drive)
        {
            float dt = Time.fixedDeltaTime;
            for (int tick = 0; tick < Ticks; tick++)
            {
                var cmd = new InputCommand
                {
                    Tick = tick,
                    Move = new Vector2(0f, 1f),
                };
                input.EnterReplay(in cmd);
                drive.ApplyMovement(cmd.Move, 0f, dt);
                Physics.Simulate(dt);
            }
            input.ExitReplay();
            return rb.position;
        }
    }
}
