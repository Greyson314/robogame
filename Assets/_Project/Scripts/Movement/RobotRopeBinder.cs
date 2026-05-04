using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches a <see cref="RopeBlock"/> to any placed block whose id
    /// matches <see cref="BlockIds.Rope"/>. Mirrors the
    /// <see cref="RobotAeroBinder"/> / <see cref="RobotWheelBinder"/>
    /// pattern: lives on the chassis root, listens to
    /// <see cref="BlockGrid.BlockPlaced"/>, and self-attaches the
    /// per-block behaviour on demand. Idempotent and re-runs on enable
    /// for blocks placed before the binder existed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotRopeBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block != null && block.Definition != null && block.Definition.Id == BlockIds.Rope;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<RopeBlock>() == null)
            {
                block.gameObject.AddComponent<RopeBlock>();
            }
        }
    }
}
