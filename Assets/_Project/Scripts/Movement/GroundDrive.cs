using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Placeholder physics-based ground drive: applies forward force and
    /// yaw torque to a <see cref="Rigidbody"/> based on planar input.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intentionally simplistic — no wheel colliders, no traction model,
    /// no slope handling. This exists so the rest of the architecture
    /// (input → movement provider → controller) can be exercised end-to-end
    /// before a real wheel system arrives.
    /// </para>
    /// <para>
    /// Replace with <c>WheelDrive</c> / <c>HoverDrive</c> / <c>JetDrive</c>
    /// later. The <see cref="IMovementProvider"/> seam means callers don't change.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class GroundDrive : MonoBehaviour, IMovementProvider
    {
        [Header("Tuning — Drive")]
        [Tooltip("Forward acceleration in m/s^2 per unit of forward input.")]
        [SerializeField, Min(0f)] private float _acceleration = 18f;

        [Tooltip("Maximum forward speed in m/s.")]
        [SerializeField, Min(0f)] private float _maxSpeed = 8f;

        [Tooltip("Yaw rate in degrees per second per unit of turn input.")]
        [SerializeField, Min(0f)] private float _turnRate = 120f;

        [Header("Tuning — Damping (only on the ground)")]
        [Tooltip("Linear damping applied to the rigidbody when grounded. Higher = stops faster.")]
        [SerializeField, Min(0f)] private float _groundedLinearDamping = 4f;

        [Tooltip("Angular damping applied to the rigidbody when grounded.")]
        [SerializeField, Min(0f)] private float _groundedAngularDamping = 8f;

        [Header("Tuning — Jump")]
        [Tooltip("Vertical impulse on jump.")]
        [SerializeField, Min(0f)] private float _jumpImpulse = 6f;

        [Tooltip("Cooldown between jumps in seconds.")]
        [SerializeField, Min(0f)] private float _jumpCooldown = 0.4f;

        private Rigidbody _rb;
        private float _nextJumpAllowedTime;

        public bool IsOperational => isActiveAndEnabled;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // Stable handling defaults — no auto-rolling, brisk damping.
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.linearDamping = _groundedLinearDamping;
            _rb.angularDamping = _groundedAngularDamping;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void ApplyMovement(Vector2 moveInput, float verticalInput, float deltaTime)
        {
            if (!IsOperational) return;

            // --- Steering: rotate the body directly. Cleaner than torque for arcade feel.
            if (!Mathf.Approximately(moveInput.x, 0f))
            {
                float yawDelta = moveInput.x * _turnRate * deltaTime;
                Quaternion yaw = Quaternion.Euler(0f, yawDelta, 0f);
                _rb.MoveRotation(_rb.rotation * yaw);
            }

            // --- Drive: accelerate along local forward, capped to max speed.
            if (!Mathf.Approximately(moveInput.y, 0f))
            {
                Vector3 desiredAccel = transform.forward * (moveInput.y * _acceleration);
                _rb.AddForce(desiredAccel, ForceMode.Acceleration);

                // Clamp planar speed.
                Vector3 v = _rb.linearVelocity;
                Vector3 horizontal = new Vector3(v.x, 0f, v.z);
                if (horizontal.sqrMagnitude > _maxSpeed * _maxSpeed)
                {
                    horizontal = horizontal.normalized * _maxSpeed;
                    _rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
                }
            }

            // --- Jump.
            if (verticalInput > 0.5f && Time.time >= _nextJumpAllowedTime)
            {
                _rb.AddForce(Vector3.up * _jumpImpulse, ForceMode.Impulse);
                _nextJumpAllowedTime = Time.time + _jumpCooldown;
            }
        }
    }
}
