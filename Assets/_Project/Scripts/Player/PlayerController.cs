using Robogame.Input;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Glues an <see cref="IInputSource"/> to an <see cref="IMovementProvider"/>
    /// each physics tick.
    /// </summary>
    /// <remarks>
    /// References are resolved either via inspector assignment or, if left
    /// blank, via <c>GetComponent</c> on the same GameObject. This keeps the
    /// dependency explicit while staying easy to wire up in the editor.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Dependencies (auto-resolved if blank)")]
        [SerializeField] private MonoBehaviour _inputSourceBehaviour;   // must implement IInputSource
        [SerializeField] private MonoBehaviour _movementProviderBehaviour; // must implement IMovementProvider

        private IInputSource _input;
        private IMovementProvider _movement;

        private void Awake()
        {
            _input = ResolveDependency<IInputSource>(_inputSourceBehaviour);
            _movement = ResolveDependency<IMovementProvider>(_movementProviderBehaviour);

            if (_input == null) Debug.LogError("[Robogame] PlayerController: no IInputSource found.", this);
            if (_movement == null) Debug.LogError("[Robogame] PlayerController: no IMovementProvider found.", this);
        }

        private void FixedUpdate()
        {
            if (_input == null || _movement == null) return;

            Vector2 move = _input.Move;
            float vertical = _input.Vertical;
            _movement.ApplyMovement(move, vertical, Time.fixedDeltaTime);
        }

        private T ResolveDependency<T>(MonoBehaviour assigned) where T : class
        {
            if (assigned is T typed) return typed;
            return GetComponent<T>();
        }
    }
}
