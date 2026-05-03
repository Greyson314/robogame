using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Replaces <c>Rigidbody.useGravity</c>'s flat <c>-Y</c> pull with a
    /// per-FixedUpdate sample of <see cref="GravityField"/>. Add this
    /// component to anything that should fall toward the planet — the
    /// player chassis, AI dummies, debris.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Compatibility note for v1:</b> existing
    /// <see cref="Robogame.Movement.WheelBlock"/> and
    /// <see cref="Robogame.Movement.GroundDriveSubsystem"/> still raycast
    /// and torque against world <c>Vector3.up</c>. So a chassis spawned
    /// at the planet's north pole drives normally on the polar cap, but
    /// once it crosses ~30° latitude the wheels stop finding ground (their
    /// <c>Vector3.down</c> raycast no longer points at the surface) and
    /// the self-righting torque fights the gravity vector instead of
    /// helping. Full spherical locomotion is the substitution work tracked
    /// as Phase A/B in
    /// [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md).
    /// This component is the gravity half of that system, ready to be
    /// joined by the locomotion half.
    /// </para>
    /// <para>
    /// Optional <c>_alignToGravity</c> applies a gentle torque rotating
    /// the rigidbody's <c>up</c> toward local-up. Disabled by default for
    /// v1 because <c>GroundDriveSubsystem</c> already applies a
    /// self-righting torque toward <c>Vector3.up</c> and we don't want
    /// them fighting until the locomotion substitution lands.
    /// </para>
    /// <para>
    /// <b>File layout:</b> separated from <see cref="PlanetBody"/> +
    /// <see cref="GravityField"/> because Unity needs each
    /// <see cref="MonoBehaviour"/> in a file matching the class name for
    /// the serializer to resolve a script GUID — combining them caused
    /// AddComponent to silently drop the component at save time.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlanetGravityBody : MonoBehaviour
    {
        [Tooltip("Apply a torque rotating the rigidbody's up toward local-up. " +
                 "Off by default in v1 — see class XML docs for the rationale.")]
        [SerializeField] private bool _alignToGravity = false;

        [Tooltip("Strength of the alignment torque when _alignToGravity is on. " +
                 "Acceleration units (rad/s² at unit angle).")]
        [SerializeField, Min(0f)] private float _alignStrength = 6f;

        private Rigidbody _rb;
        private bool _hadDefaultUseGravity;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _hadDefaultUseGravity = _rb.useGravity;
            // We own the gravity now — if we leave Rigidbody.useGravity
            // on, PhysX double-counts the pull (its flat -Y plus our sphere
            // sample), and the chassis falls at 2g on the polar cap.
            _rb.useGravity = false;
        }

        private void OnDestroy()
        {
            // Defensive restore so a non-planet scene loaded after this one
            // doesn't inherit useGravity = false on a recycled rigidbody.
            if (_rb != null) _rb.useGravity = _hadDefaultUseGravity;
        }

        private void FixedUpdate()
        {
            Vector3 g = GravityField.SampleAt(_rb.position);
            _rb.AddForce(g, ForceMode.Acceleration);

            if (_alignToGravity && g.sqrMagnitude > 0.0001f)
            {
                Vector3 localUp = -g.normalized;
                Vector3 axis = Vector3.Cross(transform.up, localUp);
                float sin = axis.magnitude;
                if (sin > 0.0001f)
                {
                    float angle = Mathf.Asin(Mathf.Clamp(sin, -1f, 1f));
                    Vector3 torque = (axis / sin) * (angle * _alignStrength);
                    _rb.AddTorque(torque, ForceMode.Acceleration);
                }
            }
        }
    }
}
