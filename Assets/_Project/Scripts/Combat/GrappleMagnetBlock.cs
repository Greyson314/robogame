using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Fire-and-retract grapple magnet. Single-shot weapon with no ammo
    /// pool: fires a rope+magnet projectile up to a configured range,
    /// latches to the first enemy chassis it touches, otherwise retracts
    /// instantly. Player can fire again only after the projectile has
    /// fully reeled back into the muzzle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State machine.</b>
    /// </para>
    /// <code>
    ///   Ready
    ///     ──[FirePressed]──▶ Firing
    ///   Firing
    ///     ──[hit enemy Robot]──▶ Latched
    ///     ──[hit static / max range]──▶ Retracting
    ///   Latched
    ///     ──[FirePressed | target died]──▶ Retracting
    ///   Retracting
    ///     ──[tip reaches muzzle]──▶ Ready
    /// </code>
    /// <para>
    /// <b>Why this lives in Combat.</b> It's a fire-on-trigger weapon
    /// driven by <c>IInputSource.FirePressed</c>, dispatched through
    /// <c>RobotWeaponBinder</c> like SMG / Cannon / Bomb. It does *use*
    /// Movement-tier types at runtime (<c>VerletRopeChain</c>,
    /// <c>VerletRopeSimulator</c>) but Combat already references Movement
    /// in its asmdef, so the dependency is one-way and clean.
    /// </para>
    /// <para>
    /// <b>Three constraint contract during Latched</b> (mirrors session
    /// 60's standard tip-block design — see TIP_BLOCK_ATTACH.md):
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Verlet chain</b> between chassis muzzle and the projectile
    ///   tip body. Positions only; renders the rope's natural drape.</item>
    ///   <item><b>Chassis↔tip leash</b> — ConfigurableJoint with linear
    ///   limit at total rope length, 8000 N spring. Forces propagate from
    ///   the rope back to the chassis once the rope is taut.</item>
    ///   <item><b>Tip↔target tether</b> — SpringJoint at rest distance 0,
    ///   <c>breakForce = ∞</c>. Holds the target at the magnet's mouth.</item>
    /// </list>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class GrappleMagnetBlock : MonoBehaviour
    {
        // -----------------------------------------------------------------
        // State machine
        // -----------------------------------------------------------------

        public enum GrappleState
        {
            /// <summary>Idle in the muzzle. <c>FirePressed</c> transitions to <see cref="Firing"/>.</summary>
            Ready,
            /// <summary>Projectile flying outward. Hit checks each FixedUpdate.</summary>
            Firing,
            /// <summary>Tethered to an enemy chassis. Player drags it.</summary>
            Latched,
            /// <summary>Reeling the projectile back to the muzzle.</summary>
            Retracting,
        }

        public GrappleState State => _state;
        private GrappleState _state = GrappleState.Ready;

        // -----------------------------------------------------------------
        // Rig (turret) layout
        // -----------------------------------------------------------------

        [Header("Rig layout (block-local)")]
        [Tooltip("Local position of the pitch yoke pivot — sits on top of the block.")]
        [SerializeField] private Vector3 _yokeLocalOffset = new(0f, 0.5f, 0f);

        [Tooltip("Local position of the muzzle (where the projectile launches from). Down the barrel from the yoke.")]
        [SerializeField] private Vector3 _muzzleLocalOffset = new(0f, 0f, 0.55f);

        [Header("Aim limits")]
        [SerializeField] private float _minPitch = -70f;
        [SerializeField] private float _maxPitch = 40f;

        [SerializeField, Range(0f, 30f)] private float _yawSpeed = 18f;
        [SerializeField, Range(0f, 30f)] private float _pitchSpeed = 22f;

        // -----------------------------------------------------------------
        // Projectile + rope tuning
        // -----------------------------------------------------------------

        [Header("Projectile flight")]
        [Tooltip("Maximum travel distance in metres. The brief: 24 blocks = 24 m.")]
        [SerializeField, Min(2f)] private float _maxRange = 24f;

        [Tooltip("Launch speed of the magnet projectile (m/s). Tuned so a max-range shot completes in ~0.4 s — fast enough to feel like a shot, slow enough that you can see the rope extend.")]
        [SerializeField, Min(5f)] private float _launchSpeed = 60f;

        [Tooltip("Sphere-cast radius used to detect hits while the projectile is in flight. Slightly larger than the visible magnet so glancing shots still bite.")]
        [SerializeField, Min(0.1f)] private float _flightCastRadius = 0.45f;

        [Header("Retract")]
        [Tooltip("Seconds to reel the projectile back from its furthest point. 0.35 s reads as a quick rewind — long enough to see, short enough not to feel slow.")]
        [SerializeField, Min(0.05f)] private float _retractDuration = 0.35f;

        [Header("Latched leash + tether")]
        [Tooltip("Stiffness of the chassis↔tip leash spring (N/m). Matches RopeBlock's default — the leash only kicks in when the rope reaches full length.")]
        [SerializeField, Min(0f)] private float _leashSpring = 8000f;
        [SerializeField, Min(0f)] private float _leashDamper = 250f;

        [Tooltip("Spring stiffness of the magnet→target tether (N/m). Same shape as the standalone MagnetBlock latch.")]
        [SerializeField, Min(0f)] private float _tetherSpring = 320f;
        [SerializeField, Min(0f)] private float _tetherDamper = 110f;

        [Header("Latched pull field")]
        [Tooltip("Radius of the latched-state pull sphere (m). Mirrors MagnetBlock's pull field — once latched, the magnet continuously pulls every nearby non-kinematic body toward the tip on top of the tether spring's hold. This is what makes a chassis-attached rope+magnet feel strong; without it, drag-around feels weak.")]
        [SerializeField, Min(0.5f)] private float _pullRadius = 6.0f;

        [Tooltip("Peak pull force (N) at the centre of the sphere, scaled down by _pullFalloffExponent toward the radius edge. The already-latched target sits at distance ≈ 0 and so eats the full force every FixedUpdate.")]
        [SerializeField, Min(0f)] private float _pullForce = 600f;

        [Tooltip("Falloff exponent for the pull field. 1 = linear, 2 = quadratic. Matches MagnetBlock's default.")]
        [SerializeField, Range(0f, 3f)] private float _pullFalloffExponent = 1.0f;

        [Header("Rope chain (Latched phase)")]
        [Tooltip("Number of Verlet particles used when the rope materialises after a successful latch. Higher = smoother drape, more solver cost. 12 reads as a real rope at 24 m total length.")]
        [SerializeField, Range(3, 32)] private int _ropeSegmentCount = 12;

        [Tooltip("Cylinder radius for the rope segment visuals (m).")]
        [SerializeField, Min(0.01f)] private float _ropeVisualRadius = 0.08f;

        // -----------------------------------------------------------------
        // Cached refs
        // -----------------------------------------------------------------

        private BlockBehaviour _block;
        private Transform _yoke;
        private Transform _muzzle;
        private WeaponMount _mount;
        private IInputSource _input;
        private Rigidbody _chassisRb;
        private Robot _ownerRobot;
        private Collider[] _chassisColliders;

        // Projectile lifecycle. The "projectile" is a scene-root GameObject
        // with a Rigidbody + SphereCollider that flies in Firing, anchors
        // the rope chain in Latched, and reels back in Retracting.
        private GameObject _tipGo;
        private Rigidbody _tipRb;
        private SphereCollider _tipCollider;
        private Renderer _tipRenderer;

        // Flight phase visual: single stretched cylinder from muzzle to
        // tip. Replaced by the proper chain visuals on Latched.
        private GameObject _flightLineGo;
        private Transform _flightLineT;
        private Renderer _flightLineR;

        // Latched-phase rope simulation + visuals + joints.
        private VerletRopeChain _chain;
        private Transform[] _chainSegments;
        private GameObject _chainSegmentContainer;
        private ConfigurableJoint _chassisTipJoint;
        private SpringJoint _targetTether;
        private Rigidbody _tetherTarget;

        // Retract phase state.
        private Vector3 _retractFromWorld;
        private float _retractElapsed;

        // Layer mask for flight hit detection. Skip our own chassis
        // colliders explicitly — set up in Awake.
        private int _flightHitMask = ~0;

        // Pre-sized scratch for the Latched pull-field OverlapSphere.
        // 32 comfortably exceeds the block count of any realistic target
        // chassis; the same buffer size MagnetBlock uses.
        private static readonly Collider[] s_pullOverlapBuffer = new Collider[32];

        // Cosmetic palette tokens.
        private static readonly Color s_bodyColor   = new(0.32f, 0.36f, 0.44f);
        private static readonly Color s_poleColor   = new(0.30f, 0.85f, 0.95f);
        private static readonly Color s_ropeColor   = new(0.42f, 0.42f, 0.45f);
        private static readonly Color s_flightTint  = new(0.55f, 0.85f, 0.95f);
        private static readonly int s_albedoId = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_baseId   = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyId = Shader.PropertyToID("_Color");

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        public Transform Muzzle => _muzzle;
        public bool IsReady => _state == GrappleState.Ready;
        public bool IsLatched => _state == GrappleState.Latched;

        /// <summary>Set by RobotWeaponBinder — mirrors WeaponBlock.Bind.</summary>
        public void Bind(WeaponMount mount) { _mount = mount; }

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            _block = GetComponent<BlockBehaviour>();
            EnsureRig();
            if (_mount == null) _mount = GetComponentInParent<WeaponMount>();
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
            _chassisRb = _ownerRobot != null ? _ownerRobot.GetComponent<Rigidbody>() : GetComponentInParent<Rigidbody>();
            _chassisColliders = _ownerRobot != null
                ? _ownerRobot.GetComponentsInChildren<Collider>(includeInactive: true)
                : System.Array.Empty<Collider>();
        }

        private void OnDestroy()
        {
            // Tear down whatever phase we're in so a destroyed grapple
            // block doesn't leak its projectile / chain / joints. Each
            // teardown helper is idempotent so calling them all is safe.
            DestroyTargetTether();
            DestroyChain();
            DestroyFlightVisual();
            DestroyProjectile();
        }

        private void Update()
        {
            UpdateAim();
            if (_input != null && _input.FirePressed)
            {
                HandleFireInput();
            }
        }

        private void FixedUpdate()
        {
            switch (_state)
            {
                case GrappleState.Firing:     TickFiring();     break;
                case GrappleState.Retracting: TickRetracting(); break;
                case GrappleState.Latched:    TickLatched();    break;
            }
        }

        // -----------------------------------------------------------------
        // Aim — turret pattern mirrors WeaponBlock.
        // -----------------------------------------------------------------

        private void UpdateAim()
        {
            if (_yoke == null || _muzzle == null) return;
            Vector3 aim = _mount != null ? _mount.AimPoint : transform.position + transform.forward * 30f;

            Vector3 flat = aim - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
            {
                Quaternion targetWorldYaw = Quaternion.LookRotation(flat, Vector3.up);
                Quaternion parentInv = transform.parent != null
                    ? Quaternion.Inverse(transform.parent.rotation)
                    : Quaternion.identity;
                Quaternion targetLocal = parentInv * targetWorldYaw;
                transform.localRotation = _yawSpeed <= 0f
                    ? targetLocal
                    : Quaternion.Slerp(transform.localRotation, targetLocal, 1f - Mathf.Exp(-_yawSpeed * Time.deltaTime));
            }

            Vector3 localAim = transform.InverseTransformPoint(aim) - _yoke.localPosition;
            float horiz = new Vector2(localAim.x, localAim.z).magnitude;
            float pitchDeg = Mathf.Atan2(-localAim.y, horiz) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Clamp(pitchDeg, _minPitch, _maxPitch);
            Quaternion targetPitch = Quaternion.Euler(pitchDeg, 0f, 0f);
            _yoke.localRotation = _pitchSpeed <= 0f
                ? targetPitch
                : Quaternion.Slerp(_yoke.localRotation, targetPitch, 1f - Mathf.Exp(-_pitchSpeed * Time.deltaTime));

            Vector3 dir = aim - _muzzle.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                _muzzle.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        // -----------------------------------------------------------------
        // Fire input dispatch
        // -----------------------------------------------------------------

        private void HandleFireInput()
        {
            switch (_state)
            {
                case GrappleState.Ready:
                    Fire();
                    break;
                case GrappleState.Latched:
                    // Player taps fire while latched → release tether and
                    // reel in. Lets them swap targets without waiting for
                    // a death.
                    BeginRetract();
                    break;
                // Firing / Retracting: ignore taps; player can't fire
                // again until Ready.
            }
        }

        // -----------------------------------------------------------------
        // Firing phase — projectile flight + hit detection
        // -----------------------------------------------------------------

        private void Fire()
        {
            if (_muzzle == null) return;
            SpawnProjectile();
            SpawnFlightVisual();
            _state = GrappleState.Firing;

            AudioRouter.PlayOneShot(AudioCue.WeaponFireCannon, _muzzle.position);
            VfxSpawner.Spawn(VfxKind.MuzzleFlash, _muzzle.position, _muzzle.forward, 0.8f);
        }

        private void TickFiring()
        {
            if (_tipRb == null) { ReturnToReady(); return; }

            Vector3 origin = _tipRb.position;
            Vector3 dir = _tipRb.transform.forward; // launch direction = muzzle forward at spawn
            float dt = Time.fixedDeltaTime;
            Vector3 nextPos = origin + dir * (_launchSpeed * dt);

            // Sphere-cast over this step's swept volume to catch fast hits
            // without tunnelling. Excludes the chassis's own colliders via
            // a per-cast scratch (we pre-ignored them at spawn).
            if (Physics.SphereCast(origin, _flightCastRadius, dir,
                    out RaycastHit hit, (nextPos - origin).magnitude,
                    _flightHitMask, QueryTriggerInteraction.Ignore))
            {
                // Move the tip to the hit point so the visual snaps to
                // exactly where it caught.
                _tipRb.position = hit.point - dir * _flightCastRadius;

                Robot enemy = hit.collider.GetComponentInParent<Robot>();
                if (enemy != null && enemy != _ownerRobot && !enemy.IsDestroyed)
                {
                    BeginLatch(enemy.GetComponent<Rigidbody>(), hit.point);
                    return;
                }
                BeginRetract();
                return;
            }

            _tipRb.position = nextPos;

            // Max-range check. From the muzzle, not from spawn-time
            // origin, so a player who's flown forward extends their
            // reach naturally.
            Vector3 muzzleWorld = _muzzle != null ? _muzzle.position : origin;
            if ((nextPos - muzzleWorld).sqrMagnitude >= _maxRange * _maxRange)
            {
                BeginRetract();
            }

            UpdateFlightVisual();
        }

        // -----------------------------------------------------------------
        // Latched phase — spawn chain + tether, drive PinTip on chain.
        // -----------------------------------------------------------------

        private void BeginLatch(Rigidbody targetRb, Vector3 contactPointWorld)
        {
            if (targetRb == null) { BeginRetract(); return; }

            // Switch tip body to non-kinematic so PhysX can move it
            // around under the rope + tether forces. The Verlet
            // simulator auto-detects this via IsTipExternallyConstrained.
            _tipRb.isKinematic = false;
            _tipRb.useGravity = false;
            _tipRb.linearDamping = 0.5f;
            _tipRb.angularDamping = 0.5f;

            BuildChassisLeash();
            BuildVerletChain();
            BuildTargetTether(targetRb, contactPointWorld);

            // Flight-phase line visual goes away; the chain segments are
            // the new visual rope.
            DestroyFlightVisual();

            _state = GrappleState.Latched;
            AudioRouter.PlayOneShot(AudioCue.TipImpact, contactPointWorld);
            VfxSpawner.Spawn(VfxKind.FlipBurst, contactPointWorld, Quaternion.identity, 0.8f);
        }

        private void TickLatched()
        {
            // Auto-release on target death — Unity nulls connectedBody.
            if (_targetTether == null
                || _tetherTarget == null
                || _targetTether.connectedBody == null)
            {
                BeginRetract();
                return;
            }

            // Pull field. Parity with the chassis-attached MagnetBlock:
            // every FixedUpdate, every non-kinematic Rigidbody inside
            // the sphere gets an AddForce toward the tip. The latched
            // target sits at distance ~0 (tether holds it there) so it
            // takes the full force every tick, which is the missing
            // ingredient that makes "drag a target on a rope+magnet"
            // feel powerful. Without this, the tether spring alone
            // (320 N/m × stretch) is the only force pulling the
            // target — strong enough to hold, weak enough to feel
            // like nothing when you try to drag.
            ApplyPullForces();
        }

        private void ApplyPullForces()
        {
            if (_tipRb == null) return;
            Vector3 worldOrigin = _tipRb.position;
            int hitCount = Physics.OverlapSphereNonAlloc(
                worldOrigin, _pullRadius, s_pullOverlapBuffer,
                ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider c = s_pullOverlapBuffer[i];
                if (c == null) continue;
                Rigidbody targetRb = c.attachedRigidbody;
                if (targetRb == null || targetRb.isKinematic) continue;

                // Self-skip: chassis + our own tip body.
                if (targetRb == _chassisRb) continue;
                if (targetRb == _tipRb) continue;

                // Dedup against earlier buffer entries (multi-collider
                // chassis would otherwise eat N applies per tick).
                bool alreadySeen = false;
                for (int j = 0; j < i; j++)
                {
                    Collider prior = s_pullOverlapBuffer[j];
                    if (prior == null) continue;
                    if (prior.attachedRigidbody == targetRb) { alreadySeen = true; break; }
                }
                if (alreadySeen) continue;

                Vector3 delta = worldOrigin - targetRb.worldCenterOfMass;
                float distance = delta.magnitude;
                if (distance < 0.05f)
                {
                    // Bodies overlapping the tip — typical for the
                    // latched target at distance 0. Apply force in the
                    // tip's forward direction so the target stays
                    // pinned to the front of the magnet rather than
                    // jittering on a sub-cm vector.
                    targetRb.AddForce(_tipRb.transform.forward * _pullForce, ForceMode.Force);
                    continue;
                }

                float t = 1f - Mathf.Clamp01(distance / _pullRadius);
                float gain = Mathf.Pow(t, Mathf.Max(0.01f, _pullFalloffExponent));
                Vector3 dir = delta / distance;
                targetRb.AddForce(dir * (_pullForce * gain), ForceMode.Force);
            }

            // Clear scratch so destroyed colliders don't linger.
            for (int i = 0; i < hitCount; i++) s_pullOverlapBuffer[i] = null;
        }

        // -----------------------------------------------------------------
        // Retract phase — lerp tip back to muzzle, tear down everything.
        // -----------------------------------------------------------------

        private void BeginRetract()
        {
            DestroyTargetTether();
            DestroyChain();

            if (_tipRb != null)
            {
                // Freeze tip motion + restore kinematic so the lerp is
                // authoritative; PhysX won't fight it.
                _tipRb.linearVelocity = Vector3.zero;
                _tipRb.angularVelocity = Vector3.zero;
                _tipRb.isKinematic = true;
                _retractFromWorld = _tipRb.position;
            }
            else
            {
                _retractFromWorld = _muzzle != null ? _muzzle.position : transform.position;
            }
            _retractElapsed = 0f;
            _state = GrappleState.Retracting;

            // Re-instate the flight-line visual; the chain visuals are
            // gone now and the tip needs *some* visible connection back
            // to the muzzle during the reel.
            if (_flightLineGo == null) SpawnFlightVisual();
        }

        private void TickRetracting()
        {
            if (_tipRb == null || _muzzle == null) { ReturnToReady(); return; }
            _retractElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(_retractElapsed / Mathf.Max(0.05f, _retractDuration));
            // Ease-in cubic — slow start (the tip has been at a distance),
            // fast finish (it slams home).
            float eased = t * t;
            _tipRb.position = Vector3.Lerp(_retractFromWorld, _muzzle.position, eased);

            UpdateFlightVisual();

            if (t >= 1f) ReturnToReady();
        }

        private void ReturnToReady()
        {
            DestroyTargetTether();
            DestroyChain();
            DestroyFlightVisual();
            DestroyProjectile();
            _state = GrappleState.Ready;
        }

        // -----------------------------------------------------------------
        // Projectile spawn / teardown
        // -----------------------------------------------------------------

        private void SpawnProjectile()
        {
            if (_tipGo != null) return;
            _tipGo = new GameObject($"GrappleTip_{name}");
            // Scene root, not parented under the chassis: the projectile
            // is a free body, the chassis must not tow it as a child
            // Rigidbody (would break the solver per BEST_PRACTICES §3.1).
            _tipGo.transform.position = _muzzle.position;
            _tipGo.transform.rotation = _muzzle.rotation;

            _tipRb = _tipGo.AddComponent<Rigidbody>();
            _tipRb.isKinematic = true; // simulator drives it during Firing
            _tipRb.useGravity = false;
            // Mass matches the rope+magnet's effective adopted-tip mass
            // (rope segment ≈ 0.4 kg + magnet block 3.0 kg ≈ 3.4 kg).
            // The chassis↔tip leash spring transmits force as
            // F = spring × stretch; the *target acceleration* that
            // results depends on how much of that force survives the
            // tip body before reaching the SpringJoint tether to the
            // target. Heavier tip → more inertia → less of the leash
            // force gets lost to tip self-acceleration before
            // transmitting through the tether. Matching the rope+magnet
            // tip mass keeps the perceived "pull strength" consistent
            // across both delivery methods.
            _tipRb.mass = 3.4f;
            _tipRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _tipRb.interpolation = RigidbodyInterpolation.Interpolate;

            _tipCollider = _tipGo.AddComponent<SphereCollider>();
            _tipCollider.radius = _flightCastRadius;
            _tipCollider.isTrigger = false;

            // Visible magnet head — small bridge + two cyan-tipped poles.
            BuildTipVisual(_tipGo.transform);

            // Ignore-pair our projectile against every chassis collider so
            // the cast doesn't immediately self-hit. Same trick TipBlock
            // uses for its swung tip vs the chassis.
            for (int i = 0; i < _chassisColliders.Length; i++)
            {
                Collider c = _chassisColliders[i];
                if (c == null) continue;
                Physics.IgnoreCollision(_tipCollider, c, ignore: true);
            }
        }

        private void DestroyProjectile()
        {
            if (_tipGo == null) return;
            if (Application.isPlaying) Destroy(_tipGo);
            else                       DestroyImmediate(_tipGo);
            _tipGo = null;
            _tipRb = null;
            _tipCollider = null;
            _tipRenderer = null;
        }

        private void BuildTipVisual(Transform parent)
        {
            // Compact magnet head, sized roughly to the projectile's
            // sphere collider (~0.9 m total). Three children: bridge +
            // two pole shafts. Cyan caps on the pole tips.
            Transform bridge = MakePrim(parent, "TipBridge", PrimitiveType.Cube,
                new Vector3(0f, 0f, 0f),
                new Vector3(0.8f, 0.25f, 0.25f),
                s_bodyColor);
            _tipRenderer = bridge.GetComponent<Renderer>();
            MakePrim(parent, "TipPoleN", PrimitiveType.Cube,
                new Vector3( 0.27f, 0f, 0.35f),
                new Vector3(0.25f, 0.25f, 0.6f),
                s_bodyColor);
            MakePrim(parent, "TipPoleS", PrimitiveType.Cube,
                new Vector3(-0.27f, 0f, 0.35f),
                new Vector3(0.25f, 0.25f, 0.6f),
                s_bodyColor);
            MakePrim(parent, "TipCapN", PrimitiveType.Cube,
                new Vector3( 0.27f, 0f, 0.62f),
                new Vector3(0.24f, 0.24f, 0.16f),
                s_poleColor);
            MakePrim(parent, "TipCapS", PrimitiveType.Cube,
                new Vector3(-0.27f, 0f, 0.62f),
                new Vector3(0.24f, 0.24f, 0.16f),
                s_poleColor);
        }

        // -----------------------------------------------------------------
        // Flight-phase visual: single stretched cylinder muzzle → tip.
        // -----------------------------------------------------------------

        private void SpawnFlightVisual()
        {
            if (_flightLineGo != null) return;
            _flightLineGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _flightLineGo.name = $"GrappleLine_{name}";
            if (_flightLineGo.TryGetComponent(out Collider c)) Destroy(c);
            _flightLineT = _flightLineGo.transform;
            _flightLineR = _flightLineGo.GetComponent<Renderer>();
            Tint(_flightLineR, s_flightTint);
        }

        private void UpdateFlightVisual()
        {
            if (_flightLineT == null || _muzzle == null || _tipRb == null) return;
            Vector3 a = _muzzle.position;
            Vector3 b = _tipRb.position;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            _flightLineT.position = mid;
            _flightLineT.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            // Cylinder mesh is height 2 along Y so localScale.y = half-length.
            _flightLineT.localScale = new Vector3(_ropeVisualRadius * 2f, len * 0.5f, _ropeVisualRadius * 2f);
        }

        private void DestroyFlightVisual()
        {
            if (_flightLineGo == null) return;
            if (Application.isPlaying) Destroy(_flightLineGo);
            else                       DestroyImmediate(_flightLineGo);
            _flightLineGo = null;
            _flightLineT = null;
            _flightLineR = null;
        }

        // -----------------------------------------------------------------
        // Chassis↔tip leash (the rope-length limit constraint).
        // -----------------------------------------------------------------

        private void BuildChassisLeash()
        {
            if (_chassisRb == null || _tipGo == null) return;
            _chassisTipJoint = _tipGo.AddComponent<ConfigurableJoint>();
            _chassisTipJoint.connectedBody = _chassisRb;
            _chassisTipJoint.autoConfigureConnectedAnchor = false;
            _chassisTipJoint.anchor = Vector3.zero;
            Vector3 muzzleLocal = _chassisRb.transform.InverseTransformPoint(_muzzle.position);
            _chassisTipJoint.connectedAnchor = muzzleLocal;
            _chassisTipJoint.xMotion = ConfigurableJointMotion.Limited;
            _chassisTipJoint.yMotion = ConfigurableJointMotion.Limited;
            _chassisTipJoint.zMotion = ConfigurableJointMotion.Limited;
            _chassisTipJoint.angularXMotion = ConfigurableJointMotion.Free;
            _chassisTipJoint.angularYMotion = ConfigurableJointMotion.Free;
            _chassisTipJoint.angularZMotion = ConfigurableJointMotion.Free;
            // Critical: limit is the *currently deployed* length, not
            // the max range. Setting it to maxRange leaves
            // (maxRange − deployed) metres of slack between chassis
            // motion and target pull — the plane has to fly that far
            // before the spring even engages, then engages hard and
            // jerks the chassis without ever building sustained
            // tension on the target. Setting it to the deployed length
            // means any forward chassis motion immediately stretches
            // the spring and the force flows chassis → leash → tip →
            // tether spring → target, same as the chassis-attached
            // rope+magnet that you can latch a target with.
            float deployedLen = Vector3.Distance(_muzzle.position, _tipRb.position);
            _chassisTipJoint.linearLimit = new SoftJointLimit
            {
                limit = Mathf.Max(0.5f, deployedLen),
                contactDistance = 0f,
            };
            _chassisTipJoint.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = _leashSpring,
                damper = _leashDamper,
            };
            _chassisTipJoint.enableCollision = false;
            _chassisTipJoint.enablePreprocessing = false;
        }

        // -----------------------------------------------------------------
        // Verlet chain — spawned at latch time, torn down on retract.
        // -----------------------------------------------------------------

        private void BuildVerletChain()
        {
            if (_chassisRb == null || _tipRb == null) return;
            int N = Mathf.Max(3, _ropeSegmentCount);
            float totalLen = Vector3.Distance(_muzzle.position, _tipRb.position);
            // Chain rest length matches the actual deployed length, not
            // _maxRange — that way the chain doesn't try to retract or
            // stretch the moment it spawns.
            float segLen = totalLen / Mathf.Max(1, N - 1);

            _chain = new VerletRopeChain
            {
                Particles = new VerletParticle[N],
                Count = N,
                HubRb = _chassisRb,
                HubAnchorLocal = _chassisRb.transform.InverseTransformPoint(_muzzle.position),
                TipRb = _tipRb,
                SegmentLength = segLen,
                LinearDamping = 0.5f,
                Iterations = 8,
                SubSteps = 4,
                BendingStiffness = 0.3f,
                PinTip = true,
            };
            for (int i = 0; i < N; i++)
            {
                float t = i / (float)(N - 1);
                Vector3 pos = Vector3.Lerp(_muzzle.position, _tipRb.position, t);
                _chain.Particles[i].Position = pos;
                _chain.Particles[i].PrevPosition = pos;
            }
            _chain.OnPostSolve = UpdateChainVisuals;

            BuildChainVisuals(N - 1);

            VerletRopeSimulator.GetOrCreate().Register(_chain);
        }

        private void BuildChainVisuals(int segmentCount)
        {
            _chainSegmentContainer = new GameObject($"GrappleChain_{name}_Segments");
            _chainSegments = new Transform[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seg.name = $"Vis_{i}";
                if (seg.TryGetComponent(out Collider col)) Destroy(col);
                seg.transform.SetParent(_chainSegmentContainer.transform, worldPositionStays: false);
                seg.transform.localScale = new Vector3(
                    _ropeVisualRadius * 2f, 1f, _ropeVisualRadius * 2f);
                Tint(seg.GetComponent<Renderer>(), s_ropeColor);
                _chainSegments[i] = seg.transform;
            }
        }

        private void UpdateChainVisuals()
        {
            if (_chain == null || _chainSegments == null) return;
            int N = _chain.Count;
            for (int i = 0; i < _chainSegments.Length && i + 1 < N; i++)
            {
                Vector3 a = _chain.Particles[i].Position;
                Vector3 b = _chain.Particles[i + 1].Position;
                Vector3 mid = (a + b) * 0.5f;
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                Transform t = _chainSegments[i];
                t.position = mid;
                t.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
                Vector3 s = t.localScale;
                s.y = len * 0.5f;
                t.localScale = s;
            }
        }

        private void DestroyChain()
        {
            if (_chain != null)
            {
                VerletRopeSimulator sim = VerletRopeSimulator.Instance;
                if (sim != null) sim.Unregister(_chain);
                _chain = null;
            }
            if (_chainSegmentContainer != null)
            {
                if (Application.isPlaying) Destroy(_chainSegmentContainer);
                else                       DestroyImmediate(_chainSegmentContainer);
                _chainSegmentContainer = null;
                _chainSegments = null;
            }
            if (_chassisTipJoint != null)
            {
                if (Application.isPlaying) Destroy(_chassisTipJoint);
                else                       DestroyImmediate(_chassisTipJoint);
                _chassisTipJoint = null;
            }
        }

        // -----------------------------------------------------------------
        // Target tether — SpringJoint, same shape as MagnetBlock.Latch.
        // -----------------------------------------------------------------

        private void BuildTargetTether(Rigidbody targetRb, Vector3 contactPointWorld)
        {
            if (_tipRb == null || targetRb == null) return;
            SpringJoint joint = _tipGo.AddComponent<SpringJoint>();
            joint.connectedBody = targetRb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor          = _tipRb.transform.InverseTransformPoint(contactPointWorld);
            joint.connectedAnchor = targetRb.transform.InverseTransformPoint(contactPointWorld);
            joint.spring = _tetherSpring;
            joint.damper = _tetherDamper;
            joint.minDistance = 0f;
            joint.maxDistance = 0f;
            joint.tolerance = 0.025f;
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            _targetTether = joint;
            _tetherTarget = targetRb;
        }

        private void DestroyTargetTether()
        {
            if (_targetTether != null)
            {
                if (Application.isPlaying) Destroy(_targetTether);
                else                       DestroyImmediate(_targetTether);
                _targetTether = null;
            }
            _tetherTarget = null;
        }

        // -----------------------------------------------------------------
        // Turret rig + helpers
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            bool yokeIsNew = transform.Find("Yoke") == null;
            _yoke = BlockVisuals.GetOrCreateChild(transform, "Yoke");
            if (yokeIsNew)
            {
                _yoke.localPosition = _yokeLocalOffset;
                // Visible launch tube — short cylinder pointing +Z.
                Transform barrel = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "Barrel", PrimitiveType.Cylinder);
                barrel.localPosition = new Vector3(0f, 0f, 0.4f);
                barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.localScale = new Vector3(0.32f, 0.45f, 0.32f);
                Tint(barrel.GetComponent<Renderer>(), s_bodyColor);
            }
            bool muzzleIsNew = _yoke.Find("Muzzle") == null;
            _muzzle = BlockVisuals.GetOrCreateChild(_yoke, "Muzzle");
            if (muzzleIsNew) _muzzle.localPosition = _muzzleLocalOffset;
        }

        private static Transform MakePrim(Transform parent, string name, PrimitiveType prim,
            Vector3 localPos, Vector3 localScale, Color color)
        {
            Transform t = BlockVisuals.GetOrCreatePrimitiveChild(
                parent, name, prim, stripCollider: true);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            t.localScale = localScale;
            Tint(t.GetComponent<Renderer>(), color);
            return t;
        }

        private static void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = new();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(s_albedoId, color);
            mpb.SetColor(s_baseId,   color);
            mpb.SetColor(s_legacyId, color);
            r.SetPropertyBlock(mpb);
        }
    }
}
