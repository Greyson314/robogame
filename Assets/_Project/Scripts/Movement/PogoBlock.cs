using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Perpetual bouncer — the pogo's identity is "upside without
    /// control". It bounces off the ground at the instant of foot contact,
    /// always; the bounce vector is the stick's own axis, so WASD air
    /// TILT control (not air movement) is how you steer: lean forward in
    /// the air and the next bounce throws you forward-and-up. Deliberately
    /// NOT the Spring module's one-shot ability launch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same non-joint propulsion pattern as <see cref="WheelBlock"/>
    /// suspension and <see cref="HoverBladeBlock"/> (physics.md §2.1):
    /// forces on the single chassis Rigidbody, zero new physics objects.
    /// </para>
    /// <para>
    /// History: v1 passive stiff spring settled after one launch (passive
    /// spring-dampers always dissipate); v2 gated hops on the jump input,
    /// which ground-chassis input never delivered — "nothing works". v3
    /// removes every input dependency from the bounce itself: contact →
    /// bounce, on a short cooldown, so a resting pogo bot immediately
    /// starts hopping. Input only shapes the tilt.
    /// </para>
    /// <para>
    /// Bounce is <see cref="ForceMode.VelocityChange"/> (consistent height
    /// across chassis masses; per-pogo speed via
    /// <see cref="BlockBehaviour.ConfigValue"/>); tilt is
    /// <see cref="ForceMode.Acceleration"/> (consistent lean feel across
    /// masses). No gravity reference anywhere — spherical arenas behave
    /// identically. Known prototype quirk: N pogos touching down together
    /// stack N impulses. Pair with a Gyro for wobble damping — that's the
    /// intended build synergy, not a bug workaround.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class PogoBlock : MonoBehaviour
    {
        [Header("Bounce")]
        [Tooltip("Reach of the foot's CONTACT face from block centre along the mount axis, metres (ray length). " +
                 "Kept short so the leg pokes just below the host cell instead of occupying the cell beneath; " +
                 "the bounce fires at the true foot-touch instant, so the foot never buries before firing.")]
        [SerializeField, Min(0.1f)] private float _restLength = 0.95f;

        [Tooltip("Base bounce take-off speed at 1× power, m/s (VelocityChange — mass-independent). " +
                 "14 m/s ≈ 10 m apex — playtest-tuned. Per-instance power rides ConfigValue (PogoDefaults, height multiplier).")]
        [SerializeField, Min(0f)] private float _bounceSpeed = 14f;

        [Tooltip("Fraction of impact speed ABOVE the base takeoff that carries into the next bounce. " +
                 "Below 1 so cliff-drop momentum decays back to base height instead of running away.")]
        [SerializeField, Range(0f, 0.95f)] private float _momentumBonus = 0.7f;

        [Tooltip("Minimum seconds between bounces from this pogo. Always ticking, so a bot that failed to lift re-pulses instead of deadlocking.")]
        [SerializeField, Min(0.05f)] private float _bounceIntervalSeconds = 0.35f;

        [Tooltip("Layers the foot can push off.")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Air tilt (WASD while airborne)")]
        [Tooltip("Tilt authority, rad/s² at full stick (Acceleration — mass-normalised). W/S pitch, A/D roll.")]
        [SerializeField, Min(0f)] private float _tiltTorque = 8f;

        [Tooltip("Pitch/roll rate damping, per rad/s (Acceleration). Keeps the lean controllable without killing it.")]
        [SerializeField, Min(0f)] private float _tiltDamping = 2f;

        [Header("Visual rig (auto-built if blank)")]
        [SerializeField] private Transform _piston; // shaft that follows extension
        [SerializeField] private Transform _foot;   // contact pad

        private Rigidbody _rb;
        private Robogame.Input.IInputSource _input;
        private BlockBehaviour _bb;
        private float _extension;
        private float _bounceCooldown;

        // ConfigValue is a bounce-HEIGHT multiplier (0.8–1.8, PogoDefaults;
        // 0 sentinel = 1×). Height ∝ v², so takeoff speed scales by √power.
        private float BaseTakeoffSpeed =>
            _bounceSpeed * Mathf.Sqrt(PogoDefaults.ResolvePower(_bb != null ? _bb.ConfigValue : 0f));

        private void Awake()
        {
            BlockVisuals.HideHostMesh(gameObject);
            EnsureRig();
            _rb = GetComponentInParent<Rigidbody>();
            _bb = GetComponent<BlockBehaviour>();
            _extension = _restLength;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;
            // Late-resolve input: PlayerController lands on the chassis root
            // after block components exist, so an Awake-time lookup can miss
            // it (v2 bug class). Cheap null-guarded retry.
            if (_input == null) _input = GetComponentInParent<Robogame.Input.IInputSource>();
            if (_bounceCooldown > 0f) _bounceCooldown -= Time.fixedDeltaTime;

            Vector3 origin = transform.position;
            Vector3 castDir = transform.up; // mount axis, points away from chassis

            // The ray IS the foot: any hit inside rest length means the
            // foot's contact face is touching ground — bounce now. (The
            // old separate trigger distance fired 0.25 m after the foot
            // visual had already buried itself — playtest pass 2.)
            bool footLoaded = RaycastIgnoringSelf(origin, castDir, _restLength, out RaycastHit hit);
            _extension = footLoaded ? hit.distance : _restLength;

            if (footLoaded)
            {
                // Contact → bounce, always. The impulse is along the STICK
                // axis, not the ground normal — tilt at the moment of
                // contact aims the bounce (lean forward → thrown forward).
                // SET the take-off speed along the stick axis rather than
                // ADD it: a raw VelocityChange is additive, so it mostly
                // cancelled the incoming fall and the bot hovered instead
                // of bouncing (caught in live play-mode test, this
                // session). Velocity perpendicular to the stick is left
                // alone — forward momentum carries across bounces.
                if (_bounceCooldown <= 0f)
                {
                    float vAlongStick = Vector3.Dot(_rb.linearVelocity, -castDir);
                    float baseSpeed = BaseTakeoffSpeed;
                    // Momentum banking: impact speed ABOVE the base takeoff
                    // carries into this bounce at _momentumBonus. Flat-ground
                    // hopping is stable (impact ≈ takeoff → zero bonus);
                    // a cliff drop launches higher and the extra decays
                    // geometrically over the next bounces. Bonus < 1 is the
                    // no-runaway guarantee.
                    float impactSpeed = Mathf.Max(0f, -vAlongStick);
                    float takeoff = baseSpeed + _momentumBonus * Mathf.Max(0f, impactSpeed - baseSpeed);
                    float deltaV = takeoff - vAlongStick;
                    if (deltaV > 0f)
                        _rb.AddForceAtPosition(-castDir * deltaV, origin, ForceMode.VelocityChange);
                    _bounceCooldown = _bounceIntervalSeconds;
                    // SpringLaunch "boing" reused as the placeholder cue
                    // (invariant #8) until the pogo gets its own recording.
                    Robogame.Core.AudioRouter.PlayOneShot(Robogame.Core.AudioCue.SpringLaunch, origin);
                }
                return;
            }

            // Airborne: WASD tilts the chassis (pitch about right, roll
            // about forward). This is attitude control only — no lateral
            // force; travel comes from where the next bounce is aimed.
            if (_input == null) return;
            Transform chassis = _rb.transform;
            Vector3 angVel = _rb.angularVelocity;

            Vector2 move = _input.Move;
            if (move.sqrMagnitude > 0.01f)
            {
                Vector3 tilt = (chassis.right * move.y - chassis.forward * move.x) * _tiltTorque;
                _rb.AddTorque(tilt, ForceMode.Acceleration);
            }

            if (_tiltDamping > 0f)
            {
                float rollRate  = Vector3.Dot(angVel, chassis.forward);
                float pitchRate = Vector3.Dot(angVel, chassis.right);
                Vector3 damping = (chassis.forward * -rollRate + chassis.right * -pitchRate) * _tiltDamping;
                _rb.AddTorque(damping, ForceMode.Acceleration);
            }
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

        // Foot sphere is 0.4 local units in diameter; its CENTRE sits one
        // radius short of the contact face so the ball kisses the ground
        // instead of half-burying (playtest pass 2).
        private const float FootRadius = 0.2f;

        private void LateUpdate()
        {
            if (_piston == null || _foot == null) return;
            // _extension is centre → contact-face distance along local +Y;
            // piston spans block centre → foot centre.
            float footCentre = Mathf.Max(0.1f, _extension - FootRadius);
            _foot.localPosition = new Vector3(0f, footCentre, 0f);
            _piston.localPosition = new Vector3(0f, footCentre * 0.5f, 0f);
            _piston.localScale = new Vector3(0.14f, footCentre * 0.5f, 0.14f);
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
