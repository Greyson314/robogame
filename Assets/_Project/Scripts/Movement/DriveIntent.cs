using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// The pilot's six-DOF demand for one physics step, each in [-1, 1].
    /// Filled once per tick by <see cref="RobotDrive"/> from the raw axes
    /// through the chassis' <see cref="ControlScheme"/>; every block then
    /// serves the demands it can physically affect at its own position
    /// (a tail foil serves pitch, a side wing roll, a forward prop surge,
    /// a lift rotor heave). Blocks never read keys.
    /// </summary>
    /// <remarks>
    /// Sign conventions (chassis frame, right-handed, +Z forward, +Y up):
    /// <list type="bullet">
    ///   <item><description><see cref="Surge"/> +1 = forward, <see cref="Sway"/> +1 = right, <see cref="Heave"/> +1 = up.</description></item>
    ///   <item><description><see cref="Pitch"/> +1 = nose UP (a negative rotation about +X).</description></item>
    ///   <item><description><see cref="Roll"/> +1 = bank RIGHT (a negative rotation about +Z).</description></item>
    ///   <item><description><see cref="Yaw"/> +1 = nose RIGHT (a positive rotation about +Y).</description></item>
    /// </list>
    /// </remarks>
    // TRACE[ADR-0009]: intent layer — keys are interpreted exactly once, here.
    public readonly struct DriveIntent
    {
        public readonly float Surge;
        public readonly float Sway;
        public readonly float Heave;
        public readonly float Pitch;
        public readonly float Roll;
        public readonly float Yaw;

        public DriveIntent(float surge, float sway, float heave, float pitch, float roll, float yaw)
        {
            Surge = Mathf.Clamp(surge, -1f, 1f);
            Sway  = Mathf.Clamp(sway,  -1f, 1f);
            Heave = Mathf.Clamp(heave, -1f, 1f);
            Pitch = Mathf.Clamp(pitch, -1f, 1f);
            Roll  = Mathf.Clamp(roll,  -1f, 1f);
            Yaw   = Mathf.Clamp(yaw,   -1f, 1f);
        }

        public static DriveIntent Zero => default;

        /// <summary>True when any rotational demand is non-zero (cheap early-out for surfaces).</summary>
        public bool HasRotation => Pitch != 0f || Roll != 0f || Yaw != 0f;

        /// <summary>
        /// Map the three raw player axes onto the six demands for
        /// <paramref name="scheme"/>. <see cref="ControlScheme.Auto"/> is
        /// treated as Ground — callers resolve Auto before reaching here.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item><description>Ground: W/S surge, A/D yaw, Space/Shift heave (jump / hover lift).</description></item>
        ///   <item><description>Plane: W/S surge (throttle), Space/Shift pitch, A/D roll + yaw (coordinated turn: ailerons and any rudder together).</description></item>
        ///   <item><description>Helicopter: Space/Shift heave (collective), W/S pitch (W = nose forward, i.e. nose DOWN), A/D yaw.</description></item>
        /// </list>
        /// </remarks>
        public static DriveIntent FromScheme(ControlScheme scheme, Vector2 move, float vertical)
        {
            switch (scheme)
            {
                case ControlScheme.Plane:
                    return new DriveIntent(surge: move.y, sway: 0f, heave: 0f,
                                           pitch: vertical, roll: move.x, yaw: move.x);
                case ControlScheme.Helicopter:
                    return new DriveIntent(surge: 0f, sway: 0f, heave: vertical,
                                           pitch: -move.y, roll: 0f, yaw: move.x);
                default:
                    return new DriveIntent(surge: move.y, sway: 0f, heave: vertical,
                                           pitch: 0f, roll: 0f, yaw: move.x);
            }
        }
    }
}
