namespace Robogame.Robots
{
    /// <summary>
    /// Stable team identifier carried by every <see cref="Robot"/>. Drives
    /// friendly-fire filtering on <c>ProjectileWorld</c>, depot faction
    /// gates, and any future per-team rule (objective ownership, repair
    /// access). Distinct from <c>Robogame.Gameplay.MatchSide</c> only
    /// because <c>Robogame.Robots</c> is a lower asmdef tier than
    /// <c>Robogame.Gameplay</c> — the two enums share numeric values so
    /// <see cref="ArenaController"/> can cast between them with no
    /// translation table.
    /// </summary>
    public enum TeamId : byte
    {
        /// <summary>Neutral: training dummies, environment props, anything that should be damageable by everyone.</summary>
        None = 0,

        /// <summary>The local player's team.</summary>
        Player = 1,

        /// <summary>The opposing team.</summary>
        Enemy = 2,
    }
}
