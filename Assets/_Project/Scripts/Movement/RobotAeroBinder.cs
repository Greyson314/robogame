using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Attaches <see cref="ThrusterBlock"/> / <see cref="AeroSurfaceBlock"/>
    /// behaviours to placed blocks based on their stable ID.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotAeroBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block) =>
            block.Definition.Category == BlockCategory.Movement &&
            (block.Definition.Id == BlockIds.Thruster ||
             block.Definition.Id == BlockIds.Aero);

        protected override void Bind(BlockBehaviour block)
        {
            switch (block.Definition.Id)
            {
                case BlockIds.Thruster:
                    if (block.GetComponent<ThrusterBlock>() == null)
                        block.gameObject.AddComponent<ThrusterBlock>();
                    break;
                case BlockIds.Aero:
                    if (block.GetComponent<AeroSurfaceBlock>() == null)
                        block.gameObject.AddComponent<AeroSurfaceBlock>();
                    break;
            }
        }
    }
}
