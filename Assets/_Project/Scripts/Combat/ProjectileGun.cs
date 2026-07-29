using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// SMG-style pellet weapon. Reads from an <see cref="IInputSource"/>
    /// on (or above) this GameObject and asks <see cref="ProjectileWorld"/>
    /// to spawn pooled, custom-stepped pellets while fire is held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As of session 32, every projectile (SMG, bomb, cannon) flies
    /// through a single shared integrator. This component is just the
    /// fire trigger + spec builder. Recoil + muzzle flash + audio
    /// remain here because they're chassis-side effects at the moment
    /// of fire, not flight or impact concerns.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ProjectileGun : MonoBehaviour, IClientSilenceable
    {
        // -----------------------------------------------------------------
        // Fallback / non-tweakable knobs (overridable in inspector for the
        // rare case where a weapon block wants its own per-instance feel).
        // -----------------------------------------------------------------

        [Header("Weapon stats (fallback when no WeaponDefinition is wired)")]
        [Tooltip("Inline fallbacks used when the firing block's BlockDefinition has no WeaponDefinition attached. " +
                 "Asset-authored WeaponDefinitions take precedence at every fire.")]
        [SerializeField, Min(0.1f)] private float _fireRate = 12f;
        [SerializeField, Min(1f)]   private float _muzzleSpeed = 80f;
        [SerializeField, Range(0f, 30f)] private float _spreadDeg = 1.2f;
        [SerializeField, Min(0f)]   private float _damage = 25f;
        [SerializeField, Min(0f)]   private float _recoilImpulse = 5f;
        [SerializeField, Min(0f)]   private float _knockbackImpulse = 3f;

        [Header("Splash falloff (block-graph rings beyond direct hit)")]
        [Tooltip("Multipliers applied to the headline damage at ring i+1. Index 0 is replaced with " +
                 "the resolved direct-hit damage at fire time. Tune the falloff ratio in the inspector " +
                 "if a chassis-shape needs more or less collateral.")]
        [SerializeField] private float[] _splashRings = { 25f, 8f, 2f };

        [Header("Range")]
        [Tooltip("Maximum projectile travel distance. Bullets self-despawn after lifetime computed from this and muzzle speed.")]
        [SerializeField, Min(5f)] private float _range = 220f;

        [Header("Layers")]
        [Tooltip("Layers projectiles can hit.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Origin (auto if blank)")]
        [Tooltip("Transform that defines the muzzle position + forward direction. Defaults to this transform.")]
        [SerializeField] private Transform _muzzle;

        // -----------------------------------------------------------------
        // Internal state
        // -----------------------------------------------------------------

        private IInputSource _input;
        private Robot _ownerRobot;
        private Robogame.Block.BlockBehaviour _block;
        private WeaponAmmoState _ammo;
        private string _blockId;
        // TRACE[ADR-0003]: shared cooldown + ammo + dry-click gate (phase D)
        private WeaponFireGate _gate;

        // Session-139 visual pass (attached by WeaponBlock after the model
        // builds, so resolve lazily at first shot rather than in Awake).
        private bool _visualsResolved;
        private WeaponVisualKick _visualKick;
        private WeaponCrankSpin _crank;

        // Tracer tint for the trail. Warm cream → tracer feel.
        private static readonly Color s_tracerHead = new Color(1f, 0.85f, 0.35f, 1f);

        private void Awake()
        {
            if (_muzzle == null) _muzzle = transform;
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
            _block = GetComponent<Robogame.Block.BlockBehaviour>();
            _ammo = GetComponentInParent<WeaponAmmoState>();
            _blockId = _block != null && _block.Definition != null ? _block.Definition.Id : null;
        }

        // Resolve weapon stats. Per-weapon-block WeaponDefinition (on
        // BlockDefinition.ComponentData) wins; otherwise fall back to
        // the inline SerializeField defaults. PHYSICS_PLAN § 5 — server-
        // authoritative blueprint data, NEVER read from Tweakables.
        private WeaponDefinition ResolveDef()
        {
            if (_block == null || _block.Definition == null) return null;
            return _block.Definition.GetComponentData<WeaponDefinition>();
        }
        private float ResolveFireRate()      { var d = ResolveDef(); return d != null ? d.FireRate      : _fireRate; }
        private float ResolveMuzzleSpeed()   { var d = ResolveDef(); return d != null ? d.MuzzleSpeed   : _muzzleSpeed; }
        private float ResolveSpread()        { var d = ResolveDef(); return d != null ? d.SpreadDeg     : _spreadDeg; }
        private float ResolveDamage()        { var d = ResolveDef(); return d != null ? d.Damage        : _damage; }
        private float ResolveRecoilImpulse() { var d = ResolveDef(); return d != null ? d.RecoilImpulse : _recoilImpulse; }
        private float ResolveKnockback()     { var d = ResolveDef(); return d != null ? d.KnockbackImpulse : _knockbackImpulse; }

        /// <summary>Override the muzzle transform (used by <see cref="WeaponBlock"/> at spawn).</summary>
        public void SetMuzzle(Transform muzzle)
        {
            if (muzzle != null) _muzzle = muzzle;
        }

        // Spin-up / overheat (session 155). Model is pure + deterministic
        // (WeaponHeatModel); this component just ticks it with frame time
        // and feeds the resolved rate into the fire gate. NOTE: heat is a
        // function of local held-duration/Time — the same clock-authority
        // debt already documented for fire-rate gating (performance.md
        // §8.10); the netcode pass owns both.
        private WeaponHeatModel _heat;
        private MeshRenderer[] _glowRenderers;
        private MaterialPropertyBlock _glowMpb;
        private float _lastGlowHeat = -1f;
        private static readonly int s_glowAlbedoId = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_glowBaseId   = Shader.PropertyToID("_BaseColor");
        private static readonly int s_glowLegacyId = Shader.PropertyToID("_Color");
        private static readonly Color s_hotColor = new Color(1f, 0.28f, 0.15f);

        private void Update()
        {
            bool held = _input != null && _input.FireHeld;

            WeaponDefinition def = ResolveDef();
            if (def != null && def.HasSpinUp)
            {
                if (_heat == null)
                {
                    _heat = new WeaponHeatModel(
                        def.SpinUpSeconds, def.SpinDownSeconds,
                        def.OverheatSeconds, def.OverheatCooldownSeconds,
                        def.MinFireRate, def.FireRate);
                }

                bool wasOverheated = _heat.IsOverheated;
                _heat.Tick(held, Time.deltaTime);
                if (_heat.IsOverheated && !wasOverheated)
                {
                    AudioRouter.PlayOneShot(AudioCue.WeaponOverheat, transform.position);
                }
                UpdateHeatGlow();

                if (!held || _heat.IsOverheated) return;
                float rate = Mathf.Max(0.1f, _heat.CurrentFireRate);
                if (_gate.TryFire(true, Time.time, 1f / rate, _ammo, _blockId, transform.position, 0.20f))
                    Fire();
                return;
            }

            if (!held) return;
            float fireRate = Mathf.Max(0.1f, ResolveFireRate());
            // Dry-click throttle matches a fast cadence so a held trigger on
            // empty doesn't strobe the cue.
            if (_gate.TryFire(true, Time.time, 1f / fireRate, _ammo, _blockId, transform.position, 0.20f))
                Fire();
        }

        // Heat readout: tint the weapon's renderers toward hot red via MPB
        // (never Renderer.material — no per-instance material churn). Only
        // touches the MPB when heat visibly moved.
        private void UpdateHeatGlow()
        {
            float h = _heat.Heat01;
            if (Mathf.Abs(h - _lastGlowHeat) < 0.01f) return;
            _lastGlowHeat = h;

            if (_glowRenderers == null)
            {
                _glowRenderers = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                _glowMpb = new MaterialPropertyBlock();
            }

            Color c = Color.Lerp(Color.white, s_hotColor, h);
            for (int i = 0; i < _glowRenderers.Length; i++)
            {
                MeshRenderer mr = _glowRenderers[i];
                if (mr == null) continue;
                mr.GetPropertyBlock(_glowMpb);
                _glowMpb.SetColor(s_glowAlbedoId, c);
                _glowMpb.SetColor(s_glowBaseId,   c);
                _glowMpb.SetColor(s_glowLegacyId, c);
                mr.SetPropertyBlock(_glowMpb);
            }
        }

        // -----------------------------------------------------------------
        // Fire
        // -----------------------------------------------------------------

        private void Fire()
        {
            if (!_visualsResolved)
            {
                _visualsResolved = true;
                _crank = GetComponent<WeaponCrankSpin>();
                _visualKick = _muzzle != null ? _muzzle.GetComponentInParent<WeaponVisualKick>() : null;
            }
            if (_visualKick != null) _visualKick.Kick();
            if (_crank != null) _crank.NotifyFired();

            float headline = ResolveDamage();
            float speed = ResolveMuzzleSpeed();
            float spreadDeg = ResolveSpread();
            float knockback = ResolveKnockback();

            // Player concoction (session 141): payload chemistry scales
            // damage / speed / spread / knockback and dyes the tracer +
            // impact spark with the recipe's mixed pigment. Empty /
            // unknown id → baseline stats, authored tracer colour. Size
            // has no stat to bite on here (no splash) — documented no-op.
            Color tint = s_tracerHead;
            bool tintImpact = false;
            Robogame.Block.Concoction fxRecipe = null;
            if (_block != null && Robogame.Block.ConcoctionRegistry.TryGet(
                    _block.ConcoctionId, out Robogame.Block.Concoction concoction))
            {
                headline  *= concoction.DamageMultiplier;
                speed     *= concoction.SpeedMultiplier;
                spreadDeg *= concoction.SpreadMultiplier;
                knockback *= concoction.KnockbackMultiplier;
                tint = concoction.MixedColor;
                tintImpact = true;
                fxRecipe = concoction;
            }

            // Index 0 of the splash profile is the direct-hit damage —
            // overwrite each fire so a tweaked WeaponDefinition damage
            // value doesn't drift the splash falloff ratios.
            if (_splashRings.Length > 0) _splashRings[0] = headline;

            Vector3 origin = _muzzle.position;
            Vector3 dir = ApplySpread(_muzzle.forward, _muzzle.right, _muzzle.up, spreadDeg);

            ProjectileSpec spec = new ProjectileSpec
            {
                Kind = ProjectileKind.SmgPellet,
                Origin = origin,
                InitialVelocity = dir * speed,
                GravityWorld = Vector3.zero,           // SMG is flat-shooting
                MaxLifetime = Mathf.Max(0.1f, _range / Mathf.Max(1f, speed)),
                CastRadius = 0f,                       // ray cast — pellet sized
                Damage = headline,
                SplashRings = _splashRings,
                SplashRadius = 0f,
                HitMask = _hitMask,
                Owner = _ownerRobot,
                Knockback = knockback,
                KnockbackSmoothed = true,              // rapid fire → debt buffer
                ShowTrail = true,
                ShowMesh = false,
                VisualTint = tint,
                TintImpact = tintImpact,
                VisualMeshDiameter = 0f,
                ImpactAudioOverride = AudioCue.ProjectileImpact,
            };
            spec.SetConcoctionFx(fxRecipe);
            ProjectileWorld.Spawn(in spec);

            // Recoil — equal-and-opposite impulse at the muzzle. Stays
            // here (not in ProjectileWorld) because it's a chassis-side
            // effect at the moment of fire, not a flight concern.
            float recoil = ResolveRecoilImpulse();
            if (recoil > 0f && _ownerRobot != null && _ownerRobot.Rigidbody != null)
            {
                _ownerRobot.Rigidbody.AddForceAtPosition(-dir * recoil, origin, ForceMode.Impulse);
            }

            // Muzzle flash + audio. Cone shape emits along +Z so we
            // orient by shot direction. Scale tracks recoil weight so
            // a future cannon SMG would ship a heavier flash.
            float flashScale = Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(2f, 25f, recoil));
            VfxSpawner.Spawn(VfxKind.MuzzleFlash, origin, dir, flashScale);
            AudioRouter.PlayOneShot(AudioCue.WeaponFire, origin);
        }

        /// <summary>
        /// Bend <paramref name="forward"/> by a uniform random offset
        /// inside a circular cone of half-angle <paramref name="spreadDeg"/>.
        /// Uses a unit-disk sample so spread distribution is rotationally
        /// uniform instead of biased toward the corners — important for
        /// SMG feel.
        /// </summary>
        private static Vector3 ApplySpread(Vector3 forward, Vector3 right, Vector3 up, float spreadDeg)
        {
            if (spreadDeg <= 0f) return forward;
            float r = Mathf.Tan(spreadDeg * Mathf.Deg2Rad);
            Vector2 disk = Random.insideUnitCircle * r;
            return (forward + right * disk.x + up * disk.y).normalized;
        }
    }
}
