using Robogame.Input;
using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Shared base for AI bot input sources (<see cref="GroundBotInputSource"/>,
    /// <see cref="AirBotInputSource"/>). Owns the <see cref="IInputSource"/>
    /// plumbing every bot repeats — output fields, player-only verb stubs,
    /// cached chassis refs, health fraction with test override, and the
    /// Update → <see cref="UpdateBrain"/> cadence — so a new bot archetype
    /// only writes its state machine.
    /// </summary>
    /// <remarks>
    /// Each derived class keeps its own state enum: Ground's
    /// Patrol/Pursue/Engage/Retreat and Air's Cruise/Pursue/Engage/LowHealth
    /// are different vocabularies, and folding them into one shared enum
    /// would put dead states on every bot. The steering math lives here as
    /// statics (moved from GroundBotInputSource — call sites still compile,
    /// C# resolves inherited statics through the derived type name).
    /// </remarks>
    public abstract class BotInputSourceBase : MonoBehaviour, IInputSource
    {
        // Cached chassis refs — resolved once in Awake, same-object only.
        protected RobotDrive _drive;
        protected Robot _robot;

        // Brain outputs. Derived state machines write these; the
        // IInputSource properties below read them.
        protected Vector2 _move;
        protected float _vertical;
        protected bool _fireHeld;

        // -----------------------------------------------------------------
        // IInputSource
        // -----------------------------------------------------------------

        public Vector2 Move => _move;
        public Vector2 Look => Vector2.zero;
        public float Vertical => _vertical;
        public bool FireHeld => _fireHeld;
        // Bots don't author single-shot weapons (grapple magnet) yet.
        // When they do, this becomes a strobed pulse on attack intent.
        public bool FirePressed => false;
        // Bots auto-reload on empty — they never manually press R.
        public bool ReloadPressed => false;
        // Bots don't trigger modules yet. When they do, this becomes a
        // strobed pulse gated on engagement state + module readiness.
        public bool GetModulePressed(int slot) => false;
        // Bots don't self-right or carry grapples yet.
        public bool FlipPressed => false;
        public bool HookReleasePressed => false;

        /// <summary>Current health fraction (0..1). Falls back to BlockCount / InitialBlockCount when a Robot is attached.</summary>
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

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        protected virtual void Awake()
        {
            _drive = GetComponent<RobotDrive>();
            _robot = GetComponent<Robot>();
        }

        protected virtual void OnDisable()
        {
            // Clear the aim override so a disabled / destroyed bot doesn't
            // leave the chassis stuck pointing at a stale position.
            if (_drive != null) _drive.AimPointOverride = null;
        }

        // Update() — not FixedUpdate. PlayerController reads IInputSource
        // each FixedUpdate; running the brain at frame-rate matches what the
        // player gets while keeping the actual physics-application cadence
        // identical between bot and human.
        private void Update() => UpdateBrain();

        /// <summary>
        /// One brain tick. Public + virtual so tests can drive it without
        /// running the full Unity Update loop.
        /// </summary>
        public abstract void UpdateBrain();

        /// <summary>
        /// Zero every output and drop the aim override — the shared body of
        /// the absorbing Dead state.
        /// </summary>
        protected void ZeroOutputs()
        {
            _move = Vector2.zero;
            _vertical = 0f;
            _fireHeld = false;
            if (_drive != null) _drive.AimPointOverride = null;
        }

        /// <summary>
        /// XZ-plane facing dot between the chassis forward and the
        /// target-relative direction. Returns the normalised dot product
        /// or 0 when either vector is degenerate. Caller decides the
        /// threshold — different states want different fire-arc widths.
        /// </summary>
        protected float ComputeFacingDot(Vector3 toTarget)
        {
            Vector3 toTargetFlat = toTarget; toTargetFlat.y = 0f;
            Vector3 forwardFlat = transform.forward; forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude < 1e-4f || toTargetFlat.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.Dot(forwardFlat.normalized, toTargetFlat.normalized);
        }

        // -----------------------------------------------------------------
        // Pure steering math (testable in isolation)
        // -----------------------------------------------------------------

        /// <summary>
        /// Patrol-circle steering. Project the chassis onto the XZ plane, build
        /// the CCW tangent at the chassis's radial vector, mix in a radial
        /// correction proportional to the radius error, then map the heading
        /// error to a (steer, throttle) pair.
        /// </summary>
        public static Vector2 ComputeSteer(
            Vector3 pos,
            Vector3 forward,
            Vector3 circleCentre,
            float circleRadius,
            float radialCorrectionGain,
            float steerGain,
            float throttle)
        {
            Vector3 fromCentre = pos - circleCentre;
            fromCentre.y = 0f;
            float r = fromCentre.magnitude;
            Vector3 radial = r > 0.01f ? fromCentre / r : Vector3.right;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            float radialError = r - circleRadius;
            Vector3 desired = tangent - radial * radialError * radialCorrectionGain;
            if (desired.sqrMagnitude < 1e-4f) desired = tangent;
            desired.Normalize();

            return ComputeSteerForHeading(forward, desired, steerGain, throttle);
        }

        /// <summary>
        /// Convert a (currentForward, desiredForward) pair into a (steer, throttle)
        /// drive command. Shared by Patrol / Engage (forward throttle) and
        /// Retreat (reverse throttle).
        /// </summary>
        public static Vector2 ComputeSteerForHeading(
            Vector3 forward,
            Vector3 desiredHeading,
            float steerGain,
            float throttle)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return new Vector2(0f, throttle);
            forward.Normalize();

            float cross = Vector3.Cross(forward, desiredHeading).y;
            float dot = Vector3.Dot(forward, desiredHeading);
            float steer = dot < -0.5f
                ? Mathf.Sign(cross == 0f ? 1f : cross)
                : Mathf.Clamp(cross * steerGain, -1f, 1f);

            // Throttle: soften when steering hard so the bot doesn't oversteer
            // into a spin (especially with the asymmetric tank wheel layout).
            float t = throttle * Mathf.Lerp(1f, 0.55f, Mathf.Abs(steer));
            return new Vector2(steer, t);
        }
    }
}
