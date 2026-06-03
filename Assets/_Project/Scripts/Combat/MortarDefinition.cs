using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-mortar-block payload + ballistic tuning. Same role as
    /// <see cref="CannonDefinition"/> / <see cref="BombDefinition"/> but
    /// for the lobbed indirect-fire path (<see cref="MortarBlock"/> →
    /// <see cref="ProjectileWorld"/>'s area-splash dispatch).
    /// </summary>
    /// <remarks>
    /// Mortar = top-mounted artillery. A slow-firing shell launched on a
    /// ballistic arc whose elevation is offset above the camera line, so
    /// the player lobs without craning the camera skyward. Explosive
    /// (area splash), so it picks up the explosive-knockback path for free.
    /// Per PHYSICS_PLAN § 5, gameplay-observable stats live here, NOT in
    /// per-machine Tweakables. The aim-feel params (elevation offset /
    /// limits, arc preview) are rig config on the MortarBlock component,
    /// matching where CannonBlock keeps its pitch limits.
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Mortar Definition", fileName = "Mortar_New", order = 9)]
    public sealed class MortarDefinition : ScriptableObject
    {
        [Tooltip("Seconds between shots while fire is held. Mortars are slow — typical 1.8–2.6 s.")]
        [SerializeField, Min(0.05f)] private float _fireInterval = 2.2f;

        [Tooltip("Muzzle velocity (m/s). Low so the lob is high and readable; range = v²·sin(2θ)/g.")]
        [SerializeField, Min(5f)] private float _muzzleSpeed = 34f;

        [Tooltip("Damage at the explosion's centre (HP). Quadratic falloff to the radius edge.")]
        [SerializeField, Min(0f)] private float _damage = 90f;

        [Tooltip("Splash radius (m). The shell detonates on contact and damages every chassis inside.")]
        [SerializeField, Min(0.1f)] private float _splashRadius = 9f;

        [Tooltip("Recoil impulse pushed back into the firing chassis on launch (N·s).")]
        [SerializeField, Min(0f)] private float _recoilImpulse = 22f;

        [Tooltip("Knockback impulse imparted to each caught chassis, pushing AWAY from the blast " +
                 "centre with an upward pop. Scales by distance falloff. Lands instantly.")]
        [SerializeField, Min(0f)] private float _knockbackImpulse = 55f;

        [Tooltip("Shell sphere visual + cast radius (m).")]
        [SerializeField, Min(0.05f)] private float _shellRadius = 0.3f;

        [Header("Ammo + reload (Phase 5/6 — SCRAP_LOOP_PLAN)")]
        [Tooltip("Rounds per clip per mortar. Total pool = ClipSize × mortars on the chassis. Scarce — 5 default.")]
        [SerializeField, Min(1)] private int _clipSize = 5;

        [Tooltip("Seconds the mortar-pool is locked during reload. Long — artillery reload is a commitment.")]
        [SerializeField, Min(0.1f)] private float _reloadDuration = 3.5f;

        [Tooltip("Grace window between firing the last shell and the auto-reload kicking in.")]
        [SerializeField, Min(0f)] private float _autoReloadDelay = 0.3f;

        public float FireInterval     => _fireInterval;
        public float MuzzleSpeed      => _muzzleSpeed;
        public float Damage           => _damage;
        public float SplashRadius     => _splashRadius;
        public float RecoilImpulse    => _recoilImpulse;
        public float KnockbackImpulse => _knockbackImpulse;
        public float ShellRadius      => _shellRadius;
        public int ClipSize           => _clipSize;
        public float ReloadDuration   => _reloadDuration;
        public float AutoReloadDelay  => _autoReloadDelay;
    }
}
