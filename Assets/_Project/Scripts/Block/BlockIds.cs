namespace Robogame.Block
{
    /// <summary>
    /// Single source of truth for stable <see cref="BlockDefinition.Id"/>
    /// strings. Asset wizards write these into authored definitions and
    /// runtime binders read them back to dispatch behaviour.
    /// </summary>
    /// <remarks>
    /// IDs are part of the on-disk + net-serialised contract: NEVER change
    /// a value after a build has shipped. Add new IDs here, don't mutate
    /// existing ones.
    /// </remarks>
    public static class BlockIds
    {
        public const string Cube       = "block.structure.cube";
        public const string Cpu        = "block.cpu.standard";
        public const string Wheel      = "block.movement.wheel";
        public const string WheelSteer = "block.movement.wheel.steer";
        public const string Thruster   = "block.movement.thruster";
        public const string Aero       = "block.movement.aero";
        public const string AeroFin    = "block.movement.aero.fin";
        public const string Rudder     = "block.movement.rudder";
        public const string Weapon     = "block.weapon.hitscan";
        public const string BombBay    = "block.weapon.bombbay";
        public const string Rope       = "block.cosmetic.rope";
        public const string Rotor      = "block.cosmetic.rotor";
        // Tip blocks: adopted by an adjacent RopeBlock at game-start and
        // attached to the rope's tip segment. Dealing contact damage on
        // collision per docs/PHYSICS_PLAN.md §3 (reduced-mass × v_rel² /
        // 2 × dmgPerKj, speed threshold, per-pair cooldown).
        public const string Hook       = "block.weapon.tip.hook";
        public const string Mace       = "block.weapon.tip.mace";
    }
}
