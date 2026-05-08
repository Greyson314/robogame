using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Immutable per-shot parameter blob fed to
    /// <see cref="ProjectileWorld.Spawn"/>. Captures the firing
    /// chassis, ballistic state, damage routing, hit filter, and
    /// visual hints. Built fresh on every fire — not pooled, not
    /// authored as a ScriptableObject; per-weapon stat assets
    /// (<see cref="WeaponDefinition"/> / <see cref="BombDefinition"/> /
    /// <see cref="CannonDefinition"/>) supply the inputs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Damage routing</b> (priority order):
    /// </para>
    /// <list type="number">
    /// <item><see cref="SplashRadius"/> &gt; 0 → area splash from
    ///       hit point, walks every chassis with blocks in the radius,
    ///       quadratic falloff. Bomb-style.</item>
    /// <item><see cref="SplashRings"/> non-null + non-empty → ring
    ///       splash on the contacted block via
    ///       <see cref="Block.BlockGrid.ApplySplashDamage"/>. SMG-style.</item>
    /// <item>Otherwise → direct contact damage of <see cref="Damage"/>
    ///       to the contacted <see cref="Robogame.Core.IDamageable"/>.
    ///       Cannon-style.</item>
    /// </list>
    /// <para>
    /// <b>Self-filter:</b> <see cref="Owner"/>'s collider hierarchy is
    /// captured at fire time and excluded from every hit query for
    /// the projectile's life. No <see cref="Physics.IgnoreCollision"/>
    /// gymnastics, no spawn-overlap edge cases.
    /// </para>
    /// </remarks>
    public struct ProjectileSpec
    {
        public ProjectileKind Kind;

        // Ballistics — caller bakes initial state at fire time.
        public Vector3 Origin;
        public Vector3 InitialVelocity;
        public Vector3 GravityWorld;       // Vector3.zero for SMG; Vector3.down*9.81 for arc weapons (TODO: planet-aware).
        public float MaxLifetime;
        public float CastRadius;            // 0 = ray (cheap); >0 = SphereCast (cannonball / bomb).

        // Damage routing — see remarks for priority order.
        public float Damage;                // direct hit
        public float[] SplashRings;         // ring splash (SMG) — caller may share a static array
        public float SplashRadius;          // area splash (Bomb)
        public LayerMask HitMask;

        // Owner — used to filter own-chassis colliders out of hit queries.
        public Robot Owner;

        // Visual hints. The world picks pooled visuals based on these.
        public bool ShowTrail;              // SMG yes, others no
        public bool ShowMesh;               // bomb / cannonball yes
        public Color VisualTint;
        public float VisualMeshDiameter;    // metres; ignored if ShowMesh is false

        // Audio hint. ProjectileWorld plays this on impact dispatch
        // alongside the kind-specific VFX. Optional; if 0 / None,
        // dispatch falls back to AudioCue.ProjectileImpact for direct
        // hits and AudioCue.BombExplosion for splash radius.
        public Robogame.Core.AudioCue ImpactAudioOverride;
    }
}
