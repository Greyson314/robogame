using Robogame.Block;
using Robogame.Input;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Whether this wheel drives, steers, or both.
    /// </summary>
    public enum WheelKind
    {
        Drive,
        Steer,
        DriveAndSteer
    }

    /// <summary>
    /// Visual + behavioural component for a movement-category block. Builds
    /// a simple wheel rig (steering hub → spin pivot → tyre cylinder) and
    /// drives them from the owning rigidbody's velocity and the
    /// <see cref="IInputSource"/> at or above this block.
    /// </summary>
    /// <remarks>
    /// Pure visuals + suspension for now — chassis-level forces come from
    /// <see cref="GroundDriveSubsystem"/> via <see cref="RobotDrive"/>.
    /// Per-wheel torque / <see cref="WheelCollider"/> is a later milestone.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class WheelBlock : MonoBehaviour
    {
        [Header("Behaviour")]
        [SerializeField] private WheelKind _kind = WheelKind.Drive;

        [Header("Steering")]
        [Tooltip("Maximum steer angle in degrees.")]
        [SerializeField, Range(0f, 60f)] private float _maxSteerAngle = 28f;

        [Tooltip("Smoothing on steer rotation. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _steerSpeed = 14f;

        [Header("Spin")]
        [Tooltip("Wheel radius in metres (for rolling visualisation).")]
        [SerializeField, Min(0.05f)] private float _radius = 0.35f;

        [Header("Suspension")]
        [Tooltip("Distance from block centre to wheel centre at full extension (no contact). " +
                 "Sized so the wheel can reach the ground for a chassis sitting at standard " +
                 "block height — too short and the chassis rides on its cube colliders, which " +
                 "produces the very bounce the suspension is supposed to absorb.")]
        [SerializeField, Min(0.1f)] private float _restLength = 1.15f;

        [Tooltip("Spring stiffness in NEWTONS per metre of compression. Mass-dependent on purpose " +
                 "(default ForceMode.Force) — heavier chassis sag more on the same springs, which " +
                 "matches real-world intuition. Rule of thumb: total k across grounded wheels × " +
                 "desired-sag = chassis weight. e.g. 6 wheels × 600 N/m × 0.08 m = 288 N ≈ 30 kg.")]
        [SerializeField, Min(0f)] private float _springStrength = 600f;

        [Tooltip("Velocity damping along the suspension axis (N per m/s). " +
                 "Tune for a slightly OVER-damped feel: 2·√(k·m_per_wheel) is critical, this " +
                 "default sits a bit above for the typical 4–6 wheel test chassis so there's no " +
                 "sustained bounce after a hit.")]
        [SerializeField, Min(0f)] private float _damper = 220f;

        [Tooltip("Hard cap on suspension force (N). Absorbs spike loads from hard landings so " +
                 "an impact can't catapult the chassis. Set high enough that normal driving " +
                 "never hits the cap.")]
        [SerializeField, Min(0f)] private float _maxForce = 6000f;

        [Tooltip("Layers the wheel can rest on.")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Grip")]
        [Tooltip("Tyre static / dynamic friction for the host-cube PhysicsMaterial. " +
                 "Note: chassis-level lateral grip lives on GroundDriveSubsystem (applied at COM " +
                 "to avoid roll moments). Per-wheel grip was removed because forces above the " +
                 "contact point produced spurious roll torque.")]
        [SerializeField, Min(0f)] private float _friction = 1.6f;

        [Header("Visual rig (auto-built if blank)")]
        [SerializeField] private Transform _hub;     // yaws for steering
        [SerializeField] private Transform _spin;    // rotates around X for rolling
        [SerializeField] private Transform _tyre;    // visible cylinder

        public WheelKind Kind
        {
            get => _kind;
            set => _kind = value;
        }

        /// <summary>True while this wheel's suspension raycast is touching ground.</summary>
        public bool IsGrounded { get; private set; }

        private Rigidbody _rb;
        private IInputSource _input;
        private float _spinAngle;
        private float _suspensionExtension;

        private void Awake()
        {
            HideHostCubeVisual();
            ApplyTyreFriction();
            EnsureRig();
            _rb = GetComponentInParent<Rigidbody>();
            _input = GetComponentInParent<IInputSource>();
            _suspensionExtension = _restLength;
        }

        private static PhysicsMaterial s_tyreMaterial;

        private void ApplyTyreFriction()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;
            if (s_tyreMaterial == null)
            {
                s_tyreMaterial = new PhysicsMaterial("Tyre")
                {
                    dynamicFriction = _friction,
                    staticFriction = _friction,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }
            col.material = s_tyreMaterial;
        }

        /// <summary>
        /// Hide host primitive renderer (keep its collider so damage rays
        /// still register on the wheel).
        /// </summary>
        private void HideHostCubeVisual() => BlockVisuals.HideHostMesh(gameObject);

        private void LateUpdate()
        {
            UpdateSteering();
            UpdateSpin();
            UpdateSuspensionVisual();
        }

        private void FixedUpdate()
        {
            UpdateSuspensionPhysics();
        }

        // -----------------------------------------------------------------
        // Suspension
        // -----------------------------------------------------------------

        private void UpdateSuspensionPhysics()
        {
            if (_rb == null) return;

            Vector3 origin = transform.position;
            float castLength = _restLength + _radius;

            if (!RaycastIgnoringSelf(origin, Vector3.down, castLength, out RaycastHit hit))
            {
                _suspensionExtension = _restLength;
                IsGrounded = false;
                return;
            }
            IsGrounded = true;

            // Wheel centre rests one radius above the contact point.
            float wheelCenterY = hit.point.y + _radius;
            float extension = Mathf.Clamp(origin.y - wheelCenterY, 0f, _restLength);
            float compression = _restLength - extension;

            // Compression rate: positive when the chassis is moving DOWN at
            // this point (suspension is being compressed). Using the full
            // point velocity (not just _rb.linearVelocity.y) means roll
            // and pitch correctly contribute to damping — without this, a
            // chassis pitching forward would flap because only the COM's
            // vertical velocity was being damped.
            Vector3 pointVel = _rb.GetPointVelocity(origin);
            float compressionRate = -pointVel.y;

            // Hooke + damper in NEWTONS (default ForceMode.Force is
            // mass-dependent on purpose, so heavier chassis sag more on
            // the same springs — matches real-world intuition). Clamped
            // to absorb spike loads on hard landings, and never pulls
            // down — suspension can only push up off the ground.
            float force = compression * _springStrength + compressionRate * _damper;
            if (force > 0f)
            {
                if (force > _maxForce) force = _maxForce;
                _rb.AddForceAtPosition(Vector3.up * force, origin);
            }

            _suspensionExtension = extension;
        }

        private void UpdateSuspensionVisual()
        {
            if (_hub == null) return;
            // Hub Y in block-local space drops by the current extension so
            // the tyre tracks the ground regardless of chassis bob.
            Vector3 lp = _hub.localPosition;
            lp.y = -_suspensionExtension;
            _hub.localPosition = lp;
        }

        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[8];

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        {
            // RaycastNonAlloc so we can skip hits on our own chassis (the block
            // host cube collider sits exactly at the ray origin).
            int count = Physics.RaycastNonAlloc(origin, dir, s_hitBuffer, maxDist, _groundMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = s_hitBuffer[i];
                if (h.collider.attachedRigidbody == _rb) continue; // self
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        private void UpdateSteering()
        {
            if (_hub == null) return;
            bool steers = _kind == WheelKind.Steer || _kind == WheelKind.DriveAndSteer;

            float targetYaw = 0f;
            if (steers && _input != null)
            {
                targetYaw = Mathf.Clamp(_input.Move.x, -1f, 1f) * _maxSteerAngle;
            }

            Quaternion target = Quaternion.Euler(0f, targetYaw, 0f);
            _hub.localRotation = _steerSpeed <= 0f
                ? target
                : Quaternion.Slerp(_hub.localRotation, target,
                    1f - Mathf.Exp(-_steerSpeed * Time.deltaTime));
        }

        private void UpdateSpin()
        {
            if (_spin == null) return;
            bool drives = _kind == WheelKind.Drive || _kind == WheelKind.DriveAndSteer;
            if (!drives || _rb == null) return;

            // Roll speed = forward component of robot velocity, in the wheel's
            // hub-rotated frame so steering wheels visually roll along their
            // current heading.
            Vector3 forward = _hub != null ? _hub.forward : transform.forward;
            float linearSpeed = Vector3.Dot(_rb.linearVelocity, forward);
            float angularDeg = (linearSpeed / Mathf.Max(_radius, 0.01f)) * Mathf.Rad2Deg;
            _spinAngle += angularDeg * Time.deltaTime;
            _spin.localRotation = Quaternion.Euler(_spinAngle, 0f, 0f);
        }

        // -----------------------------------------------------------------
        // Rig construction
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            if (_hub == null) _hub = BlockVisuals.GetOrCreateChild(transform, "Hub");
            if (_spin == null) _spin = BlockVisuals.GetOrCreateChild(_hub, "Spin");

            if (_tyre == null)
            {
                _tyre = BlockVisuals.GetOrCreatePrimitiveChild(_spin, "Tyre", PrimitiveType.Cylinder);
                // Cylinder default points +Y; rotate 90° on Z so its long
                // axis lies along world X (wheel-axle direction).
                _tyre.localRotation = Quaternion.Euler(0f, 0f, 90f);
                float d = _radius * 2f;
                _tyre.localScale = new Vector3(d, 0.3f, d);
            }
        }
    }
}
