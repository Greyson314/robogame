using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-bomb-bay-block payload + drop tuning. Same role as
    /// <see cref="WeaponDefinition"/> but for the gravity-bomb path
    /// (<see cref="BombBayBlock"/> / <see cref="Bomb"/>).
    /// </summary>
    /// <remarks>
    /// Splits the four bomb knobs out of the <c>Tweakables</c> registry
    /// so a chassis with two bomb bays of different yields can ship
    /// without a global slider stomping both. PHYSICS_PLAN § 5.
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Bomb Definition", fileName = "Bomb_New", order = 7)]
    public sealed class BombDefinition : ScriptableObject
    {
        [Tooltip("Seconds between drops while fire is held.")]
        [SerializeField, Min(0.05f)] private float _dropInterval = 1.2f;

        [Tooltip("Damage at the explosion's centre cell (HP). Splash decays via the projectile rings.")]
        [SerializeField, Min(0f)] private float _damage = 80.0f;

        [Tooltip("Splash radius (m).")]
        [SerializeField, Min(0.1f)] private float _radius = 18.0f;

        [Tooltip("Initial downward velocity at drop time (m/s). Adds to chassis velocity.")]
        [SerializeField, Min(0f)] private float _initialSpeed = 2.0f;

        public float DropInterval => _dropInterval;
        public float Damage       => _damage;
        public float Radius       => _radius;
        public float InitialSpeed => _initialSpeed;
    }
}
