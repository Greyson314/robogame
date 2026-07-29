using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Passive raycast spring-damper bouncer. The foot extends along the
    /// block's mount axis (local +Y, i.e. away from the chassis); when the
    /// ray finds ground inside rest length, a stiff under-damped spring
    /// pushes the chassis away. Repeated automatic bounce — deliberately
    /// NOT the Spring module's one-shot ability launch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same non-joint propulsion pattern as <see cref="WheelBlock"/>
    /// suspension and <see cref="HoverBladeBlock"/> (physics.md §2.1):
    /// forces on the single chassis Rigidbody, zero new physics objects.
    /// </para>
    /// <para>
    /// The damper term is mandatory — an undamped auto-spring is exactly
    /// the feedback-loop runaway best-practices §15.7 warns about. Pogo
    /// wants bounce, so it sits well UNDER critical damping, but never at
    /// zero; the force cap absorbs the rest.
    /// </para>
    /// <para>
    /// Casting along the mount axis (not world down) means a bottom-mounted
    /// pogo bounces the bot upward, a side-mounted one kicks it sideways
    /// off walls, and gravity direction is never referenced — spherical
    /// arenas behave identically.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class PogoBlock : MonoBehaviour
    {
        [Header("Spring")]
        [Tooltip("Foot reach from block centre along the mount axis, metres.")]
        [SerializeField, Min(0.1f)] private float _restLength = 1.15f;

        [Tooltip("Spring stiffness, N per metre of compression. Stiffer than wheel suspension on purpose.")]
        [SerializeField, Min(0f)] private float _springStrength = 1400f;

        [Tooltip("Damping, N per m/s of compression rate. Deliberately under-damped for bounce; never zero (§15.7).")]
        [SerializeField, Min(0f)] private float _damper = 60f;

        [Tooltip("Hard cap on spring force, N — absorbs landing spikes so a drop can't catapult the chassis.")]
        [SerializeField, Min(0f)] private float _maxForce = 5000f;

        [Tooltip("Layers the foot can push off.")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Visual rig (auto-built if blank)")]
        [SerializeField] private Transform _piston; // shaft that follows extension
        [SerializeField] private Transform _foot;   // contact pad

        private Rigidbody _rb;
        private float _extension;

        private void Awake()
        {
            BlockVisuals.HideHostMesh(gameObject);
            EnsureRig();
            _rb = GetComponentInParent<Rigidbody>();
            _extension = _restLength;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            Vector3 origin = transform.position;
            Vector3 castDir = transform.up; // mount axis, points away from chassis

            if (!RaycastIgnoringSelf(origin, castDir, _restLength, out RaycastHit hit))
            {
                _extension = _restLength;
                return;
            }

            float compression = _restLength - hit.distance;

            // Compression rate: positive when this point moves toward the
            // ground along the cast axis. GetPointVelocity so chassis
            // rotation contributes correctly (same rationale as WheelBlock).
            float compressionRate = Vector3.Dot(_rb.GetPointVelocity(origin), castDir);

            float force = compression * _springStrength + compressionRate * _damper;
            if (force > 0f)
            {
                if (force > _maxForce) force = _maxForce;
                _rb.AddForceAtPosition(-castDir * force, origin, ForceMode.Force);
            }

            _extension = hit.distance;
        }

        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[8];

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        {
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

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private static Material s_pogoMaterial;

        private void LateUpdate()
        {
            if (_piston == null || _foot == null) return;
            // Foot tracks the current extension along local +Y; piston spans
            // block centre → foot.
            float ext = _extension;
            _foot.localPosition = new Vector3(0f, ext, 0f);
            _piston.localPosition = new Vector3(0f, ext * 0.5f, 0f);
            _piston.localScale = new Vector3(0.14f, ext * 0.5f, 0.14f);
        }

        private void EnsureRig()
        {
            BlockBehaviour bb = GetComponent<BlockBehaviour>();
            if (bb != null && bb.Definition != null
                && bb.Definition.VisualModelStatic
                && bb.Definition.VisualModel != null)
            {
                return;
            }
            if (_piston != null) return;

            _piston = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Piston", PrimitiveType.Cylinder);
            _piston.localRotation = Quaternion.identity;

            _foot = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Foot", PrimitiveType.Sphere);
            _foot.localRotation = Quaternion.identity;
            _foot.localScale = Vector3.one * 0.4f;

            Material mat = s_pogoMaterial;
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                s_pogoMaterial = mat = new Material(shader) { color = new Color(0.8f, 0.35f, 0.2f) };
            }
            foreach (Transform t in new[] { _piston, _foot })
            {
                MeshRenderer mr = t.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }
        }
    }
}
