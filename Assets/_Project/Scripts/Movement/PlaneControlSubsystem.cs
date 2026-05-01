using UnityEngine;
using Robogame.Core;

namespace Robogame.Movement
{
    /// <summary>
    /// Chassis-level pitch / roll / auto-yaw controller for aircraft. Reads
    /// the same <see cref="DriveControl.Move"/> stick that
    /// <see cref="GroundDriveSubsystem"/> uses, but maps it to flight
    /// torques instead of ground forces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mapping (chosen for arcade feel — flips of these are easy):
    /// <list type="bullet">
    ///   <item><description><c>Vertical</c> (Space / Shift): pitch. +1 = nose up.</description></item>
    ///   <item><description><c>Move.x</c> (A/D): roll. +1 = bank right.</description></item>
    ///   <item><description><c>Move.y</c> (W/S): throttle, consumed by <see cref="ThrusterBlock"/>.</description></item>
    ///   <item><description>Yaw: automatic, proportional to bank angle. Banked right → yaws right (coordinated turn).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Torques are applied with <see cref="ForceMode.Acceleration"/> so the
    /// response is mass-independent. This means tuning carries between
    /// chassis sizes — a heavier plane just needs more thrust + lift, not
    /// more pitch authority.
    /// </para>
    /// <para>
    /// Coexists with <see cref="ThrusterBlock"/>s (Order 0, force) and
    /// <see cref="AeroSurfaceBlock"/>s (passive lift, no subsystem).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlaneControlSubsystem : MonoBehaviour, IDriveSubsystem
    {
        [Tooltip("Optional tuning profile. If assigned, OVERRIDES the inline values below.")]
        [SerializeField] private PlaneControlTuning _tuning;

        [Header("Authority (rad/s²)")]
        [Tooltip("Pitch torque (acceleration) at full input.")]
        [SerializeField, Min(0f)] private float _pitchPower = 7.5f;

        [Tooltip("Roll torque (acceleration) at full input.")]
        [SerializeField, Min(0f)] private float _rollPower = 9.0f;

        [Tooltip("Yaw acceleration per unit of bank tilt (rad/s² per [-1..1] of right.y).")]
        [SerializeField, Min(0f)] private float _yawFromBank = 2.0f;

        [Header("Damping (rad/s² per rad/s)")]
        [Tooltip("Local pitch-rate damping. Higher = stiffer feel.")]
        [SerializeField, Min(0f)] private float _pitchDamping = 3.5f;

        [Tooltip("Local roll-rate damping.")]
        [SerializeField, Min(0f)] private float _rollDamping = 2.8f;

        [Tooltip("Local yaw-rate damping.")]
        [SerializeField, Min(0f)] private float _yawDamping = 1.6f;

        private float PitchPower   => Tweakables.Get(Tweakables.PlanePitchPower);
        private float RollPower    => Tweakables.Get(Tweakables.PlaneRollPower);
        private float YawFromBank  => Tweakables.Get(Tweakables.PlaneYawFromBank);
        private float PitchDamping => Tweakables.Get(Tweakables.PlanePitchDamping);
        private float RollDamping  => Tweakables.Get(Tweakables.PlaneRollDamping);
        private float YawDamping   => Tweakables.Get(Tweakables.PlaneYawDamping);

        public int Order => 50; // between actuators (0) and assists (200)
        public bool IsOperational => isActiveAndEnabled;

        private Rigidbody _rb;
        private RobotDrive _drive;

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);
            Debug.Log(
                $"[Robogame] PlaneControl live values (source=Tweakables): " +
                $"pitch={PitchPower:F2} roll={RollPower:F2} yawFromBank={YawFromBank:F2} " +
                $"pitchDamp={PitchDamping:F2} rollDamp={RollDamping:F2} yawDamp={YawDamping:F2}",
                this);
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;

            // Local angular velocity for damping.
            Vector3 localOmega = transform.InverseTransformDirection(_rb.angularVelocity);

            // Pitch around local right (X). Right-hand rule: a positive
            // torque around +X rotates +Y toward +Z, which is nose-DOWN. So
            // we negate to make Space (+Vertical) pitch up.
            float pitchInput = Mathf.Clamp(control.Vertical, -1f, 1f);
            float pitchAccel = -pitchInput * PitchPower - localOmega.x * PitchDamping;

            // Roll around local forward (Z). Right-hand rule means rolling
            // right (top toward +X) is a NEGATIVE Z rotation, hence the sign flip.
            float rollInput = Mathf.Clamp(control.Move.x, -1f, 1f);
            float rollAccel = -rollInput * RollPower - localOmega.z * RollDamping;

            // Auto-yaw: when banked right, transform.right.y < 0, so we want
            // a positive yaw (turn right). Coupling sign: yaw = -right.y * gain.
            float bankSignal = -Vector3.Dot(transform.right, Vector3.up);
            float yawAccel = bankSignal * YawFromBank - localOmega.y * YawDamping;

            // Compose in local space then push out to world.
            Vector3 localTorque = new Vector3(pitchAccel, yawAccel, rollAccel);
            Vector3 worldTorque = transform.TransformDirection(localTorque);
            _rb.AddTorque(worldTorque, ForceMode.Acceleration);
        }
    }
}
