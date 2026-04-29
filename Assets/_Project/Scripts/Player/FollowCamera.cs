using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Smoothed third-person chase camera. Follows <see cref="Target"/>'s position
    /// while keeping a fixed offset in <i>world</i> space, then looks at the target.
    /// </summary>
    /// <remarks>
    /// World-space offset (rather than target-local) keeps the camera from spinning
    /// when the robot rotates — better for a driving game. Swap to <c>TransformPoint</c>
    /// once we want a true behind-the-shoulder cam.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;

        [Tooltip("Offset from the target in world space.")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 8f, -10f);

        [Tooltip("Smoothing time for position (lower = snappier).")]
        [SerializeField, Min(0f)] private float _positionSmoothTime = 0.15f;

        [Tooltip("If true, the camera looks at the target each frame.")]
        [SerializeField] private bool _lookAtTarget = true;

        [Tooltip("If true, auto-rebind the target when a Robot with the matching name is rebuilt.")]
        [SerializeField] private bool _autoRebindOnRebuild = true;

        public Transform Target { get => _target; set => _target = value; }

        private Vector3 _velocity;
        private string _targetName;

        private void OnEnable()
        {
            _targetName = _target != null ? _target.name : null;
            if (_autoRebindOnRebuild) Robot.Rebuilt += HandleRobotRebuilt;
        }

        private void OnDisable()
        {
            if (_autoRebindOnRebuild) Robot.Rebuilt -= HandleRobotRebuilt;
        }

        private void HandleRobotRebuilt(Robot robot)
        {
            if (robot == null) return;
            if (string.IsNullOrEmpty(_targetName) || robot.name == _targetName)
            {
                _target = robot.transform;
                _targetName = robot.name;
            }
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            Vector3 desired = _target.position + _offset;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, _positionSmoothTime);

            if (_lookAtTarget)
            {
                transform.LookAt(_target.position);
            }
        }
    }
}
