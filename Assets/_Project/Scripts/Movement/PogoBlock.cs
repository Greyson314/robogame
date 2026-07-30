using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Player-controlled hopper. A soft over-damped stand spring holds the
    /// chassis up on its foot; while the jump input is held
    /// (<see cref="Robogame.Input.IInputSource.Vertical"/> &gt; 0), each
    /// grounded contact fires a hop impulse along the mount axis. Hold to
    /// keep hopping, release to stand. Deliberately NOT the Spring
    /// module's one-shot ability launch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same non-joint propulsion pattern as <see cref="WheelBlock"/>
    /// suspension and <see cref="HoverBladeBlock"/> (physics.md §2.1):
    /// forces on the single chassis Rigidbody, zero new physics objects.
    /// </para>
    /// <para>
    /// v1 was a stiff under-damped passive spring: it launched once, then
    /// settled at static equilibrium — a passive spring-damper always
    /// dissipates. Repeated hopping needs energy injected per bounce, so
    /// v2 makes the hop an explicit player-commanded impulse
    /// (<see cref="ForceMode.VelocityChange"/> for consistent hop height
    /// across chassis masses; per-pogo height via
    /// <see cref="BlockBehaviour.ConfigValue"/>).
    /// </para>
    /// <para>
    /// Casting along the mount axis (not world down) means a bottom-mounted
    /// pogo hops the bot upward, a side-mounted one kicks it off walls, and
    /// gravity direction is never referenced — spherical arenas behave
    /// identically. Known prototype quirk: N pogos grounded simultaneously
    /// stack N hop impulses — quad-pogo bots jump ~4×. Revisit if that
    /// reads as exploit rather than build expression.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class PogoBlock : MonoBehaviour
    {
        [Header("Stand spring")]
        [Tooltip("Foot reach from block centre along the mount axis, metres.")]
        [SerializeField, Min(0.1f)] private float _restLength = 1.15f;

        [Tooltip("Spring stiffness, N per metre of compression. Wheel-suspension-soft: stands, never self-bounces.")]
        [SerializeField, Min(0f)] private float _springStrength = 600f;

        [Tooltip("Damping, N per m/s of compression rate. Over-damped on purpose (§15.7) — hop energy comes from the impulse, not the spring.")]
        [SerializeField, Min(0f)] private float _damper = 220f;

        [Tooltip("Hard cap on spring force, N — absorbs landing spikes so a drop can't catapult the chassis.")]
        [SerializeField, Min(0f)] private float _maxForce = 6000f;

        [Header("Hop")]
        [Tooltip("Hop take-off speed, m/s (VelocityChange — mass-independent). ConfigValue overrides per instance.")]
        [SerializeField, Min(0f)] private float _hopSpeed = 5f;

        [Tooltip("Minimum seconds between hops from this pogo.")]
        [SerializeField, Min(0.05f)] private float _hopIntervalSeconds = 0.45f;

        [Tooltip("Layers the foot can push off.")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Visual rig (auto-built if blank)")]
        [SerializeField] private Transform _piston; // shaft that follows extension
        [SerializeField] private Transform _foot;   // contact pad

        private Rigidbody _rb;
        private Robogame.Input.IInputSource _input;
        private BlockBehaviour _bb;
        private float _extension;
        private float _hopCooldown;

        private float HopSpeed => _bb != null && _bb.ConfigValue > 0f ? _bb.ConfigValue : _hopSpeed;

        private void Awake()
        {
            BlockVisuals.HideHostMesh(gameObject);
            EnsureRig();
            _rb = GetComponentInParent<Rigidbody>();
            _input = GetComponentInParent<Robogame.Input.IInputSource>();
            _bb = GetComponent<BlockBehaviour>();
            _extension = _restLength;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;
            if (_hopCooldown > 0f) _hopCooldown -= Time.fixedDeltaTime;

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

            // Player-commanded hop: jump held + foot loaded + off cooldown.
            // Bots currently report Vertical 0, so their pogos just stand.
            if (_input != null && _input.Vertical > 0.25f
                && _hopCooldown <= 0f && compression > 0.05f)
            {
                _rb.AddForceAtPosition(-castDir * HopSpeed, origin, ForceMode.VelocityChange);
                _hopCooldown = _hopIntervalSeconds;
                // SpringLaunch "boing" reused as the placeholder hop cue
                // (invariant #8) until the pogo gets its own recording.
                Robogame.Core.AudioRouter.PlayOneShot(Robogame.Core.AudioCue.SpringLaunch, origin);
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
