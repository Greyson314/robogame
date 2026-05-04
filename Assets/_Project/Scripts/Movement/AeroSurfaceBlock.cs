using Robogame.Block;
using Robogame.Core;
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
    /// <para>
    /// <b>Rotor mode.</b> When this surface is parented to a kinematic
    /// <see cref="Rigidbody"/> (the rotor hub), velocity sampling uses
    /// the hub (so <see cref="Rigidbody.GetPointVelocity"/> picks up
    /// the rotor's tangential ω×r at the blade position) but the lift
    /// force is applied to the first non-kinematic Rigidbody up the
    /// chain — i.e. the chassis. Drag and sideslip are skipped in
    /// rotor mode: a symmetric ring of blades would produce a pure
    /// counter-torque on the chassis from drag, which we deliberately
    /// don't model (Robocraft did the same; arcade kinematic rotors
    /// don't kick reaction torque into the airframe). See
    /// <c>docs/PHYSICS_PLAN.md</c> §2.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AeroSurfaceBlock : MonoBehaviour
    {
        [Header("Orientation")]
        [Tooltip("If true, this surface acts as a vertical fin (rudder/stabiliser): lift axis is the chassis-right vector instead of chassis-up, and the sideslip damping axis is vertical. Set by RobotAeroBinder for fin block ids.")]
        [SerializeField] private bool _vertical = false;

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
        // Wing dimensions (span / thickness / chord) come from the
        // Aero.* tweakables — see ApplyOrientationToVisual.

        // Velocity reference: the IMMEDIATE parent Rigidbody. On a
        // plane wing this is the chassis. On a rotor blade this is the
        // kinematic hub spinning at ω rad/s, so GetPointVelocity at the
        // blade picks up the tangential blade speed for free.
        private Rigidbody _velocityRb;
        // Force target: the first NON-KINEMATIC Rigidbody up the chain.
        // Plane wings: same as _velocityRb (chassis). Rotor blades:
        // chassis (skips past the kinematic hub). Force on a kinematic
        // body is silently dropped by PhysX, so without this split the
        // lift on rotor blades would be eaten by the hub.
        private Rigidbody _forceTargetRb;
        // True when we're a rotor blade: velocity-source != force-target.
        // Suppresses drag and sideslip so a symmetric blade ring doesn't
        // dump reaction torque into the chassis.
        private bool _rotorMode;

        /// <summary>True for tail fins / rudders. Set this BEFORE the first FixedUpdate (e.g. from a binder right after AddComponent).</summary>
        public bool Vertical
        {
            get => _vertical;
            set
            {
                if (_vertical == value) return;
                _vertical = value;
                if (_wingMesh != null) ApplyOrientationToVisual();
            }
        }

        private void Awake()
        {
            EnsureRig();
        }

        private void OnEnable()
        {
            // Subscribe unconditionally so the rotor-mode early-return
            // below doesn't strand foils without live resize.
            Tweakables.Changed += OnTweakablesChanged;
            ApplyOrientationToVisual();

            // If a rotor builder already injected an explicit force
            // target via ConfigureRotorMode, don't clobber it here.
            if (_rotorMode && _forceTargetRb != null) return;

            _velocityRb = GetComponentInParent<Rigidbody>();
            _forceTargetRb = ResolveForceTarget(_velocityRb);
            _rotorMode = _velocityRb != null && _forceTargetRb != null && _velocityRb != _forceTargetRb;
        }

        private void OnDisable()
        {
            Tweakables.Changed -= OnTweakablesChanged;
        }

        private void OnTweakablesChanged() => ApplyOrientationToVisual();

        /// <summary>
        /// Wire this surface up as a rotor blade. <paramref name="hub"/>
        /// is the kinematic spinning Rigidbody this blade is parented to
        /// (used for velocity sampling so blade tangential ω×r feeds the
        /// AoA/lift math); <paramref name="chassis"/> is the dynamic
        /// Rigidbody that should receive the lift force. Call this right
        /// after <see cref="GameObject.AddComponent{T}()"/> in the rotor
        /// builder — it overrides what <see cref="OnEnable"/> would have
        /// resolved (the hub is at scene root, so the auto-walk can't
        /// find the chassis).
        /// </summary>
        public void ConfigureRotorMode(Rigidbody hub, Rigidbody chassis)
        {
            _velocityRb    = hub;
            _forceTargetRb = chassis;
            _rotorMode     = true;
        }

        // Walk up parents from the velocity rb until we find a non-kinematic
        // Rigidbody. On a plane wing this is the chassis (one rb up the
        // chain, already non-kinematic, returns it directly). On a rotor
        // blade this skips past the kinematic hub to find the chassis.
        private static Rigidbody ResolveForceTarget(Rigidbody start)
        {
            if (start == null) return null;
            if (!start.isKinematic) return start;
            Transform t = start.transform.parent;
            while (t != null)
            {
                Rigidbody rb = t.GetComponentInParent<Rigidbody>();
                if (rb == null) return null;
                if (!rb.isKinematic) return rb;
                t = rb.transform.parent;
            }
            return null;
        }

        private void FixedUpdate()
        {
            if (_velocityRb == null || _forceTargetRb == null) return;

            Vector3 worldPos = transform.position;
            // Velocity at the blade includes (chassis bulk motion) +
            // (chassis angular vel × r) + (rotor spin × r when on a
            // rotor hub) — PhysX synthesises the rotor contribution from
            // the kinematic MoveRotation deltas the hub does each step.
            Vector3 worldVel = _velocityRb.GetPointVelocity(worldPos);
            Vector3 localVel = transform.InverseTransformDirection(worldVel);

            float forward = localVel.z;
            // "Cross" airspeed component drives AoA; "side" drives the
            // damping that keeps the surface tracking through turns.
            // Horizontal wing: cross = local Y (vertical airflow), side = local X.
            // Vertical fin:    cross = local X (lateral airflow),  side = local Y.
            float crossVel = _vertical ? localVel.x : localVel.y;
            float sideVel  = _vertical ? localVel.y : localVel.x;
            Vector3 liftAxis = _vertical ? transform.right : transform.up;
            Vector3 sideAxis = _vertical ? transform.up    : transform.right;
            float speedSqr = forward * forward;

            // Angle of attack: positive when the airflow strikes the
            // lift-producing side of the surface. Threshold lowered
            // from 0.5 to 0.05 m/s so a low-RPM rotor blade still
            // produces collective-pitch lift on spin-up. For plane
            // wings this changes nothing — they sit way above 0.5
            // even on the runway, so the gate was always vestigial.
            float aoa = forward > 0.05f ? Mathf.Atan2(-crossVel, forward) : 0f;
            float aoaClamped = Mathf.Clamp(aoa, -_stallAoA, _stallAoA);
            // Soft stall: past the stall angle, retain only postStallLift × cap.
            float stallFalloff = Mathf.Abs(aoa) > _stallAoA
                ? Mathf.Lerp(1f, _postStallLift, Mathf.Clamp01((Mathf.Abs(aoa) - _stallAoA) / _stallAoA))
                : 1f;
            float liftFactor = (aoaClamped + _zeroLiftBias * Mathf.Sign(forward)) * stallFalloff;

            float liftMag = speedSqr * _liftCoef * liftFactor * Mathf.Sign(forward);
            if (_maxLift > 0f) liftMag = Mathf.Clamp(liftMag, -_maxLift, _maxLift);
            _forceTargetRb.AddForceAtPosition(liftAxis * liftMag, worldPos);

            // Drag + sideslip are suppressed in rotor mode. A symmetric
            // blade ring would otherwise apply equal-and-opposite
            // tangential drags whose net torque on the chassis is the
            // anti-torque of the rotor — realistic, but we explicitly
            // don't model it (kinematic-hub design choice). Plane wings
            // (rotorMode == false) keep both terms.
            if (_rotorMode) return;

            // Drag along the chassis velocity (not local-Z), so going
            // sideways still costs energy.
            if (worldVel.sqrMagnitude > 0.001f)
            {
                float dragMag = worldVel.sqrMagnitude * _dragCoef;
                _forceTargetRb.AddForceAtPosition(-worldVel.normalized * dragMag, worldPos);
            }

            // Sideslip / yaw-slip damping: linear in cross-axis velocity.
            float sideForce = -sideVel * _sideDamping;
            _forceTargetRb.AddForceAtPosition(sideAxis * sideForce, worldPos);
        }

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            BlockVisuals.HideHostMesh(gameObject);
            if (_wingMesh != null) return;

            _wingMesh = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Wing", PrimitiveType.Cube);
            ApplyOrientationToVisual();
        }

        /// <summary>
        /// Apply the orientation flag to the visual mesh: horizontal uses
        /// the configured wing size, vertical rotates 90° around forward
        /// (so the cube becomes a tall fin) and swaps span/thickness.
        /// </summary>
        /// <remarks>
        /// Wing dimensions come from the Aero.* tweakables (cosmetic-only
        /// per docs/PHYSICS_PLAN.md §1.5). Live slider drags fire
        /// <see cref="Tweakables.Changed"/>, which re-runs this method on
        /// every active foil for instant resize.
        /// </remarks>
        private void ApplyOrientationToVisual()
        {
            if (_wingMesh == null) return;
            float span      = Tweakables.Get(Tweakables.AeroWingSpan);
            float thickness = Tweakables.Get(Tweakables.AeroWingThickness);
            float chord     = Tweakables.Get(Tweakables.AeroWingChord);
            if (_vertical)
            {
                // Tall thin fin: swap X and Y so the long axis points up.
                _wingMesh.localScale = new Vector3(thickness, span, chord);
            }
            else
            {
                _wingMesh.localScale = new Vector3(span, thickness, chord);
            }
        }
    }
}
