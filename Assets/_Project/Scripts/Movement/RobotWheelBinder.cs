using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches <see cref="WheelBlock"/> behaviour to wheel blocks placed
    /// into the chassis grid, and configures their <see cref="WheelKind"/>
    /// from the block's stable ID.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotWheelBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Category == BlockCategory.Movement &&
            (block.Definition.Id == BlockIds.Wheel ||
             block.Definition.Id == BlockIds.WheelSteer);

        protected override void Bind(BlockBehaviour block)
        {
            WheelKind kind = block.Definition.Id == BlockIds.WheelSteer
                ? WheelKind.Steer
                : WheelKind.Drive;

            WheelBlock wheel = block.GetComponent<WheelBlock>();
            if (wheel == null) wheel = block.gameObject.AddComponent<WheelBlock>();
            wheel.Kind = kind;
        }
    }
}
