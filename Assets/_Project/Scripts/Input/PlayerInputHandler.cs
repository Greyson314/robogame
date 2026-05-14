using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Robogame.Input
{
    /// <summary>
    /// Reads from a Unity Input System <see cref="InputActionAsset"/> and
    /// exposes its values via the engine-agnostic <see cref="IInputSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drag the project's <c>InputSystem_Actions.inputactions</c> asset into
    /// the inspector field, set the action map name (default: "Player") and
    /// action names. Action lookups are resolved once in <c>OnEnable</c>.
    /// </para>
    /// <para>
    /// This component is the ONLY place that should reference the
    /// Unity Input System — gameplay code talks to <see cref="IInputSource"/>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerInputHandler : MonoBehaviour, IInputSource
    {
        [Header("Input Asset")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Action Map / Names")]
        [SerializeField] private string _actionMap = "Player";
        [SerializeField] private string _moveAction = "Move";
        [SerializeField] private string _lookAction = "Look";
        [SerializeField] private string _jumpAction = "Jump";
        [SerializeField] private string _descendAction = "Sprint";
        [SerializeField] private string _fireAction = "Attack";

        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _descend;
        private InputAction _fire;

        public Vector2 Move => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
        public float Vertical
        {
            get
            {
                float up = (_jump != null && _jump.IsPressed()) ? 1f : 0f;
                float down = (_descend != null && _descend.IsPressed()) ? 1f : 0f;
                return up - down;
            }
        }
        public bool FireHeld
        {
            get
            {
                if (_fire == null || !_fire.IsPressed()) return false;
                // IMGUI/UGUI buttons don't auto-block Input System actions.
                // Suppress fire while the cursor is over a UI element so HUD
                // clicks (e.g. "Launch") don't also trigger a weapon shot.
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return false;
                return true;
            }
        }

        /// <summary>
        /// True for the one frame the fire button transitions
        /// up→down. Edge-triggered companion to <see cref="FireHeld"/>;
        /// single-shot weapons (grapple magnet) consume this so the
        /// player can't double-fire by holding the button.
        /// </summary>
        public bool FirePressed
        {
            get
            {
                if (_fire == null || !_fire.WasPressedThisFrame()) return false;
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return false;
                return true;
            }
        }

        /// <summary>
        /// True only on the frame the player presses R. Reads the
        /// keyboard device directly (Reload isn't a binding in the project
        /// InputActionAsset — adding it would require asset edits). Same
        /// HUD-pointer suppression as fire so an R press in a focused
        /// text field doesn't reload mid-typing.
        /// </summary>
        public bool ReloadPressed
        {
            get
            {
                Keyboard kb = Keyboard.current;
                if (kb == null) return false;
                if (!kb.rKey.wasPressedThisFrame) return false;
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return false;
                return true;
            }
        }

        private void OnEnable()
        {
            if (_actions == null)
            {
                Debug.LogError("[Robogame] PlayerInputHandler has no InputActionAsset assigned.", this);
                return;
            }

            var map = _actions.FindActionMap(_actionMap, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError($"[Robogame] Action map '{_actionMap}' not found.", this);
                return;
            }

            _move = map.FindAction(_moveAction, throwIfNotFound: false);
            _look = map.FindAction(_lookAction, throwIfNotFound: false);
            _jump = map.FindAction(_jumpAction, throwIfNotFound: false);
            _descend = map.FindAction(_descendAction, throwIfNotFound: false);
            _fire = map.FindAction(_fireAction, throwIfNotFound: false);

            map.Enable();
        }

        private void OnDisable()
        {
            if (_actions == null) return;
            _actions.FindActionMap(_actionMap, throwIfNotFound: false)?.Disable();
        }
    }
}
