using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Lives on a <see cref="BlockBehaviour"/> with id
    /// <see cref="BlockIds.BombBay"/>. While the player holds Fire,
    /// drops gravity bombs from the underside of the block at the
    /// configured drop interval. Bombs fly through
    /// <see cref="ProjectileWorld"/> alongside SMG pellets and cannon
    /// shells — same custom integrator, different damage profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As of session 32 there is no <c>Bomb</c> MonoBehaviour and no
    /// per-bomb Rigidbody. The visual sphere is a pooled
    /// <see cref="ProjectileVisual"/>; gravity is integrated
    /// analytically by the world. Splash radius drives the world's
    /// area-splash damage path, which mirrors the prior
    /// <c>Bomb.DamageRobot</c> quadratic falloff exactly.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class BombBayBlock : MonoBehaviour
    {
        [Header("Drop geometry (block-local)")]
        [Tooltip("Local position of the drop point — should sit on the underside of the block so bombs don't clip the host cube.")]
        [SerializeField] private Vector3 _dropLocalOffset = new Vector3(0f, -0.6f, 0f);

        [Tooltip("Radius of the spawned bomb's sphere visual + cast (m).")]
        [SerializeField, Min(0.05f)] private float _bombRadius = 0.3f;

        [Header("Bomb stats (fallback when no BombDefinition is wired)")]
        [Tooltip("Inline fallback values used when the firing block's BlockDefinition has no BombDefinition. " +
                 "Asset-authored BombDefinitions take precedence at every drop. PHYSICS_PLAN §5: gameplay-" +
                 "observable stats live in server-authoritative blueprint data, NOT in per-machine Tweakables.")]
        [SerializeField, Min(0.05f)] private float _dropInterval = 1.2f;
        [SerializeField, Min(0f)]    private float _damage       = 80f;
        [SerializeField, Min(0.1f)]  private float _radius       = 18f;
        [SerializeField, Min(0f)]    private float _initialSpeed = 2f;

        [Header("Layers")]
        [Tooltip("Layers the bomb's explosion can damage / hit.")]
        [SerializeField] private LayerMask _hitMask = ~0;

        // 8 s lifetime carries a bomb dropped from a high plane down
        // to the terrain across the largest arena (~150 m drop ≈ 5.5 s
        // at planet gravity). Splash fires on terrain hit, so this is
        // an upper bound — a stuck-in-the-air bomb still detonates
        // gracefully.
        private const float BombLifetime = 8f;

        // Iron-bomb tint (near-black with a hint of blue).
        private static readonly Color s_bombTint = new Color(0.10f, 0.10f, 0.12f);

        private float _nextDropTime;
        private IInputSource _input;
        private Robot _ownerRobot;
        private Rigidbody _ownerRb;
        private BlockBehaviour _block;

        public Transform DropPoint { get; private set; }

        private void Awake()
        {
            _input = GetComponentInParent<IInputSource>();
            _ownerRobot = GetComponentInParent<Robot>();
            _ownerRb = _ownerRobot != null ? _ownerRobot.GetComponent<Rigidbody>() : null;
            _block = GetComponent<BlockBehaviour>();

            DropPoint = BlockVisuals.GetOrCreateChild(transform, "DropPoint");
            DropPoint.localPosition = _dropLocalOffset;
        }

        private BombDefinition ResolveDef()
        {
            if (_block == null || _block.Definition == null) return null;
            return _block.Definition.GetComponentData<BombDefinition>();
        }

        private void Update()
        {
            if (_input == null || !_input.FireHeld) return;
            if (Time.time < _nextDropTime) return;

            BombDefinition def = ResolveDef();
            float interval = Mathf.Max(0.05f, def != null ? def.DropInterval : _dropInterval);
            _nextDropTime = Time.time + interval;
            DropOne();
        }

        private void DropOne()
        {
            BombDefinition def = ResolveDef();
            float damage     = def != null ? def.Damage       : _damage;
            float radius     = def != null ? def.Radius       : _radius;
            float startSpeed = def != null ? def.InitialSpeed : _initialSpeed;

            Vector3 dropWorld = DropPoint.position;
            // Chassis-relative "down" so on a planet (where chassis up
            // = away from centre) bombs fall sensibly toward the surface.
            Vector3 down = transform.parent != null
                ? -transform.parent.up
                : Vector3.down;

            Vector3 velocity = down * startSpeed;
            if (_ownerRb != null) velocity += _ownerRb.linearVelocity;

            ProjectileSpec spec = new ProjectileSpec
            {
                Kind = ProjectileKind.Bomb,
                Origin = dropWorld,
                InitialVelocity = velocity,
                // Gravity along chassis-relative down. Adequate today;
                // a planet-aware projectile would re-evaluate gravity
                // each step against the planet centre.
                GravityWorld = down * Physics.gravity.magnitude,
                MaxLifetime = BombLifetime,
                CastRadius = _bombRadius,
                Damage = damage,
                SplashRings = null,
                SplashRadius = radius,
                HitMask = _hitMask,
                Owner = _ownerRobot,
                ShowTrail = false,
                ShowMesh = true,
                VisualTint = s_bombTint,
                VisualMeshDiameter = _bombRadius * 2f,
                ImpactAudioOverride = AudioCue.BombExplosion,
            };
            ProjectileWorld.Spawn(in spec);
        }

        /// <summary>Editor / scaffolder helper — kept for parity with WeaponBlock.</summary>
        public void Bind(WeaponMount mount)
        {
            // Bomb bays don't aim, so the mount reference is unused.
        }
    }
}
