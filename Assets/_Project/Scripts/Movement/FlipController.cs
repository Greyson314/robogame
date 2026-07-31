using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Self-righting flip verb on a chassis. Reads
    /// <see cref="Robogame.Input.IInputSource.FlipPressed"/> and
    /// rotates the chassis Rigidbody so its local +Y axis aligns with
    /// the local gravity-up direction — animated over
    /// <see cref="_flipDuration"/> seconds with an ease-in-out curve so
    /// the bot reads as flipping itself rather than teleporting upright.
    /// Cooldown-gated to prevent spam.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Animated rotation rather than instant snap (session-34 user
    /// follow-up). The chassis stays dynamic — interpolated
    /// <see cref="Rigidbody.MoveRotation"/> calls in
    /// <see cref="FixedUpdate"/> drive a slerp from start to target
    /// rotation; linear velocity is preserved so a mid-air flip keeps
    /// its airspeed; angular velocity is held at zero through the flip
    /// so the chassis doesn't keep spinning past target.
    /// </para>
    /// <para>
    /// Per the project's interpolated-rigidbody flag set in
    /// <see cref="RobotDrive.Awake"/> (<c>RigidbodyInterpolation.Interpolate</c>),
    /// MoveRotation interpolates between physics steps — that's what
    /// keeps a 0.5 s, ~10-fixed-step rotation looking smooth at 60 fps
    /// rendering.
    /// </para>
    /// <para>
    /// MP-shape: the verb rides <see cref="Robogame.Input.IInputSource.FlipPressed"/>
    /// (H on the player handler, a serialized bit on the netcode
    /// <c>InputCommand</c>), so this component works unchanged on a
    /// server applying a remote owner's command. The cooldown gate here
    /// is the server-side validation.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FlipController : MonoBehaviour
    {

        [Tooltip("How long the rotate-to-upright animation takes, in seconds. ~0.4–0.6 s reads as " +
                 "a confident self-right; longer feels sluggish, shorter feels like a teleport.")]
        [SerializeField, Min(0.05f)] private float _flipDuration = 0.5f;

        [Tooltip("Cooldown between flips in seconds. Measured from flip START, so the flip's own " +
                 "duration counts toward the cooldown — feel-tuned for ~7 s total downtime.")]
        [SerializeField, Min(0f)] private float _cooldown = 7f;

        [Tooltip("Audio cue fired on a successful flip.")]
        [SerializeField] private AudioCue _activateCue = AudioCue.FlipActivate;

        [Tooltip("VFX kind spawned at the chassis centre on a successful flip.")]
        [SerializeField] private VfxKind _activateVfx = VfxKind.FlipBurst;

        [Tooltip("Scale multiplier applied to the activation VFX.")]
        [SerializeField, Min(0.1f)] private float _vfxScale = 1.5f;

        private Rigidbody _rb;
        private Robogame.Input.IInputSource _input;
        private float _nextFlipTime;

        // Active-flip state. _flipping is false outside of an in-progress
        // flip, true while we're slerping toward _flipTargetRot.
        private bool _flipping;
        private float _flipStartTime;
        private Quaternion _flipStartRot;
        private Quaternion _flipTargetRot;

        /// <summary>True if the cooldown is elapsed and no flip is in flight.</summary>
        public bool IsReady => !_flipping && Time.time >= _nextFlipTime;

        /// <summary>Seconds until the next flip is allowed. Zero or negative when ready.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _nextFlipTime - Time.time);

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (_rb == null || _flipping) return;
            // Late-resolve: OnEnable ordering can land this component
            // before the input source (LOG-132 activation-order class).
            if (_input == null)
            {
                _input = GetComponentInParent<Robogame.Input.IInputSource>();
                if (_input == null) return;
            }
            if (!_input.FlipPressed) return;
            if (Time.time < _nextFlipTime) return;

            StartFlip();
        }

        private void FixedUpdate()
        {
            if (!_flipping || _rb == null) return;

            float t = (Time.time - _flipStartTime) / _flipDuration;
            if (t >= 1f)
            {
                _rb.MoveRotation(_flipTargetRot);
                _rb.angularVelocity = Vector3.zero;
                _flipping = false;
                return;
            }

            // Smoothstep eases in and out so the chassis doesn't snap into
            // motion at t=0 or punch through at t=1.
            float eased = t * t * (3f - 2f * t);
            Quaternion next = Quaternion.Slerp(_flipStartRot, _flipTargetRot, eased);
            _rb.MoveRotation(next);
            // Hold angular velocity at zero so the chassis doesn't carry
            // residual spin past the target. Linear velocity is left
            // untouched.
            _rb.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Public entry point so a future server-authoritative input path
        /// can apply the flip without going through the keyboard poll.
        /// Bypasses the cooldown — callers (or the server) own that gate.
        /// </summary>
        public void StartFlip()
        {
            if (_rb == null) return;

            // Local "up" = the direction opposite to gravity at this point
            // in space. On flat arenas with no IGravitySource registered,
            // GravityField.SampleAt returns Physics.gravity (defaults to
            // (0,-9.81,0) → up = (0,1,0)). On spherical arenas it's the
            // outward radial direction.
            Vector3 gravity = GravityField.SampleAt(_rb.position);
            Vector3 up = gravity.sqrMagnitude > 0.0001f
                ? -gravity.normalized
                : Vector3.up;

            // Build the target rotation: shortest-arc rotation that maps
            // the chassis's current up onto the local-up vector, applied
            // on top of the current rotation. Keeps the chassis's heading
            // (forward axis) intact — flipping fixes roll/pitch only.
            // Antiparallel special case: resting exactly on its back is
            // the flip's primary use case, and FromToRotation's axis is
            // undefined there — an arbitrary 180° arc can pitch the bot
            // nose-over-tail and reverse its heading. Roll about the
            // up-plane-projected forward axis instead: up flips, heading
            // stays.
            Quaternion delta;
            if (Vector3.Dot(transform.up, up) < -0.999f)
            {
                Vector3 axis = Vector3.ProjectOnPlane(transform.forward, up);
                if (axis.sqrMagnitude < 1e-6f)
                    axis = Vector3.ProjectOnPlane(transform.right, up);
                delta = Quaternion.AngleAxis(180f, axis.normalized);
            }
            else
            {
                delta = Quaternion.FromToRotation(transform.up, up);
            }
            _flipStartRot = transform.rotation;
            _flipTargetRot = delta * transform.rotation;
            _flipStartTime = Time.time;
            _flipping = true;
            _nextFlipTime = Time.time + _cooldown;

            VfxSpawner.Spawn(_activateVfx, _rb.worldCenterOfMass, Quaternion.identity, _vfxScale);
            AudioRouter.PlayOneShot(_activateCue, _rb.worldCenterOfMass);
        }
    }
}
