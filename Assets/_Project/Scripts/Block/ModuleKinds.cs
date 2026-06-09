namespace Robogame.Block
{
    /// <summary>
    /// The id ↔ <see cref="ModuleKind"/> map. There is one Module-category
    /// block type per ability, so a placed module block resolves its ability
    /// from its own <see cref="BlockDefinition.Id"/> — no per-chassis kind
    /// selection. Lives in <c>Robogame.Block</c> (cycle-free) so the assembler,
    /// budget, and serializer can reason about modules without depending on
    /// <c>Robogame.Combat</c>.
    /// </summary>
    public static class ModuleKinds
    {
        /// <summary>True when <paramref name="blockId"/> is a module block.</summary>
        public static bool IsModuleId(string blockId) => blockId switch
        {
            BlockIds.Spring => true,
            BlockIds.ModuleEmp => true,
            BlockIds.ModuleBlink => true,
            BlockIds.ModuleShield => true,
            BlockIds.ModuleSmoke => true,
            BlockIds.ModuleInvis => true,
            BlockIds.ModuleMines => true,
            BlockIds.ModuleRepair => true,
            _ => false,
        };

        /// <summary>
        /// The ability a module block grants, or null if the id is not a
        /// module block.
        /// </summary>
        public static ModuleKind? ForBlockId(string blockId) => blockId switch
        {
            BlockIds.Spring => ModuleKind.Spring,
            BlockIds.ModuleEmp => ModuleKind.EmpBurst,
            BlockIds.ModuleBlink => ModuleKind.SpeedBurst,
            BlockIds.ModuleShield => ModuleKind.DiscShield,
            BlockIds.ModuleSmoke => ModuleKind.Smoke,
            BlockIds.ModuleInvis => ModuleKind.Invisibility,
            BlockIds.ModuleMines => ModuleKind.Mines,
            BlockIds.ModuleRepair => ModuleKind.Repair,
            _ => null,
        };

        /// <summary>The block id that grants <paramref name="kind"/>.</summary>
        public static string BlockIdFor(ModuleKind kind) => kind switch
        {
            ModuleKind.Spring => BlockIds.Spring,
            ModuleKind.EmpBurst => BlockIds.ModuleEmp,
            ModuleKind.SpeedBurst => BlockIds.ModuleBlink,
            ModuleKind.DiscShield => BlockIds.ModuleShield,
            ModuleKind.Smoke => BlockIds.ModuleSmoke,
            ModuleKind.Invisibility => BlockIds.ModuleInvis,
            ModuleKind.Mines => BlockIds.ModuleMines,
            ModuleKind.Repair => BlockIds.ModuleRepair,
            _ => BlockIds.ModuleEmp,
        };

        /// <summary>Short uppercase label for HUD tiles.</summary>
        public static string Label(ModuleKind kind) => kind switch
        {
            ModuleKind.Spring => "SPRING",
            ModuleKind.EmpBurst => "EMP",
            ModuleKind.SpeedBurst => "BOOST",
            ModuleKind.DiscShield => "SHIELD",
            ModuleKind.Smoke => "SMOKE",
            ModuleKind.Invisibility => "CLOAK",
            ModuleKind.Mines => "MINE",
            ModuleKind.Repair => "REPAIR",
            _ => "MODULE",
        };
    }
}
