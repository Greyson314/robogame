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
        public float PitchPower = 7.5f;
        public float RollPower = 9.0f;
        [Tooltip("Yaw acceleration per unit of bank tilt.")]
        public float YawFromBank = 2.0f;

        [Header("Damping (rad/s² per rad/s)")]
        public float PitchDamping = 3.5f;
        public float RollDamping = 2.8f;
        public float YawDamping = 1.6f;
    }
}
