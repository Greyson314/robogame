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
    /// identically. Multi-pogo chassis are arbitrated: every pogo claims
    /// <see cref="PogoBounceArbiter"/> before bouncing and only one claim
    /// per bounce window wins, so extra pogos buy landing coverage, not
    /// stacked Δv (the unarbitrated version was a functional rocket —
    /// 10 pogos × full velocity-set ≈ 560 m/s). Pair with a Gyro for
    /// wobble damping — that's the intended build synergy.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class PogoBlock : MonoBehaviour
    {
        [Header("Bounce")]
        [Tooltip("Reach of the foot's CONTACT face from block centre along the mount axis, metres (ray length). " +
                 "2.5 = a 3-cell-tall assembly (host cell + 2-cell leg), per the components-sized-to-the-mechanic " +
                 "convention — the bot rides high on its stilt and bounces the moment the distant foot touches. " +
                 "The bounce fires at the true foot-touch instant, so the foot never buries before firing.")]
        [SerializeField, Min(0.1f)] private float _restLength = 2.5f;

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
        private PogoBounceArbiter _arbiter;
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

        private void OnDisable()
        {
            if (_arbiter != null) _arbiter.Unregister(this);
            _arbiter = null; // re-resolve + re-register on next FixedUpdate if re-enabled
        }

        /// <summary>
        /// True when this foot's ray currently finds ground inside rest
        /// length. Called by <see cref="PogoBounceArbiter.CountLoadedFeet"/>
        /// so the diminishing-returns stack count is same-step accurate.
        /// </summary>
        public bool ProbeFootLoaded()
            => RaycastIgnoringSelf(transform.position, transform.up, _restLength, out _);

        private void FixedUpdate()
        {
            if (_rb == null) return;
            // Late-resolve input: PlayerController lands on the chassis root
            // after block components exist, so an Awake-time lookup can miss
            // it (v2 bug class). Cheap null-guarded retry.
            if (_input == null) _input = GetComponentInParent<Robogame.Input.IInputSource>();
            if (_arbiter == null)
            {
                _arbiter = _rb.gameObject.GetComponent<PogoBounceArbiter>();
                if (_arbiter == null) _arbiter = _rb.gameObject.AddComponent<PogoBounceArbiter>();
                _arbiter.Register(this);
            }
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
                // Chassis-level arbitration: only ONE pogo per body may
                // bounce per window. A denied pogo keeps its own cooldown
                // untouched, so it stays ready for the next landing where
                // it might be the foot that actually touches first.
                if (_bounceCooldown <= 0f && _arbiter.TryClaim(Time.fixedTime, _bounceIntervalSeconds))
                {
                    float vAlongStick = Vector3.Dot(_rb.linearVelocity, -castDir);
                    // Diminishing returns for stacked feet (StackingCurves):
                    // N loaded feet → N^0.5 bounce HEIGHT, so speed scales
                    // by the square root of that. 4 feet ≈ 2× height,
                    // 10 ≈ 3.2× — more than one pogo, never the N× rocket.
                    int feet = _arbiter.CountLoadedFeet();
                    float stackHeightMul = StackingCurves.PowerLaw(feet, PogoDefaults.StackHeightExponent);
                    float baseSpeed = BaseTakeoffSpeed * Mathf.Sqrt(stackHeightMul);
                    // Momentum banking: impact speed ABOVE the base takeoff
                    // carries into this bounce at _momentumBonus. Flat-ground
                    // hopping is stable (impact ≈ takeoff → zero bonus);
                    // a cliff drop launches higher and the extra decays
                    // geometrically over the next bounces. Bonus < 1 is the
                    // no-runaway guarantee.
                    float impactSpeed = Mathf.Max(0f, -vAlongStick);
                    float takeoff = baseSpeed + _momentumBonus * Mathf.Max(0f, impactSpeed - baseSpeed);
                    float deltaV = takeoff - vAlongStick;
                    // Applied at the COM, not the foot: the arbiter picks ONE
                    // winning foot per bounce, and a corner-foot VelocityChange
                    // off-COM converts into violent spin (live probe: a quad
                    // rig tumbled straight through the floor). Direction stays
                    // the stick axis, so tilt-aiming is unchanged; landing
                    // asymmetry still comes from the collision itself.
                    if (deltaV > 0f)
                        _rb.AddForce(-castDir * deltaV, ForceMode.VelocityChange);
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

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
            => ChassisRaycast.TryNearestIgnoring(_rb, origin, dir, maxDist, _groundMask, out best);

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
