using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches <see cref="GyroBlock"/> behaviour to gyro blocks placed
    /// into the chassis grid.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotGyroBinder : SingleBlockBinder<GyroBlock>
    {
        protected override string BlockId => BlockIds.Gyro;
    }
}
