using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches a <see cref="RotorBlock"/> to any placed block whose id
    /// matches <see cref="BlockIds.Rotor"/>. Mirrors
    /// <see cref="RobotRopeBinder"/> exactly — lives on the chassis root,
    /// listens to <see cref="BlockGrid.BlockPlaced"/>, and self-attaches
    /// the per-block behaviour on demand. Idempotent and re-runs on
    /// enable for blocks placed before the binder existed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotRotorBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block != null && block.Definition != null && block.Definition.Id == BlockIds.Rotor;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<RotorBlock>() == null)
            {
                block.gameObject.AddComponent<RotorBlock>();
            }
        }
    }
}
