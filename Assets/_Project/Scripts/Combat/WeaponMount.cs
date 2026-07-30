using Robogame.Movement;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Robot-level aim controller. Mirrors <see cref="RobotDrive.AimPoint"/>
    /// (single source of truth across drive + weapons) into an
    /// <see cref="AimPoint"/> that all weapon blocks on the robot converge on.
    /// </summary>
    /// <remarks>
    /// The old camera-ray fallback aim and mount-yaw rotation were removed as
    /// dead code: <c>ChassisAssembler</c> always adds a
    /// <see cref="RobotDrive"/> before binding weapons, and nothing reads the
    /// mount's transform rotation. Turret orientation is owned by
    /// <see cref="TurretYoke"/> (gravity-aware; see ADR-0003).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponMount : MonoBehaviour
    {
        private RobotDrive _drive;

        /// <summary>Latest world-space aim target.</summary>
        public Vector3 AimPoint { get; private set; }

        private void Awake()
        {
            _drive = GetComponentInParent<RobotDrive>();
            AimPoint = transform.position + transform.forward * 10f;
        }

        private void LateUpdate()
        {
            if (_drive == null) _drive = GetComponentInParent<RobotDrive>();
            if (_drive != null) AimPoint = _drive.AimPoint;
            // No drive (shouldn't happen in any current assembly flow):
            // keep the last aim point rather than inventing one.
        }
    }
}
