using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// <see cref="IInputSource"/> for plane / helicopter chassis bots. Holds
    /// altitude, leads its target on a flying trajectory, and bleeds altitude
    /// gracefully when health drops past a threshold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same plumbing pattern as <see cref="GroundBotInputSource"/>: attached
    /// to the chassis root before <see cref="Gameplay.ChassisFactory.Build"/>
    /// activates it, so <see cref="Robogame.Player.PlayerController.Awake"/>'s
    /// <c>GetComponent&lt;IInputSource&gt;</c> resolves it without any
    /// special wiring. The inputs are read by the same drive subsystems
    /// (<see cref="PlaneControlSubsystem"/>) the human pilots — so the
    /// MP-readiness rule of "AI feeds inputs, not state" holds.
    /// </para>
    /// <para>
    /// State machine:
    /// <code>
    /// Cruise  --(target in chase range)-->  Engage
    /// Engage  --(target out of disengage range)-->  Cruise
    /// Cruise or Engage --(health &lt; LowHealthFraction)-->  LowHealth
    /// Any     --(Robot.IsDestroyed)-->  Dead
    /// </code>
    /// LowHealth is sticky — the bot bleeds altitude and disengages until
    /// destroyed.
    /// </para>
    /// <para>
    /// <b>Pitch / roll clamps</b> are deliberately conservative. Session 21
    /// documented helicopter-rotor instability when the chassis pitches
    /// hard; clamping the bot's pitch to ±0.5 keeps the chassis flyable on
    /// the existing rotor-lift physics without retuning the whole flight
    /// model.
    /// </para>
    /// <para>
    /// <b>PlanetArena gravity caveat.</b> Altitude is measured against world
    /// +Y, which is correct for flat / water arenas but wrong for the
    /// spherical PlanetArena (gravity points toward the planet centre).
    /// Air bots in PlanetArena will be confused; flagged as a follow-up.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class AirBotInputSource : MonoBehaviour, IInputSource
    {
        public enum BotState
        {
            Cruise = 0,
            Pursue = 1,
            Engage = 2,
            LowHealth = 3,
            Dead = 4,
        }

        // -----------------------------------------------------------------
        // Cruise
        // -----------------------------------------------------------------

        [Header("Cruise")]
        [Tooltip("World-space centre of the cruise circle (XZ).")]
        [SerializeField] private Vector3 _circleCentre = Vector3.zero;

        [Tooltip("Cruise circle radius (m).")]
        [SerializeField, Min(20f)] private float _circleRadius = 80f;

        [Tooltip("Target altitude above world Y=0 the bot tries to hold.")]
        [SerializeField, Min(5f)] private float _targetAltitude = 40f;

        [Tooltip("Throttle while cruising (0..1). 1 = full forward, ground-bot-style — flying chassis stalls if throttle drops below ~0.4.")]
        [SerializeField, Range(0f, 1f)] private float _cruiseThrottle = 1f;

        [Tooltip("Steering output is clamped to ±1 — this scales how strongly heading error maps to steer input.")]
        [SerializeField, Range(0.5f, 4f)] private float _steerGain = 1.5f;

        [Tooltip("Vertical input gain. Multiplies the (target - current) altitude error to produce the [-1,1] vertical command.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _verticalGain = 0.1f;

        // -----------------------------------------------------------------
        // Combat
        // -----------------------------------------------------------------

        [Header("Combat")]
        [Tooltip("Target chassis. Set by ArenaController.")]
        [SerializeField] private Transform _target;

        [Tooltip("Master fire toggle.")]
        [SerializeField] private bool _fireAtTarget = true;

        [Tooltip("Fire range (m). Outside this range the gun stays cold.")]
        [SerializeField, Min(20f)] private float _fireRange = 200f;

        [Tooltip("Range (m) at which Cruise transitions to Pursue (initial detection).")]
        [SerializeField, Min(20f)] private float _chaseRange = 220f;

        [Tooltip("Range (m) at which Pursue falls back to Cruise (target lost). Wider than ChaseRange so a brief outrun doesn't reset the hunt.")]
        [SerializeField, Min(20f)] private float _disengageRange = 320f;

        [Tooltip("Ideal engagement distance. In Engage, the bot orbits the player at this radius — close enough to land hits, far enough that the chassis isn't always pitched into the ground.")]
        [SerializeField, Min(30f)] private float _optimalRange = 150f;

        [Tooltip("Hysteresis gap (m) for Pursue ↔ Engage. Pursue → Engage at OptimalRange + buffer*0.5; Engage → Pursue at OptimalRange + buffer*1.5.")]
        [SerializeField, Min(10f)] private float _engageBuffer = 30f;

        [Tooltip("Estimated projectile travel speed (m/s) used for target-leading. Overshoots if too high; lags if too low.")]
        [SerializeField, Min(10f)] private float _projectileSpeedEstimate = 60f;

        [Tooltip("Cosine threshold of the target-direction vs forward dot for fire-gating. Air bots have fixed-forward guns more often than ground bots, so this defaults stricter than the ground-bot threshold (0.6 ≈ 100° fire arc — roughly 'target ahead').")]
        [SerializeField, Range(-1f, 1f)] private float _engageFacingDotThreshold = 0.6f;

        // -----------------------------------------------------------------
        // Low health
        // -----------------------------------------------------------------

        [Header("Low health")]
        [Tooltip("Health fraction below which the bot disengages and bleeds altitude.")]
        [SerializeField, Range(0f, 1f)] private float _lowHealthFraction = 0.35f;

        [Tooltip("Throttle while in LowHealth. Reduced to simulate a damaged chassis losing power.")]
        [SerializeField, Range(0f, 1f)] private float _lowHealthThrottle = 0.5f;

        [Tooltip("Vertical command while in LowHealth. Negative = descending.")]
        [SerializeField, Range(-1f, 0f)] private float _lowHealthVertical = -0.3f;

        // -----------------------------------------------------------------
        // Cached refs
        // -----------------------------------------------------------------

        private RobotDrive _drive;
        private Robot _robot;
        private Rigidbody _targetRb;
        private Transform _targetRbTransform;

        // -----------------------------------------------------------------
        // IInputSource
        // -----------------------------------------------------------------

        private Vector2 _move;
        private float _vertical;
        private bool _fireHeld;
        private BotState _state = BotState.Cruise;

        public Vector2 Move => _move;
        public Vector2 Look => Vector2.zero;
        public float Vertical => _vertical;
        public bool FireHeld => _fireHeld;
        // Bots don't author single-shot weapons (grapple magnet) yet.
        public bool FirePressed => false;
        // Bots auto-reload on empty — they never manually press R.
        public bool ReloadPressed => false;
        public BotState State => _state;

        public float HealthFraction
        {
            get
            {
                if (_healthOverride.HasValue) return _healthOverride.Value;
                if (_robot != null && _robot.InitialBlockCount > 0)
                    return (float)_robot.BlockCount / _robot.InitialBlockCount;
                return 1f;
            }
            set => _healthOverride = value;
        }
        private float? _healthOverride;

        public Transform Target { get => _target; set { _target = value; _targetRb = null; _targetRbTransform = null; } }
        public bool FireAtTarget { get => _fireAtTarget; set => _fireAtTarget = value; }
        public Vector3 CircleCentre { get => _circleCentre; set => _circleCentre = value; }
        public float CircleRadius { get => _circleRadius; set => _circleRadius = Mathf.Max(20f, value); }
        public float TargetAltitude { get => _targetAltitude; set => _targetAltitude = Mathf.Max(5f, value); }
        public float ChaseRange { get => _chaseRange; set => _chaseRange = Mathf.Max(20f, value); }
        public float DisengageRange { get => _disengageRange; set => _disengageRange = Mathf.Max(20f, value); }
        public float OptimalRange { get => _optimalRange; set => _optimalRange = Mathf.Max(30f, value); }
        public float EngageBuffer { get => _engageBuffer; set => _engageBuffer = Mathf.Max(10f, value); }
        public float LowHealthFraction { get => _lowHealthFraction; set => _lowHealthFraction = Mathf.Clamp01(value); }

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            _drive = GetComponent<RobotDrive>();
            _robot = GetComponent<Robot>();
        }

        private void OnDisable()
        {
            if (_drive != null) _drive.AimPointOverride = null;
        }

        private void Update() => UpdateBrain();

        /// <summary>
        /// One brain tick. Public so tests can drive it without running the
        /// full Unity Update loop.
        /// </summary>
        public virtual void UpdateBrain()
        {
            using var _scope = PerfMarkers.BotInputUpdate.Auto();

            // Death is absorbing.
            if (_robot != null && _robot.IsDestroyed)
            {
                _state = BotState.Dead;
                _move = Vector2.zero;
                _vertical = 0f;
                _fireHeld = false;
                if (_drive != null) _drive.AimPointOverride = null;
                return;
            }

            // LowHealth is the air-bot's "Retreat" — sticky once entered.
            if (_state != BotState.LowHealth && HealthFraction < _lowHealthFraction)
            {
                _state = BotState.LowHealth;
            }

            // Distance-driven transitions for the live-combat states.
            float dist = float.PositiveInfinity;
            if (_target != null)
            {
                dist = (_target.position - transform.position).magnitude;
            }

            switch (_state)
            {
                case BotState.Cruise:
                    if (_target != null && dist < _chaseRange) _state = BotState.Pursue;
                    break;
                case BotState.Pursue:
                    if (_target == null || dist > _disengageRange) _state = BotState.Cruise;
                    else if (dist < _optimalRange + _engageBuffer * 0.5f) _state = BotState.Engage;
                    break;
                case BotState.Engage:
                    if (_target == null) _state = BotState.Cruise;
                    else if (dist > _optimalRange + _engageBuffer * 1.5f) _state = BotState.Pursue;
                    break;
                // LowHealth is sticky; only cleared by re-cross of HP threshold
                // (no heal mechanic today, so effectively until destroyed).
            }

            switch (_state)
            {
                case BotState.Cruise:    TickCruise();      break;
                case BotState.Pursue:    TickPursue(dist);  break;
                case BotState.Engage:    TickEngage(dist);  break;
                case BotState.LowHealth: TickLowHealth();   break;
            }
        }

        // -----------------------------------------------------------------
        // Per-state behaviours
        // -----------------------------------------------------------------

        private void TickCruise()
        {
            // Wide circle around the configured cruise centre, hold target
            // altitude, no fire.
            _move = GroundBotInputSource.ComputeSteer(
                transform.position, transform.forward,
                _circleCentre, _circleRadius,
                radialCorrectionGain: 0.04f,
                _steerGain, _cruiseThrottle);
            _vertical = AltitudeCommand();
            _fireHeld = false;
            if (_drive != null) _drive.AimPointOverride = null;
        }

        private void TickPursue(float dist)
        {
            // Fly toward the target while holding altitude. No fire — plane
            // bots have a fixed-forward gun and the target's rarely in the
            // forward arc until we're committed.
            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f)
            {
                _move = new Vector2(0f, _cruiseThrottle);
            }
            else
            {
                Vector3 desired = toTarget.normalized;
                _move = GroundBotInputSource.ComputeSteerForHeading(transform.forward, desired, _steerGain, _cruiseThrottle);
            }
            _vertical = AltitudeCommand();

            // Aim turret at lead-point in case the chassis has one; for a
            // fixed-gun plane this is the same as no override.
            Vector3 leadPoint = ComputeLeadPoint(_target.position, dist);
            if (_drive != null) _drive.AimPointOverride = leadPoint;

            // Don't fire during pursue — the angle is rarely good and we
            // burn ammo for nothing. Engage handles firing.
            _fireHeld = false;
        }

        private void TickEngage(float dist)
        {
            // Orbit the TARGET at OptimalRange — patrol-circle math centred
            // on the player. Same trick as the ground bot: circle-strafing
            // is just patrolling around the target.
            _move = GroundBotInputSource.ComputeSteer(
                transform.position, transform.forward,
                _target.position, _optimalRange,
                radialCorrectionGain: 0.04f,
                _steerGain, _cruiseThrottle);
            _vertical = AltitudeCommand();

            // Aim + lead.
            Vector3 leadPoint = ComputeLeadPoint(_target.position, dist);
            if (_drive != null) _drive.AimPointOverride = leadPoint;

            if (!_fireAtTarget || dist > _fireRange)
            {
                _fireHeld = false;
                return;
            }

            // Plane chassis with a fixed-forward gun can't hit a target
            // behind it; stricter facing threshold than the ground bot.
            Vector3 toLead = leadPoint - transform.position;
            float facingDot = transform.forward.sqrMagnitude > 1e-4f && toLead.sqrMagnitude > 1e-4f
                ? Vector3.Dot(transform.forward.normalized, toLead.normalized)
                : 0f;
            _fireHeld = facingDot > _engageFacingDotThreshold;
        }

        private void TickLowHealth()
        {
            // Bleed altitude, half throttle, no fire. Steer continues to use
            // the cruise-circle so the bot peels off the engagement instead
            // of nose-diving into the player.
            _move = GroundBotInputSource.ComputeSteer(
                transform.position, transform.forward,
                _circleCentre, _circleRadius,
                radialCorrectionGain: 0.04f,
                _steerGain, _lowHealthThrottle);
            _vertical = _lowHealthVertical;
            _fireHeld = false;
            if (_drive != null) _drive.AimPointOverride = null;
        }

        // -----------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Linear P-controller on altitude. Positive = climb, negative =
        /// descend. <see cref="_verticalGain"/> should keep |output| ≤ 1 for
        /// any reasonable altitude error.
        /// </summary>
        private float AltitudeCommand()
        {
            float altErr = _targetAltitude - transform.position.y;
            return Mathf.Clamp(altErr * _verticalGain, -1f, 1f);
        }

        // -----------------------------------------------------------------
        // Lead-the-target
        // -----------------------------------------------------------------

        private Vector3 ComputeLeadPoint(Vector3 targetPos, float currentDist)
        {
            // Cache the target Rigidbody once per target change. Avoids a
            // GetComponent per frame.
            if (_target != null && _targetRbTransform != _target)
            {
                _targetRb = _target.GetComponent<Rigidbody>()
                            ?? _target.GetComponentInParent<Rigidbody>();
                _targetRbTransform = _target;
            }
            if (_targetRb == null || _projectileSpeedEstimate <= 0f) return targetPos;

            // First-order lead: assume target velocity is constant for the
            // travel time. travelTime = dist / muzzleSpeed; lead = vel * travelTime.
            // Iteration once for slight accuracy, but a single pass is plenty
            // for a fixed-direction gun.
            float travelTime = currentDist / _projectileSpeedEstimate;
            return targetPos + _targetRb.linearVelocity * travelTime;
        }
    }
}
