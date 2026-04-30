using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Snapshot of the driver's intent for a single physics step. Passed by
    /// <see cref="RobotDrive"/> to every <see cref="IDriveSubsystem"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>readonly</c> so subsystems cannot mutate input mid-frame. The
    /// raw <see cref="IInputSource"/> values are pre-routed (e.g. swapped
    /// for plane mode) by the aggregator before being snapshotted here.
    /// </para>
    /// <para>
    /// Add new channels to this struct rather than passing extra args to
    /// <see cref="IDriveSubsystem.Tick"/> — that way old subsystems keep
    /// compiling when new control modes are added.
    /// </para>
    /// </remarks>
    public readonly struct DriveControl
    {
        /// <summary>(x = strafe/yaw/roll, y = forward/pitch) in [-1, 1].</summary>
        public readonly Vector2 Move;

        /// <summary>Vertical intent in [-1, 1]: jump / lift / throttle.</summary>
        public readonly float Vertical;

        /// <summary>True while primary fire is held.</summary>
        public readonly bool FireHeld;

        /// <summary>Where the player is aiming, world-space.</summary>
        public readonly Vector3 AimPoint;

        /// <summary>Physics step delta.</summary>
        public readonly float DeltaTime;

        public DriveControl(Vector2 move, float vertical, bool fireHeld, Vector3 aimPoint, float dt)
        {
            Move = move;
            Vertical = vertical;
            FireHeld = fireHeld;
            AimPoint = aimPoint;
            DeltaTime = dt;
        }
    }
}
