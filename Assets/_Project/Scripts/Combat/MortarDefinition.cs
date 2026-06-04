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
    public sealed class MortarDefinition : WeaponStatsDefinition
    {
        [Header("Mortar ballistics")]
        [Tooltip("Seconds between shots while fire is held. Mortars are slow — typical 1.8–2.6 s.")]
        [SerializeField, Min(0.05f)] private float _fireInterval = 2.2f;

        [Tooltip("Muzzle velocity (m/s). Low so the lob is high and readable; range = v²·sin(2θ)/g.")]
        [SerializeField, Min(5f)] private float _muzzleSpeed = 34f;

        [Tooltip("Splash radius (m). The shell detonates on contact and damages every chassis inside.")]
        [SerializeField, Min(0.1f)] private float _splashRadius = 9f;

        [Tooltip("Recoil impulse pushed back into the firing chassis on launch (N·s).")]
        [SerializeField, Min(0f)] private float _recoilImpulse = 22f;

        [Tooltip("Shell sphere visual + cast radius (m).")]
        [SerializeField, Min(0.05f)] private float _shellRadius = 0.3f;

        public override float FireInterval => _fireInterval;
        public float MuzzleSpeed      => _muzzleSpeed;
        public float SplashRadius     => _splashRadius;
        public float RecoilImpulse    => _recoilImpulse;
        public float ShellRadius      => _shellRadius;
    }
}
