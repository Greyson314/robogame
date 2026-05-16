using Robogame.Block;
using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Attaches a <see cref="DrillBlock"/> to any placed block whose ID
    /// matches <see cref="BlockIds.Drill"/>. Mirrors the
    /// <c>RobotTipBlockBinder</c> pattern in Robogame.Movement.
    /// </summary>
    /// <remarks>
    /// Lives on the chassis root next to the BlockGrid; subscribes to
    /// <see cref="BlockGrid.BlockPlaced"/> and re-binds existing children
    /// on enable so template rebuilds work transparently.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RobotDrillBinder : BlockBinder
    {
        protected override bool ShouldBind(BlockBehaviour block)
        {
            if (block == null || block.Definition == null) return false;
            return block.Definition.Id == BlockIds.Drill;
        }

        protected override void Bind(BlockBehaviour block)
        {
            if (block.GetComponent<DrillBlock>() == null)
                block.gameObject.AddComponent<DrillBlock>();

            // Unity routes OnCollisionStay to the GameObject hosting the
            // Rigidbody — the chassis root, not this child cell. Add a
            // forwarder on the chassis root (where this binder lives) so
            // contacts reach the drill blocks. Idempotent via
            // DisallowMultipleComponent.
            DrillCollisionForwarder forwarder = GetComponent<DrillCollisionForwarder>();
            if (forwarder == null) forwarder = gameObject.AddComponent<DrillCollisionForwarder>();
            forwarder.RefreshDrills();
        }
    }
}
