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
    /// <c>docs/subsystems/physics.md</c> § 1.5 / § 5.
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
    public sealed class WeaponDefinition : WeaponStatsDefinition
    {
        [Header("SMG ballistics")]
        [Tooltip("Shots per second.")]
        [SerializeField, Min(0.1f)] private float _fireRate = 12.0f;

        [Tooltip("Initial projectile speed (m/s).")]
        [SerializeField, Min(1f)] private float _muzzleSpeed = 80.0f;

        [Tooltip("Cone half-angle of dispersion (degrees). 0 = laser-accurate.")]
        [SerializeField, Range(0f, 30f)] private float _spreadDeg = 1.2f;

        [Tooltip("Newton-seconds of impulse pushed back into the firing chassis. " +
                 "Visible kickback under sustained fire.")]
        [SerializeField, Min(0f)] private float _recoilImpulse = 5.0f;

        // Overheat only (session 155, spin-up reverted by playtest): fire
        // rate is constant; sustained fire trips a lockout.
        [Header("Overheat (session 155)")]
        [Tooltip("Seconds of unbroken fire before overheat lockout. Heat cools at the same rate " +
                 "while the trigger is released, so feathering is the skill expression. 0 = no overheat.")]
        [SerializeField, Min(0f)] private float _overheatSeconds = 4f;

        [Tooltip("Lockout duration after an overheat trip. Holding the trigger neither extends nor shortens it.")]
        [SerializeField, Min(0.1f)] private float _overheatCooldownSeconds = 2.5f;

        // SMG is the one weapon authored as a fire-RATE; the canonical
        // FireInterval is its reciprocal. _fireRate is the shipped field
        // name — do not rename (ADR-0003 phase A note).
        public override float FireInterval => 1f / _fireRate;
        public float FireRate        => _fireRate;
        public float MuzzleSpeed     => _muzzleSpeed;
        public float SpreadDeg       => _spreadDeg;
        public float RecoilImpulse   => _recoilImpulse;

        public bool  HasOverheat              => _overheatSeconds > 0f;
        public float OverheatSeconds          => _overheatSeconds;
        public float OverheatCooldownSeconds  => _overheatCooldownSeconds;
    }
}
