using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-weapon-block ballistic + damage stats. Referenced from
    /// <see cref="Robogame.Block.BlockDefinition"/> so each weapon kind
    /// (SMG, future plasma / rail / mortar) ships its own asset; the
    /// firing component (<see cref="ProjectileGun"/>) reads from the
    /// asset rather than the global tweakables registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Multiplayer prerequisite: gameplay-observable values (damage,
    /// fire rate, muzzle speed) MUST live in server-authoritative
    /// blueprint data, not in the per-machine Tweakables JSON. See
    /// <c>docs/PHYSICS_PLAN.md</c> § 1.5 / § 5.
    /// </para>
    /// <para>
    /// One asset per weapon kind today. Per-instance overrides (e.g. a
    /// specific weapon block carrying tuned dims) would extend the
    /// blueprint <c>Entry</c> with a dims/stats blob; the resolution
    /// order in the firing component would prefer per-entry overrides
    /// and fall back to this asset.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Weapon Definition", fileName = "Weapon_New", order = 6)]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Tooltip("Shots per second.")]
        [SerializeField, Min(0.1f)] private float _fireRate = 12.0f;

        [Tooltip("Initial projectile speed (m/s).")]
        [SerializeField, Min(1f)] private float _muzzleSpeed = 80.0f;

        [Tooltip("Cone half-angle of dispersion (degrees). 0 = laser-accurate.")]
        [SerializeField, Range(0f, 30f)] private float _spreadDeg = 1.2f;

        [Tooltip("Direct-hit damage (HP). Splash falloff is per-projectile authoring.")]
        [SerializeField, Min(0f)] private float _damage = 25.0f;

        [Tooltip("Newton-seconds of impulse pushed back into the firing chassis. " +
                 "Visible kickback under sustained fire.")]
        [SerializeField, Min(0f)] private float _recoilImpulse = 5.0f;

        [Header("Ammo + reload (Phase 5/6 — SCRAP_LOOP_PLAN)")]
        [Tooltip("Rounds per clip per weapon instance. Total pool size = ClipSize × instances of this weapon type on the chassis.")]
        [SerializeField, Min(1)] private int _clipSize = 30;

        [Tooltip("Seconds the weapon-type pool is locked out during reload. SMGs are fast — 1.5 s default.")]
        [SerializeField, Min(0.1f)] private float _reloadDuration = 1.5f;

        [Tooltip("Brief grace window after firing the last round before the auto-reload kicks in. Lets sustained-fire releases feel clean.")]
        [SerializeField, Min(0f)] private float _autoReloadDelay = 0.3f;

        public float FireRate        => _fireRate;
        public float MuzzleSpeed     => _muzzleSpeed;
        public float SpreadDeg       => _spreadDeg;
        public float Damage          => _damage;
        public float RecoilImpulse   => _recoilImpulse;
        public int ClipSize          => _clipSize;
        public float ReloadDuration  => _reloadDuration;
        public float AutoReloadDelay => _autoReloadDelay;
    }
}
