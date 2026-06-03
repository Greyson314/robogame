namespace Robogame.Block
{
    /// <summary>
    /// The active ability a Module-category block grants. Identity is derived
    /// from the block's own <see cref="BlockDefinition.Id"/> via
    /// <see cref="ModuleKinds.ForBlockId"/> — there is one module block type
    /// per kind, so what abilities a chassis has is simply which module blocks
    /// it carries. The set + per-block power are frozen at match start
    /// (invariant #2). A chassis may carry up to
    /// <see cref="ModuleBudget.MaxModules"/> modules.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Robogame.Block</c> (not <c>Robogame.Combat</c>) so the
    /// block layer (<see cref="BlockIds"/>, <see cref="ModuleKinds"/>) can map
    /// ids ↔ kinds without an asmdef cycle — see the
    /// <see cref="BlockDefinition.ComponentData"/> note on the Block ↛ Combat
    /// edge. The combat-side effect dispatch (<c>ModuleEffects</c>,
    /// <c>ModuleSystem</c>) references this from the other direction.
    /// Enum values are append-only.
    /// </remarks>
    public enum ModuleKind
    {
        /// <summary>Burst that briefly disables enemy weapon blocks in a radius.</summary>
        EmpBurst = 0,

        /// <summary>Instant short-range teleport along the chassis facing.</summary>
        Blink = 1,

        /// <summary>Transient bubble that blocks incoming projectiles for a few seconds.</summary>
        DiscShield = 2,

        /// <summary>Grounded-only impulse launch off the spring's mount face (a jump / dash).</summary>
        Spring = 3,

        /// <summary>Deploys a smoke cloud that obscures the bot and hides its healthbar.</summary>
        Smoke = 4,

        /// <summary>Fades the bot to near-invisible and hides its healthbar until a timer or 5% HP damage.</summary>
        Invisibility = 5,

        /// <summary>Deploys a proximity mine on the ground that detonates when an enemy bot drives over it.</summary>
        Mines = 6,
    }
}
