using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tank-style chassis-level drive. Translates planar move input into
    /// forward force + yaw torque on the parent rigidbody, plus a vertical
    /// jump impulse. Registers itself as an <see cref="IDriveSubsystem"/>
    /// with <see cref="RobotDrive"/>.
    /// </summary>
    /// <remarks>
    /// This is the simplest possible composite drive subsystem — one block
    /// of behaviour, no per-wheel torque allocation. Coexists with
    /// <c>WheelBlock</c>s (visual + suspension) on the same chassis. A
    /// later realism pass can replace this with per-wheel torque.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GroundDriveSubsystem : MonoBehaviour, IDriveSubsystem
    {
        [Tooltip("Optional tuning profile. If assigned, OVERRIDES the inline values below.")]
        [SerializeField] private GroundDriveTuning _tuning;

        [Header("Tuning — Drive")]
        [SerializeField, Min(0f)] private float _acceleration = 26.25f;
        [SerializeField, Min(0f)] private float _maxSpeed = 13.5f;

        [Tooltip("Yaw acceleration (rad/s^2) per unit of turn input.")]
        [SerializeField, Min(0f)] private float _turnRate = 7.5f;

        [Header("Tuning — Jump")]
        [SerializeField, Min(0f)] private float _jumpImpulse = 6f;
        [SerializeField, Min(0f)] private float _jumpCooldown = 0.4f;

        [Header("Tuning — Stability")]
        [Tooltip("Self-righting torque (rad/s²) per radian of tilt away from world-up. " +
                 "Keeps the chassis upright after bumps without locking rotation.")]
        [SerializeField, Min(0f)] private float _uprightStrength = 3f;

        [Tooltip("Damping (rad/s² per rad/s) on roll + pitch rates. Yaw is unaffected so steering still works.")]
        [SerializeField, Min(0f)] private float _rollPitchDamping = 1.5f;

        [Tooltip("Chassis-level lateral grip when ANY wheel is grounded. Applied at the rigidbody centre of mass " +
                 "(not at wheel positions) so it produces ZERO roll moment. 0 = ice, 1 = perfect rails.")]
        [SerializeField, Range(0f, 1f)] private float _lateralGrip = 0.85f;

        // Resolved values (tuning profile overrides inline fields).
        private float Acceleration     => Tweakables.Get(Tweakables.GroundAccel);
        private float MaxSpeed         => Tweakables.Get(Tweakables.GroundMaxSpeed);
        private float TurnRate         => Tweakables.Get(Tweakables.GroundTurnRate);
        private float JumpImpulse      => _tuning != null ? _tuning.JumpImpulse      : _jumpImpulse;
        private float JumpCooldown     => _tuning != null ? _tuning.JumpCooldown     : _jumpCooldown;
        private float UprightStrength  => _tuning != null ? _tuning.UprightStrength  : _uprightStrength;
        private float RollPitchDamping => _tuning != null ? _tuning.RollPitchDamping : _rollPitchDamping;
        private float LateralGrip      => _tuning != null ? _tuning.LateralGrip      : _lateralGrip;

        public int Order => 0;
        public bool IsOperational => isActiveAndEnabled;

        private Rigidbody _rb;
        private RobotDrive _drive;
        private float _nextJumpTime;
        private readonly HashSet<WheelBlock> _wheels = new HashSet<WheelBlock>();
        private BlockGrid _grid;

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);
            SubscribeToGrid();
            SeedWheelsFromHierarchy();
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
            UnsubscribeFromGrid();
            _wheels.Clear();
        }

        private void SubscribeToGrid()
        {
            _grid = GetComponentInParent<BlockGrid>();
            if (_grid == null) return;
            _grid.BlockPlaced += OnBlockPlaced;
            _grid.BlockRemoving += OnBlockRemoving;
        }

        private void UnsubscribeFromGrid()
        {
            if (_grid == null) return;
            _grid.BlockPlaced -= OnBlockPlaced;
            _grid.BlockRemoving -= OnBlockRemoving;
            _grid = null;
        }

        private void OnBlockPlaced(BlockBehaviour block)
        {
            if (block == null) return;
            var wheel = block.GetComponent<WheelBlock>();
            if (wheel != null) _wheels.Add(wheel);
        }

        private void OnBlockRemoving(BlockBehaviour block)
        {
            if (block == null) return;
            var wheel = block.GetComponent<WheelBlock>();
            if (wheel != null) _wheels.Remove(wheel);
        }

        /// <summary>
        /// Pick up wheels that already exist in the hierarchy at OnEnable —
        /// e.g. when this subsystem is added after blocks have been placed,
        /// or when the scaffolder builds the chassis pre-Awake.
        /// </summary>
        private void SeedWheelsFromHierarchy()
        {
            var existing = GetComponentsInChildren<WheelBlock>(includeInactive: false);
            for (int i = 0; i < existing.Length; i++) _wheels.Add(existing[i]);
        }

        /// <summary>True if any attached <see cref="WheelBlock"/> is touching ground this step.</summary>
        private bool AnyWheelGrounded()
        {
            foreach (WheelBlock w in _wheels)
            {
                if (w != null && w.IsGrounded) return true;
            }
            return false;
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;

            // --- Steering: yaw around WORLD up so a tilted chassis doesn't
            //     accidentally roll itself when the player presses A/D. ---
            if (!Mathf.Approximately(control.Move.x, 0f))
            {
                Vector3 torque = Vector3.up * (control.Move.x * TurnRate);
                _rb.AddTorque(torque, ForceMode.Acceleration);
            }

            // --- Self-right + damp roll/pitch (but NOT yaw). ---
            //     Without this, removing the rotation freezes lets transient
            //     side forces (lateral grip, suspension snap) accumulate into
            //     a permanent roll. We compute a torque that points along the
            //     axis from chassis-up to world-up, scaled by the angle, and
            //     also damp the roll/pitch components of angular velocity.
            if (UprightStrength > 0f || RollPitchDamping > 0f)
            {
                Vector3 chassisUp = transform.up;
                Vector3 axis = Vector3.Cross(chassisUp, Vector3.up);
                float sin = axis.magnitude;
                if (sin > 0.0001f)
                {
                    float angle = Mathf.Asin(Mathf.Clamp(sin, -1f, 1f));
                    Vector3 uprightTorque = (axis / sin) * (angle * UprightStrength);
                    _rb.AddTorque(uprightTorque, ForceMode.Acceleration);
                }

                if (RollPitchDamping > 0f)
                {
                    // Strip the world-up component so we don't fight steering.
                    Vector3 omega = _rb.angularVelocity;
                    Vector3 yawComponent = Vector3.up * Vector3.Dot(omega, Vector3.up);
                    Vector3 rollPitch = omega - yawComponent;
                    _rb.AddTorque(-rollPitch * RollPitchDamping, ForceMode.Acceleration);
                }
            }

            // --- Drive: planar acceleration along chassis forward, capped. ---
            if (!Mathf.Approximately(control.Move.y, 0f))
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();
                _rb.AddForce(fwd * (control.Move.y * Acceleration), ForceMode.Acceleration);

                Vector3 v = _rb.linearVelocity;
                Vector3 horiz = new Vector3(v.x, 0f, v.z);
                if (horiz.sqrMagnitude > MaxSpeed * MaxSpeed)
                {
                    horiz = horiz.normalized * MaxSpeed;
                    _rb.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);
                }
            }

            // --- Chassis-level lateral grip. Applied at COM with NO
            //     positional offset, so it never produces a roll moment.
            //     Only active when at least one wheel is grounded so the
            //     car can still drift through the air after a jump. ---
            if (LateralGrip > 0f && AnyWheelGrounded())
            {
                Vector3 right = transform.right;
                right.y = 0f;
                if (right.sqrMagnitude > 0.0001f)
                {
                    right.Normalize();
                    Vector3 v = _rb.linearVelocity;
                    float lateral = Vector3.Dot(new Vector3(v.x, 0f, v.z), right);
                    Vector3 cancel = -right * (lateral * LateralGrip);
                    // Use VelocityChange so it's a clean per-frame nudge
                    // instead of a force that depends on dt + mass.
                    _rb.AddForce(cancel, ForceMode.VelocityChange);
                }
            }

            // --- Jump. ---
            if (control.Vertical > 0.5f && Time.time >= _nextJumpTime)
            {
                _rb.AddForce(Vector3.up * JumpImpulse, ForceMode.Impulse);
                _nextJumpTime = Time.time + JumpCooldown;
            }
        }
    }
}
