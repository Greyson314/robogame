using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Movement
{
    /// <summary>
    /// Self-righting flip hotkey on a player chassis. Polls
    /// <see cref="Keyboard.current"/> for <see cref="_flipKey"/> and
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
    /// MP-shape: today this polls keyboard locally, identical to
    /// <see cref="RobotHookReleaseInput"/>. When the netcode contract
    /// adds a <c>FlipRequested</c> bit to the per-tick input command,
    /// this becomes a server-side validate-cooldown-and-rotate; the
    /// rotation logic in <see cref="StartFlip"/> + <see cref="FixedUpdate"/>
    /// stays unchanged.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FlipController : MonoBehaviour
    {
        [Tooltip("Hotkey that triggers the flip. Default H — F is reserved, R is grapple-release.")]
        [SerializeField] private Key _flipKey = Key.H;

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
            Keyboard kb = Keyboard.current;
            if (kb == null || _rb == null) return;
            if (_flipping) return;
            if (!kb[_flipKey].wasPressedThisFrame) return;
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
            Quaternion delta = Quaternion.FromToRotation(transform.up, up);
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
