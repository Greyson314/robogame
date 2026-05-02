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
             block.Definition.Id == BlockIds.Aero ||
             block.Definition.Id == BlockIds.AeroFin ||
             block.Definition.Id == BlockIds.Rudder);

        protected override void Bind(BlockBehaviour block)
        {
            switch (block.Definition.Id)
            {
                case BlockIds.Thruster:
                    if (block.GetComponent<ThrusterBlock>() == null)
                        block.gameObject.AddComponent<ThrusterBlock>();
                    break;
                case BlockIds.Aero:
                {
                    AeroSurfaceBlock aero = block.GetComponent<AeroSurfaceBlock>();
                    if (aero == null) aero = block.gameObject.AddComponent<AeroSurfaceBlock>();
                    aero.Vertical = false;
                    break;
                }
                case BlockIds.AeroFin:
                {
                    AeroSurfaceBlock fin = block.GetComponent<AeroSurfaceBlock>();
                    if (fin == null) fin = block.gameObject.AddComponent<AeroSurfaceBlock>();
                    fin.Vertical = true;
                    break;
                }
                case BlockIds.Rudder:
                    if (block.GetComponent<RudderBlock>() == null)
                        block.gameObject.AddComponent<RudderBlock>();
                    break;
            }
        }
    }
}
