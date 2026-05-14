using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Marker on the generous sphere collider parented under a
    /// <see cref="RopeBlock"/>'s static (build-mode) chain visual,
    /// sitting at the chain's free end. Expands the placement-targeting
    /// hitbox for the rope's tip cell so the player doesn't have to
    /// thread the cursor through the chain cylinder's tiny end cap
    /// (default segment radius ≈ 0.08 m ≈ 16 cm diameter).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Robogame.Gameplay.BlockEditor"/> resolves a hit on
    /// this marker to <c>rope.GridPosition + ChainCellCount × rope.Up</c>
    /// and forces the placement-up to <c>rope.Up</c> — same outcome as
    /// the cylinder-end-cap hit path, just reachable from a much wider
    /// aim cone.
    /// </para>
    /// <para>
    /// Build-mode only. The live verlet path needs precise hit geometry
    /// for grapple contact (hook hits a wall mid-swing should resolve
    /// against the wall, not be eaten by a generous aim sphere), so the
    /// sphere is not spawned in arena.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RopeTipAimTarget : MonoBehaviour
    {
        /// <summary>Back-reference to the rope this aim target belongs
        /// to. Set by <see cref="RopeBlock"/> at spawn time so the
        /// editor doesn't have to re-walk the parent chain.</summary>
        public BlockBehaviour Rope;
    }
}
