using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches <see cref="PogoBlock"/> behaviour to pogo blocks placed
    /// into the chassis grid.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotPogoBinder : SingleBlockBinder<PogoBlock>
    {
        protected override string BlockId => BlockIds.Pogo;
    }
}
