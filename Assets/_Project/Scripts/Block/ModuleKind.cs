namespace Robogame.Block
{
    /// <summary>
    /// The active ability a chassis's <see cref="BlockIds.ActiveModule"/>
    /// block performs when the player triggers it. One garage-chosen value
    /// per chassis, frozen onto the blueprint at match start (invariant #2).
    /// </summary>
    /// <remarks>
    /// Lives in <c>Robogame.Block</c> (not <c>Robogame.Combat</c>) so
    /// <see cref="ChassisBlueprint"/> can carry the selection without an
    /// asmdef cycle — see the <see cref="BlockDefinition.ComponentData"/>
    /// note on the Block ↛ Combat edge. The combat-side effect dispatch
    /// (<c>ModuleEffects</c>) references this from the other direction.
    /// </remarks>
    public enum ModuleKind
    {
        /// <summary>Burst that briefly disables enemy weapon blocks in a radius.</summary>
        EmpBurst = 0,

        /// <summary>Instant short-range teleport along the chassis facing.</summary>
        Blink = 1,

        /// <summary>Transient bubble that blocks incoming projectiles for a few seconds.</summary>
        DiscShield = 2,
    }
}
