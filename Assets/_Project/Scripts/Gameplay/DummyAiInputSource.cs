using Robogame.Input;
using Robogame.Movement;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Drop-in <see cref="IInputSource"/> for non-player target chassis.
    /// Drives the bot in a circle around a configured world-space centre and
    /// optionally aims + fires at a target chassis (typically the player).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Steering model.</b> Simple proportional controller: project the
    /// chassis's XZ position relative to <see cref="_circleCentre"/>, build
    /// the CCW tangent, mix in a radial correction toward
    /// <see cref="_circleRadius"/>, and steer toward the resulting heading.
    /// Output is fed into <see cref="GroundDriveSubsystem"/> via the same
    /// <c>Move.x = steer / Move.y = throttle</c> contract used by
    /// <see cref="PlayerInputHandler"/>.
    /// </para>
    /// <para>
    /// <b>Aiming.</b> When <see cref="FireAtTarget"/> is true and a target
    /// is configured, sets <see cref="RobotDrive.AimPointOverride"/> to the
    /// target's position so the chassis's <c>WeaponMount</c> rotates the
    /// turret toward the player. Cleared when the toggle goes off so the
    /// turret falls back to its default-forward rest pose.
    /// </para>
    /// <para>
    /// <b>Networking debt.</b> AI behaviour, target selection, and fire
    /// gating are server-authoritative concerns. This singleplayer-only
    /// component lives client-side; when netcode lands the behaviour moves
    /// to a server-side simulator and the client sees only the resulting
    /// drive inputs replayed deterministically.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DummyAiInputSource : MonoBehaviour, IInputSource
    {
        [Header("Patrol")]
        [Tooltip("World-space centre of the patrol circle.")]
        [SerializeField] private Vector3 _circleCentre = Vector3.zero;

        [Tooltip("Patrol circle radius (m).")]
        [SerializeField, Min(2f)] private float _circleRadius = 30f;

        [Tooltip("Forward throttle (0..1) while patrolling.")]
        [SerializeField, Range(0f, 1f)] private float _throttle = 0.7f;

        [Tooltip("How aggressively the AI corrects radial drift back toward _circleRadius. Higher = tighter circle, harder turns.")]
        [SerializeField, Range(0.005f, 0.2f)] private float _radialCorrectionGain = 0.04f;

        [Tooltip("Steering output is clamped to ±1 — this scales how strongly heading error maps to steer input.")]
        [SerializeField, Range(0.5f, 4f)] private float _steerGain = 1.5f;

        [Header("Combat")]
        [Tooltip("Target chassis to aim and fire at when FireAtTarget is true. Leave null and the AI patrols silently.")]
        [SerializeField] private Transform _target;

        [Tooltip("Master fire toggle. When true and a target exists, the AI aims and holds fire.")]
        [SerializeField] private bool _fireAtTarget = false;

        [Tooltip("Maximum range (m) at which the AI will fire. Outside this range the gun stays cold even if the toggle is on.")]
        [SerializeField, Min(5f)] private float _fireRange = 80f;

        // Cache: chassis drive (for AimPointOverride). Found at Awake.
        private RobotDrive _drive;

        private Vector2 _move;
        private bool _fireHeld;

        public Vector2 Move => _move;
        public Vector2 Look => Vector2.zero;
        public float Vertical => 0f;
        public bool FireHeld => _fireHeld;

        public bool FireAtTarget
        {
            get => _fireAtTarget;
            set => _fireAtTarget = value;
        }

        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        public Vector3 CircleCentre
        {
            get => _circleCentre;
            set => _circleCentre = value;
        }

        public float CircleRadius
        {
            get => _circleRadius;
            set => _circleRadius = Mathf.Max(2f, value);
        }

        private void Awake()
        {
            _drive = GetComponent<RobotDrive>();
        }

        private void OnDisable()
        {
            // Clear the aim override so a disabled / destroyed AI doesn't
            // leave the chassis stuck pointing at a stale position.
            if (_drive != null) _drive.AimPointOverride = null;
        }

        // We compute drive inputs in Update, not FixedUpdate. PlayerController
        // reads IInputSource each FixedUpdate via Move/FireHeld; using Update
        // means the AI brain runs at frame-rate, but the values it produces
        // are read once per physics step — same cadence the player gets.
        private void Update()
        {
            UpdatePatrolSteering();
            UpdateAimAndFire();
        }

        private void UpdatePatrolSteering()
        {
            Vector3 pos = transform.position;
            Vector3 fromCentre = pos - _circleCentre;
            fromCentre.y = 0f;
            float r = fromCentre.magnitude;
            // Avoid singularity if the bot is exactly on the centre.
            Vector3 radial = r > 0.01f ? fromCentre / r : Vector3.right;
            // Tangent for CCW circling (looking down +Y).
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            // Mix in radial correction: when r > target radius, push inward;
            // when r < target, push outward.
            float radialError = r - _circleRadius;
            Vector3 desired = (tangent - radial * radialError * _radialCorrectionGain);
            if (desired.sqrMagnitude < 1e-4f) desired = tangent;
            desired.Normalize();

            // Steering: map signed angle between forward and desired into [-1, 1].
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                _move = new Vector2(0f, _throttle);
                return;
            }
            forward.Normalize();
            // Cross.y gives signed turn direction (right-hand rule with world up).
            float cross = Vector3.Cross(forward, desired).y;
            float dot = Vector3.Dot(forward, desired);
            // If facing nearly backward, force a hard turn rather than relying
            // on tiny cross magnitudes.
            float steer = dot < -0.5f
                ? Mathf.Sign(cross == 0f ? 1f : cross)
                : Mathf.Clamp(cross * _steerGain, -1f, 1f);

            // Throttle: soften when steering hard so the bot doesn't oversteer
            // into a spin (especially with the asymmetric tank wheel layout).
            float throttle = _throttle * Mathf.Lerp(1f, 0.55f, Mathf.Abs(steer));
            _move = new Vector2(steer, throttle);
        }

        private void UpdateAimAndFire()
        {
            bool wantFire = _fireAtTarget && _target != null;
            if (!wantFire)
            {
                if (_drive != null) _drive.AimPointOverride = null;
                _fireHeld = false;
                return;
            }

            Vector3 targetPos = _target.position;
            // Only fire if the target is within range AND roughly in the
            // forward arc — turret yaw is limited and a fire-while-spinning
            // shot would just hit our own wheels at low elevation.
            Vector3 toTarget = targetPos - transform.position;
            float dist = toTarget.magnitude;
            if (_drive != null) _drive.AimPointOverride = targetPos;

            if (dist > _fireRange)
            {
                _fireHeld = false;
                return;
            }

            Vector3 toTargetFlat = toTarget;
            toTargetFlat.y = 0f;
            Vector3 forwardFlat = transform.forward;
            forwardFlat.y = 0f;
            float facingDot = forwardFlat.sqrMagnitude > 1e-4f && toTargetFlat.sqrMagnitude > 1e-4f
                ? Vector3.Dot(forwardFlat.normalized, toTargetFlat.normalized)
                : 0f;
            // Roughly within ±60° of forward = turret can swing onto target.
            _fireHeld = facingDot > 0.5f;
        }
    }
}
