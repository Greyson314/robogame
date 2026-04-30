using Robogame.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Combat
{
    /// <summary>
    /// Robot-level aim controller. Prefers <see cref="RobotDrive.AimPoint"/>
    /// (single source of truth across drive + weapons), and falls back to a
    /// camera-ray reticle if no drive is present. Exposes an
    /// <see cref="AimPoint"/> that all weapon blocks on the robot converge on.
    /// </summary>
    /// <remarks>
    /// Camera-ray aim works in any chassis orientation (driving / flying /
    /// inverted), unlike the previous ground-plane projection.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponMount : MonoBehaviour
    {
        [Header("Fallback aim (used only if no RobotDrive is present)")]
        [Tooltip("Layers the camera reticle can latch onto.")]
        [SerializeField] private LayerMask _aimMask = ~0;

        [Tooltip("Max aim distance for the camera ray.")]
        [SerializeField, Min(1f)] private float _aimRange = 200f;

        [Tooltip("If the camera ray hits nothing, aim this many metres ahead of it.")]
        [SerializeField, Min(1f)] private float _fallbackRange = 60f;

        [Header("Smoothing")]
        [Tooltip("How quickly the visible mount rotates toward the aim point. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _rotationSpeed = 18f;

        [Tooltip("Camera used for the fallback aim ray. Defaults to Camera.main.")]
        [SerializeField] private Camera _aimCamera;

        private RobotDrive _drive;

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
            _drive = GetComponentInParent<RobotDrive>();
            AimPoint = transform.position + transform.forward * 10f;
        }

        private void LateUpdate()
        {
            if (_drive == null) _drive = GetComponentInParent<RobotDrive>();
            if (_drive != null)
            {
                AimPoint = _drive.AimPoint;
            }
            else
            {
                if (_aimCamera == null) _aimCamera = Camera.main;
                AimPoint = ComputeFallbackAim();
            }
            UpdateRotation();
        }

        private static readonly RaycastHit[] s_aimHits = new RaycastHit[16];

        private Vector3 ComputeFallbackAim()
        {
            if (_aimCamera == null) return transform.position + transform.forward * _fallbackRange;

            Mouse mouse = Mouse.current;
            Vector2 screen = mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Ray ray = _aimCamera.ScreenPointToRay(screen);

            int count = Physics.RaycastNonAlloc(ray, s_aimHits, _aimRange, _aimMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            Vector3 best = ray.origin + ray.direction * _fallbackRange;
            Robots.Robot self = GetComponentInParent<Robots.Robot>();
            for (int i = 0; i < count; i++)
            {
                Robots.Robot hitRobot = s_aimHits[i].collider.GetComponentInParent<Robots.Robot>();
                if (hitRobot != null && hitRobot == self) continue;
                if (s_aimHits[i].distance < bestDist)
                {
                    bestDist = s_aimHits[i].distance;
                    best = s_aimHits[i].point;
                }
            }
            return best;
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
