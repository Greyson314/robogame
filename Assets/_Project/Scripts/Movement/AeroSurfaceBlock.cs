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
        [Tooltip("Lift slope per radian of AoA × speed² (N·s²/m²/rad). Tune so a level cruise produces ~chassis weight from main wing summed.")]
        [SerializeField, Min(0f)] private float _liftCoef = 0.95f;

        [Tooltip("Lift produced at zero angle of attack as a fraction of the AoA term at 1 rad. Small (~0.05–0.15) keeps level cruise from drifting up; 0 = symmetric wing.")]
        [SerializeField, Range(0f, 0.5f)] private float _zeroLiftBias = 0.12f;

        [Tooltip("Hard cap on AoA-based lift factor (radians-equivalent). Past this the wing 'stalls' — lift falls off.")]
        [SerializeField, Min(0.05f)] private float _stallAoA = 0.35f; // ~20°

        [Tooltip("Lift retained past the stall AoA (multiplied into the cap). 1 = no stall.")]
        [SerializeField, Range(0f, 1f)] private float _postStallLift = 0.55f;

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

            // Angle of attack: positive when the airflow strikes the wing's
            // underside (i.e. local -Y velocity at the wing position with
            // positive forward speed). Real symmetric airfoils produce zero
            // lift at zero AoA — modelling that here is what fixes the
            // "constant buoyancy" feel: at level flight every wing produces
            // only enough lift to balance gravity given the small AoA from
            // sinking, so a wing far behind the COM no longer pushes the
            // tail up unconditionally.
            float aoa = forward > 0.5f ? Mathf.Atan2(-localVel.y, forward) : 0f;
            float aoaClamped = Mathf.Clamp(aoa, -_stallAoA, _stallAoA);
            // Soft stall: past the stall angle, retain only postStallLift × cap.
            float stallFalloff = Mathf.Abs(aoa) > _stallAoA
                ? Mathf.Lerp(1f, _postStallLift, Mathf.Clamp01((Mathf.Abs(aoa) - _stallAoA) / _stallAoA))
                : 1f;
            float liftFactor = (aoaClamped + _zeroLiftBias * Mathf.Sign(forward)) * stallFalloff;

            float liftMag = speedSqr * _liftCoef * liftFactor * Mathf.Sign(forward);
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
