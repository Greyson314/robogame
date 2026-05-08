using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Per-chassis wind loop. Plays the
    /// <see cref="AudioCue.WindLoop"/> at a volume + pitch driven by
    /// the chassis Rigidbody's linear speed — quiet when parked, full
    /// when sustained-flight fast. Auto-attached by
    /// <see cref="Gameplay.ChassisFactory"/>; one per chassis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why per-chassis, not per-camera?</b> Bot chassis benefit from
    /// the same cue (you can hear an enemy plane scream past at speed).
    /// The cue's <see cref="AudioCueLibrary.Entry.SpatialBlend"/> is 0
    /// (2D), so the local player's wind sits in the centre of the mix
    /// regardless of camera orbit; distance attenuation on a bot's
    /// wind is the AudioListener's 3D math + the bot's small base
    /// volume gating it down naturally.
    /// </para>
    /// <para>
    /// <b>Speed → mix mapping.</b> Both volume and pitch ramp linearly
    /// with speed inside the threshold band. Below
    /// <see cref="QuietBelow"/> the loop is silent (parked / taxiing).
    /// At <see cref="LoudAt"/> and above the loop is at full base
    /// volume. Pitch shifts subtly to sell the speed change without
    /// tonal glitching.
    /// </para>
    /// <para>
    /// <b>Performance.</b> One pooled <see cref="AudioLoopHandle"/>;
    /// per-frame work is two field reads + two interp + two writes
    /// to the AudioSource. No allocations.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ChassisWindAudio : MonoBehaviour
    {
        // Speed (m/s) below which wind is fully silent. A taxiing or
        // hovering chassis shouldn't whistle.
        private const float QuietBelow = 5f;
        // Speed at which wind reaches its max base volume. Tuned to
        // the project's plane chassis terminal velocity (~35 m/s).
        private const float LoudAt = 35f;
        // Max base volume the loop is set to at full speed. Stacks on
        // top of SFX bus + master volume.
        private const float MaxBaseVolume = 0.5f;
        // Pitch range. Subtle — too wide and the loop's harmonic
        // content audibly drifts; the user reads it as "broken sound"
        // rather than "fast wind".
        private const float MinPitch = 0.85f;
        private const float MaxPitch = 1.15f;

        private Rigidbody _rb;
        private AudioLoopHandle _loop;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            // Loop is owned by this transform — when the chassis is
            // destroyed, OnDestroy stops the loop. Disable / re-enable
            // (e.g. snapshot capture in Robot.CaptureTemplate) tears
            // and recreates so the audio always tracks live state.
            if (_loop == null || !_loop.IsValid)
            {
                _loop = AudioRouter.PlayLoop(AudioCue.WindLoop, transform);
                ApplyFromSpeed(); // initialise so spawn isn't silent at speed
            }
        }

        private void OnDisable()
        {
            // Stop here too so the snapshot-clone Robot.CaptureTemplate
            // creates doesn't keep its own audio source running on a
            // hidden GameObject. Real teardown happens in OnDestroy
            // (which also calls Stop, idempotent).
            _loop?.Stop();
            _loop = null;
        }

        private void OnDestroy()
        {
            _loop?.Stop();
            _loop = null;
        }

        private void Update()
        {
            ApplyFromSpeed();
        }

        private void ApplyFromSpeed()
        {
            if (_loop == null || !_loop.IsValid || _rb == null) return;
            float speed = _rb.linearVelocity.magnitude;
            // Linear ramp inside the threshold band; clamped outside.
            float t = Mathf.Clamp01((speed - QuietBelow) / (LoudAt - QuietBelow));
            _loop.SetBaseVolume(MaxBaseVolume * t);
            _loop.SetPitch(Mathf.Lerp(MinPitch, MaxPitch, t));
        }
    }
}
