namespace Robogame.Core
{
    /// <summary>
    /// Anything that can be damaged in gameplay (a block, a shield, a destructible prop).
    /// </summary>
    /// <remarks>
    /// Lives in <see cref="Robogame.Core"/> so any module can implement or call it
    /// without pulling in Combat or Block.
    /// </remarks>
    public interface IDamageable
    {
        /// <summary>True until <see cref="CurrentHealth"/> reaches zero.</summary>
        bool IsAlive { get; }

        /// <summary>Current hit points.</summary>
        float CurrentHealth { get; }

        /// <summary>Apply <paramref name="amount"/> damage. Returns the actual damage dealt.</summary>
        float TakeDamage(float amount);
    }
}
