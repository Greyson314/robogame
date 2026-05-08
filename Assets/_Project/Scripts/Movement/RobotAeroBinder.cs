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
                    // Mount-aware geometry: top/bottom mounts (up=±Y) get
                    // the horizontal-wing treatment (span along chassis ±X);
                    // side / front / back mounts get the vertical-fin
                    // treatment so the wing visually extends OUTWARD along
                    // the mount normal instead of degenerating into a
                    // sideways-hanging sheet. Lift physics also flips to
                    // the vertical-fin path for non-±Y mounts — that's a
                    // visual-first fix; symmetric lift across faces is a
                    // Phase 1.c follow-up. Default Plane / Helicopter
                    // blueprints all author wings as up=+Y so they're
                    // unaffected.
                    Vector3Int mountUp = block.Up;
                    bool sideMount = mountUp != Vector3Int.up && mountUp != Vector3Int.down;
                    aero.Vertical = sideMount;
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
