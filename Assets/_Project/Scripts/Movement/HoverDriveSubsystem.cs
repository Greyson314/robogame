using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Hover-style chassis-level drive. Companion to
    /// <see cref="HoverBladeBlock"/>: translates planar WASD input into a
    /// subtle forward force + yaw torque on the parent rigidbody, gated
    /// on at least one hover blade having ground contact this frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <see cref="GroundDriveSubsystem"/>'s tank-drive shape but
    /// is intentionally less aggressive — Robocraft-style hover bots
    /// were meant to feel more delicate than wheeled tanks, "moving on
    /// air" rather than gripping the ground. The
    /// <see cref="HoverThrustScale"/> multiplier dials forward thrust
    /// and yaw down relative to the wheel-tank baseline; the user can
    /// add explicit thruster blocks if they want overtly powered hover
    /// movement.
    /// </para>
    /// <para>
    /// Tuning piggy-backs on <see cref="GroundTuningConfig"/> from the
    /// blueprint (Acceleration / MaxSpeed / TurnRate). Hover tanks are
    /// authored as Ground-kind chassis already; reusing the existing
    /// per-blueprint tuning struct avoids a new schema field.
    /// </para>
    /// <para>
    /// Skipped vs. <see cref="GroundDriveSubsystem"/>: lateral grip
    /// (hovers don't grip the ground), self-righting / roll-pitch
    /// damping (lift force at each blade attach point naturally rights
    /// the chassis), jump (hovers float by design).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HoverDriveSubsystem : MonoBehaviour, IDriveSubsystem
    {
        // Subtle-by-default multiplier — hover thrust is ~60% of the
        // equivalent wheel-tank thrust so wheels still feel snappier.
        // Players who want hover-tanks to be the dominant ground archetype
        // can dial this up via the dev override layer (session 98 pattern).
        public const float HoverThrustScale = 0.6f;
        public const float HoverYawScale    = 0.8f;

        public int Order => 0;
        public bool IsOperational => isActiveAndEnabled;

        private Rigidbody _rb;
        private RobotDrive _drive;
        private BlockGrid _grid;
        private GroundTuningConfig _cfg = new();
        private readonly HashSet<HoverBladeBlock> _blades = new();

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);
            ResolveTuning();
            Tweakables.Changed += ResolveTuning;
            SubscribeToGrid();
            SeedBladesFromHierarchy();
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
            Tweakables.Changed -= ResolveTuning;
            UnsubscribeFromGrid();
            _blades.Clear();
        }

        private void ResolveTuning()
        {
            _cfg = _drive != null && _drive.Blueprint != null
                ? _drive.Blueprint.GroundTuning
                : new GroundTuningConfig();
            DevTuningOverride.ApplyGround(ref _cfg);
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
            var blade = block.GetComponent<HoverBladeBlock>();
            if (blade != null) _blades.Add(blade);
        }

        private void OnBlockRemoving(BlockBehaviour block)
        {
            if (block == null) return;
            var blade = block.GetComponent<HoverBladeBlock>();
            if (blade != null) _blades.Remove(blade);
        }

        // Mirror GroundDriveSubsystem.SeedWheelsFromHierarchy: the subsystem
        // may be added after blocks already exist, so re-scan once on enable.
        private void SeedBladesFromHierarchy()
        {
            var existing = GetComponentsInChildren<HoverBladeBlock>(includeInactive: false);
            for (int i = 0; i < existing.Length; i++) _blades.Add(existing[i]);
        }

        private bool AnyBladeInContact()
        {
            foreach (HoverBladeBlock b in _blades)
            {
                if (b != null && b.HasGroundContact) return true;
            }
            return false;
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;

            // No ground beneath ANY blade → no propulsion. The chassis
            // glides on momentum until lift returns; matches the Robocraft
            // feel where a hover-bot launched off a cliff couldn't course-
            // correct laterally until something was below it again.
            if (!AnyBladeInContact()) return;

            float accel    = _cfg.Acceleration * HoverThrustScale;
            float maxSpeed = _cfg.MaxSpeed     * HoverThrustScale;
            float yawRate  = _cfg.TurnRate     * HoverYawScale;
            float carryMul = control.SpeedMultiplier;

            // --- Yaw: spin around world up so a slightly tilted chassis
            //     doesn't accidentally roll itself when A/D is pressed.
            if (!Mathf.Approximately(control.Move.x, 0f))
            {
                Vector3 torque = Vector3.up * (control.Move.x * yawRate);
                _rb.AddTorque(torque, ForceMode.Acceleration);
            }

            // --- Forward thrust: chassis-forward, capped at scaled max
            //     speed. Horizontal component only — hovers don't drive
            //     themselves up an incline by accident.
            if (!Mathf.Approximately(control.Move.y, 0f))
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();
                _rb.AddForce(fwd * (control.Move.y * accel * carryMul), ForceMode.Acceleration);

                Vector3 v = _rb.linearVelocity;
                Vector3 horiz = new Vector3(v.x, 0f, v.z);
                float capped = maxSpeed * carryMul;
                if (horiz.sqrMagnitude > capped * capped)
                {
                    horiz = horiz.normalized * capped;
                    _rb.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);
                }
            }
        }
    }
}
