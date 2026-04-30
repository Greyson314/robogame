using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Combat
{
    /// <summary>
    /// Robot-level aim controller. Reads the mouse position, raycasts onto a
    /// horizontal aim plane through the mount, and exposes an
    /// <see cref="AimPoint"/> that all weapon blocks on the robot converge on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Twin-stick / Robocraft-style: cursor on ground = "shoot here". Falls
    /// back to "straight ahead" if no mouse is present (gamepad / headless).
    /// </para>
    /// <para>
    /// The mount itself is a single transform — the visible barrel/yaw
    /// gimbal is rotated each frame to face <see cref="AimPoint"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponMount : MonoBehaviour
    {
        [Header("Aim plane")]
        [Tooltip("Vertical offset of the fallback aim plane above the mount (world units).")]
        [SerializeField] private float _aimPlaneHeight = 0f;

        [Tooltip("If the mouse ray hits nothing, aim this many metres ahead of the camera.")]
        [SerializeField, Min(1f)] private float _fallbackRange = 60f;

        [Header("World pick")]
        [Tooltip("Layers the cursor can latch onto for true 3D aim (enemy blocks, terrain, etc.).")]
        [SerializeField] private LayerMask _aimMask = ~0;

        [Tooltip("Max distance the cursor can pick world geometry from.")]
        [SerializeField, Min(1f)] private float _aimRange = 200f;

        [Header("Smoothing")]
        [Tooltip("How quickly the visible mount rotates toward the aim point. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _rotationSpeed = 18f;

        [Tooltip("Camera used to project the mouse cursor. Defaults to Camera.main.")]
        [SerializeField] private Camera _aimCamera;

        /// <summary>Latest world-space aim target.</summary>
        public Vector3 AimPoint { get; private set; }

        /// <summary>Unit vector from the mount toward <see cref="AimPoint"/>.</summary>
        public Vector3 AimDirection
        {
            get
            {
                Vector3 d = AimPoint - transform.position;
                return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
            }
        }

        private void Awake()
        {
            if (_aimCamera == null) _aimCamera = Camera.main;
            AimPoint = transform.position + transform.forward * 10f;
        }

        private void LateUpdate()
        {
            if (_aimCamera == null) _aimCamera = Camera.main;
            UpdateAimPoint();
            UpdateRotation();
        }

        private void UpdateAimPoint()
        {
            if (_aimCamera == null) return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                AimPoint = transform.position + transform.forward * _fallbackRange;
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            Ray ray = _aimCamera.ScreenPointToRay(screen);

            // 1) Try a real world raycast first so the cursor latches onto
            //    enemy blocks / props at the correct elevation.
            if (Physics.Raycast(ray, out RaycastHit hit, _aimRange, _aimMask, QueryTriggerInteraction.Ignore))
            {
                // Skip our own colliders so the cursor doesn't snap to our chassis.
                Robots.Robot self = GetComponentInParent<Robots.Robot>();
                Robots.Robot hitRobot = hit.collider.GetComponentInParent<Robots.Robot>();
                if (hitRobot == null || hitRobot != self)
                {
                    AimPoint = hit.point;
                    return;
                }
            }

            // 2) Fall back to a horizontal aim plane at mount height.
            Plane plane = new Plane(Vector3.up, new Vector3(0f, transform.position.y + _aimPlaneHeight, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                AimPoint = ray.GetPoint(enter);
            }
            else
            {
                AimPoint = ray.origin + ray.direction * _fallbackRange;
            }
        }

        private void UpdateRotation()
        {
            Vector3 dir = AimPoint - transform.position;
            // Project onto horizontal so we don't tilt the mount weirdly.
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = _rotationSpeed <= 0f
                ? target
                : Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-_rotationSpeed * Time.deltaTime));
        }
    }
}
