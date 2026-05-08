namespace Robogame.Combat
{
    /// <summary>
    /// Tags the projectile's "shape" — drives impact VFX / audio
    /// dispatch in <see cref="ProjectileWorld"/> without weapons
    /// having to plumb individual cues. Damage routing (direct vs
    /// ring-splash vs area-splash) is decided by the
    /// <see cref="ProjectileSpec"/> fields, not this enum.
    /// </summary>
    public enum ProjectileKind
    {
        /// <summary>SMG-style fast pellet. Trail visual, no mesh, ring-splash damage on a hit block.</summary>
        SmgPellet,

        /// <summary>Heavy gravity bomb. Sphere mesh visual, area-splash damage with quadratic falloff.</summary>
        Bomb,

        /// <summary>Pirate cannonball. Sphere mesh visual, direct contact damage.</summary>
        Cannonball,
    }
}
