using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tuning profile for the chassis-level <see cref="RobotDrive"/>:
    /// rigidbody damping + centre-of-mass offset. Drives feel-defining
    /// stuff that differs per chassis archetype (ground / plane / hover).
    /// </summary>
    [CreateAssetMenu(fileName = "ChassisTuning", menuName = "Robogame/Tuning/Chassis", order = 9)]
    public sealed class ChassisTuning : ScriptableObject
    {
        [Tooltip("Centre-of-mass offset in chassis-local space.")]
        public Vector3 CenterOfMassOffset = new Vector3(0f, -0.5f, 0f);

        public float LinearDamping = 0.2f;
        public float AngularDamping = 2f;
    }
}
