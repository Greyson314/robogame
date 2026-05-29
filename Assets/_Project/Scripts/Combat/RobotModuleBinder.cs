using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Attaches an <see cref="ActiveModuleBlock"/> to any placed
    /// <see cref="BlockCategory.Module"/> block. Added unconditionally by the
    /// assembler (like <c>RobotAeroBinder</c>) — zero per-frame cost when the
    /// chassis carries no module block.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotModuleBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Category == BlockCategory.Module &&
            block.Definition.Id == BlockIds.ActiveModule;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<ActiveModuleBlock>() == null)
                block.gameObject.AddComponent<ActiveModuleBlock>();
        }
    }
}
