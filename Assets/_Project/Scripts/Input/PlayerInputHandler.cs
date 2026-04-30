using UnityEngine;
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
        [SerializeField] private string _fireAction = "Attack";

        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _fire;

        public Vector2 Move => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
        public float Vertical => _jump != null && _jump.IsPressed() ? 1f : 0f;
        public bool FireHeld => _fire != null && _fire.IsPressed();

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
