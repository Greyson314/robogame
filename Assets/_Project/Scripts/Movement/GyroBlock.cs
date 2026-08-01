using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Reaction-wheel assist. Adds yaw turn authority from steer input and
    /// damps roll/pitch wobble, all as torque on the single chassis
    /// Rigidbody — no new physics objects (invariants #4/#5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stabiliser damps roll/pitch <i>rates</i> (angular velocity about
    /// the chassis forward/right axes) rather than righting toward a world
    /// up. Rate damping needs no gravity reference, so it behaves the same
    /// on spherical arenas; a true self-righting mode is a follow-up.
    /// </para>
    /// <para>
    /// Authority (yaw torque in N·m at full steer) is per-gyro
    /// server-authoritative config via <see cref="BlockBehaviour.ConfigValue"/>;
    /// 0 = the authored default. Torque is mass-dependent
    /// (<see cref="ForceMode.Force"/>) on purpose: the same gyro turns a
    /// light bot faster than a barge.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class GyroBlock : MonoBehaviour, IDriveSubsystem
    {
        [Header("Visual flywheel (auto-built if blank)")]
        [SerializeField] private Transform _flywheel;
        [SerializeField] private Color _flywheelColor = new Color(0.75f, 0.55f, 0.25f);

        [Tooltip("Idle flywheel spin, deg/s. Cosmetic only.")]
        [SerializeField] private float _visualSpinDegPerSec = 540f;

        [Tooltip("Roll/pitch rate-damping torque per rad/s of wobble, as a fraction of Authority.")]
        [SerializeField, Min(0f)] private float _stabilizerGain = 0.35f;

        public int Order => 200; // assist stage
        public bool IsOperational => isActiveAndEnabled;

        // Wave-1 placeholder default; per-gyro override via Entry.BlockConfig.
        private const float DefaultAuthority = 25f;
        private BlockBehaviour _bb;
        private float Authority => _bb != null && _bb.ConfigValue > 0f ? _bb.ConfigValue : DefaultAuthority;

        private Rigidbody _rb;

        // Audio — flywheel hum, looped for the block's live lifetime.
        // Idles quiet and swells toward the library ceiling with steer
        // input; the two consts are absolute base volumes (the library
        // row's Volume is the full-steer ceiling, 0.30).
        private const float LoopIdleVolume = 0.10f;
        private const float LoopFullVolume = 0.30f;
        private AudioLoopHandle _loop;
        private float _loopVolume = LoopIdleVolume;

        // CSP replay redirect (ADR-0002): when non-null, Tick drives this
        // prediction-mirror body instead of the chassis. Null in normal play.
        private Rigidbody _replayBody;
        public void SetForceTarget(Rigidbody body) => _replayBody = body;
        private Rigidbody Body => _replayBody != null ? _replayBody : _rb;
        private RobotDrive _drive;

        private void Awake()
        {
            _bb = GetComponent<BlockBehaviour>();
            EnsureRig();
        }

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);

            // HoverBladeBlock pattern: idempotent re-check via IsValid so
            // re-enable doesn't double-allocate a loop voice.
            if (_loop == null || !_loop.IsValid)
            {
                _loop = AudioRouter.PlayLoop(AudioCue.GyroLoop, transform);
                _loopVolume = LoopIdleVolume;
                _loop?.SetBaseVolume(_loopVolume);
            }
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
            _loop?.Stop();
            _loop = null;
        }

        private void OnDestroy()
        {
            _loop?.Stop();
            _loop = null;
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;
            Rigidbody body = Body;
            Transform chassis = body.transform;

            float authority = Authority;

            // Yaw assist about the chassis up axis. Works at a standstill —
            // that's the point of a reaction wheel (contrast the rudder,
            // which is speed-dependent by design).
            float steer = Mathf.Clamp(control.Move.x, -1f, 1f);
            if (!Mathf.Approximately(steer, 0f))
            {
                body.AddTorque(chassis.up * (steer * authority), ForceMode.Force);
            }

            // Hum swells with steer effort. Skipped during CSP replay —
            // replay re-runs many ticks per frame and audio is
            // presentation, not simulation.
            if (_replayBody == null && _loop != null && _loop.IsValid)
            {
                float target = Mathf.Lerp(LoopIdleVolume, LoopFullVolume, Mathf.Abs(steer));
                _loopVolume = Mathf.MoveTowards(_loopVolume, target, 1.5f * Time.fixedDeltaTime);
                _loop.SetBaseVolume(_loopVolume);
            }

            // Roll/pitch rate damping: counter the angular-velocity
            // components about chassis forward (roll) and right (pitch).
            // Pure damping — no target attitude, so no windup and no
            // gravity reference needed.
            float gain = authority * _stabilizerGain;
            if (gain > 0f)
            {
                Vector3 angVel = body.angularVelocity;
                float rollRate  = Vector3.Dot(angVel, chassis.forward);
                float pitchRate = Vector3.Dot(angVel, chassis.right);
                Vector3 damping = (chassis.forward * -rollRate + chassis.right * -pitchRate) * gain;
                body.AddTorque(damping, ForceMode.Force);
            }
        }

        private void LateUpdate()
        {
            if (_flywheel == null) return;
            _flywheel.Rotate(0f, _visualSpinDegPerSec * Time.deltaTime, 0f, Space.Self);
        }

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private static Material s_flywheelMaterial;

        private void EnsureRig()
        {
            BlockVisuals.HideHostMesh(gameObject);
            BlockBehaviour bb = GetComponent<BlockBehaviour>();
            if (bb != null && bb.Definition != null
                && bb.Definition.VisualModelStatic
                && bb.Definition.VisualModel != null)
            {
                return;
            }
            if (_flywheel != null) return;

            // Squat spinning disc — reads as a flywheel in a cage.
            _flywheel = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Flywheel", PrimitiveType.Cylinder);
            _flywheel.localPosition = Vector3.zero;
            _flywheel.localRotation = Quaternion.identity;
            _flywheel.localScale = new Vector3(0.85f, 0.18f, 0.85f);

            MeshRenderer mr = _flywheel.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (s_flywheelMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    s_flywheelMaterial = new Material(shader) { color = _flywheelColor };
                }
                mr.sharedMaterial = s_flywheelMaterial;
            }
        }
    }
}
