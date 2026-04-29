using UnityEngine;

namespace Robogame.Input
{
    /// <summary>
    /// Abstract input source for any controller (player, AI, replay, network).
    /// </summary>
    /// <remarks>
    /// The contract is intentionally minimal. Anything driving a robot
    /// should be expressible as: a planar move vector, a vertical scalar,
    /// and a few discrete action flags. AI bots will implement this exactly
    /// the same way as the player.
    /// </remarks>
    public interface IInputSource
    {
        /// <summary>Normalised planar movement. <c>x</c> = strafe/turn, <c>y</c> = forward/back.</summary>
        Vector2 Move { get; }

        /// <summary>Vertical intent in [-1, 1] (jump / jet thrust / dive).</summary>
        float Vertical { get; }

        /// <summary>True while the primary fire button is held.</summary>
        bool FireHeld { get; }
    }
}
