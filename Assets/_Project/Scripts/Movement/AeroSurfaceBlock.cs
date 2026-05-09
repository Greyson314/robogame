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
        // Wing dimensions (span / thickness / chord) are per-block — they
        // come from the BlockBehaviour's Dims field (populated from the
        // ChassisBlueprint.Entry that placed this foil). Vector3.zero in
        // Dims means "use the block defaults below" so legacy blueprints
        // without per-block sizing render correctly.

        // Foil shape constants are aliases of the authoritative values
        // in Block.FoilDefaults so the build-mode swept bounds, the
        // ghost factory, and the placed-mesh geometry can't drift.
        // Keep these aliases for source-compat with shipped consumers
        // (tests, VariantConfigPanel) until they migrate directly.
        /// <summary>Block-default span (m) when the entry's Dims is Vector3.zero.</summary>
        public const float DefaultSpan      = FoilDefaults.DefaultSpan;
        /// <summary>Block-default thickness (m) when the entry's Dims is Vector3.zero.</summary>
        public const float DefaultThickness = FoilDefaults.DefaultThickness;
        /// <summary>Block-default chord (m) when the entry's Dims is Vector3.zero.</summary>
        public const float DefaultChord     = FoilDefaults.DefaultChord;
        /// <summary>Min/max for the build-mode variant config sliders.</summary>
        public const float MinSpan = FoilDefaults.MinSpan, MaxSpan = FoilDefaults.MaxSpan;
        public const float MinThickness = FoilDefaults.MinThickness, MaxThickness = FoilDefaults.MaxThickness;
        public const float MinChord = FoilDefaults.MinChord, MaxChord = FoilDefaults.MaxChord;

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
        // Rotor reference + spin axis (in rotor-block local space). When
        // set, the lift force in rotor mode is applied along the rotor's
        // spin axis rather than the foil's tilted transform.up. The
        // collective-pitch tilt still drives AoA via crossVel, but
        // purifying the force direction prevents the lift's tangential
        // component from yawing the chassis. See ConfigureRotorMode +
        // FixedUpdate for the why.
        private Transform _rotorTransform;
        private Vector3 _rotorSpinAxisLocal = Vector3.up;
        // Cached planform-area scale for the lift formula:
        //   (span * chord) / (DefaultSpan * DefaultChord)
        // Equals 1 at default dims so existing chassis numbers are
        // preserved; equals N for an N× larger wing. Recomputed on
        // OnEnable + DimsChanged, never per-frame.
        private float _liftAreaScale = 1f;
        // Cached pitch in radians (per-instance AoA offset). Refreshed on
        // OnEnable + PitchChanged. Added to airflow-derived AoA in
        // FixedUpdate, no per-frame conversion.
        private float _pitchRad;

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

        private BlockBehaviour _block;

        private void OnEnable()
        {
            // Defensive: re-run rig setup so a foil placed at runtime
            // (e.g. RepairPad regen) is visually correct even if Awake's
            // pass somehow didn't take. EnsureRig is idempotent — early-
            // returns when _wingMesh is already set, so this is a free
            // safety net.
            EnsureRig();

            // Subscribe to BlockBehaviour.DimsChanged + PitchChanged so a
            // runtime SetDims / SetPitch call (build-mode editing, rotor
            // adopt-pass, tests) re-applies the visual + cached state
            // immediately.
            _block = GetComponent<BlockBehaviour>();
            if (_block != null)
            {
                _block.DimsChanged += OnDimsChanged;
                _block.PitchChanged += OnPitchChanged;
            }
            RecomputePitch();
            ApplyOrientationToVisual();
            RecomputeAreaScale();

            // If a rotor builder already injected an explicit force
            // target via ConfigureRotorMode, don't clobber it here.
            if (_rotorMode && _forceTargetRb != null) return;

            _velocityRb = GetComponentInParent<Rigidbody>();
            _forceTargetRb = ResolveForceTarget(_velocityRb);
            _rotorMode = _velocityRb != null && _forceTargetRb != null && _velocityRb != _forceTargetRb;
        }

        private void OnDisable()
        {
            if (_block != null)
            {
                _block.DimsChanged -= OnDimsChanged;
                _block.PitchChanged -= OnPitchChanged;
            }
        }

        private void OnDimsChanged(BlockBehaviour _)
        {
            ApplyOrientationToVisual();
            RecomputeAreaScale();
        }

        private void OnPitchChanged(BlockBehaviour _)
        {
            RecomputePitch();
            ApplyOrientationToVisual();
        }

        // Lift now scales with planform area (span × chord) so a 2× wing
        // produces 2× lift. Default dims (span=1, chord=DefaultChord)
        // give scale=1, preserving every shipped chassis's behaviour.
        // Per-block driven, not Tweakable-driven — see PHYSICS_PLAN §5.
        private void RecomputeAreaScale()
        {
            Vector3 dims = _block != null ? _block.Dims : Vector3.zero;
            ResolveDims(dims, out float span, out _, out float chord);
            _liftAreaScale = (span * chord) / (DefaultSpan * DefaultChord);
        }

        // Cache pitch in radians so FixedUpdate's lift hot path avoids the
        // Mathf.Deg2Rad multiply every step. Refreshed only when pitch
        // actually changes (PitchChanged event).
        private void RecomputePitch()
        {
            float pitchDeg = _block != null ? _block.PitchDeg : 0f;
            _pitchRad = pitchDeg * Mathf.Deg2Rad;
        }

        /// <summary>
        /// Wire this surface up as a rotor blade. <paramref name="hub"/>
        /// is the kinematic spinning Rigidbody this blade is parented to
        /// (used for velocity sampling so blade tangential ω×r feeds the
        /// AoA/lift math); <paramref name="chassis"/> is the dynamic
        /// Rigidbody that should receive the lift force.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <paramref name="rotorTransform"/> + <paramref name="spinAxisLocal"/>
        /// are optional but strongly recommended for rotor builds. When
        /// provided, the lift force in rotor mode is applied along the
        /// rotor's spin axis (in world space) rather than along the
        /// foil's tilted <c>transform.up</c>. This is what keeps the
        /// chassis from yawing under load: with collective pitch the
        /// foil's <c>transform.up</c> has a tangential component, and
        /// the lift force projected onto the spin tangent is "induced
        /// drag" — which dumps a counter-torque into the chassis at the
        /// rotor's full power. Real rotors handle this via anti-torque
        /// (tail rotor, NOTAR); we explicitly don't model that, so the
        /// blades' lift must be coplanar with the spin axis to stay
        /// torque-neutral.
        /// </para>
        /// <para>
        /// The pitch tilt still drives AoA via the foil's tilted local
        /// frame and the velocity-vs-chord computation. Only the
        /// FORCE DIRECTION is purified — magnitude is unchanged.
        /// </para>
        /// </remarks>
        public void ConfigureRotorMode(
            Rigidbody hub, Rigidbody chassis,
            Transform rotorTransform = null, Vector3 spinAxisLocal = default)
        {
            _velocityRb       = hub;
            _forceTargetRb    = chassis;
            _rotorMode        = true;
            _rotorTransform   = rotorTransform;
            _rotorSpinAxisLocal = spinAxisLocal == default ? Vector3.up : spinAxisLocal.normalized;
            // OnEnable already called ApplyOrientationToVisual with
            // _rotorMode = false (the foil was a free wing then). Re-run
            // now that we know it's a rotor blade so the wing snaps back
            // to centered (a shifted blade unbalances the rotor disc).
            ApplyOrientationToVisual();
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
            using var _scope = Robogame.Core.PerfMarkers.AeroSurfaceFixedUpdate.Auto();
            if (_velocityRb == null || _forceTargetRb == null) return;

            Vector3 worldPos = transform.position;
            // Velocity sampling differs between plane wings and rotor blades:
            //
            // Plane wing: full point velocity (chassis bulk + chassis angular
            // tangent). The wing needs to "feel" the chassis flying through
            // air for lift to scale with airspeed.
            //
            // Rotor blade: ONLY the rotor's tangential ω×r, with chassis bulk
            // motion stripped. Including chassis bulk creates dissymmetry of
            // lift — the advancing blade sees airspeed (ω + V_chassis) and
            // the retreating blade sees (ω − V_chassis), so lift on the
            // advancing side is ((ω+V)/(ω−V))² × the retreating side, which
            // sums to a roll torque toward the retreating blade. Real rotors
            // compensate via cyclic pitch; we don't model cyclic, so the
            // disc would never balance. Same arcade reasoning as the drag /
            // reaction-torque suppression below: kinematic-hub design,
            // rotor disc kept symmetric on purpose.
            Vector3 worldVel;
            if (_rotorMode)
            {
                Vector3 rFromCom = worldPos - _velocityRb.worldCenterOfMass;
                worldVel = Vector3.Cross(_velocityRb.angularVelocity, rFromCom);
            }
            else
            {
                worldVel = _velocityRb.GetPointVelocity(worldPos);
            }
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
            // Rotor mode: replace the foil-tilted lift axis with the
            // rotor's spin axis so a symmetric blade ring's lift is
            // purely axial and produces zero net yaw torque on the
            // chassis. AoA still comes from the tilted foil's crossVel
            // — only the force *direction* is purified. See
            // ConfigureRotorMode docstring for the reasoning.
            if (_rotorMode && _rotorTransform != null)
            {
                liftAxis = _rotorTransform.TransformDirection(_rotorSpinAxisLocal).normalized;
            }
            float speedSqr = forward * forward;

            // Angle of attack: positive when the airflow strikes the
            // lift-producing side of the surface. Threshold lowered
            // from 0.5 to 0.05 m/s so a low-RPM rotor blade still
            // produces collective-pitch lift on spin-up. For plane
            // wings this changes nothing — they sit way above 0.5
            // even on the runway, so the gate was always vestigial.
            //
            // _pitchRad adds the per-instance pitch / incidence (foil
            // role-default + player tuning, or rotor collective written
            // by the adopt-pass). At zero airspeed it produces zero lift
            // anyway via speedSqr; once the wing starts moving it gives
            // the foil a built-in AoA without the player having to fake
            // it via mounting tilt.
            float airflowAoa = forward > 0.05f ? Mathf.Atan2(-crossVel, forward) : 0f;
            float aoa = airflowAoa + _pitchRad;
            float aoaClamped = Mathf.Clamp(aoa, -_stallAoA, _stallAoA);
            // Soft stall: past the stall angle, retain only postStallLift × cap.
            float stallFalloff = Mathf.Abs(aoa) > _stallAoA
                ? Mathf.Lerp(1f, _postStallLift, Mathf.Clamp01((Mathf.Abs(aoa) - _stallAoA) / _stallAoA))
                : 1f;
            // Vertical fins are symmetric airfoils: no camber-equivalent
            // lateral lift at zero sideslip. Applying _zeroLiftBias to a
            // vertical fin pushes the chassis sideways under any forward
            // airspeed, which yaws + rolls the chassis (the fin sits
            // behind and above COM, so a +X push induces both -Y yaw and
            // -Z roll torques). _zeroLiftBias models cambered horizontal
            // wings only.
            float biasTerm = _vertical ? 0f : _zeroLiftBias * Mathf.Sign(forward);
            float liftFactor = (aoaClamped + biasTerm) * stallFalloff;

            float liftMag = speedSqr * _liftCoef * _liftAreaScale * liftFactor * Mathf.Sign(forward);
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
        /// Resolve a foil's effective dims (zero fallback → block defaults)
        /// into the (span, thickness, chord) triplet used by the visual rig.
        /// </summary>
        public static void ResolveDims(Vector3 raw, out float span, out float thickness, out float chord)
        {
            span      = raw.x > 0f ? raw.x : DefaultSpan;
            thickness = raw.y > 0f ? raw.y : DefaultThickness;
            chord     = raw.z > 0f ? raw.z : DefaultChord;
        }

        /// <summary>
        /// Foil mesh's local-scale (the wing primitive cube's
        /// <c>localScale</c>) for a foil with the given dimensions and
        /// rotor-or-not status. Single source of truth used by both
        /// <see cref="ApplyOrientationToVisual"/> and the build-mode
        /// ghost factory so the preview can never disagree with the
        /// placed block's geometry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Regular foil</b> — <c>span</c> goes along foil-local +Y
        /// (the mount-up direction). After
        /// <see cref="Robogame.Block.BlockGrid.OrientationFromUp"/> rotates
        /// the foil into chassis frame, foil-local +Y points outward from
        /// the host face. So a top-mounted foil extends straight up
        /// (vertical stabiliser), a side-mounted foil extends sideways
        /// (wing), a bottom-mounted foil extends down (rudder), etc.
        /// One rule for every face.
        /// </para>
        /// <para>
        /// <b>Rotor blade</b> — <c>span</c> goes along foil-local +X
        /// (tangent to the rotor disc) so blades extend perpendicular
        /// to the spin axis, the way real rotor blades do. Otherwise
        /// blades would extend along the spin axis and look like
        /// vertical knives.
        /// </para>
        /// </remarks>
        public static Vector3 ComputeFoilMeshScale(float span, float thickness, float chord, bool rotorMode)
        {
            return rotorMode
                ? new Vector3(span, thickness, chord)
                : new Vector3(thickness, span, chord);
        }

        /// <summary>
        /// Outward offset for the wing mesh inside its cell, so a
        /// <c>span > 1</c> foil extends past its host's adjacent cells
        /// instead of straddling its own cell symmetrically.
        /// </summary>
        /// <remarks>
        /// Regular foils shift along foil-local +Y (the mount-up
        /// direction); rotor blades shift along foil-local +X (the
        /// blade tangent direction). Rotor blade direction is derived
        /// from <paramref name="cellPos"/>.x sign so the blade extends
        /// outward from the rotor hub.
        /// </remarks>
        public static Vector3 ComputeWingShift(Vector3Int cellPos, float span, bool rotorMode)
        {
            // span-1 foil = unit cube, no shift; span > 1 shifts by
            // (span-1)/2 so the inner edge stays at the cell's near face.
            float magnitude = Mathf.Max(0f, span * 0.5f - 0.5f);
            if (magnitude <= 0f) return Vector3.zero;
            if (rotorMode)
            {
                int signX = cellPos.x > 0 ? 1 : (cellPos.x < 0 ? -1 : 0);
                return new Vector3(signX * magnitude, 0f, 0f);
            }
            // Non-rotor foil: foil-local +Y always points away from the
            // host (OrientationFromUp guarantees this). Shift positive Y
            // = shift "outward" regardless of mount face.
            return new Vector3(0f, magnitude, 0f);
        }

        /// <summary>
        /// Build the wing mesh's transform from the foil's per-instance
        /// dims + pitch. Reads <c>bb.Dims</c> and <c>bb.PitchDeg</c> with
        /// default fallbacks; uses <see cref="ComputeFoilMeshScale"/> +
        /// <see cref="ComputeWingShift"/> so the placed-block geometry
        /// matches the build-mode ghost. Pitch is applied as a localRotation
        /// around foil-local +Z (the chord axis) so the wing's leading edge
        /// tilts up/down by <c>PitchDeg</c>; this is purely visual — the
        /// lift formula reads pitch directly from <see cref="_pitchRad"/>
        /// rather than transform.rotation.
        /// </summary>
        private void ApplyOrientationToVisual()
        {
            if (_wingMesh == null) return;
            BlockBehaviour bb = GetComponent<BlockBehaviour>();
            Vector3 dims = bb != null ? bb.Dims : Vector3.zero;
            ResolveDims(dims, out float span, out float thickness, out float chord);
            _wingMesh.localScale = ComputeFoilMeshScale(span, thickness, chord, _rotorMode);
            Vector3Int gridPos = bb != null ? bb.GridPosition : Vector3Int.zero;
            _wingMesh.localPosition = ComputeWingShift(gridPos, span, _rotorMode);
            float pitchDeg = bb != null ? bb.PitchDeg : 0f;
            _wingMesh.localRotation = Quaternion.AngleAxis(pitchDeg, Vector3.forward);
        }
    }
}
