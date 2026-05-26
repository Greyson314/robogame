using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches a <see cref="HoverBladeBlock"/> to any placed block whose
    /// id matches <see cref="BlockIds.HoverBlade"/>. Mirrors
    /// <see cref="RobotRotorBinder"/> — lives on the chassis root, listens
    /// to <see cref="BlockGrid.BlockPlaced"/>, self-attaches the per-block
    /// behaviour on demand. Idempotent and re-runs on enable for blocks
    /// placed before the binder existed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotHoverBladeBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block != null && block.Definition != null && block.Definition.Id == BlockIds.HoverBlade;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<HoverBladeBlock>() == null)
            {
                block.gameObject.AddComponent<HoverBladeBlock>();
            }
        }
    }
}
