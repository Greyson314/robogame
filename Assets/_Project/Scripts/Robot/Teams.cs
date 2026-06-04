namespace Robogame.Robots
{
    /// <summary>
    /// Team-relationship predicates — the single source of truth for "do
    /// these two chassis count as friendlies?" Used by projectile
    /// friendly-fire filtering and AoE/EMP target selection so the rule
    /// can't drift between call sites.
    /// </summary>
    public static class Teams
    {
        // TRACE[AUDIT-15]: one friendly-fire predicate (was forked: ProjectileWorld vs EmpBurst, letting EMP hit teammates)
        /// <summary>
        /// True when an attack from <paramref name="a"/> should be withheld
        /// from <paramref name="b"/>: both chassis alive and on the SAME
        /// non-neutral team. Neutral (<see cref="TeamId.None"/>) chassis —
        /// training dummies, props — are never friendly, so they stay
        /// damageable by everyone.
        /// </summary>
        public static bool IsFriendlyFire(Robot a, Robot b)
            => a != null && b != null && IsFriendlyFire(a.Team, b.Team);

        /// <summary>Team-only overload — the pure rule, no chassis needed.</summary>
        public static bool IsFriendlyFire(TeamId a, TeamId b)
            => a != TeamId.None && b != TeamId.None && a == b;
    }
}
