using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Drop-in <see cref="IInputSource"/> for ground-chassis bots. Drives the
    /// bot through one of five behaviour states (Patrol / Pursue / Engage /
    /// Retreat / Dead) by writing into the same <see cref="IInputSource.Move"/>
    /// + <see cref="IInputSource.FireHeld"/> values that the human player
    /// produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in <c>Robogame.Gameplay</c> and is added to the chassis root
    /// BEFORE <see cref="ChassisFactory.Build"/> activates the GameObject, so
    /// <see cref="Robogame.Player.PlayerController.Awake"/>'s
    /// <c>GetComponent&lt;IInputSource&gt;</c> resolves it the same way the
    /// player's <see cref="Robogame.Input.PlayerInputHandler"/> does. Net
    /// effect: bot and player share the entire drive / weapon / damage path.
    /// </para>
    /// <para>
    /// State machine:
    /// <code>
    /// Patrol  --(target spotted, dist &lt; ChaseRange)-->          Pursue
    /// Pursue  --(target null / dist &gt; DisengageRange)-->         Patrol
    /// Pursue  --(dist &lt; OptimalRange + EngageBuffer*0.5)-->      Engage
    /// Engage  --(dist &gt; OptimalRange + EngageBuffer*1.5)-->      Pursue
    /// Engage  --(target null)-->                                    Patrol
    /// Any     --(Health &lt; RetreatHealthFraction)-->              Retreat
    /// Any     --(Robot.IsDestroyed)-->                              Dead
    /// </code>
    /// </para>
    /// <para>
    /// <b>Behavioural intent.</b>
    /// <list type="bullet">
    ///   <item><b>Patrol</b>: no target known. Drive in a wide circle around
    ///         <see cref="CircleCentre"/>. Holds fire.</item>
    ///   <item><b>Pursue</b>: target spotted but too far. Drive directly at
    ///         the target at high throttle. Fires only if it happens to be in
    ///         the forward arc.</item>
    ///   <item><b>Engage</b>: target inside <see cref="OptimalRange"/>. Orbit
    ///         the <i>target</i> (not the patrol point) at OptimalRange — the
    ///         tank-skirmish pattern. Fires whenever the turret can swing
    ///         onto target.</item>
    ///   <item><b>Retreat</b>: health &lt; <see cref="RetreatHealthFraction"/>.
    ///         Reverse + steer away from the target. Holds fire so the player
    ///         can disengage cleanly.</item>
    ///   <item><b>Dead</b>: zero outputs.</item>
    /// </list>
    /// Engage / Pursue use bandgap hysteresis — once you're orbiting, the
    /// player has to break ~1.5× the buffer before you re-pursue, so a small
    /// player wiggle doesn't flip-flop the bot's gait.
    /// </para>
    /// <para>
    /// <b>MP debt.</b> AI behaviour, target selection, and fire gating are
    /// server-authoritative concerns. This component lives client-side; when
    /// netcode lands the brain moves to a server-side simulator and the
    /// client sees only the resulting <see cref="Move"/>, <see cref="FireHeld"/>
    /// values replayed deterministically.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class GroundBotInputSource : BotInputSourceBase
    {
        /// <summary>Internal behaviour state. Exposed for tests + the perf HUD.</summary>
        public enum BotState
        {
            Patrol = 0,
            Pursue = 1,
            Engage = 2,
            Retreat = 3,
            Dead = 4,
        }

        // -----------------------------------------------------------------
        // Patrol
        // -----------------------------------------------------------------

        [Header("Patrol")]
        [Tooltip("World-space centre of the patrol circle.")]
        [SerializeField] private Vector3 _circleCentre = Vector3.zero;

        [Tooltip("Patrol circle radius (m).")]
        [SerializeField, Min(2f)] private float _circleRadius = 30f;

        [Tooltip("Forward throttle (0..1) while patrolling.")]
        [SerializeField, Range(0f, 1f)] private float _throttle = 0.7f;

        [Tooltip("How aggressively the AI corrects radial drift back toward _circleRadius. Higher = tighter circle.")]
        [SerializeField, Range(0.005f, 0.2f)] private float _radialCorrectionGain = 0.04f;

        [Tooltip("Steering output is clamped to ±1 — this scales how strongly heading error maps to steer input.")]
        [SerializeField, Range(0.5f, 4f)] private float _steerGain = 1.5f;

        // -----------------------------------------------------------------
        // Combat
        // -----------------------------------------------------------------

        [Header("Combat")]
        [Tooltip("Target chassis to chase / aim / fire at. Set by ArenaController; can be left null for a silent patrol.")]
        [SerializeField] private Transform _target;

        [Tooltip("Master fire toggle. When false the bot patrols silently regardless of state.")]
        [SerializeField] private bool _fireAtTarget = false;

        [Tooltip("Maximum range (m) at which the AI will fire. Outside this range the gun stays cold.")]
        [SerializeField, Min(5f)] private float _fireRange = 80f;

        [Tooltip("Range (m) at which Patrol transitions to Pursue. Initial detection radius — the bot starts moving toward the player from this far away.")]
        [SerializeField, Min(5f)] private float _chaseRange = 120f;

        [Tooltip("Range (m) at which Pursue falls back to Patrol (target lost). Wider than ChaseRange so a player who briefly outruns the bot doesn't immediately reset its hunt.")]
        [SerializeField, Min(5f)] private float _disengageRange = 160f;

        [Tooltip("Ideal engagement distance. In Engage, the bot orbits the player at this radius — close enough to land hits, far enough to avoid rams. Tank-style 25 m by default.")]
        [SerializeField, Min(5f)] private float _optimalRange = 25f;

        [Tooltip("Hysteresis gap (m) for Pursue ↔ Engage. Pursue → Engage at OptimalRange + buffer*0.5; Engage → Pursue at OptimalRange + buffer*1.5.")]
        [SerializeField, Min(2f)] private float _engageBuffer = 8f;

        [Tooltip("Throttle while Pursuing (driving at the target). Higher than patrol throttle — the bot commits to closing distance.")]
        [SerializeField, Range(0f, 1f)] private float _pursueThrottle = 0.95f;

        [Tooltip("Cosine threshold of the target-direction vs forward dot for fire-gating in Engage. -0.3 ≈ 250° fire arc (the turret swings onto target as long as the chassis isn't pointed away).")]
        [SerializeField, Range(-1f, 1f)] private float _engageFacingDotThreshold = -0.3f;

        [Header("Retreat")]
        [Tooltip("Health fraction (BlockCount / InitialBlockCount) below which the bot enters Retreat.")]
        [SerializeField, Range(0f, 1f)] private float _retreatHealthFraction = 0.3f;

        // -----------------------------------------------------------------
        // State (IInputSource plumbing + outputs live in BotInputSourceBase)
        // -----------------------------------------------------------------

        private BotState _state = BotState.Patrol;

        // -----------------------------------------------------------------
        // Public API (test-friendly)
        // -----------------------------------------------------------------

        public BotState State => _state;

        public bool FireAtTarget { get => _fireAtTarget; set => _fireAtTarget = value; }
        public Transform Target { get => _target; set => _target = value; }
        public Vector3 CircleCentre { get => _circleCentre; set => _circleCentre = value; }
        public float CircleRadius { get => _circleRadius; set => _circleRadius = Mathf.Max(2f, value); }
        public float ChaseRange { get => _chaseRange; set => _chaseRange = Mathf.Max(5f, value); }
        public float DisengageRange { get => _disengageRange; set => _disengageRange = Mathf.Max(5f, value); }
        public float OptimalRange { get => _optimalRange; set => _optimalRange = Mathf.Max(5f, value); }
        public float EngageBuffer { get => _engageBuffer; set => _engageBuffer = Mathf.Max(2f, value); }
        public float RetreatHealthFraction { get => _retreatHealthFraction; set => _retreatHealthFraction = Mathf.Clamp01(value); }
        public float FireRange { get => _fireRange; set => _fireRange = Mathf.Max(5f, value); }

        // -----------------------------------------------------------------
        // Per-frame brain (Update cadence + lifecycle in BotInputSourceBase)
        // -----------------------------------------------------------------

        /// <summary>
        /// One brain tick. Sets <see cref="BotInputSourceBase.Move"/>,
        /// <see cref="BotInputSourceBase.FireHeld"/>, <see cref="State"/>,
        /// and (optionally) the chassis's <see cref="RobotDrive.AimPointOverride"/>.
        /// </summary>
        public override void UpdateBrain()
        {
            using var _scope = PerfMarkers.BotInputUpdate.Auto();

            // Death is absorbing.
            if (_robot != null && _robot.IsDestroyed)
            {
                _state = BotState.Dead;
                ZeroOutputs();
                return;
            }

            // Health gate runs at the top of every non-dead frame so a hit
            // that drops us past the threshold flips state on the same tick
            // — no extra firing frame past the line.
            if (_state != BotState.Retreat && HealthFraction < _retreatHealthFraction)
            {
                _state = BotState.Retreat;
            }

            // State transitions driven by target distance (only meaningful
            // when there's a target). Run BEFORE the per-state body so the
            // body always sees the right state.
            float dist = float.PositiveInfinity;
            if (_target != null)
            {
                Vector3 toTarget = _target.position - transform.position;
                dist = toTarget.magnitude;
            }

            switch (_state)
            {
                case BotState.Patrol:
                    if (_target != null && dist < _chaseRange) _state = BotState.Pursue;
                    break;

                case BotState.Pursue:
                    if (_target == null || dist > _disengageRange) _state = BotState.Patrol;
                    else if (dist < _optimalRange + _engageBuffer * 0.5f) _state = BotState.Engage;
                    break;

                case BotState.Engage:
                    if (_target == null) _state = BotState.Patrol;
                    else if (dist > _optimalRange + _engageBuffer * 1.5f) _state = BotState.Pursue;
                    break;

                case BotState.Retreat:
                    // Sticky — stays in Retreat until destroyed or HP rises
                    // back above the threshold (RepairPad / RepairPulse
                    // module can heal it back over).
                    if (HealthFraction >= _retreatHealthFraction) _state = _target != null && dist < _chaseRange ? BotState.Pursue : BotState.Patrol;
                    break;
            }

            // Per-state body. Each writes _move / _fireHeld / AimPointOverride.
            switch (_state)
            {
                case BotState.Patrol:  TickPatrol();  break;
                case BotState.Pursue:  TickPursue(dist); break;
                case BotState.Engage:  TickEngage(dist); break;
                case BotState.Retreat: TickRetreat();    break;
            }
        }

        // -----------------------------------------------------------------
        // Per-state behaviours
        // -----------------------------------------------------------------

        private void TickPatrol()
        {
            // Wide circle around the configured patrol point. No target,
            // no fire.
            _move = ComputeSteer(
                transform.position, transform.forward,
                _circleCentre, _circleRadius,
                _radialCorrectionGain, _steerGain, _throttle);
            _fireHeld = false;
            if (_drive != null) _drive.AimPointOverride = null;
        }

        private void TickPursue(float dist)
        {
            // Drive directly at the target at high throttle. Fires only if
            // the target ends up in the forward arc (which is naturally true
            // when we're driving at it).
            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f)
            {
                _move = new Vector2(0f, _pursueThrottle);
            }
            else
            {
                Vector3 desired = toTarget.normalized;
                _move = ComputeSteerForHeading(transform.forward, desired, _steerGain, _pursueThrottle);
            }

            // Aim turret at target; fire only when target is in front and in range.
            if (_drive != null) _drive.AimPointOverride = _target.position;

            bool inFireRange = dist <= _fireRange;
            float facingDot = ComputeFacingDot(toTarget);
            _fireHeld = _fireAtTarget && inFireRange && facingDot > 0.5f;
        }

        private void TickEngage(float dist)
        {
            // Orbit the TARGET (not the original patrol centre) at OptimalRange.
            // The same patrol-circle math handles this — circle-strafing is
            // just patrolling around the player. Radial correction pushes the
            // bot back out toward OptimalRange when the player closes;
            // tangent steering keeps the bot moving sideways (= the gun
            // tracks via WeaponMount yaw).
            _move = ComputeSteer(
                transform.position, transform.forward,
                _target.position, _optimalRange,
                _radialCorrectionGain, _steerGain, _throttle);

            // Aim + fire. Relaxed fire-arc threshold (default −0.3, ~250°) so
            // the gun fires whenever the turret can swing onto target —
            // which it can across most of the chassis's facing because
            // WeaponMount yaw isn't hard-clamped. Without this relaxation a
            // circle-strafing bot would never fire (its forward is always
            // tangent to the player ⇒ facingDot ≈ 0 ⇒ fails the old > 0.5
            // gate).
            if (_drive != null) _drive.AimPointOverride = _target.position;

            if (!_fireAtTarget || dist > _fireRange)
            {
                _fireHeld = false;
                return;
            }

            Vector3 toTarget = _target.position - transform.position;
            float facingDot = ComputeFacingDot(toTarget);
            _fireHeld = facingDot > _engageFacingDotThreshold;
        }

        private void TickRetreat()
        {
            // Reverse + steer away from target. Holds fire so the player can
            // reset the engagement and chase if they want.
            _fireHeld = false;
            if (_drive != null) _drive.AimPointOverride = null;

            if (_target == null)
            {
                _move = new Vector2(0f, -0.5f);
                return;
            }

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f)
            {
                _move = new Vector2(0f, -0.5f);
                return;
            }
            Vector3 desired = -toTarget.normalized;
            _move = ComputeSteerForHeading(transform.forward, desired, _steerGain, throttle: -0.6f);
        }

        // ComputeFacingDot + the pure steering math (ComputeSteer,
        // ComputeSteerForHeading) live in BotInputSourceBase; existing
        // GroundBotInputSource.ComputeSteer call sites resolve through
        // the derived type unchanged.
    }
}
