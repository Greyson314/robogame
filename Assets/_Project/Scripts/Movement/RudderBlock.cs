using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Boat rudder. Applies a sideways force at the rudder's world
    /// position, scaled by the chassis' forward speed and the player's
    /// steer input. Because the force is applied off-centre (rudder lives
    /// at the stern), PhysX naturally turns it into yaw torque around the
    /// chassis COM — the same way a real rudder works.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Speed-dependent on purpose: at zero forward speed the rudder is
    /// inert. That's the boaty feel we want — no steering at a standstill,
    /// turn rate grows with throttle, exactly like a real outboard. If we
    /// ever want zero-speed turning we'd add a bow-thruster block, not
    /// hack it into the rudder.
    /// </para>
    /// <para>
    /// Sign convention: pressing <b>D</b> (<c>Move.x = +1</c>) should turn
    /// the bow right. A right-turn rotates the stern *left*, which means
    /// we apply a -right side force at the rudder when steer is positive.
    /// Hence <c>sideForce = -steer · speed · authority</c> along the
    /// chassis right axis.
    /// </para>
    /// <para>
    /// Tweakable: <see cref="Tweakables.RudderAuthority"/> is the side
    /// force per (m/s forward) per (1.0 steer input). Drop it for stately
    /// barge-feel, raise it for arcade jet-ski response.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class RudderBlock : MonoBehaviour, IDriveSubsystem
    {
        [Header("Visual blade (auto-built if blank)")]
        [SerializeField] private Transform _blade;
        [SerializeField] private Color _bladeColor = new Color(0.55f, 0.6f, 0.65f);

        public int Order => 0; // actuator stage
        public bool IsOperational => isActiveAndEnabled;
        private float Authority => Tweakables.Get(Tweakables.RudderAuthority);

        private Rigidbody _rb;
        private RobotDrive _drive;
        private Transform _chassisRoot;

        private void Awake()
        {
            EnsureRig();
        }

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _chassisRoot = _rb != null ? _rb.transform : null;
            _drive?.Register(this);
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null || _chassisRoot == null) return;

            float steer = Mathf.Clamp(control.Move.x, -1f, 1f);
            if (Mathf.Approximately(steer, 0f)) return;

            // Forward speed of the rudder location, projected onto the
            // chassis forward axis. Using GetPointVelocity (not just
            // _rb.velocity) means a yawing chassis already has a swirl
            // component baked in, and the rudder bites it correctly.
            Vector3 chassisFwd = _chassisRoot.forward;
            Vector3 chassisRight = _chassisRoot.right;
            float forwardSpeed = Vector3.Dot(_rb.GetPointVelocity(transform.position), chassisFwd);

            // Reverse-rudder feel: when going backward the rudder steers
            // the bow the opposite way, just like a real boat. Using the
            // signed forward speed handles that for free.
            float forceMag = -steer * forwardSpeed * Authority;
            if (Mathf.Approximately(forceMag, 0f)) return;

            _rb.AddForceAtPosition(chassisRight * forceMag, transform.position, ForceMode.Force);
        }

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private static Material s_bladeMaterial;

        private void EnsureRig()
        {
            BlockVisuals.HideHostMesh(gameObject);
            if (_blade != null) return;

            // Thin vertical blade. Long axis Y (deep), chord along Z so
            // the rudder looks like a flat fin pointing fore/aft.
            _blade = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Blade", PrimitiveType.Cube);
            _blade.localPosition = Vector3.zero;
            _blade.localRotation = Quaternion.identity;
            _blade.localScale = new Vector3(0.08f, 0.9f, 0.7f);

            MeshRenderer mr = _blade.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (s_bladeMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    s_bladeMaterial = new Material(shader) { color = _bladeColor };
                }
                mr.sharedMaterial = s_bladeMaterial;
            }
        }
    }
}
