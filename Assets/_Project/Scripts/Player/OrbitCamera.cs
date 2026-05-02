using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Robogame.Player
{
    /// <summary>
    /// Mouse-orbit camera intended for the Garage Build Mode. Right-mouse
    /// drag rotates around <see cref="Target"/>, scroll-wheel zooms,
    /// middle-mouse drag pans the look-at point. No cursor lock — the
    /// cursor stays free so the player can interact with the build hotbar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sibling to <see cref="FollowCamera"/>: only one of the two should
    /// be enabled at a time. The build-mode controller flips them.
    /// </para>
    /// <para>
    /// Keeps a separate yaw/pitch from FollowCamera so the player's
    /// in-arena aim isn't perturbed by the build-mode camera framing.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;

        [Header("Orbit")]
        [Tooltip("Distance from the target (camera radius) — clamped between Min and Max distance below.")]
        [SerializeField, Min(1f)] private float _distance = 8f;

        [Tooltip("Vertical offset from the target's pivot to the look-at point.")]
        [SerializeField] private float _height = 0.5f;

        [Tooltip("Initial pitch in degrees (positive = looking down).")]
        [SerializeField, Range(-89f, 89f)] private float _initialPitch = 22f;

        [Tooltip("Initial yaw in degrees relative to world.")]
        [SerializeField] private float _initialYaw = 35f;

        [Header("Zoom")]
        [SerializeField, Min(1f)] private float _minDistance = 3f;
        [SerializeField, Min(1f)] private float _maxDistance = 20f;
        [SerializeField, Min(0.05f)] private float _zoomStep = 0.6f;

        [Header("Sensitivity")]
        [Tooltip("Yaw degrees per pixel of right-drag.")]
        [SerializeField, Min(0f)] private float _yawSensitivity = 0.25f;
        [Tooltip("Pitch degrees per pixel of right-drag.")]
        [SerializeField, Min(0f)] private float _pitchSensitivity = 0.20f;

        [SerializeField, Range(-89f, 89f)] private float _minPitch = -20f;
        [SerializeField, Range(-89f, 89f)] private float _maxPitch =  85f;

        [Header("Pan")]
        [Tooltip("Middle-drag panning speed per pixel of mouse delta.")]
        [SerializeField, Min(0f)] private float _panSensitivity = 0.01f;

        [Tooltip("Maximum lateral offset of the pivot from Target. " +
                 "0 disables panning — the camera always looks at Target.")]
        [SerializeField, Min(0f)] private float _maxPanRadius = 4f;

        [Header("Smoothing")]
        [SerializeField, Min(0f)] private float _smoothTime = 0.05f;

        public Transform Target { get => _target; set => _target = value; }

        private float _yaw;
        private float _pitch;
        private Vector3 _panOffset;
        private Vector3 _positionVel;

        private void OnEnable()
        {
            _yaw = _initialYaw;
            _pitch = _initialPitch;
            _panOffset = Vector3.zero;
            _positionVel = Vector3.zero;
            SnapToDesired();
        }

        /// <summary>Reset orbit framing to the initial values. Called when entering build mode.</summary>
        public void RecenterOnTarget()
        {
            _yaw = _initialYaw;
            _pitch = _initialPitch;
            _panOffset = Vector3.zero;
            SnapToDesired();
        }

        private void Update()
        {
            Mouse m = Mouse.current;
            if (m == null) return;

            // If the cursor is over a UI element, only consume scroll if the
            // pointer happens to be on a non-interactive element. We always
            // suppress drag-rotate / drag-pan over UI to avoid the camera
            // spinning while the player drags a HUD slider.
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            Vector2 delta = m.delta.ReadValue();

            if (!overUI && m.rightButton.isPressed)
            {
                _yaw   += delta.x * _yawSensitivity;
                _pitch  = Mathf.Clamp(_pitch - delta.y * _pitchSensitivity, _minPitch, _maxPitch);
            }

            if (!overUI && m.middleButton.isPressed && _maxPanRadius > 0f)
            {
                // Pan in screen-axis space, projected through the camera's
                // current basis so dragging "right" feels right regardless
                // of yaw.
                Vector3 right = transform.right;
                Vector3 up    = transform.up;
                _panOffset += (-right * delta.x + -up * delta.y) * _panSensitivity * _distance;
                if (_panOffset.magnitude > _maxPanRadius)
                    _panOffset = _panOffset.normalized * _maxPanRadius;
            }

            float scroll = m.scroll.ReadValue().y;
            if (!overUI && Mathf.Abs(scroll) > 0.01f)
            {
                // Mouse scroll arrives in 120-unit "ticks" on Windows; normalise.
                _distance = Mathf.Clamp(_distance - Mathf.Sign(scroll) * _zoomStep, _minDistance, _maxDistance);
            }
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 lookAt = _target.position + Vector3.up * _height + _panOffset;
            Vector3 desired = lookAt - rot * Vector3.forward * _distance;

            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref _positionVel, _smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.rotation = rot;
        }

        private void SnapToDesired()
        {
            if (_target == null) return;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 lookAt = _target.position + Vector3.up * _height + _panOffset;
            transform.position = lookAt - rot * Vector3.forward * _distance;
            transform.rotation = rot;
        }
    }
}
