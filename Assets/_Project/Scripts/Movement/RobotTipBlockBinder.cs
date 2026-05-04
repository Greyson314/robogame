using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches the right tip-block component (HookBlock, MaceBlock) to
    /// any placed block whose id matches a known tip type. Mirrors the
    /// <see cref="RobotRopeBinder"/> pattern.
    /// </summary>
    /// <remarks>
    /// Adoption onto a rope happens in <see cref="RopeBlock.Build"/> at
    /// game-start — this binder only ensures the component exists on the
    /// block. A tip block placed in the grid without an adjacent rope
    /// is harmless (just sits there, dealing no damage). The contact
    /// callback only fires once <see cref="TipBlock.AttachToHost"/> has
    /// wired in a host segment.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RobotTipBlockBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block)
        {
            if (block == null || block.Definition == null) return false;
            string id = block.Definition.Id;
            return id == BlockIds.Hook || id == BlockIds.Mace;
        }

        protected override void Bind(BlockBehaviour block)
        {
            if (block.Definition.Id == BlockIds.Hook)
            {
                if (block.GetComponent<HookBlock>() == null)
                    block.gameObject.AddComponent<HookBlock>();
            }
            else if (block.Definition.Id == BlockIds.Mace)
            {
                if (block.GetComponent<MaceBlock>() == null)
                    block.gameObject.AddComponent<MaceBlock>();
            }
        }
    }
}
