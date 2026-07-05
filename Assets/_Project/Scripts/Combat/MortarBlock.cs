using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Top-mounted indirect-fire weapon. Lobs an explosive shell on a
    /// ballistic arc. Same yaw/pitch yoke rig as <see cref="CannonBlock"/>,
    /// but the pitch is driven by a <b>launch-elevation</b> offset above the
    /// camera/aim line rather than aimed straight at the reticle — so the
    /// player fires a lob without craning the camera at the sky. A short
    /// arc preview draws only the <b>start</b> of the trajectory, enough to
    /// read the firing angle, not the landing spot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shell is an area-splash projectile (<see cref="ProjectileKind.MortarShell"/>),
    /// so it routes through the bomb explosion treatment on impact and picks
    /// up the explosive-knockback path for free. Stats live on a per-block
    /// <see cref="MortarDefinition"/> via
    /// <see cref="BlockDefinition.GetComponentData{T}"/>; inline SerializeFields
    /// are inspector-time fallbacks. Same pattern as CannonBlock / BombBayBlock.
    /// </para>
    /// <para>
    /// <b>Targeter rationale</b> (design-pilot research, session 108): a
    /// start-of-arc preview with a camera-offset launch beats a ground-target
    /// reticle (frustrating vs. fast-moving bots), a WoT-style top-down arty
    /// view (creates a defenceless window), and Robocraft's no-preview
    /// camera-pitch (baffling to new players). The player stays spatially
    /// aware; the arc gives an angle read without a landing crutch.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class MortarBlock : MonoBehaviour, IClientSilenceable
    {
        [Header("Rig layout (block-local)")]
        [Tooltip("Local position of the pitch-yoke pivot — sits on top of the block.")]
        [SerializeField] private Vector3 _yokeLocalOffset = new Vector3(0f, 0.45f, 0f);

        [Tooltip("Local position of the muzzle relative to the yoke (down the short tube).")]
        [SerializeField] private Vector3 _muzzleLocalOffset = new Vector3(0f, 0f, 0.55f);

        [Header("Lob targeting")]
        [Tooltip("Degrees the launch elevation sits ABOVE the camera/aim line. The whole point: " +
                 "lob without pointing the camera at the sky.")]
        [SerializeField, Range(0f, 80f)] private float _elevationOffsetDeg = 35f;

        [Tooltip("Floor on launch elevation above horizontal (deg) — how flat the lob can get when looking down.")]
        [SerializeField, Range(0f, 89f)] private float _minLaunchElevationDeg = 25f;

        [Tooltip("Ceiling on launch elevation above horizontal (deg) — keeps the shell off near-vertical.")]
        [SerializeField, Range(1f, 89f)] private float _maxLaunchElevationDeg = 72f;

        [Header("Smoothing")]
        [Tooltip("How quickly the block yaws to face the aim direction. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _yawSpeed = 8f;

        [Tooltip("How quickly the yoke pitches to the launch elevation. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _pitchSpeed = 10f;

        [Header("Arc preview")]
        [Tooltip("Seconds of trajectory drawn from the muzzle — only the START of the arc, " +
                 "enough to read the firing angle, not the landing spot.")]
        [SerializeField, Min(0.05f)] private float _previewSeconds = 0.55f;

        [Tooltip("Sample count for the preview polyline.")]
        [SerializeField, Min(2)] private int _previewSamples = 14;

        [Header("Mortar stats (fallback when no MortarDefinition is wired)")]
        [Tooltip("Inline fallbacks. Asset-authored MortarDefinition takes precedence.")]
        [SerializeField, Min(0.1f)] private float _fireInterval = 2.2f;
        [SerializeField, Min(5f)]   private float _muzzleSpeed = 34f;
        [SerializeField, Min(0f)]   private float _damage = 90f;
        [SerializeField, Min(0.1f)] private float _splashRadius = 9f;
        [SerializeField, Min(0f)]   private float _recoilImpulse = 22f;
        [SerializeField, Min(0f)]   private float _knockbackImpulse = 55f;
        [SerializeField, Min(0.05f)] private float _shellRadius = 0.3f;

        [Header("Layers")]
        [Tooltip("Layers the shell's explosion can damage / hit.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Wiring (auto if blank)")]
        [SerializeField] private Transform _yoke;
        [SerializeField] private Transform _muzzle;
        [SerializeField] private WeaponMount _mount;

        public Transform Muzzle => _muzzle;

        // Stubby olive-drab tube — reads as "mortar", distinct from the
        // cannon's long brass-tipped barrel.
        private static readonly Color s_tubeColor = new Color(0.22f, 0.26f, 0.18f);
        private static readonly Color s_arcColor  = new Color(0.95f, 0.55f, 0.10f, 0.55f);

        // Shells live long enough for a full high lob across the largest
        // arena. 8 s matches the bomb's lifetime; splash fires on contact.
        private const float ShellLifetime = 8f;
        private static readonly Color s_shellTint = new Color(0.12f, 0.13f, 0.10f);

        private IInputSource _input;
        private Robot _ownerRobot;
        private Rigidbody _ownerRb;
        private BlockBehaviour _block;
        private WeaponAmmoState _ammo;
        private string _blockId;
        // TRACE[ADR-0003]: shared cooldown + ammo + dry-click gate (phase D)
        private WeaponFireGate _gate;

        private LineRenderer _arcLine;
        private static Material s_arcMaterial;
        private Quaternion _restLocalRotation = Quaternion.identity;

        private void Awake()
        {
            // Rest rotation before the first Yaw overwrites it — the yaw
            // axis is the authored mount orientation, not gravity.
            _restLocalRotation = transform.localRotation;
            // _block must resolve before EnsureRig so the rig can read the
            // MortarDefinition's optional turret model.
            _block = GetComponent<BlockBehaviour>();
            EnsureRig();
            EnsureArcLine();
            if (_mount == null) _mount = GetComponentInParent<WeaponMount>();
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
            _ownerRb = _ownerRobot != null ? _ownerRobot.GetComponent<Rigidbody>() : null;
            _ammo = GetComponentInParent<WeaponAmmoState>();
            _blockId = _block != null && _block.Definition != null ? _block.Definition.Id : null;
        }

        // Stylized turret model (Fatty pack) when the definition supplies one;
        // returns false to fall through to the procedural tube below.
        private bool TryBuildTurretModel()
        {
            MortarDefinition def = ResolveDef();
            if (def == null || def.TurretModel == null) return false;
            if (!WeaponModelRig.TryBuild(this, def.TurretModel, def.TurretModelScale,
                    def.TurretModelOffset, out Transform yoke, out Transform muzzle))
                return false;
            _yoke = yoke;
            _muzzle = muzzle;
            return true;
        }

        // -----------------------------------------------------------------
        // Stat resolution (per-block MortarDefinition wins over inline)
        // -----------------------------------------------------------------

        private MortarDefinition ResolveDef()
        {
            if (_block == null || _block.Definition == null) return null;
            return _block.Definition.GetComponentData<MortarDefinition>();
        }
        private float ResolveFireInterval() { var d = ResolveDef(); return d != null ? d.FireInterval     : _fireInterval; }
        private float ResolveMuzzleSpeed()  { var d = ResolveDef(); return d != null ? d.MuzzleSpeed      : _muzzleSpeed; }
        private float ResolveDamage()       { var d = ResolveDef(); return d != null ? d.Damage           : _damage; }
        private float ResolveSplashRadius() { var d = ResolveDef(); return d != null ? d.SplashRadius      : _splashRadius; }
        private float ResolveRecoil()       { var d = ResolveDef(); return d != null ? d.RecoilImpulse     : _recoilImpulse; }
        private float ResolveKnockback()    { var d = ResolveDef(); return d != null ? d.KnockbackImpulse  : _knockbackImpulse; }
        private float ResolveShellRadius()  { var d = ResolveDef(); return d != null ? d.ShellRadius       : _shellRadius; }

        // -----------------------------------------------------------------
        // Aim — yaw toward target, pitch to the offset launch elevation
        // -----------------------------------------------------------------

        private void LateUpdate()
        {
            if (_yoke == null || _muzzle == null) return;

            Vector3 aim = _mount != null
                ? _mount.AimPoint
                : transform.position + transform.forward * 40f;

            // ---- Yaw the whole block toward the aim direction. ----
            // TRACE[ADR-0003]: shared yaw (phase C); the mortar drives its own
            // lob pitch below, so it uses Yaw, not the full Track. Yaw axis is
            // the chassis-frame mount up since LOG-131, so the tube follows
            // the block through rolls.
            new TurretYoke(transform, _yoke, _muzzle, _yawSpeed, _pitchSpeed, 0f, 0f)
                .Yaw(aim, TurretYoke.MountUp(transform, _restLocalRotation), Time.deltaTime);

            // ---- Pitch the yoke to the launch elevation (offset above aim). ----
            // Unity X-rot: positive = nose down, so -elevation pitches the
            // tube UP. This is the launch direction, NOT a look-at the aim
            // point — that decoupling is what makes it a lob.
            float launchElev = ComputeLaunchElevationDeg(aim);
            Quaternion targetPitch = Quaternion.Euler(-launchElev, 0f, 0f);
            _yoke.localRotation = _pitchSpeed <= 0f
                ? targetPitch
                : Quaternion.Slerp(_yoke.localRotation, targetPitch,
                    1f - Mathf.Exp(-_pitchSpeed * Time.deltaTime));

            UpdateArcPreview();
        }

        // Launch elevation = the aim line's own pitch, offset upward, clamped.
        // Looking flat ahead still lobs (offset); looking up extends range;
        // looking down flattens it toward the floor.
        private float ComputeLaunchElevationDeg(Vector3 aim)
        {
            Vector3 from = _muzzle != null ? _muzzle.position : transform.position;
            Vector3 toAim = aim - from;
            float horiz = new Vector2(toAim.x, toAim.z).magnitude;
            float aimPitchUp = Mathf.Atan2(toAim.y, Mathf.Max(0.01f, horiz)) * Mathf.Rad2Deg;
            return Mathf.Clamp(aimPitchUp + _elevationOffsetDeg, _minLaunchElevationDeg, _maxLaunchElevationDeg);
        }

        // World gravity at the muzzle so the lob (and its preview) stay correct
        // on flat and planet arenas.
        // TRACE[AUDIT-15]: unified muzzle gravity (was chassis-relative -parent.up)
        private Vector3 GravityWorld() => ProjectileGravity.ForMuzzle(_muzzle);

        private void UpdateArcPreview()
        {
            if (_arcLine == null || _muzzle == null) return;

            // Only the player's own mortar draws an arc. (Singleplayer: the
            // local player carries an IInputSource; static dummies don't.
            // Refine to a local-ownership check when netcode lands.)
            bool show = _input != null;
            if (_arcLine.enabled != show) _arcLine.enabled = show;
            if (!show) return;

            int n = Mathf.Max(2, _previewSamples);
            if (_arcLine.positionCount != n) _arcLine.positionCount = n;

            Vector3 origin = _muzzle.position;
            Vector3 v0 = _muzzle.forward * ResolveMuzzleSpeed();
            Vector3 g = GravityWorld();
            float dt = _previewSeconds / (n - 1);
            for (int i = 0; i < n; i++)
            {
                float t = dt * i;
                _arcLine.SetPosition(i, origin + v0 * t + 0.5f * g * (t * t));
            }
        }

        // -----------------------------------------------------------------
        // Fire
        // -----------------------------------------------------------------

        private void Update()
        {
            // TRACE[LOG-132]: activation-order tolerant — a bind-once Awake
            // lookup goes stale when the input source lands after this block
            // (arena spawn / future netcode possession). Same lazy re-resolve
            // DrillBlock already carries. Null check is free per frame.
            if (_input == null) _input = GetComponentInParent<IInputSource>();
            if (_input == null || !_input.FireHeld) return;
            float interval = Mathf.Max(0.05f, ResolveFireInterval());
            if (_gate.TryFire(true, Time.time, interval, _ammo, _blockId, transform.position, 0.40f))
                FireMortar();
        }

        private void FireMortar()
        {
            if (_muzzle == null) return;

            float speed = ResolveMuzzleSpeed();
            Vector3 origin = _muzzle.position;
            Vector3 dir = _muzzle.forward;                 // launch along the tube
            Vector3 velocity = dir * speed;
            // Inherit chassis velocity so a moving bot's shell leads its motion.
            if (_ownerRb != null) velocity += _ownerRb.linearVelocity;

            // Player concoction (ADR-0004): scale the explosive stats by the
            // recipe chosen for this block. Empty / unknown id → 1× (baseline).
            // Scaling SplashRadius also scales the shockwave VFX + crater.
            float dmgMult = 1f, sizeMult = 1f, knockMult = 1f;
            if (_block != null && ConcoctionRegistry.TryGet(_block.ConcoctionId, out Concoction concoction))
            {
                dmgMult = concoction.DamageMultiplier;
                sizeMult = concoction.SizeMultiplier;
                knockMult = concoction.KnockbackMultiplier;
            }

            ProjectileSpec spec = new ProjectileSpec
            {
                Kind = ProjectileKind.MortarShell,
                Origin = origin,
                InitialVelocity = velocity,
                GravityWorld = GravityWorld(),
                MaxLifetime = ShellLifetime,
                CastRadius = ResolveShellRadius(),
                Damage = ResolveDamage() * dmgMult,
                SplashRings = null,
                SplashRadius = ResolveSplashRadius() * sizeMult,  // area splash — explosive
                HitMask = _hitMask,
                Owner = _ownerRobot,
                Knockback = ResolveKnockback() * knockMult,
                KnockbackSmoothed = false,                  // explosion — always immediate
                ShowTrail = false,
                ShowMesh = true,
                VisualTint = s_shellTint,
                VisualMeshDiameter = ResolveShellRadius() * 2f,
                ImpactAudioOverride = AudioCue.BombExplosion,
            };
            ProjectileWorld.Spawn(in spec);

            // Recoil — opposite the launch, at the muzzle. Chassis-side effect.
            float recoil = ResolveRecoil();
            if (recoil > 0f && _ownerRb != null)
            {
                _ownerRb.AddForceAtPosition(-dir * recoil, origin, ForceMode.Impulse);
            }

            // Launch flash + thump. Reuse the cannon's heavy report; a
            // dedicated mortar cue can be authored later (invariant #8 is
            // satisfied — it ships with VFX + audio today).
            VfxSpawner.Spawn(VfxKind.MuzzleFlash, origin, dir, scale: 1.7f);
            VfxSpawner.Spawn(VfxKind.BombShockwave, origin, Quaternion.identity, scale: 0.4f);
            AudioRouter.PlayOneShot(AudioCue.WeaponFireCannon, origin);
        }

        // -----------------------------------------------------------------
        // Rig construction
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            // Prefer the authored turret model; fall back to the procedural
            // tube when the definition has no model assigned.
            if (TryBuildTurretModel()) return;

            bool yokeIsNew = transform.Find("Yoke") == null;
            _yoke = BlockVisuals.GetOrCreateChild(transform, "Yoke");
            if (yokeIsNew)
            {
                _yoke.localPosition = _yokeLocalOffset;

                // Base plate so the tube reads as mounted, not floating.
                Transform basePlate = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "BasePlate", PrimitiveType.Cube);
                basePlate.localPosition = new Vector3(0f, -0.05f, 0f);
                basePlate.localScale = new Vector3(0.6f, 0.18f, 0.6f);
                Tint(basePlate, s_tubeColor);

                // Short, fat tube — the mortar's headline shape. Cylinder
                // default points +Y; lay it along +Z (the launch axis).
                Transform tube = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "Tube", PrimitiveType.Cylinder);
                tube.localPosition = new Vector3(0f, 0f, 0.3f);
                tube.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tube.localScale = new Vector3(0.46f, 0.32f, 0.46f);
                Tint(tube, s_tubeColor);
            }

            bool muzzleIsNew = _yoke.Find("Muzzle") == null;
            _muzzle = BlockVisuals.GetOrCreateChild(_yoke, "Muzzle");
            if (muzzleIsNew) _muzzle.localPosition = _muzzleLocalOffset;
        }

        private void EnsureArcLine()
        {
            Transform existing = transform.Find("ArcPreview");
            GameObject go = existing != null ? existing.gameObject : new GameObject("ArcPreview");
            if (existing == null) go.transform.SetParent(transform, worldPositionStays: false);

            _arcLine = go.GetComponent<LineRenderer>();
            if (_arcLine == null) _arcLine = go.AddComponent<LineRenderer>();
            _arcLine.useWorldSpace = true;
            _arcLine.widthMultiplier = 0.06f;
            _arcLine.numCapVertices = 2;
            _arcLine.textureMode = LineTextureMode.Stretch;
            _arcLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arcLine.receiveShadows = false;
            _arcLine.positionCount = Mathf.Max(2, _previewSamples);
            if (s_arcMaterial == null) s_arcMaterial = RuntimeMaterials.UnlitTransparent(s_arcColor);
            _arcLine.sharedMaterial = s_arcMaterial;
            _arcLine.startColor = s_arcColor;
            _arcLine.endColor = new Color(s_arcColor.r, s_arcColor.g, s_arcColor.b, 0f); // fade out the tip
            _arcLine.enabled = false;
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
