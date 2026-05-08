using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-cannon-block payload + ballistic tuning. Same role as
    /// <see cref="WeaponDefinition"/> / <see cref="BombDefinition"/>
    /// but for the cannon path (<see cref="CannonBlock"/> →
    /// <see cref="ProjectileWorld"/>'s direct-hit dispatch).
    /// </summary>
    /// <remarks>
    /// Cannon = pirate-feel slow-firing high-impact gravity projectile.
    /// Direct contact damage only (no splash today; an "explosive
    /// shell" variant could later wire to <see cref="BombDefinition"/>'s
    /// splash path). Per PHYSICS_PLAN § 5, gameplay-observable stats
    /// live here, NOT in per-machine Tweakables.
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Cannon Definition", fileName = "Cannon_New", order = 8)]
    public sealed class CannonDefinition : ScriptableObject
    {
        [Tooltip("Seconds between shots while fire is held. Cannons are slow — typical 0.6–1.2 s.")]
        [SerializeField, Min(0.05f)] private float _fireInterval = 0.85f;

        [Tooltip("Muzzle velocity (m/s). Gravity drops the ball over distance, so a flat shot at 80 m/s arcs ~3 m over 50 m of travel.")]
        [SerializeField, Min(5f)] private float _muzzleSpeed = 80f;

        [Tooltip("Damage on direct contact (HP). Single-target; no splash today.")]
        [SerializeField, Min(0f)] private float _damage = 60f;

        [Tooltip("Cannonball radius (m). Larger reads as 'big iron ball', smaller as 'shotgun pellet'.")]
        [SerializeField, Min(0.05f)] private float _ballRadius = 0.28f;

        [Tooltip("Recoil impulse applied opposite the shot direction (N·s). Pushes the chassis back perceptibly.")]
        [SerializeField, Min(0f)] private float _recoilImpulse = 28f;

        [Tooltip("Cannonball Rigidbody mass (kg). Affects collision response on contact.")]
        [SerializeField, Min(0.1f)] private float _ballMass = 5f;

        public float FireInterval  => _fireInterval;
        public float MuzzleSpeed   => _muzzleSpeed;
        public float Damage        => _damage;
        public float BallRadius    => _ballRadius;
        public float RecoilImpulse => _recoilImpulse;
        public float BallMass      => _ballMass;
    }
}
