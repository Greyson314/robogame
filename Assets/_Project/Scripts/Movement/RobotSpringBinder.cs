using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches a <see cref="SpringBlock"/> to any placed block whose id
    /// matches <see cref="BlockIds.Spring"/>. Mirrors
    /// <see cref="RobotHoverBladeBinder"/> — lives on the chassis root,
    /// listens to <see cref="BlockGrid.BlockPlaced"/>, self-attaches the
    /// per-block behaviour on demand. Idempotent; re-runs on enable for
    /// blocks placed before the binder existed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotSpringBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block != null && block.Definition != null && block.Definition.Id == BlockIds.Spring;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<SpringBlock>() == null)
            {
                block.gameObject.AddComponent<SpringBlock>();
            }
        }
    }
}
