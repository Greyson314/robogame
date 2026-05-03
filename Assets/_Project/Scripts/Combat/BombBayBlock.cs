using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Lives on a <see cref="BlockBehaviour"/> with id
    /// <see cref="BlockIds.BombBay"/>. While the player holds Fire, drops
    /// gravity bombs from the underside of the block at the configured
    /// drop interval. Sibling to <see cref="WeaponBlock"/> (which builds a
    /// turret rig and shoots projectiles) — bomb bays don't aim, they
    /// just open and let physics do the work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Does <em>not</em> build the yoke/barrel rig that <see cref="WeaponBlock"/>
    /// does. The host primitive cube is the visible bomb bay; we just
    /// expose a <see cref="DropPoint"/> at <c>(0, -0.5, 0)</c> in block
    /// local space so bombs spawn outside the block's collider.
    /// </para>
    /// <para>
    /// Bombs are spawned <em>unparented</em> so they don't inherit chassis
    /// motion at fixed-update boundaries, but their initial velocity
    /// matches the chassis rigidbody's velocity at spawn — so a fast
    /// plane releases bombs that "carry" forward rather than stalling
    /// in mid-air behind it.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class BombBayBlock : MonoBehaviour
    {
        [Header("Drop geometry (block-local)")]
        [Tooltip("Local position of the drop point — should sit on the underside of the block so bombs don't clip the cube collider.")]
        [SerializeField] private Vector3 _dropLocalOffset = new Vector3(0f, -0.6f, 0f);

        [Tooltip("Radius of the spawned bomb's sphere collider (m).")]
        [SerializeField, Min(0.05f)] private float _bombColliderRadius = 0.3f;

        [Header("Layers")]
        [Tooltip("Layers the bomb's explosion can damage.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        private float _nextDropTime;
        private IInputSource _input;
        private Robot _ownerRobot;
        private Rigidbody _ownerRb;
        private static Material s_bombMaterial;

        public Transform DropPoint { get; private set; }

        private void Awake()
        {
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
            _ownerRb = _ownerRobot != null ? _ownerRobot.GetComponent<Rigidbody>() : null;

            // Drop point is just an empty child so designers can re-aim
            // it from the inspector if a future bomb-bay variant wants
            // a forward-throwing release.
            DropPoint = BlockVisuals.GetOrCreateChild(transform, "DropPoint");
            DropPoint.localPosition = _dropLocalOffset;
        }

        private void Update()
        {
            if (_input == null || !_input.FireHeld) return;
            if (Time.time < _nextDropTime) return;

            float interval = Mathf.Max(0.05f, Tweakables.Get(Tweakables.BombDropInterval));
            _nextDropTime = Time.time + interval;
            DropOne();
        }

        private void DropOne()
        {
            float damage     = Tweakables.Get(Tweakables.BombDamage);
            float radius     = Tweakables.Get(Tweakables.BombRadius);
            float startSpeed = Tweakables.Get(Tweakables.BombInitialSpeed);

            Vector3 dropWorld = DropPoint.position;
            // "Down" in chassis-local space — uses the parent rigidbody's
            // own up vector so on a planet (where chassis up = away from
            // centre) bombs fall sensibly toward the surface.
            Vector3 down = transform.parent != null
                ? -transform.parent.up
                : Vector3.down;

            Vector3 velocity = down * startSpeed;
            if (_ownerRb != null) velocity += _ownerRb.linearVelocity;

            GameObject go = new GameObject("Bomb");
            go.transform.position = dropWorld;
            go.transform.rotation = Quaternion.LookRotation(down, Vector3.up);

            // Visible body: small dark sphere primitive. Strip its primitive
            // SphereCollider — we put a single explicit collider on the
            // root below so the Rigidbody / OnCollisionEnter routing is
            // unambiguous (compound colliders + auto-RequireComponent on
            // an abstract Collider type were dropping collision messages
            // on some bombs).
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            Collider primitiveCol = body.GetComponent<Collider>();
            if (primitiveCol != null) Object.Destroy(primitiveCol);
            body.transform.SetParent(go.transform, worldPositionStays: false);
            float diameter = _bombColliderRadius * 2f;
            body.transform.localScale = new Vector3(diameter, diameter, diameter * 1.6f); // egg-shaped
            ApplyBombMaterial(body);

            // Single root-level collider: sized to the visible body.
            // Putting it on the rigidbody's GameObject guarantees
            // OnCollisionEnter on the Bomb script runs every time.
            SphereCollider rootCol = go.AddComponent<SphereCollider>();
            rootCol.radius = _bombColliderRadius;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 8f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            // Continuous speculative — bombs are slow but a fast forward
            // chassis can launch them at 50+ m/s; keep them from
            // tunnelling through the planet's MeshCollider triangles.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            Bomb bomb = go.AddComponent<Bomb>();
            bomb.Configure(damage, radius, _hitMask, _ownerRobot, velocity);
        }

        private static void ApplyBombMaterial(GameObject go)
        {
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;

            if (s_bombMaterial == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                s_bombMaterial = new Material(sh) { name = "BombBody (runtime)" };
                if (s_bombMaterial.HasProperty("_BaseColor"))
                    s_bombMaterial.SetColor("_BaseColor", new Color(0.10f, 0.10f, 0.12f));
                if (s_bombMaterial.HasProperty("_Color"))
                    s_bombMaterial.SetColor("_Color", new Color(0.10f, 0.10f, 0.12f));
                if (s_bombMaterial.HasProperty("_Smoothness"))
                    s_bombMaterial.SetFloat("_Smoothness", 0.6f);
            }
            mr.sharedMaterial = s_bombMaterial;
        }

        /// <summary>Editor / scaffolder helper — kept for parity with WeaponBlock.</summary>
        public void Bind(WeaponMount mount)
        {
            // Bomb bays don't aim, so the mount reference is unused. The
            // method exists so RobotWeaponBinder can call it identically
            // for both behaviour types.
        }
    }
}
