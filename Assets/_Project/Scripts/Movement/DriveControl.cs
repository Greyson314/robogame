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

        /// <summary>
        /// Scalar in [0..1] applied to drive-force output (ground accel,
        /// thruster thrust). Used by the carry-weight penalty so a chassis
        /// hauling scrap moves slower. Direction / torque channels are
        /// untouched — only force magnitude scales. Default 1 (no penalty).
        /// </summary>
        public readonly float SpeedMultiplier;

        /// <summary>
        /// The pilot's six-DOF demand for this step, produced from
        /// <see cref="Move"/> / <see cref="Vertical"/> through the chassis'
        /// control scheme by <see cref="RobotDrive"/>. New consumers read
        /// THIS, not the raw axes (ADR-0009); <see cref="Move"/> /
        /// <see cref="Vertical"/> stay for the not-yet-migrated drives.
        /// </summary>
        public readonly DriveIntent Intent;

        public DriveControl(Vector2 move, float vertical, bool fireHeld, Vector3 aimPoint, float dt, float speedMultiplier = 1f)
            : this(move, vertical, DriveIntent.Zero, fireHeld, aimPoint, dt, speedMultiplier) { }

        public DriveControl(Vector2 move, float vertical, DriveIntent intent, bool fireHeld, Vector3 aimPoint, float dt, float speedMultiplier = 1f)
        {
            Move = move;
            Vertical = vertical;
            Intent = intent;
            FireHeld = fireHeld;
            AimPoint = aimPoint;
            DeltaTime = dt;
            SpeedMultiplier = speedMultiplier > 0f ? speedMultiplier : 1f;
        }
    }
}
