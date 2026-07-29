using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches <see cref="PogoBlock"/> behaviour to pogo blocks placed
    /// into the chassis grid. Same shape as <see cref="RobotWheelBinder"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotPogoBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Id == BlockIds.Pogo;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<PogoBlock>() == null)
                block.gameObject.AddComponent<PogoBlock>();
        }
    }
}
