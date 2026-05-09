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
                case BlockIds.AeroFin:
                {
                    AeroSurfaceBlock aero = block.GetComponent<AeroSurfaceBlock>();
                    if (aero == null) aero = block.gameObject.AddComponent<AeroSurfaceBlock>();
                    // Single rule for every mount face: span extends along
                    // the mount normal (foil-local +Y), thickness along
                    // foil-local +X, chord along foil-local +Z. Lift acts
                    // along foil-local +X (the "right" axis) so it's
                    // perpendicular to both span and chord — gives sensible
                    // per-mount lift: side wing → vertical lift, top stab
                    // → lateral yaw force, canard → pitch force, etc.
                    // Vertical=true is what enables both behaviours.
                    aero.Vertical = true;
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
