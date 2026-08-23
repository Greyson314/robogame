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

        // Altitude control. More blades / bigger blades = higher ceiling.
        // The ceiling above the resting baseline scales with total lift
        // capacity (sum of per-blade LiftScale, in (N/2)² baseline units).
        public const float HeightPerCapacityUnit = 1.0f;
        // Headroom above max target altitude so the spring still has gap
        // information when the chassis is at the ceiling — without this
        // the ray would clip at gap = maxAlt and damping would jitter.
        public const float RaycastMarginMeters   = 1.5f;
        // How fast the shared target altitude lerps toward its current
        // input goal (climb / descend / rest). 4 m/s ≈ 1 cell-height per
        // 0.25 s — fast enough to feel responsive without snapping.
        public const float AltitudeLerpRate      = 4f;
        // Floor: shift can pull the target altitude here, which puts the
        // chassis on the ground (spring force clamps to zero when current
        // gap > 0, so the chassis simply rests on its colliders).
        public const float MinTargetAltitude     = 0f;

        public int Order => 0;
        public bool IsOperational => isActiveAndEnabled;

        private Rigidbody _rb;
        // CSP replay redirect (ADR-0002): when non-null, Tick drives this
        // prediction-mirror body instead of the chassis. Null in normal play.
        private Rigidbody _replayBody;
        public void SetForceTarget(Rigidbody body) => _replayBody = body;
        private Rigidbody Body => _replayBody != null ? _replayBody : _rb;
        private RobotDrive _drive;
        private BlockGrid _grid;
        private GroundTuningConfig _cfg = new();
        private readonly HashSet<HoverBladeBlock> _blades = new();

        // Per-chassis altitude state. Shared across blades so they agree
        // on a single hover height — independent per-blade target alt
        // would let them disagree and tilt the chassis.
        private float _baseTargetAlt;
        private float _maxTargetAlt;
        private float _currentTargetAlt;

        /// <summary>
        /// The hover target altitude the blades should aim for this
        /// frame. Lerped toward the player's input goal in
        /// <see cref="Tick"/>; read by every
        /// <see cref="HoverBladeBlock"/> on the chassis.
        /// </summary>
        public float CurrentTargetAltitude => _currentTargetAlt;

        /// <summary>
        /// Max raycast distance the blades should use. Scales with
        /// <see cref="MaxTargetAltitude"/> so the spring can still
        /// resolve gap when the chassis is at the ceiling.
        /// </summary>
        public float EffectiveMaxRaycast => _maxTargetAlt + RaycastMarginMeters;

        /// <summary>The ceiling the chassis can rise to with Space held.</summary>
        public float MaxTargetAltitude => _maxTargetAlt;

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

            // Baseline altitude pulls from the same dev-override layer the
            // blades use, so a single Hover.TargetAltitude slider moves
            // both the resting height and the ceiling formula together.
            HoverBladeTuningConfig hoverCfg = HoverBladeTuningConfig.Default;
            DevTuningOverride.ApplyHoverBlade(ref hoverCfg);
            _baseTargetAlt = hoverCfg.TargetAltitude;
            // First-time init: current altitude rests at the baseline.
            // Subsequent ResolveTuning calls (Tweakables.Changed) preserve
            // the player's current input-driven altitude where possible.
            if (_currentTargetAlt <= 0f) _currentTargetAlt = _baseTargetAlt;
            RecomputeMaxAltitude();
        }

        /// <summary>
        /// Sum the per-blade <see cref="HoverBladeBlock.LiftScale"/> values
        /// (each in N²/4 baseline units) and project that into a height
        /// bonus over the baseline. Called whenever the blade set
        /// changes — at seed time, on placement, on removal.
        /// </summary>
        private void RecomputeMaxAltitude()
        {
            float capacity = 0f;
            foreach (HoverBladeBlock b in _blades)
            {
                if (b == null) continue;
                capacity += b.LiftScale;
            }
            _maxTargetAlt = _baseTargetAlt + capacity * HeightPerCapacityUnit;
            // Clamp current altitude if a blade just died and shrank the
            // ceiling beneath the chassis's current setpoint.
            if (_currentTargetAlt > _maxTargetAlt) _currentTargetAlt = _maxTargetAlt;
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
            // RobotHoverBladeBinder normally adds the HoverBladeBlock
            // component itself, which then self-registers via
            // RegisterBlade in its OnEnable. This event path catches the
            // case where a blade is placed AFTER the chassis is live and
            // RobotHoverBladeBinder is already subscribed — same blade
            // shouldn't double-register because RegisterBlade is
            // idempotent on the HashSet.
            var blade = block.GetComponent<HoverBladeBlock>();
            if (blade != null) RegisterBlade(blade);
        }

        private void OnBlockRemoving(BlockBehaviour block)
        {
            if (block == null) return;
            var blade = block.GetComponent<HoverBladeBlock>();
            if (blade != null) UnregisterBlade(blade);
        }

        // Mirror GroundDriveSubsystem.SeedWheelsFromHierarchy: the subsystem
        // may be added after blocks already exist. At normal chassis-spawn
        // time this finds zero (blades haven't been attached yet by the
        // binder) and the blades' OnEnable register themselves later via
        // RegisterBlade. The seed is the defensive path for re-enable / hot
        // attach scenarios.
        private void SeedBladesFromHierarchy()
        {
            var existing = GetComponentsInChildren<HoverBladeBlock>(includeInactive: false);
            for (int i = 0; i < existing.Length; i++) _blades.Add(existing[i]);
            RecomputeMaxAltitude();
        }

        /// <summary>
        /// Called by <see cref="HoverBladeBlock.OnEnable"/> as the blade
        /// comes online. Centralises the "set changed → recompute ceiling"
        /// invariant.
        /// </summary>
        public void RegisterBlade(HoverBladeBlock blade)
        {
            if (blade == null) return;
            if (_blades.Add(blade)) RecomputeMaxAltitude();
        }

        /// <summary>
        /// Called by <see cref="HoverBladeBlock.OnDisable"/> and by the
        /// grid's BlockRemoving path.
        /// </summary>
        public void UnregisterBlade(HoverBladeBlock blade)
        {
            if (blade == null) return;
            if (_blades.Remove(blade)) RecomputeMaxAltitude();
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

            // --- Altitude setpoint. Hold Space → climb toward ceiling;
            //     hold Shift → descend toward ground; release both →
            //     altitude LATCHES at the current value (does not return
            //     to baseline). The model is "hold to change, release to
            //     hold" — Robocraft-style altitude trim rather than
            //     spring-loaded throttle. Always runs (even airborne) so
            //     the player can pre-set altitude before contact returns.
            float vert = control.Vertical;
            float step = AltitudeLerpRate * control.DeltaTime;
            if      (vert >  0.05f) _currentTargetAlt = Mathf.MoveTowards(_currentTargetAlt, _maxTargetAlt,     step);
            else if (vert < -0.05f) _currentTargetAlt = Mathf.MoveTowards(_currentTargetAlt, MinTargetAltitude, step);
            // Neither held: leave _currentTargetAlt where the player set it.

            // No ground beneath ANY blade → no LATERAL propulsion. The
            // chassis glides on momentum until lift returns; matches the
            // Robocraft feel where a hover-bot launched off a cliff
            // couldn't course-correct laterally until something was below
            // it again. (Altitude setpoint above still updates so the
            // player can pre-set their landing height.)
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
                Body.AddTorque(torque, ForceMode.Acceleration);
            }

            // --- Forward thrust: chassis-forward, capped at scaled max
            //     speed. Horizontal component only — hovers don't drive
            //     themselves up an incline by accident.
            if (!Mathf.Approximately(control.Move.y, 0f))
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();
                // TRACE[INV-11]: thrust splits across the PAD SET and acts
                // at each pad's lift point projected to CoM height (169,
                // hover slice of the per-block migration). Same probe-pass
                // findings as the ground drive: distributing over the
                // per-frame in-contact subset makes the force centroid
                // flicker on rough ground, and pad-height force points add
                // a pitch couple the tuned presets never had. The rigid
                // layout is the honest distribution — losing a pad still
                // skews the centroid so the bot pulls under throttle — and
                // the surrounding AnyBladeInContact gate keeps "no mid-air
                // propulsion". Yaw + altitude stay chassis-level (trim
                // state, not authority; see class doc).
                Vector3 thrust = fwd * (control.Move.y * accel * carryMul);
                int padCount = 0;
                foreach (HoverBladeBlock b in _blades)
                    if (b != null) padCount++;
                if (padCount > 0)
                {
                    Vector3 com = Body.worldCenterOfMass;
                    Vector3 chassisUp = transform.up;
                    Vector3 perPad = thrust / padCount;
                    foreach (HoverBladeBlock b in _blades)
                    {
                        if (b == null) continue;
                        Vector3 p = b.WorldLiftPosition;
                        p += chassisUp * Vector3.Dot(com - p, chassisUp);
                        Body.AddForceAtPosition(perPad, p, ForceMode.Acceleration);
                    }
                }
                else
                {
                    Body.AddForce(thrust, ForceMode.Acceleration);
                }

                Vector3 v = Body.linearVelocity;
                Vector3 horiz = new Vector3(v.x, 0f, v.z);
                float capped = maxSpeed * carryMul;
                if (horiz.sqrMagnitude > capped * capped)
                {
                    horiz = horiz.normalized * capped;
                    Body.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);
                }
            }
        }
    }
}
