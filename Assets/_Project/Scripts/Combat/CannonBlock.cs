using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Pirate-themed cannon. Same yaw + pitch turret rig as
    /// <see cref="WeaponBlock"/>, but builds a gravity-affected
    /// projectile spec routed through <see cref="ProjectileWorld"/>
    /// instead of a flat-trajectory pellet. Slow, hard-hitting; low
    /// fire rate. Mounts on top of or facing forward off a chassis cell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a fork of WeaponBlock, not a subclass?</b> The barrel
    /// geometry is conspicuously different (cannon = chunkier, longer,
    /// bronze-tipped) and the firing path passes a different
    /// <see cref="ProjectileSpec"/> shape (mesh visual, gravity, direct
    /// hit). The shared concept (yaw/pitch yoke aimed at
    /// <see cref="WeaponMount.AimPoint"/>) is small enough that a fork
    /// keeps each block's intent legible without leaning on inheritance
    /// gymnastics.
    /// </para>
    /// <para>
    /// Stats live on a per-block <see cref="CannonDefinition"/>
    /// referenced via <see cref="BlockDefinition.GetComponentData{T}"/>;
    /// inline SerializeFields are inspector-time fallbacks for the
    /// rare case where a designer wants per-block tuning without
    /// authoring an SO. Same pattern as WeaponBlock / BombBayBlock.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class CannonBlock : MonoBehaviour
    {
        [Header("Rig layout (block-local)")]
        [Tooltip("Local position of the pitch yoke pivot — sits on top of the block by default; set to (0,0,0.5) for a forward-mounted barrel.")]
        [SerializeField] private Vector3 _yokeLocalOffset = new Vector3(0f, 0.4f, 0f);

        [Tooltip("Local position of the muzzle relative to the yoke (down the barrel — z increases out the front).")]
        [SerializeField] private Vector3 _muzzleLocalOffset = new Vector3(0f, 0f, 0.95f);

        [Header("Aim limits")]
        [Tooltip("Pitch clamp (degrees). Negative = look up, positive = look down (Unity convention). Cannons sweep wider than SMGs to support arc-aiming.")]
        [SerializeField] private float _minPitch = -75f;
        [SerializeField] private float _maxPitch = 35f;

        [Header("Smoothing")]
        [Tooltip("How quickly the block yaws to face the aim point. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _yawSpeed = 10f;

        [Tooltip("How quickly the yoke pitches. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _pitchSpeed = 14f;

        [Header("Cannon stats (fallback when no CannonDefinition is wired)")]
        [Tooltip("Inline fallbacks. Asset-authored CannonDefinition takes precedence.")]
        [SerializeField, Min(0.1f)] private float _fireInterval = 0.85f;
        [SerializeField, Min(5f)]   private float _muzzleSpeed = 80f;
        [SerializeField, Min(0f)]   private float _damage = 60f;
        [SerializeField, Min(0.05f)] private float _ballRadius = 0.28f;
        [SerializeField, Min(0f)]   private float _recoilImpulse = 28f;

        [Header("Layers")]
        [Tooltip("Layers the cannonball can damage / collide with.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Wiring (auto if blank)")]
        [SerializeField] private Transform _yoke;
        [SerializeField] private Transform _muzzle;
        [SerializeField] private WeaponMount _mount;

        public Transform Muzzle => _muzzle;

        // Brass-tipped gunmetal — pirate-cannon palette read.
        private static readonly Color s_barrelColor = new Color(0.20f, 0.18f, 0.14f);
        private static readonly Color s_muzzleColor = new Color(0.45f, 0.32f, 0.10f); // brass

        private IInputSource _input;
        private Robot _ownerRobot;
        private Rigidbody _ownerRb;
        private BlockBehaviour _block;
        private float _nextFireTime;

        private void Awake()
        {
            EnsureRig();
            if (_mount == null) _mount = GetComponentInParent<WeaponMount>();
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
            _ownerRb = _ownerRobot != null ? _ownerRobot.GetComponent<Rigidbody>() : null;
            _block = GetComponent<BlockBehaviour>();
        }

        // -----------------------------------------------------------------
        // Stat resolution (per-block CannonDefinition wins over inline)
        // -----------------------------------------------------------------

        private CannonDefinition ResolveDef()
        {
            if (_block == null || _block.Definition == null) return null;
            return _block.Definition.GetComponentData<CannonDefinition>();
        }
        private float ResolveFireInterval()  { var d = ResolveDef(); return d != null ? d.FireInterval  : _fireInterval; }
        private float ResolveMuzzleSpeed()   { var d = ResolveDef(); return d != null ? d.MuzzleSpeed   : _muzzleSpeed; }
        private float ResolveDamage()        { var d = ResolveDef(); return d != null ? d.Damage        : _damage; }
        private float ResolveBallRadius()    { var d = ResolveDef(); return d != null ? d.BallRadius    : _ballRadius; }
        private float ResolveRecoilImpulse() { var d = ResolveDef(); return d != null ? d.RecoilImpulse : _recoilImpulse; }

        // -----------------------------------------------------------------
        // Aim
        // -----------------------------------------------------------------

        private void LateUpdate()
        {
            if (_yoke == null || _muzzle == null) return;

            Vector3 aim = _mount != null
                ? _mount.AimPoint
                : transform.position + transform.forward * 30f;

            // Yaw the whole block around Y.
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
                    : Quaternion.Slerp(transform.localRotation, targetLocal,
                        1f - Mathf.Exp(-_yawSpeed * Time.deltaTime));
            }

            // Pitch the yoke.
            Vector3 localAim = transform.InverseTransformPoint(aim) - _yoke.localPosition;
            float horiz = new Vector2(localAim.x, localAim.z).magnitude;
            float pitchDeg = Mathf.Atan2(-localAim.y, horiz) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Clamp(pitchDeg, _minPitch, _maxPitch);
            Quaternion targetPitch = Quaternion.Euler(pitchDeg, 0f, 0f);
            _yoke.localRotation = _pitchSpeed <= 0f
                ? targetPitch
                : Quaternion.Slerp(_yoke.localRotation, targetPitch,
                    1f - Mathf.Exp(-_pitchSpeed * Time.deltaTime));

            // Muzzle: precise lookat for barrel-line VFX / audio cue.
            Vector3 dir = aim - _muzzle.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                _muzzle.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        // -----------------------------------------------------------------
        // Fire
        // -----------------------------------------------------------------

        private void Update()
        {
            if (_input == null || !_input.FireHeld) return;
            if (Time.time < _nextFireTime) return;
            float interval = Mathf.Max(0.05f, ResolveFireInterval());
            _nextFireTime = Time.time + interval;
            FireCannon();
        }

        // Iron-ball tint for the visual proxy.
        private static readonly Color s_ballTint = new Color(0.10f, 0.10f, 0.12f);

        // Cannon shells live longer than SMG pellets to allow long
        // arcing shots under planet gravity. 6 s × 80 m/s = 480 m max
        // travel — comfortably past arena scale, and the swept hit
        // query terminates earlier on any actual contact.
        private const float CannonLifetime = 6f;

        private void FireCannon()
        {
            if (_muzzle == null) return;

            float speed = ResolveMuzzleSpeed();
            float damage = ResolveDamage();
            float radius = ResolveBallRadius();
            float recoil = ResolveRecoilImpulse();

            Vector3 origin = _muzzle.position;
            Vector3 dir = _muzzle.forward;
            Vector3 velocity = dir * speed;
            // Inherit chassis velocity so a fast-moving plane firing
            // forward gets a faster shot — same convention as the bomb
            // bay's drop velocity.
            if (_ownerRb != null) velocity += _ownerRb.linearVelocity;

            // Chassis-relative gravity so cannon arcs work on planet
            // arenas. Adequate today; full planet-aware projectiles
            // would re-evaluate gravity per step against the planet
            // centre.
            Vector3 gravityDir = transform.parent != null
                ? -transform.parent.up
                : Vector3.down;

            ProjectileSpec spec = new ProjectileSpec
            {
                Kind = ProjectileKind.Cannonball,
                Origin = origin,
                InitialVelocity = velocity,
                GravityWorld = gravityDir * Physics.gravity.magnitude,
                MaxLifetime = CannonLifetime,
                CastRadius = radius,
                Damage = damage,
                SplashRings = null,
                SplashRadius = 0f,                  // direct contact — single-target
                HitMask = _hitMask,
                Owner = _ownerRobot,
                ShowTrail = false,
                ShowMesh = true,
                VisualTint = s_ballTint,
                VisualMeshDiameter = radius * 2f,
                ImpactAudioOverride = AudioCue.ProjectileImpact,
            };
            ProjectileWorld.Spawn(in spec);

            // Recoil at the muzzle — chassis-side effect at fire time,
            // not a flight concern.
            if (recoil > 0f && _ownerRb != null)
            {
                _ownerRb.AddForceAtPosition(-dir * recoil, origin, ForceMode.Impulse);
            }

            // Muzzle flash + audio. Bigger flash than SMG.
            VfxSpawner.Spawn(VfxKind.MuzzleFlash, origin, dir, scale: 2.0f);
            VfxSpawner.Spawn(VfxKind.BombShockwave, origin, Quaternion.identity, scale: 0.45f);
            AudioRouter.PlayOneShot(AudioCue.WeaponFireCannon, origin);
        }

        // -----------------------------------------------------------------
        // Rig construction
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            // Yoke pivot. Build the cannon's chunky barrel + a brass
            // muzzle-tip ring on first creation. Idempotent on
            // subsequent calls (asset reimport, scene reload).
            bool yokeIsNew = transform.Find("Yoke") == null;
            _yoke = BlockVisuals.GetOrCreateChild(transform, "Yoke");
            if (yokeIsNew)
            {
                _yoke.localPosition = _yokeLocalOffset;

                // Trunnion block (visual only): squat box around the
                // pivot point so the yoke reads as mounted, not floating.
                Transform trunnion = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "Trunnion", PrimitiveType.Cube);
                trunnion.localPosition = new Vector3(0f, 0f, 0f);
                trunnion.localScale = new Vector3(0.55f, 0.35f, 0.45f);
                Tint(trunnion, s_barrelColor);

                // Long thick barrel — the cannon's headline shape.
                // Cylinder default points +Y; rotate 90° on X to lay
                // it along +Z (barrel forward).
                Transform barrel = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "Barrel", PrimitiveType.Cylinder);
                barrel.localPosition = new Vector3(0f, 0f, 0.55f);
                barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.localScale = new Vector3(0.34f, 0.50f, 0.34f);
                Tint(barrel, s_barrelColor);

                // Brass muzzle ring at the front — pirate cannon
                // signature detail.
                Transform muzzleRing = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "MuzzleRing", PrimitiveType.Cylinder);
                muzzleRing.localPosition = new Vector3(0f, 0f, 1.02f);
                muzzleRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
                muzzleRing.localScale = new Vector3(0.40f, 0.06f, 0.40f);
                Tint(muzzleRing, s_muzzleColor);
            }

            // Muzzle: child of yoke so it inherits both yaw + pitch.
            bool muzzleIsNew = _yoke.Find("Muzzle") == null;
            _muzzle = BlockVisuals.GetOrCreateChild(_yoke, "Muzzle");
            if (muzzleIsNew) _muzzle.localPosition = _muzzleLocalOffset;
        }

        private static void Tint(Transform t, Color color)
        {
            Renderer r = t.GetComponent<Renderer>();
            if (r == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_AlbedoColor"), color);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"),   color);
            mpb.SetColor(Shader.PropertyToID("_Color"),       color);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Editor / scaffolder helper.</summary>
        public void Bind(WeaponMount mount)
        {
            _mount = mount;
        }
    }
}
