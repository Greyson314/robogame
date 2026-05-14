using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-bomb-bay-block payload + drop tuning. Same role as
    /// <see cref="WeaponDefinition"/> but for the gravity-bomb path
    /// (<see cref="BombBayBlock"/> → <see cref="ProjectileWorld"/>'s
    /// area-splash dispatch).
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

        [Header("Ammo + reload (Phase 5/6 — SCRAP_LOOP_PLAN)")]
        [Tooltip("Rounds per clip per bomb bay. Total pool = ClipSize × bomb bays on the chassis. Bombs are scarce — 4 by default.")]
        [SerializeField, Min(1)] private int _clipSize = 4;

        [Tooltip("Seconds the bomb-bay pool is locked during reload. Long — bomb reload is a commitment.")]
        [SerializeField, Min(0.1f)] private float _reloadDuration = 4.0f;

        [Tooltip("Grace window between firing the last bomb and the auto-reload kicking in.")]
        [SerializeField, Min(0f)] private float _autoReloadDelay = 0.3f;

        public float DropInterval    => _dropInterval;
        public float Damage          => _damage;
        public float Radius          => _radius;
        public float InitialSpeed    => _initialSpeed;
        public int ClipSize          => _clipSize;
        public float ReloadDuration  => _reloadDuration;
        public float AutoReloadDelay => _autoReloadDelay;
    }
}
