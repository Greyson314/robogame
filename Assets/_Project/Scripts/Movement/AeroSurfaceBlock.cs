using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// A passive aerodynamic surface (wing / stabiliser). Each FixedUpdate
    /// it samples the local airspeed at its position and applies lift along
    /// its <see cref="Transform.up"/>, drag along the chassis velocity, and
    /// a sideslip-damping force along its <see cref="Transform.right"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Arcade flight model: lift is proportional to <c>forwardSpeed^2</c>
    /// rather than to angle of attack, so wings always produce upward force
    /// when the plane has forward airspeed. Stalls and inverted flight come
    /// "for free" from the chassis orientation: when the plane rolls, lift
    /// rotates with the wings, which is exactly what you want for banked
    /// turns.
    /// </para>
    /// <para>
    /// Sideslip damping is what makes a plane "track" through turns rather
    /// than slide sideways through the air. Tune via <see cref="_sideDamping"/>.
    /// </para>
    /// <para>
    /// Place several wing blocks in a row to build a wing of any span; the
    /// summed forces give correct pitch / roll torque around the COM.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class AeroSurfaceBlock : MonoBehaviour
    {
        [Header("Aero")]
        [Tooltip("Lift per (m/s)^2 of forward airspeed (N·s²/m²). Sum across all wings should ~= chassis weight at cruise.")]
        [SerializeField, Min(0f)] private float _liftCoef = 0.18f;

        [Tooltip("Drag per (m/s)^2 of forward airspeed (N·s²/m²). Acts opposite to chassis velocity.")]
        [SerializeField, Min(0f)] private float _dragCoef = 0.012f;

        [Tooltip("Sideslip damping per (m/s) of lateral airspeed (N·s/m). Resists sideways sliding.")]
        [SerializeField, Min(0f)] private float _sideDamping = 4f;

        [Tooltip("Optional cap on lift force per surface (N). 0 = uncapped. Prevents Vne explosions.")]
        [SerializeField, Min(0f)] private float _maxLift = 0f;

        [Header("Visual rig (auto-built if blank)")]
        [SerializeField] private Transform _wingMesh;
        [Tooltip("Wing visual size in metres (X = span, Y = thickness, Z = chord).")]
        [SerializeField] private Vector3 _wingSize = new Vector3(1f, 0.08f, 0.9f);

        private Rigidbody _rb;

        private void Awake()
        {
            EnsureRig();
        }

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            Vector3 worldPos = transform.position;
            Vector3 worldVel = _rb.GetPointVelocity(worldPos);
            Vector3 localVel = transform.InverseTransformDirection(worldVel);

            float forward = localVel.z;
            float side = localVel.x;
            float speedSqr = forward * forward;

            // Lift acts along the wing's local up. Sign(forward) so flying
            // backwards produces downforce (wings can't push you up if
            // they're going the wrong way).
            float liftMag = speedSqr * _liftCoef * Mathf.Sign(forward);
            if (_maxLift > 0f) liftMag = Mathf.Clamp(liftMag, -_maxLift, _maxLift);
            _rb.AddForceAtPosition(transform.up * liftMag, worldPos);

            // Drag along the chassis velocity (not local-Z), so going
            // sideways still costs energy.
            if (worldVel.sqrMagnitude > 0.001f)
            {
                float dragMag = worldVel.sqrMagnitude * _dragCoef;
                _rb.AddForceAtPosition(-worldVel.normalized * dragMag, worldPos);
            }

            // Sideslip damping: linear in lateral velocity.
            float sideForce = -side * _sideDamping;
            _rb.AddForceAtPosition(transform.right * sideForce, worldPos);
        }

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            BlockVisuals.HideHostMesh(gameObject);
            if (_wingMesh != null) return;

            _wingMesh = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Wing", PrimitiveType.Cube);
            _wingMesh.localScale = _wingSize;
        }
    }
}
