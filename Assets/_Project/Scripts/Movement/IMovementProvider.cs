using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Common interface for any drive component that can move a robot
    /// (e.g. <c>WheelDrive</c>, <c>HoverDrive</c>, <c>JetDrive</c>, <c>LegDrive</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The robot's controller (player or AI) feeds normalised input each
    /// physics tick; the implementation translates that into forces / torques
    /// on the underlying <see cref="Rigidbody"/>.
    /// </para>
    /// <para>
    /// Inputs are intentionally abstract (a 2D move vector + a scalar throttle)
    /// so the same controller can drive a wheeled bot, a hovercraft, or a
    /// quadruped without branching.
    /// </para>
    /// </remarks>
    public interface IMovementProvider
    {
        /// <summary>
        /// Whether this provider is currently able to apply movement
        /// (e.g. wheels not destroyed, fuel not empty, etc.).
        /// </summary>
        bool IsOperational { get; }

        /// <summary>
        /// Apply movement intent for this physics tick.
        /// Called from <c>FixedUpdate</c> on the controlling component.
        /// </summary>
        /// <param name="moveInput">
        /// Normalised planar input. <c>x</c> = strafe/turn, <c>y</c> = forward/back.
        /// </param>
        /// <param name="verticalInput">
        /// Vertical intent in the range [-1, 1] (jump/jet thrust/dive).
        /// Implementations that do not support vertical movement should ignore this.
        /// </param>
        /// <param name="deltaTime">Fixed delta time for this tick.</param>
        void ApplyMovement(Vector2 moveInput, float verticalInput, float deltaTime);
    }
}
