using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches <see cref="GyroBlock"/> behaviour to gyro blocks placed
    /// into the chassis grid. Same shape as <see cref="RobotWheelBinder"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotGyroBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Id == BlockIds.Gyro;

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<GyroBlock>() == null)
                block.gameObject.AddComponent<GyroBlock>();
        }
    }
}
