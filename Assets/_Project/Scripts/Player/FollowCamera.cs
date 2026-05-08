using System.Collections.Generic;
using Robogame.Block;
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
    /// <para>
    /// <b>Up axis:</b> by default the orbit basis uses world
    /// <see cref="Vector3.up"/>. On spherical-gravity arenas the chassis's
    /// local up rotates as it travels around the planet; if we keep using
    /// world up the chassis appears tilted on screen everywhere except the
    /// spawn pole, and yawing the camera traces a circle of latitude in
    /// world space rather than around the chassis. Set
    /// <see cref="UpProvider"/> to return local up at any world position
    /// (e.g. <c>-GravityField.SampleAt(pos).normalized</c>) and the orbit
    /// basis follows the chassis cleanly. The provider is called every
    /// LateUpdate; <c>null</c> means "use world up" — the historical
    /// behaviour for the flat arena scenes.
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
        [SerializeField] private float _height = 2.4f;

        [Header("Zoom")]
        [Tooltip("Mouse-scroll zoom multiplier floor. 0.6 = camera can zoom IN to 60% of base distance (40% closer).")]
        [SerializeField, Range(0.4f, 1f)] private float _zoomMin = 0.6f;
        [Tooltip("Mouse-scroll zoom multiplier ceiling. 1.4 = camera can zoom OUT to 140% of base distance (40% farther).")]
        [SerializeField, Range(1f, 2.5f)] private float _zoomMax = 1.4f;
        [Tooltip("How much each scroll-wheel notch shifts the zoom multiplier.")]
        [SerializeField, Min(0.005f)] private float _zoomStep = 0.08f;

        [Header("Aim Down Sights")]
        [Tooltip("Field of view (degrees) while right-mouse is held. Lower = tighter zoom. Mouse sensitivity scales with FoV automatically (see _referenceFov).")]
        [SerializeField, Range(10f, 80f)] private float _adsFov = 35f;

        [Tooltip("Seconds to ease the FoV between hipfire and ADS. 0 = instant snap.")]
        [SerializeField, Range(0f, 0.5f)] private float _adsLerpTime = 0.12f;

        [Tooltip("Hide own-chassis renderers while ADS is active so the bot doesn't obscure the view. Colliders/scripts untouched — only Renderer.enabled toggles.")]
        [SerializeField] private bool _adsHideChassis = true;

        // Live multiplier on _distance. 1 = base, _zoomMin..max bounded.
        // STATIC so the player's chosen zoom level survives chassis
        // respawn (Robot.Rebuilt fires after a crash, the camera's
        // OnEnable runs again, but the static keeps the multiplier).
        // Per CLAUDE.md "Statics survive domain reload, GameObjects
        // don't" — reset on domain reload via the SubsystemRegistration
        // callback below.
        private static float s_distanceMultiplier = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => s_distanceMultiplier = 1f;

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

        [Tooltip("Minimum height above the chassis target the camera is allowed to drop to. " +
                 "Stops a ground-bot's pitch-down aim from pulling the camera underground via " +
                 "the obstacle SphereCast. Set negative to disable.")]
        [SerializeField] private float _minHeightAboveTarget = 0.5f;

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

        /// <summary>
        /// Optional source of the camera's "up" axis. Receives the target's
        /// world position and returns the world-space up vector the orbit
        /// basis should align to. <c>null</c> (default) means world
        /// <see cref="Vector3.up"/>, which is what every flat-arena scene
        /// expects. Spherical-gravity scenes (PlanetArena) wire this to a
        /// gravity-field sample so the camera never sees the chassis tilt
        /// when it crosses latitude lines.
        /// </summary>
        public System.Func<Vector3, Vector3> UpProvider { get; set; }

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

        // ADS state. _baseFov captured at Awake so the inspector value is
        // honoured. _chassisRenderers is rebuilt whenever the target swaps
        // (Robot.Rebuilt) so we don't toggle stale references after respawn.
        private bool _adsActive;
        private bool _chassisHidden;
        private float _baseFov = 60f;
        private float _fovVelocity;
        private Renderer[] _chassisRenderers;
        private Transform _renderersForTarget;

        // Single-element non-alloc buffer — SphereCast doesn't have a
        // NonAlloc overload that returns the *closest* hit cleanly, so we
        // use the simple SphereCast(out hit) variant; nothing to allocate.

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera != null) _baseFov = _camera.fieldOfView;
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

            // Restore chassis visibility + reset ADS so re-enabling (e.g.
            // exiting build mode) starts in hipfire with the bot showing.
            _adsActive = false;
            if (_chassisHidden) RestoreChassisVisibility();
            if (_camera != null) _camera.fieldOfView = _baseFov;
            _fovVelocity = 0f;
        }

        private void RestoreChassisVisibility()
        {
            if (_chassisRenderers != null)
            {
                for (int i = 0; i < _chassisRenderers.Length; i++)
                {
                    Renderer r = _chassisRenderers[i];
                    if (r != null) r.enabled = true;
                }
            }
            _chassisHidden = false;
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
                // Stale renderer refs from the destroyed chassis can't
                // be re-enabled later — drop the cache so the next ADS
                // entry rebuilds against the new BlockGrid.
                _chassisRenderers = null;
                _renderersForTarget = null;
                _chassisHidden = false;
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
            if (!inputActive)
            {
                // Cursor was released mid-ADS (Esc): drop the zoom so we
                // don't stay stuck zoomed while the player navigates UI.
                _adsActive = false;
                return;
            }

            Mouse m = Mouse.current;
            if (m == null) return;

            // Scroll-wheel zoom. Each notch nudges the multiplier by
            // _zoomStep within [_zoomMin, _zoomMax]. Suppress while the
            // cursor is over UI so scrolling a settings list doesn't
            // also zoom the camera.
            float scroll = m.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f
                && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                s_distanceMultiplier = Mathf.Clamp(
                    s_distanceMultiplier - Mathf.Sign(scroll) * _zoomStep,
                    _zoomMin, _zoomMax);
            }

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

            // Aim Down Sights — held while captured and not over UI. Build
            // mode disables this whole component, so no explicit gate needed.
            _adsActive = m.rightButton.isPressed
                && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        }

        private void LateUpdate()
        {
            using var _scope = Robogame.Core.PerfMarkers.FollowCameraLateUpdate.Auto();
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

            Quaternion rot = ComputeOrbitRotation(out Vector3 up);
            Vector3 lookAt = _target.position + up * _height;
            Vector3 desired = ResolveCameraPosition(lookAt, rot);

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref _positionVel,
                _positionSmoothTime,
                _maxFollowSpeed > 0f ? _maxFollowSpeed : Mathf.Infinity,
                dt);
            transform.rotation = rot;

            ApplyAdsFov(dt);
            ApplyChassisVisibility();
        }

        private void ApplyAdsFov(float dt)
        {
            if (_camera == null) return;
            float target = _adsActive ? _adsFov : _baseFov;
            if (_adsLerpTime <= 0f)
            {
                _camera.fieldOfView = target;
                _fovVelocity = 0f;
                return;
            }
            _camera.fieldOfView = Mathf.SmoothDamp(
                _camera.fieldOfView, target, ref _fovVelocity, _adsLerpTime, Mathf.Infinity, dt);
        }

        private void ApplyChassisVisibility()
        {
            bool shouldHide = _adsActive && _adsHideChassis;
            if (shouldHide == _chassisHidden) return;
            EnsureChassisRendererCache();
            if (_chassisRenderers == null) return;
            for (int i = 0; i < _chassisRenderers.Length; i++)
            {
                Renderer r = _chassisRenderers[i];
                if (r != null) r.enabled = !shouldHide;
            }
            _chassisHidden = shouldHide;
        }

        // Build the renderer list from the chassis's BlockGrid so adopted
        // foils (reparented under a kinematic rotor hub at scene root) are
        // captured too — a plain GetComponentsInChildren on the chassis
        // transform would miss them. Cached per-target; invalidated by
        // HandleRobotRebuilt + OnDisable.
        private void EnsureChassisRendererCache()
        {
            if (_target == null) { _chassisRenderers = null; return; }
            if (_renderersForTarget == _target && _chassisRenderers != null) return;

            BlockGrid grid = _target.GetComponentInChildren<BlockGrid>(includeInactive: true);
            if (grid != null)
            {
                List<Renderer> all = new List<Renderer>(grid.Count * 2);
                foreach (var kvp in grid.Blocks)
                {
                    BlockBehaviour b = kvp.Value;
                    if (b == null) continue;
                    all.AddRange(b.GetComponentsInChildren<Renderer>(includeInactive: true));
                }
                _chassisRenderers = all.ToArray();
            }
            else
            {
                _chassisRenderers = _target.GetComponentsInChildren<Renderer>(includeInactive: true);
            }
            _renderersForTarget = _target;
        }

        /// <summary>
        /// Camera spot for the current orbit angles, with sphere-cast
        /// obstacle avoidance so geometry between target and camera pulls
        /// the camera in instead of clipping through walls.
        /// </summary>
        private Vector3 ResolveCameraPosition(Vector3 lookAt, Quaternion rot)
        {
            Vector3 dir = -(rot * Vector3.forward);
            float effectiveDistance = _distance * s_distanceMultiplier;
            Vector3 desired = lookAt + dir * effectiveDistance;

            if (_avoidObstacles && Physics.SphereCast(
                    lookAt,
                    _collisionProbeRadius,
                    dir,
                    out RaycastHit hit,
                    effectiveDistance,
                    _obstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                // Ignore hits on the player's own chassis so we don't pull
                // the camera into our own root.
                if (_target == null || !hit.transform.IsChildOf(_target))
                {
                    float clamped = Mathf.Max(0f, hit.distance - _collisionPadding);
                    desired = lookAt + dir * clamped;
                }
            }

            // Y-floor: never let the camera dip below (target.y +
            // _minHeightAboveTarget). A ground-bot pitching down hard
            // to aim at another ground bot was using the SphereCast's
            // terrain hit to pull the camera through ground; the
            // floor breaks that geometry trap. Set
            // _minHeightAboveTarget to a negative number to disable.
            if (_target != null && _minHeightAboveTarget >= 0f)
            {
                float floorY = _target.position.y + _minHeightAboveTarget;
                if (desired.y < floorY) desired.y = floorY;
            }
            return desired;
        }

        private void SnapToDesired()
        {
            if (_target == null) return;
            Quaternion rot = ComputeOrbitRotation(out Vector3 up);
            Vector3 lookAt = _target.position + up * _height;
            transform.position = ResolveCameraPosition(lookAt, rot);
            transform.rotation = rot;
        }

        /// <summary>
        /// Build the orbit rotation in a basis whose up is
        /// <see cref="UpProvider"/>'s sample (or world up by default).
        /// Equivalent to <c>Quaternion.Euler(pitch, yaw, 0)</c> with the
        /// vertical axis remapped — yaw rotates around <i>local</i> up,
        /// pitch around the local right-vector. <see cref="Quaternion.FromToRotation"/>
        /// gives the shortest-arc realignment from world up to local up,
        /// which is stable for any non-antipodal pair (and the chassis is
        /// never below the planet centre, so antipodal is unreachable).
        /// </summary>
        private Quaternion ComputeOrbitRotation(out Vector3 up)
        {
            up = Vector3.up;
            if (UpProvider != null && _target != null)
            {
                Vector3 sampled = UpProvider(_target.position);
                if (sampled.sqrMagnitude > 0.0001f) up = sampled.normalized;
            }
            Quaternion upAlign = Quaternion.FromToRotation(Vector3.up, up);
            return upAlign * Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);
        }

        /// <summary>
        /// Lock the OS cursor to the centre of the window and re-arm the
        /// per-frame relock guard so a focus drop is auto-recovered. Public
        /// so external systems (post-warmup gameplay resume, settings panel
        /// dismiss) can hand cursor control back to the FollowCamera without
        /// poking <c>Cursor.lockState</c> directly. No-op if
        /// <c>_lockCursor</c> is disabled in the inspector.
        /// </summary>
        public void ApplyCursorLock()
        {
            if (!_lockCursor) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _cursorWasLocked = true;
        }

        /// <summary>
        /// Release the OS cursor and stop the per-frame re-lock guard. Public
        /// so external systems (match-end overlay, settings panel, scripted
        /// cutscenes) can hand control back to the OS without fighting the
        /// FollowCamera's "did focus drop?" relock heuristic in
        /// <see cref="HandleCursorLockHotkey"/>. Without this, simply assigning
        /// <c>Cursor.lockState = None</c> from outside lasts exactly one frame
        /// because the next <see cref="LateUpdate"/> sees
        /// <c>_cursorWasLocked == true</c> and re-locks.
        /// </summary>
        public void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorWasLocked = false;
        }
    }
}
