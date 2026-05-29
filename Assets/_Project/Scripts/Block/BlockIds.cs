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
        public const string Cannon     = "block.weapon.cannon";
        public const string Rope       = "block.cosmetic.rope";
        public const string Rotor      = "block.cosmetic.rotor";
        // Tip blocks: adopted by an adjacent RopeBlock at game-start and
        // attached to the rope's tip segment. Dealing contact damage on
        // collision per docs/subsystems/physics.md §3 (reduced-mass × v_rel² /
        // 2 × dmgPerKj, speed threshold, per-pair cooldown).
        public const string Hook       = "block.weapon.tip.hook";
        public const string Mace       = "block.weapon.tip.mace";
        // Magnet (session 59): pull-field tip block. Adopted onto a
        // rope tip like Hook/Mace, but instead of grappling or hitting
        // hard, it continuously yanks Rigidbodies in a sphere toward
        // itself. Crowd control / utility tool — see MagnetBlock.cs.
        public const string Magnet     = "block.weapon.tip.magnet";
        // Grapple magnet (session 61): single-shot fire-and-retract
        // weapon. Fires a rope+magnet projectile up to 24 m; on enemy
        // contact it latches and the player can drag the target around
        // via the rope. Misses retract instantly. Standalone weapon
        // block — NOT a tip block (the tip is spawned as a transient
        // child of the block, not a grid cell).
        public const string GrappleMagnet = "block.weapon.grapple_magnet";
        // Drill (Phase 3b): a tool block that carves voxel dig zones via
        // CapsuleSubtract brush ops on contact. Not a TipBlock — directly
        // mounted on the chassis grid. See Robogame.Voxel.DrillBlock and
        // TERRAFORMING_PLAN §12 Phase 3 for the design.
        public const string Drill      = "block.tool.drill";
        // Hover blade (session 99): raycast-based spring-damper hover
        // propulsion. Scalable footprint N×N×1 cells (N ∈ {2,3,4}) via
        // the variable-dim pattern. Applies vertical lift along the
        // gravity vector when within range of ground; clamped ≥ 0 so it
        // can't propel above target altitude. First non-joint propulsion
        // block — see docs/subsystems/physics.md §6.
        public const string HoverBlade = "block.movement.hoverblade";
        // Spring (session 104): a jump block. Mounted on a chassis face
        // (typically the underside), it fires a cooldown-gated impulse on the
        // jump input, shoving the chassis off the surface its outward face
        // braces against — an underside spring jumps the bot up. Shares the
        // reusable SpringSolver math with the hover blade. See SpringBlock.
        public const string Spring     = "block.movement.spring";
        // Active module (session 101): a Module-category block that grants
        // one garage-chosen keybind ability (EMP / Blink / Disc Shield).
        // The chassis-wide choice rides ChassisBlueprint.ActiveModuleKind;
        // the block itself is the destructible carrier — destroy it and the
        // ability goes dark (functional disable). See ActiveModuleBlock.
        public const string ActiveModule = "block.module.active";
    }
}
