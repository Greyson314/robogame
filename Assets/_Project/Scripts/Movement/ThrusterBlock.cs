using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    // Note: Robogame.Block.BlockVisuals is used for rig construction.
    /// <summary>
    /// A jet / rocket block. Pushes the parent rigidbody along its own
    /// <see cref="Transform.forward"/>, scaled by a throttle derived from
    /// <see cref="DriveControl.Vertical"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Self-registers with the ancestor <see cref="RobotDrive"/> as an
    /// <see cref="IDriveSubsystem"/>. Each thruster is independent, so a
    /// chassis with multiple thrusters off-axis from the COM will produce
    /// torque automatically (welcome, VTOL).
    /// </para>
    /// <para>
    /// Throttle mapping: <c>0.5 + 0.5 * Vertical</c>, clamped to [0, 1]. So
    /// a controller idle puts the thruster at 50% (cruise), holding Space
    /// gives 100%, holding Ctrl gives 0%. This keeps planes flyable
    /// without a held-button discipline.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class ThrusterBlock : MonoBehaviour, IDriveSubsystem
    {
        [Tooltip("Optional tuning profile. If assigned, OVERRIDES the inline values below.")]
        [SerializeField] private ThrusterTuning _tuning;

        [Header("Thrust")]
        [Tooltip("Maximum forward force (N) at full throttle.")]
        [SerializeField, Min(0f)] private float _maxThrust = 220f;

        [Tooltip("Idle throttle when no input is being applied. 0 = off, 1 = full.")]
        [SerializeField, Range(0f, 1f)] private float _idleThrottle = 0.5f;

        [Tooltip("How quickly throttle slews to its target value (per second). 0 = instant.")]
        [SerializeField, Min(0f)] private float _throttleResponse = 2.2f;

        [Header("Visual nozzle (auto-built if blank)")]
        [SerializeField] private Transform _nozzle;
        [SerializeField] private Color _nozzleColor = new Color(0.95f, 0.45f, 0.1f);

        public int Order => 0; // actuator stage
        public bool IsOperational => isActiveAndEnabled;
        public float CurrentThrottle => _throttle;
        public float MaxThrust => _tuning != null ? _tuning.MaxThrust : _maxThrust;
        private float IdleThrottle     => _tuning != null ? _tuning.IdleThrottle     : _idleThrottle;
        private float ThrottleResponse => _tuning != null ? _tuning.ThrottleResponse : _throttleResponse;

        private Rigidbody _rb;
        private RobotDrive _drive;
        private float _throttle;

        private void Awake()
        {
            EnsureRig();
        }

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;

            // Throttle from Move.y (W = full, S = idle off). Vertical is
            // reserved for pitch on aircraft (space/shift).
            float target = Mathf.Clamp01(IdleThrottle + 0.5f * control.Move.y);
            _throttle = ThrottleResponse <= 0f
                ? target
                : Mathf.MoveTowards(_throttle, target, ThrottleResponse * control.DeltaTime);

            float thrust = _throttle * MaxThrust;
            if (thrust <= 0f) return;

            // Push along this thruster's forward axis (which is also the
            // chassis forward, since blocks inherit chassis orientation).
            _rb.AddForceAtPosition(transform.forward * thrust, transform.position);
        }

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private static Material s_nozzleMaterial;

        private void EnsureRig()
        {
            BlockVisuals.HideHostMesh(gameObject);
            if (_nozzle != null) return;

            _nozzle = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Nozzle", PrimitiveType.Cube);
            _nozzle.localScale = new Vector3(0.6f, 0.6f, 0.9f);

            // Glowing cone at the back. Cylinder long axis defaults to +Y;
            // rotate 90° so it lies along the thruster's Z (back) axis.
            Transform flame = BlockVisuals.GetOrCreatePrimitiveChild(_nozzle, "Flame", PrimitiveType.Cylinder);
            flame.localPosition = new Vector3(0f, 0f, -0.7f);
            flame.localRotation = Quaternion.Euler(90f, 0f, 0f);
            flame.localScale = new Vector3(0.5f, 0.4f, 0.5f);

            MeshRenderer fmr = flame.GetComponent<MeshRenderer>();
            if (fmr != null)
            {
                if (s_nozzleMaterial == null)
                {
                    s_nozzleMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = _nozzleColor };
                }
                fmr.sharedMaterial = s_nozzleMaterial;
            }
        }
    }
}
