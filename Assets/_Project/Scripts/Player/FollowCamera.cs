using Robogame.Robots;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Robogame.Player
{
    /// <summary>
    /// Mouse-orbit chase camera. The camera sits at a fixed distance from
    /// <see cref="Target"/>; the player's mouse delta drives yaw and pitch
    /// around the target so the camera doubles as the aim controller —
    /// wherever the screen-centre reticle points is the world-space aim
    /// direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Best-practice notes baked in:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>Rotation is 1:1 with input</b> (no rotational smoothing) — the reticle must track the mouse exactly or the player feels rubber-banded.</description></item>
    ///   <item><description><b>Mouse runs on <see cref="Time.unscaledDeltaTime"/></b> so pause / slow-mo don't change aim feel.</description></item>
    ///   <item><description><b>Position uses <see cref="Vector3.SmoothDamp"/></b> — handles target acceleration smoothly without a pop on enable.</description></item>
    ///   <item><description><b>Sphere-cast obstacle avoidance</b> pulls the camera in when geometry is between the target and the desired camera spot, so we never clip and the aim ray's origin stays usable.</description></item>
    ///   <item><description><b>Sensitivity scales with FOV</b> so changing the camera FOV doesn't change the player's effective look speed.</description></item>
    /// </list>
    /// <para>
    /// All chassis (cars, planes, …) use this same rig. The camera does not
    /// roll with the chassis; the player frames their own view with the
    /// mouse, matching <see cref="Movement.RobotDrive.AimPoint"/>'s
    /// screen-centre ray.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;

        [Header("Orbit")]
        [Tooltip("Distance from the target (camera radius).")]
        // Restored to the original 18m pull-back. A previous tuning pass
        // dropped this to 9m to frame the new shell-grass ground better,
        // but the closer rig made dogfights and incoming projectiles
        // hard to read. 18m gives the chassis breathing room and lets
        // the player spot pellets in flight (which now matters — see
        // ProjectileGun's projectile travel time).
        [SerializeField, Min(1f)] private float _distance = 18f;

        [Tooltip("Vertical offset from the target's pivot to the look-at point.")]
        [SerializeField] private float _height = 1f;

        [Tooltip("Initial pitch in degrees (positive = looking down at the target).")]
        [SerializeField, Range(-89f, 89f)] private float _initialPitch = 18f;

        [Header("Sensitivity")]
        [Tooltip("Mouse yaw sensitivity, degrees per pixel of mouse delta at the reference FOV.")]
        [SerializeField, Min(0f)] private float _yawSensitivity = 0.18f;

        [Tooltip("Mouse pitch sensitivity, degrees per pixel of mouse delta at the reference FOV.")]
        [SerializeField, Min(0f)] private float _pitchSensitivity = 0.14f;

        [Tooltip("Reference FOV the sensitivity values are tuned for. Sensitivity scales linearly with FOV / reference.")]
        [SerializeField, Range(20f, 120f)] private float _referenceFov = 60f;

        [SerializeField, Range(-89f, 89f)] private float _minPitch = -35f;
        [SerializeField, Range(-89f, 89f)] private float _maxPitch =  75f;

        [Tooltip("If true, invert vertical mouse for pitch.")]
        [SerializeField] private bool _invertY = false;

        [Tooltip("Optional input smoothing window in seconds. 0 = raw 1:1 input (recommended). Higher values feel cinematic but add latency.")]
        [SerializeField, Range(0f, 0.1f)] private float _inputSmoothing = 0f;

        [Header("Position smoothing")]
        [Tooltip("How long the camera takes to catch up to the target's position (lower = snappier).")]
        [SerializeField, Min(0f)] private float _positionSmoothTime = 0.06f;

        [Tooltip("Cap on the camera's catch-up speed (m/s). 0 = uncapped.")]
        [SerializeField, Min(0f)] private float _maxFollowSpeed = 0f;

        [Header("Collision")]
        [Tooltip("Push the camera in when geometry is between the look-at point and the desired camera position.")]
        [SerializeField] private bool _avoidObstacles = true;

        [Tooltip("Radius of the sphere-cast used for obstacle checks.")]
        [SerializeField, Min(0.05f)] private float _collisionProbeRadius = 0.25f;

        [Tooltip("Extra clearance kept between the camera and any wall it pulls into.")]
        [SerializeField, Min(0f)] private float _collisionPadding = 0.2f;

        [Tooltip("Layers considered obstacles. Default: everything except IgnoreRaycast.")]
        [SerializeField] private LayerMask _obstacleMask = ~(1 << 2);

        [Header("Rebinding")]
        [Tooltip("If true, auto-rebind the target when a Robot with the matching name is rebuilt.")]
        [SerializeField] private bool _autoRebindOnRebuild = true;

        [Header("Cursor")]
        [Tooltip("Lock and hide the OS cursor so mouse delta drives the orbit cleanly. " +
                 "When CaptureOnClick is true, locking happens on the first left-click " +
                 "inside the game view and releases on Escape — never on Play start. " +
                 "This prevents the editor's Game view from resizing when entering Play, " +
                 "which would otherwise re-flow IMGUI / dev HUD layouts.")]
        [SerializeField] private bool _lockCursor = true;

        [Tooltip("If true (recommended), only lock the cursor after the player clicks. " +
                 "Press Escape to release. If false, lock immediately when this component enables.")]
        [SerializeField] private bool _captureOnClick = true;

        public Transform Target { get => _target; set => _target = value; }

        // Persistent orbit state. Yaw/pitch are the *committed* angles; the
        // smoothed versions are what the camera actually renders at when
        // input smoothing is non-zero.
        private float _yaw;
        private float _pitch;
        private float _smoothYaw;
        private float _smoothPitch;
        private float _yawVel;
        private float _pitchVel;

        private Vector3 _positionVel;
        private Camera _camera;
        private string _targetName;
        private bool _cursorWasLocked;

        // Single-element non-alloc buffer — SphereCast doesn't have a
        // NonAlloc overload that returns the *closest* hit cleanly, so we
        // use the simple SphereCast(out hit) variant; nothing to allocate.

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            _targetName = _target != null ? _target.name : null;
            _yaw = _target != null ? _target.eulerAngles.y : 0f;
            _pitch = _initialPitch;
            _smoothYaw = _yaw;
            _smoothPitch = _pitch;
            _yawVel = _pitchVel = 0f;
            _positionVel = Vector3.zero;

            // Snap the transform on enable so we never lurch from the origin.
            SnapToDesired();

            // Click-to-capture by default — only auto-lock when explicitly
            // configured. Auto-locking on Play tends to make the editor's
            // Game view re-acquire its rendering surface, which shifts
            // Screen.width/height mid-frame and breaks IMGUI layout.
            if (_lockCursor && !_captureOnClick) ApplyCursorLock();
            if (_autoRebindOnRebuild) Robot.Rebuilt += HandleRobotRebuilt;
        }

        private void OnDisable()
        {
            if (_autoRebindOnRebuild) Robot.Rebuilt -= HandleRobotRebuilt;
            // Only release the cursor if WE locked it, so we don't fight
            // other systems (UI screens, debug menus) that own the cursor.
            if (_cursorWasLocked) ReleaseCursor();
        }

        private void HandleRobotRebuilt(Robot robot)
        {
            if (robot == null) return;
            if (string.IsNullOrEmpty(_targetName) || robot.name == _targetName)
            {
                _target = robot.transform;
                _targetName = robot.name;
                // Reseed yaw to the new chassis facing so we don't snap-pan.
                _yaw = robot.transform.eulerAngles.y;
                _smoothYaw = _yaw;
                SnapToDesired();
            }
        }

        private void Update()
        {
            // --- Cursor capture / release ---
            if (_lockCursor)
            {
                Mouse mouse = Mouse.current;
                Keyboard kb = Keyboard.current;

                // Click-to-capture: lock when the player left-clicks inside
                // a focused game view. Skip when the click landed on a UI
                // element so HUD buttons (e.g. "Launch") still work.
                if (_captureOnClick
                    && !_cursorWasLocked
                    && Application.isFocused
                    && mouse != null
                    && mouse.leftButton.wasPressedThisFrame
                    && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                {
                    ApplyCursorLock();
                }

                // Escape releases (matches every modern shooter).
                if (_cursorWasLocked
                    && kb != null
                    && kb.escapeKey.wasPressedThisFrame)
                {
                    ReleaseCursor();
                }

                // Re-apply lock if focus changed while captured (Unity
                // sometimes drops it on alt-tab).
                if (_cursorWasLocked
                    && Cursor.lockState != CursorLockMode.Locked
                    && Application.isFocused)
                {
                    ApplyCursorLock();
                }
            }

            // --- Mouse-driven orbit (only while captured, or if not using lock at all) ---
            bool inputActive = !_lockCursor || _cursorWasLocked;
            if (!inputActive) return;

            Mouse m = Mouse.current;
            if (m == null) return;

            Vector2 delta = m.delta.ReadValue();

            // Mouse delta is already a per-frame pixel count, so we do NOT
            // multiply by deltaTime. We DO scale by FOV so changing zoom
            // doesn't change the player's effective look speed.
            float fovScale = _camera != null && _camera.fieldOfView > 0.01f
                ? _camera.fieldOfView / _referenceFov
                : 1f;

            _yaw   += delta.x * _yawSensitivity   * fovScale;
            float pitchDelta = delta.y * _pitchSensitivity * fovScale * (_invertY ? 1f : -1f);
            _pitch  = Mathf.Clamp(_pitch + pitchDelta, _minPitch, _maxPitch);
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // Optional input smoothing — uses unscaled time so pause/slow-mo
            // doesn't change aim feel. With _inputSmoothing == 0 this is a
            // no-op and rotation tracks the mouse 1:1.
            float dt = Time.unscaledDeltaTime;
            if (_inputSmoothing > 0f)
            {
                _smoothYaw   = Mathf.SmoothDampAngle(_smoothYaw,   _yaw,   ref _yawVel,   _inputSmoothing, Mathf.Infinity, dt);
                _smoothPitch = Mathf.SmoothDamp     (_smoothPitch, _pitch, ref _pitchVel, _inputSmoothing, Mathf.Infinity, dt);
            }
            else
            {
                _smoothYaw = _yaw;
                _smoothPitch = _pitch;
                _yawVel = _pitchVel = 0f;
            }

            Quaternion rot = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);
            Vector3 lookAt = _target.position + Vector3.up * _height;
            Vector3 desired = ResolveCameraPosition(lookAt, rot);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref _positionVel,
                _positionSmoothTime,
                _maxFollowSpeed > 0f ? _maxFollowSpeed : Mathf.Infinity,
                dt);
            transform.rotation = rot;
        }

        /// <summary>
        /// Camera spot for the current orbit angles, with sphere-cast
        /// obstacle avoidance so geometry between target and camera pulls
        /// the camera in instead of clipping through walls.
        /// </summary>
        private Vector3 ResolveCameraPosition(Vector3 lookAt, Quaternion rot)
        {
            Vector3 dir = -(rot * Vector3.forward);
            Vector3 desired = lookAt + dir * _distance;

            if (!_avoidObstacles) return desired;

            if (Physics.SphereCast(
                    lookAt,
                    _collisionProbeRadius,
                    dir,
                    out RaycastHit hit,
                    _distance,
                    _obstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                // Ignore hits on the player's own chassis so we don't pull
                // the camera into our own root.
                if (_target != null && hit.transform.IsChildOf(_target))
                    return desired;

                float clamped = Mathf.Max(0f, hit.distance - _collisionPadding);
                return lookAt + dir * clamped;
            }
            return desired;
        }

        private void SnapToDesired()
        {
            if (_target == null) return;
            Quaternion rot = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);
            Vector3 lookAt = _target.position + Vector3.up * _height;
            transform.position = ResolveCameraPosition(lookAt, rot);
            transform.rotation = rot;
        }

        private void ApplyCursorLock()
        {
            if (!_lockCursor) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _cursorWasLocked = true;
        }

        private void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorWasLocked = false;
        }
    }
}
