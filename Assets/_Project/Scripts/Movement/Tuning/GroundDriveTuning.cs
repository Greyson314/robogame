using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tuning profile for <see cref="GroundDriveSubsystem"/>. Authored as a
    /// ScriptableObject so designers can hot-swap chassis feel without
    /// touching scene serialised values.
    /// </summary>
    [CreateAssetMenu(fileName = "GroundDriveTuning", menuName = "Robogame/Tuning/Ground Drive", order = 10)]
    public sealed class GroundDriveTuning : ScriptableObject
    {
        [Header("Drive")]
        public float Acceleration = 26.25f;
        public float MaxSpeed = 13.5f;
        [Tooltip("Yaw acceleration (rad/s²) per unit of turn input.")]
        public float TurnRate = 7.5f;

        [Header("Jump")]
        public float JumpImpulse = 6f;
        public float JumpCooldown = 0.4f;

        [Header("Stability")]
        [Tooltip("Self-righting torque (rad/s²) per radian of tilt away from world-up.")]
        public float UprightStrength = 3f;
        [Tooltip("Damping (rad/s² per rad/s) on roll + pitch rates. Yaw unaffected.")]
        public float RollPitchDamping = 1.5f;
        [Tooltip("Chassis-level lateral grip when ANY wheel is grounded.")]
        [Range(0f, 1f)] public float LateralGrip = 0.85f;
    }
}
