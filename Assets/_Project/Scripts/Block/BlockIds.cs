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
        // Wing (session 140): the bat-wing aero part. Side-mounted like a
        // foil and rides the same AeroSurfaceBlock lift path, but carries
        // its own authored shape (WingDefaults) and a skinned flap
        // animation that plays in arenas only — see WingFlapAnimator.
        // Foils-vs-Wings philosophy: docs/changes/139 §"Current philosophy".
        public const string Wing       = "block.movement.wing";
        public const string Rudder     = "block.movement.rudder";
        public const string Weapon     = "block.weapon.hitscan";
        public const string BombBay    = "block.weapon.bombbay";
        public const string Cannon     = "block.weapon.cannon";
        // Mortar (session 108): top-mounted indirect-fire weapon. Lobs an
        // explosive shell on a ballistic arc; the launch elevation sits
        // offset above the camera line so the player fires a lob without
        // craning the camera skyward, and a short arc-preview shows the
        // firing angle (not the landing spot). Must mount on a top face
        // (BlockConnectivity top-mount rule). See MortarBlock.
        public const string Mortar     = "block.weapon.mortar";
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
        // Modules (session 105): each Module-category block grants one
        // triggered, cooldown-gated ability surfaced on the in-match ability
        // bar. Identity is the block type — ModuleKinds maps each id ↔ a
        // ModuleKind. A chassis carries up to ModuleBudget.MaxModules of them;
        // each is the destructible carrier whose death disables its slot. The
        // per-instance power rides BlockBehaviour.ConfigValue (trades power for
        // a longer cooldown). See ModuleBlock / ModuleSystem.
        //
        // Spring keeps its original id string (shipped session 104) but is a
        // Module now: a grounded-only impulse launch off its mount face. Id
        // strings never change once shipped, so the "movement" prefix is
        // cosmetic — ModuleKinds.IsModuleId treats it as a module.
        public const string Spring      = "block.movement.spring";
        public const string ModuleEmp   = "block.module.emp";
        public const string ModuleBlink = "block.module.blink";
        public const string ModuleShield = "block.module.shield";
        public const string ModuleSmoke = "block.module.smoke";
        public const string ModuleInvis = "block.module.invis";
        // Mines (session 108): deploys a proximity mine on the ground below
        // the chassis. Arms after a short delay (so it can't pop in your
        // face), then detonates one physics-tick after an enemy bot drives
        // into its trigger radius. No friendly fire; the deployer is immune.
        // Active mines are capped per chassis (oldest trims out). Mine TYPES
        // are a later extension — DeployedMine takes its profile as params.
        public const string ModuleMines = "block.module.mines";
        // Repair (session 116): field self-repair module. Pulses HP back into
        // the chassis's own still-alive blocks within a radius of the carrier —
        // the first in-field healing in the game. (The repair PAD rebuilds
        // DESTROYED blocks at base; this tops up DAMAGED ones mid-fight.) No
        // offense, no enemy/ally effect; trades a slot + CPU for sustain. The
        // per-instance power (ConfigValue) sets HP healed per block. See
        // ModuleEffects.RepairPulse.
        public const string ModuleRepair = "block.module.repair";
        // Wave-1 prototypes (session 155, EA triage docs/research/ea-block-triage.md):
        // Gyro: IDriveSubsystem torque assist — yaw authority + upright
        // stabilization on the single chassis Rigidbody. No physics objects.
        public const string Gyro          = "block.movement.gyro";
        // Pogo: passive raycast spring-damper bouncer. Deliberately NOT the
        // Spring module (one-shot ability launch) — this is repeated, automatic
        // bounce, kin to HoverBlade's non-joint propulsion pattern.
        public const string Pogo          = "block.movement.pogo";
        // Counterweight: dense structural block. Pure BlockDefinition data.
        public const string Counterweight = "block.structure.counterweight";
        // Feather: huge-but-light fragile bulk (renamed from "foam"). Visual
        // oversize is placeholder until scalable-parts Phase 4 lands.
        public const string Feather       = "block.structure.feather";
        // Spike armor: victims ramming into it take boosted ring-0 damage
        // (attacker-bonus reading; defensive discount is a possible follow-up).
        public const string SpikeArmor    = "block.structure.spike";
        // Wedge armor: glancing hits deal reduced damage via ArmorDeflection.
        // Prototype uses the standard box collider's reported hit normal, so
        // the sloped VISUAL and the mechanic can disagree — known tradeoff.
        public const string WedgeArmor    = "block.structure.wedge";
        // Fuse: splash-chain breaker — BlockGrid.ApplySplashDamage never
        // propagates through a live fuse.
        public const string Fuse          = "block.structure.fuse";

        /// <summary>
        /// The single "is this a rope-tip block" predicate (ADR-0008).
        /// Tips are intrinsically code classes (each is a TipBlock
        /// subclass), so the registry lives here next to the id constants
        /// rather than as an SO flag. Adding a tip block: new TipBlock
        /// subclass, new const above, one entry here — every consumer
        /// (graph rope-bridge, placement, connectivity, binders) follows.
        /// GrappleMagnet is deliberately NOT a tip: it is a turret weapon
        /// that fires a tethered projectile.
        /// </summary>
        public static bool IsTipId(string blockId)
            => blockId == Hook || blockId == Mace || blockId == Magnet;
    }
}
