using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tuning profile for <see cref="PlaneControlSubsystem"/> + the chassis
    /// <see cref="RobotDrive"/> damping/COM that aircraft want.
    /// </summary>
    [CreateAssetMenu(fileName = "PlaneControlTuning", menuName = "Robogame/Tuning/Plane Control", order = 11)]
    public sealed class PlaneControlTuning : ScriptableObject
    {
        [Header("Authority (rad/s²)")]
        public float PitchPower = 3.2f;
        public float RollPower = 4.5f;
        [Tooltip("Yaw acceleration per unit of bank tilt.")]
        public float YawFromBank = 1.4f;

        [Header("Damping (rad/s² per rad/s)")]
        public float PitchDamping = 2.6f;
        public float RollDamping = 2.6f;
        public float YawDamping = 1.4f;
    }
}
