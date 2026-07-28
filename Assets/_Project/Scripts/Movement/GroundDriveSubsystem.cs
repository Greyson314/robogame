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
        // Baseline wheel "hop" on the jump input. Kept deliberately below a
        // spring block's launch so springs read as a real boost ON TOP of
        // the hop (both apply an impulse to the chassis Rb on the same Space
        // press — additive). Bumped 6 → 40 → 120 (session 104 playtest): the
        // chassis is heavy enough that 40 N·s was still barely legible.
        [SerializeField, Min(0f)] private float _jumpImpulse = 120f;
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

        [Header("Tuning — Parking brake")]
        // Raycast suspension has no contact friction, so before LOG-154 a
        // bot nudged on a hill kept its in-plane velocity forever (nothing
        // damped it) and slid until a chassis cube collider caught the
        // terrain — the classic "stuck on the slope" state. The brake
        // bleeds that creep while idle. No gravity-cancel term is needed:
        // the suspension pushes world-VERTICAL, so at rest it already
        // cancels all of gravity including the along-slope component
        // (unlike a real car's slope-normal contact force) — a stopped
        // bot holds the hill by construction.
        [Tooltip("Fraction of in-plane velocity bled per physics step while grounded with no " +
                 "drive input — rolls the bot to a stop instead of coasting, and stops hill creep.")]
        [SerializeField, Range(0f, 1f)] private float _idleBrake = 0.2f;

        // Acceleration / MaxSpeed / TurnRate are server-authoritative
        // (blueprint), resolved once in OnEnable — were per-machine
        // Tweakables read every FixedUpdate. PHYSICS_PLAN §1.5 / §5.
        private GroundTuningConfig _cfg = new();
        private float Acceleration     => _cfg.Acceleration;
        private float MaxSpeed         => _cfg.MaxSpeed;
        private float TurnRate         => _cfg.TurnRate;
        // Jump / upright / grip stay on the GroundDriveTuning SO — they're
        // already invariant-1 compliant (SO, not a per-machine Tweakable).
        private float JumpImpulse      => _tuning != null ? _tuning.JumpImpulse      : _jumpImpulse;
        private float JumpCooldown     => _tuning != null ? _tuning.JumpCooldown     : _jumpCooldown;
        private float UprightStrength  => _tuning != null ? _tuning.UprightStrength  : _uprightStrength;
        private float RollPitchDamping => _tuning != null ? _tuning.RollPitchDamping : _rollPitchDamping;
        private float LateralGrip      => _tuning != null ? _tuning.LateralGrip      : _lateralGrip;
        private float IdleBrake        => _tuning != null ? _tuning.IdleBrake        : _idleBrake;

        public int Order => 0;
        public bool IsOperational => isActiveAndEnabled;

        private Rigidbody _rb;
        // CSP replay redirect (ADR-0002): when non-null, Tick drives this
        // prediction-mirror body instead of the chassis. Null in normal play.
        private Rigidbody _replayBody;
        public void SetForceTarget(Rigidbody body) => _replayBody = body;
        private Rigidbody Body => _replayBody != null ? _replayBody : _rb;
        private RobotDrive _drive;
        private float _nextJumpTime;
        private readonly HashSet<WheelBlock> _wheels = new HashSet<WheelBlock>();
        private BlockGrid _grid;

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);
            ResolveTuning();
            // Re-resolve on Tweakables.Changed so dev-only override sliders
            // update live without a chassis respawn.
            Robogame.Core.Tweakables.Changed += ResolveTuning;
            SubscribeToGrid();
            SeedWheelsFromHierarchy();
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
            Robogame.Core.Tweakables.Changed -= ResolveTuning;
            UnsubscribeFromGrid();
            _wheels.Clear();
        }

        private void ResolveTuning()
        {
            _cfg = _drive != null && _drive.Blueprint != null
                ? _drive.Blueprint.GroundTuning
                : new GroundTuningConfig();
            // Dev-only override (compile-stripped from shipping builds).
            Robogame.Block.DevTuningOverride.ApplyGround(ref _cfg);
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

        /// <summary>
        /// True if any attached <see cref="WheelBlock"/> is touching ground
        /// this step; <paramref name="groundNormal"/> is the normalised
        /// average of the grounded wheels' contact normals (world up when
        /// airborne). Drive, grip and the speed cap all act in this plane
        /// so throttle climbs a slope instead of plowing into it (LOG-153).
        /// </summary>
        private bool ProbeGround(out Vector3 groundNormal)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (WheelBlock w in _wheels)
            {
                if (w == null || !w.IsGrounded) continue;
                sum += w.GroundNormal;
                count++;
            }
            if (count == 0 || sum.sqrMagnitude < 0.0001f)
            {
                groundNormal = Vector3.up;
                return count > 0;
            }
            groundNormal = sum.normalized;
            return true;
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;

            // Wheels can only put down FORWARD drive force while at least one
            // is touching ground — wheels with nothing to push against can't
            // generate linear momentum. Steering yaw, by contrast, IS allowed
            // in the air: turning the bot mid-hop is a fun, low-stakes bit of
            // air control that doesn't let you cheat distance. Self-right +
            // roll damping below stay ungated (stability assists, not
            // propulsion). Lateral grip is gated with drive (further down).
            bool grounded = ProbeGround(out Vector3 groundNormal);

            // --- Steering: yaw around WORLD up so a tilted chassis doesn't
            //     accidentally roll itself when the player presses A/D.
            //     Ungated — air-steering is intentional (see above). ---
            if (!Mathf.Approximately(control.Move.x, 0f))
            {
                Vector3 torque = Vector3.up * (control.Move.x * TurnRate);
                Body.AddTorque(torque, ForceMode.Acceleration);
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
                    Body.AddTorque(uprightTorque, ForceMode.Acceleration);
                }

                if (RollPitchDamping > 0f)
                {
                    // Strip the world-up component so we don't fight steering.
                    Vector3 omega = Body.angularVelocity;
                    Vector3 yawComponent = Vector3.up * Vector3.Dot(omega, Vector3.up);
                    Vector3 rollPitch = omega - yawComponent;
                    Body.AddTorque(-rollPitch * RollPitchDamping, ForceMode.Acceleration);
                }
            }

            // --- Drive: planar acceleration along chassis forward, capped. ---
            // Carry-weight penalty: scrap-laden chassis accelerate slower
            // AND clamp at a lower top speed. Per SCRAP_LOOP_PLAN § 3 the
            // curve is stepped at 1.00 / 0.95 / 0.85 / 0.70 by carried
            // scrap count — RobotDrive computes the value, we apply it
            // to both accel and max speed so the chassis feels
            // unambiguously heavier.
            float carryMul = control.SpeedMultiplier;
            bool throttling = !Mathf.Approximately(control.Move.y, 0f);
            if (grounded && throttling)
            {
                // Push along the ground plane, not the horizontal — on a
                // hill the horizontal projection wasted sin(θ) of the
                // throttle plowing into the slope face (LOG-153).
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, groundNormal);
                if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();
                Body.AddForce(fwd * (control.Move.y * Acceleration * carryMul), ForceMode.Acceleration);

                // Cap in-plane speed (leave the ground-normal component
                // alone so suspension bounce isn't clamped).
                Vector3 v = Body.linearVelocity;
                Vector3 inPlane = Vector3.ProjectOnPlane(v, groundNormal);
                float cappedSpeed = MaxSpeed * carryMul;
                if (inPlane.sqrMagnitude > cappedSpeed * cappedSpeed)
                {
                    Vector3 normalPart = v - inPlane;
                    Body.linearVelocity = inPlane.normalized * cappedSpeed + normalPart;
                }
            }

            // --- Chassis-level lateral grip. Applied at COM with NO
            //     positional offset, so it never produces a roll moment.
            //     Only active when at least one wheel is grounded so the
            //     car can still drift through the air after a jump. ---
            if (LateralGrip > 0f && grounded)
            {
                Vector3 right = Vector3.ProjectOnPlane(transform.right, groundNormal);
                if (right.sqrMagnitude > 0.0001f)
                {
                    right.Normalize();
                    Vector3 v = Body.linearVelocity;
                    float lateral = Vector3.Dot(v, right);
                    Vector3 cancel = -right * (lateral * LateralGrip);
                    // Use VelocityChange so it's a clean per-frame nudge
                    // instead of a force that depends on dt + mass.
                    Body.AddForce(cancel, ForceMode.VelocityChange);
                }
            }

            // --- Parking brake (LOG-154). While grounded with no throttle,
            //     bleed in-plane creep so a nudge on a hill decays to a
            //     stop instead of persisting forever (raycast suspension
            //     has no contact friction to do it). Once stopped, the
            //     vertical suspension holds the slope by construction —
            //     see the field comment. Tuned via the SO like jump /
            //     upright (invariant-1 compliant — not a Tweakable). ---
            if (grounded && !throttling && IdleBrake > 0f)
            {
                Vector3 creep = Vector3.ProjectOnPlane(Body.linearVelocity, groundNormal);
                Body.AddForce(-creep * IdleBrake, ForceMode.VelocityChange);
            }

            // --- Jump. Grounded-only: an airborne re-hop would let a bot
            //     fly forever by spamming Space. You hop off the ground, then
            //     must land before hopping again. ---
            if (grounded && control.Vertical > 0.5f && Time.time >= _nextJumpTime)
            {
                Body.AddForce(Vector3.up * JumpImpulse, ForceMode.Impulse);
                _nextJumpTime = Time.time + JumpCooldown;
            }
        }
    }
}
