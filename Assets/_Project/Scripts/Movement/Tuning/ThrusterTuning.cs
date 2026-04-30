using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tuning profile for <see cref="ThrusterBlock"/> instances. One asset
    /// can be shared across every thruster on a class of chassis.
    /// </summary>
    [CreateAssetMenu(fileName = "ThrusterTuning", menuName = "Robogame/Tuning/Thruster", order = 12)]
    public sealed class ThrusterTuning : ScriptableObject
    {
        [Tooltip("Maximum forward force (N) at full throttle.")]
        public float MaxThrust = 360f;

        [Tooltip("Idle throttle when no input is being applied. 0 = off, 1 = full.")]
        [Range(0f, 1f)] public float IdleThrottle = 0.5f;

        [Tooltip("How quickly throttle slews to its target value (per second). 0 = instant.")]
        public float ThrottleResponse = 2.2f;
    }
}
