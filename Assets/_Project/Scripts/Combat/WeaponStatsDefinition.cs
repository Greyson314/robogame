using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Base for the per-weapon-block stat assets (ADR-0003 phase A). Holds the
    /// fields every weapon shares — damage, knockback, and the ammo/reload
    /// triple — so adding a weapon stops re-declaring them. The concrete
    /// definitions (<see cref="WeaponDefinition"/>, <see cref="CannonDefinition"/>,
    /// <see cref="MortarDefinition"/>, <see cref="BombDefinition"/>) inherit it
    /// and add their kind-specific fields + the <see cref="FireInterval"/> map.
    /// </summary>
    /// <remarks>
    /// <b>Serialization.</b> Unity serializes fields by name across the whole
    /// type hierarchy (flat), so moving these fields up from the concrete
    /// classes keeps the exact YAML keys (<c>_damage</c>, <c>_clipSize</c>, …)
    /// and authored assets round-trip unchanged. The field names here MUST stay
    /// identical to the pre-refactor concrete declarations — do not rename.
    /// </remarks>
    public abstract class WeaponStatsDefinition : ScriptableObject, IWeaponStats
    {
        [Header("Damage + knockback")]
        [Tooltip("Headline damage (HP). Direct hit for single-target weapons; explosion centre for splash.")]
        [SerializeField, Min(0f)] protected float _damage = 25f;

        [Tooltip("Newton-seconds of impulse imparted to the TARGET along the hit direction. Keep small on rapid-fire weapons — it accumulates.")]
        [SerializeField, Min(0f)] protected float _knockbackImpulse = 3f;

        [Header("Ammo + reload (Phase 5/6 — SCRAP_LOOP_PLAN)")]
        [Tooltip("Rounds per clip per weapon instance. Total pool = ClipSize × instances of this weapon type on the chassis.")]
        [SerializeField, Min(1)] protected int _clipSize = 10;

        [Tooltip("Seconds the weapon-type pool is locked during reload.")]
        [SerializeField, Min(0.1f)] protected float _reloadDuration = 1.5f;

        [Tooltip("Grace window after firing the last round before the auto-reload kicks in.")]
        [SerializeField, Min(0f)] protected float _autoReloadDelay = 0.3f;

        [Header("Visual model (session 120 — stylized turret pack)")]
        [Tooltip("Optional turret model prefab. When set, the weapon block instantiates it instead " +
                 "of its procedural primitive barrel: a child named 'Turret' becomes the pitch yoke and " +
                 "a child named 'ShootPoint' becomes the muzzle. Null → fall back to the procedural rig.")]
        [SerializeField] private GameObject _turretModel;

        [Tooltip("Uniform scale applied to the turret model so it fits a ~1 m block cell.")]
        [SerializeField, Min(0.01f)] private float _turretModelScale = 0.35f;

        [Tooltip("Local position offset for the turret model relative to the block cell centre.")]
        [SerializeField] private Vector3 _turretModelOffset = new Vector3(0f, -0.45f, 0f);

        /// <summary>Seconds between shots while fire is held. Each weapon kind
        /// maps its own authored field — SMG a fire-rate, cannon/mortar a fire-
        /// interval, bomb a drop-interval — onto this canonical seconds value.</summary>
        public abstract float FireInterval { get; }

        public float Damage => _damage;
        public float KnockbackImpulse => _knockbackImpulse;
        public GameObject TurretModel => _turretModel;
        public float TurretModelScale => _turretModelScale;
        public Vector3 TurretModelOffset => _turretModelOffset;
        public int ClipSize => _clipSize;
        public float ReloadDuration => _reloadDuration;
        public float AutoReloadDelay => _autoReloadDelay;
    }
}
