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
    /// Pure visuals + steering for now — actual physics still come from
    /// <see cref="GroundDrive"/> on the robot root. Per-wheel torque /
    /// <see cref="WheelCollider"/> is a later milestone.
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
        [Tooltip("Distance from block centre to wheel centre at full extension (no contact).")]
        [SerializeField, Min(0.1f)] private float _restLength = 1.15f;

        [Tooltip("Spring stiffness (N/m). Higher = stiffer ride.")]
        [SerializeField, Min(0f)] private float _springStrength = 3500f;

        [Tooltip("Velocity damping along the suspension axis.")]
        [SerializeField, Min(0f)] private float _damper = 350f;

        [Tooltip("Layers the wheel can rest on.")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Grip")]
        [Tooltip("Tyre static / dynamic friction. Real rubber on dry tarmac is ~1.0; arcade-grippy is 1.5–2.")]
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
        /// Block prefabs default to a 1×1 cube primitive (in <see cref="BlockBehaviour"/>'s
        /// host GameObject). For wheels we want to see only the rig, so hide
        /// the host renderer + collider.
        /// </summary>
        private void HideHostCubeVisual()
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = null;
            // Keep a collider so damage raycasts still register; primitive
            // BoxCollider on a 1m cube is fine for a wheel hit volume.
        }

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
                return;
            }

            // wheel centre rests one radius above the contact point
            float wheelCenterY = hit.point.y + _radius;
            float extension = Mathf.Clamp(origin.y - wheelCenterY, 0f, _restLength);
            float compression = _restLength - extension;

            // spring + damper acting along world-up
            float velUp = _rb.GetPointVelocity(origin).y;
            float force = compression * _springStrength - velUp * _damper;
            if (force > 0f)
            {
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

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        {
            // RaycastAll so we can skip hits on our own chassis (the block
            // host cube collider sits exactly at the ray origin).
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, maxDist, _groundMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
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
            if (_hub == null)
            {
                Transform existing = transform.Find("Hub");
                if (existing != null) _hub = existing;
                else
                {
                    GameObject go = new GameObject("Hub");
                    go.transform.SetParent(transform, worldPositionStays: false);
                    _hub = go.transform;
                }
            }

            if (_spin == null)
            {
                Transform existing = _hub.Find("Spin");
                if (existing != null) _spin = existing;
                else
                {
                    GameObject go = new GameObject("Spin");
                    go.transform.SetParent(_hub, worldPositionStays: false);
                    _spin = go.transform;
                }
            }

            if (_tyre == null)
            {
                Transform existing = _spin.Find("Tyre");
                if (existing != null) _tyre = existing;
                else
                {
                    // Cylinder default points +Y; rotate 90° on Z so its long
                    // axis lies along world X (wheel-axle direction).
                    GameObject tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    tyre.name = "Tyre";
                    Collider col = tyre.GetComponent<Collider>();
                    if (col != null) Destroy(col);

                    tyre.transform.SetParent(_spin, worldPositionStays: false);
                    tyre.transform.localPosition = Vector3.zero;
                    tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    // Diameter ~2*_radius, thickness ~0.3.
                    float d = _radius * 2f;
                    tyre.transform.localScale = new Vector3(d, 0.3f, d);
                    _tyre = tyre.transform;
                }
            }
        }
    }
}
