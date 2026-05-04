using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Robogame.Player
{
    /// <summary>
    /// Free-fly camera used in the garage's Build Mode. Replaces the
    /// chassis-locked <see cref="OrbitCamera"/> with a Robocraft-style
    /// "fly around the bot from any angle" rig — WASD translates,
    /// Q/E (or Space/Ctrl) raise/lower, right-mouse-held + drag
    /// rotates, scroll dolly's forward/back. The chassis grid stays
    /// where it is; only the camera moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads input directly from <see cref="Keyboard.current"/> /
    /// <see cref="Mouse.current"/> rather than going through an
    /// <see cref="UnityEngine.InputSystem.InputAction"/> map, mirroring
    /// <see cref="FollowCamera"/>'s pattern. Avoids editing the
    /// <c>InputSystem_Actions.inputactions</c> JSON, which is fragile
    /// to hand-edit and rebuilds via Unity's editor on every change.
    /// </para>
    /// <para>
    /// Rotation uses cursor-not-locked mode by default (player can still
    /// click hotbar buttons to pick blocks). Holding right mouse drags
    /// the camera; release stops the drag. Identical convention to
    /// <see cref="OrbitCamera"/> so the muscle memory carries over.
    /// </para>
    /// <para>
    /// <see cref="BuildModeController"/> enables this component on
    /// build-mode entry and disables it on exit. The component never
    /// destroys / re-creates itself; cost when disabled is zero.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildFreeCam : MonoBehaviour
    {
        [Header("Translate")]
        [Tooltip("Base movement speed (m/s) for WASD + vertical input.")]
        [SerializeField, Min(0.1f)] private float _moveSpeed = 12f;

        [Tooltip("Multiplier when LeftShift is held — boost for fast traversal.")]
        [SerializeField, Min(1f)] private float _fastMultiplier = 3f;

        [Header("Rotate")]
        [Tooltip("Yaw sensitivity (deg per pixel of mouse delta) while right-mouse is held.")]
        [SerializeField, Min(0f)] private float _yawSensitivity = 0.25f;

        [Tooltip("Pitch sensitivity (deg per pixel of mouse delta) while right-mouse is held.")]
        [SerializeField, Min(0f)] private float _pitchSensitivity = 0.20f;

        [SerializeField, Range(-89f, 89f)] private float _minPitch = -85f;
        [SerializeField, Range(-89f, 89f)] private float _maxPitch =  85f;

        [Header("Scroll dolly")]
        [Tooltip("Metres added to forward distance per scroll-wheel notch.")]
        [SerializeField, Min(0.1f)] private float _scrollDolly = 1.5f;

        // Persistent yaw / pitch — updated each frame from mouse delta.
        private float _yaw;
        private float _pitch;

        private void OnEnable()
        {
            // Capture current orientation so we don't snap-pan on enable.
            Vector3 e = transform.eulerAngles;
            _yaw   = e.y;
            _pitch = NormalisePitch(e.x);
        }

        private static float NormalisePitch(float pitchDeg)
        {
            // Unity stores pitch in [0, 360); fold into [-180, 180] then clamp.
            if (pitchDeg > 180f) pitchDeg -= 360f;
            return pitchDeg;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse m     = Mouse.current;
            if (kb == null && m == null) return;

            float dt = Time.unscaledDeltaTime;

            // -----------------------------------------------------------------
            // Rotate (right-mouse held). UI-aware: skip if cursor is over UI
            // so dragging on a hotbar / panel doesn't also spin the camera.
            // -----------------------------------------------------------------
            if (m != null
                && m.rightButton.isPressed
                && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                Vector2 delta = m.delta.ReadValue();
                _yaw   += delta.x * _yawSensitivity;
                _pitch  = Mathf.Clamp(_pitch - delta.y * _pitchSensitivity, _minPitch, _maxPitch);
            }

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // -----------------------------------------------------------------
            // Translate. WASD = local +Z / -X / -Z / +X. Q/E or
            // Space/LCtrl = world +Y / -Y. LeftShift boosts speed.
            // -----------------------------------------------------------------
            if (kb == null) return;

            Vector3 inLocal = Vector3.zero;
            if (kb.wKey.isPressed) inLocal += Vector3.forward;
            if (kb.sKey.isPressed) inLocal -= Vector3.forward;
            if (kb.dKey.isPressed) inLocal += Vector3.right;
            if (kb.aKey.isPressed) inLocal -= Vector3.right;
            // Vertical: Q/E by default; Space/LCtrl as a secondary binding for
            // players who came from FPS muscle memory.
            float vertical = 0f;
            if (kb.eKey.isPressed || kb.spaceKey.isPressed)    vertical += 1f;
            if (kb.qKey.isPressed || kb.leftCtrlKey.isPressed) vertical -= 1f;

            // Boost.
            float speed = _moveSpeed * (kb.leftShiftKey.isPressed ? _fastMultiplier : 1f);

            // Local-frame movement so W is "into the screen."
            Vector3 worldMove = transform.TransformDirection(inLocal) + Vector3.up * vertical;
            transform.position += worldMove * (speed * dt);

            // -----------------------------------------------------------------
            // Scroll dolly — pushes the camera along its forward axis. Stacks
            // with W/S so the player can dolly while flying.
            // -----------------------------------------------------------------
            if (m != null
                && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                float scroll = m.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    transform.position += transform.forward * (Mathf.Sign(scroll) * _scrollDolly);
                }
            }
        }
    }
}
