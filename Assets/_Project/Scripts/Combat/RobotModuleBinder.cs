using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Attaches a <see cref="ModuleBlock"/> to any placed module block (every
    /// id <see cref="ModuleKinds.IsModuleId"/> recognises, including the
    /// spring). Added unconditionally by the assembler (like
    /// <c>RobotAeroBinder</c>) — zero per-frame cost when the chassis carries
    /// no module block.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotModuleBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block != null && block.Definition != null &&
            ModuleKinds.IsModuleId(block.Definition.Id);

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<ModuleBlock>() == null)
                block.gameObject.AddComponent<ModuleBlock>();
        }
    }
}
