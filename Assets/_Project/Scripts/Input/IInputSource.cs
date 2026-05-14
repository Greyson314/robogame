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

        /// <summary>Mouse delta or right-stick look. <c>x</c> = yaw, <c>y</c> = pitch.</summary>
        Vector2 Look { get; }

        /// <summary>Vertical intent in [-1, 1] (jump / jet thrust / dive).</summary>
        float Vertical { get; }

        /// <summary>True while the primary fire button is held.</summary>
        bool FireHeld { get; }

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// fire button. Edge-triggered companion to <see cref="FireHeld"/>.
        /// Consumed by single-shot weapons whose firing cadence is
        /// player-pace rather than a fire-rate timer — notably the
        /// grapple magnet (fire once → wait for retract → fire again).
        /// Bots: stub to false until they author single-shot logic.
        /// </summary>
        bool FirePressed { get; }

        /// <summary>
        /// True for exactly one tick on the frame the player pressed the
        /// reload key. Consumed by <c>WeaponAmmoState</c> to start a
        /// manual reload on every non-full weapon pool. Bots return false
        /// — they rely on auto-reload-on-empty.
        /// </summary>
        bool ReloadPressed { get; }
    }
}
