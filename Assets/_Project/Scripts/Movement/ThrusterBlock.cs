using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    // Note: Robogame.Block.BlockVisuals is used for rig construction.
    /// <summary>
    /// A jet / rocket block. Pushes the parent rigidbody along its own
    /// <see cref="Transform.forward"/>, scaled by a throttle derived from
    /// <see cref="DriveControl.Vertical"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Self-registers with the ancestor <see cref="RobotDrive"/> as an
    /// <see cref="IDriveSubsystem"/>. Each thruster is independent, so a
    /// chassis with multiple thrusters off-axis from the COM will produce
    /// torque automatically (welcome, VTOL).
    /// </para>
    /// <para>
    /// Throttle mapping: <c>0.5 + 0.5 * Vertical</c>, clamped to [0, 1]. So
    /// a controller idle puts the thruster at 50% (cruise), holding Space
    /// gives 100%, holding Ctrl gives 0%. This keeps planes flyable
    /// without a held-button discipline.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class ThrusterBlock : MonoBehaviour, IDriveSubsystem
    {
        [Tooltip("Optional tuning profile. If assigned, OVERRIDES the inline values below.")]
        [SerializeField] private ThrusterTuning _tuning;

        [Header("Thrust")]
        [Tooltip("Maximum forward force (N) at full throttle.")]
        [SerializeField, Min(0f)] private float _maxThrust = 155f;

        [Tooltip("Idle throttle when no input is being applied. 0 = off, 1 = full.")]
        [SerializeField, Range(0f, 1f)] private float _idleThrottle = 0.4f;

        [Tooltip("How quickly throttle slews to its target value (per second). 0 = instant.")]
        [SerializeField, Min(0f)] private float _throttleResponse = 2.6f;

        [Header("Visual nozzle (auto-built if blank)")]
        [SerializeField] private Transform _nozzle;
        [SerializeField] private Color _nozzleColor = new Color(0.95f, 0.45f, 0.1f);

        public int Order => 0; // actuator stage
        public bool IsOperational => isActiveAndEnabled;
        public float CurrentThrottle => _throttle;
        public float MaxThrust         => Tweakables.Get(Tweakables.ThrusterMaxThrust);
        private float IdleThrottle     => Tweakables.Get(Tweakables.ThrusterIdle);
        private float ThrottleResponse => Tweakables.Get(Tweakables.ThrusterResponse);

        private Rigidbody _rb;
        private RobotDrive _drive;
        private float _throttle;
        private ParticleSystem _plumePs;
        private ParticleSystem.EmissionModule _plumeEmission;
        private bool _plumeBound;
        // Headline emission when throttle = 1. Tuned so a four-thruster
        // chassis at full throttle reads as "engaged" without smearing
        // into a continuous flame sheet — see VfxSpawner header for the
        // perf-cap rationale (this is per-thruster but capped at
        // PlumeMaxRate).
        private const float PlumeMaxRate = 60f;

        // Audio: ignite / shutdown one-shots fire on throttle threshold
        // crossings rather than every tick. Hysteresis prevents a noisy
        // joystick from re-triggering the cue every other frame.
        private const float AudioIgniteThreshold  = 0.55f;
        private const float AudioShutdownThreshold = 0.45f;
        private bool _audioIgnited;

        private void Awake()
        {
            EnsureRig();
        }

        private void OnEnable()
        {
            _rb = GetComponentInParent<Rigidbody>();
            _drive = GetComponentInParent<RobotDrive>();
            _drive?.Register(this);
            Debug.Log(
                $"[Robogame] Thruster live values (source=Tweakables): " +
                $"maxThrust={MaxThrust:F1} idle={IdleThrottle:F2} response={ThrottleResponse:F2}",
                this);
        }

        private void OnDisable()
        {
            _drive?.Unregister(this);
        }

        public void Tick(in DriveControl control)
        {
            if (_rb == null) return;

            // Throttle from Move.y (W = full, S = idle off). Vertical is
            // reserved for pitch on aircraft (space/shift).
            float target = Mathf.Clamp01(IdleThrottle + 0.5f * control.Move.y);
            _throttle = ThrottleResponse <= 0f
                ? target
                : Mathf.MoveTowards(_throttle, target, ThrottleResponse * control.DeltaTime);

            UpdatePlumeEmission();

            float thrust = _throttle * MaxThrust;
            if (thrust <= 0f) return;

            // Push along this thruster's forward axis (which is also the
            // chassis forward, since blocks inherit chassis orientation).
            _rb.AddForceAtPosition(transform.forward * thrust, transform.position);
        }

        private void UpdatePlumeEmission()
        {
            if (!_plumeBound || _plumePs == null) return;
            // Emission rate scales with throttle. 0.05 floor lets the
            // idle-throttle band (0.4 default) read as a small pilot
            // flame without producing zero particles at hard cutoff.
            float t = Mathf.Max(0f, _throttle - 0.05f);
            _plumeEmission.rateOverTime = t * PlumeMaxRate;

            // Audio: ignite when throttle crosses up through the
            // ignite threshold, shutdown when it crosses down through
            // the shutdown threshold. Hysteresis between the two
            // prevents stutter at the boundary. Both cues are 3D so
            // they pan and attenuate from the thruster's world position.
            if (!_audioIgnited && _throttle >= AudioIgniteThreshold)
            {
                _audioIgnited = true;
                AudioRouter.PlayOneShot(AudioCue.ThrusterIgnite, transform.position);
            }
            else if (_audioIgnited && _throttle <= AudioShutdownThreshold)
            {
                _audioIgnited = false;
                AudioRouter.PlayOneShot(AudioCue.ThrusterShutdown, transform.position);
            }
        }

        // -----------------------------------------------------------------
        // Visual rig
        // -----------------------------------------------------------------

        private static Material s_nozzleMaterial;
        private static Material s_plumeMaterial;
        private static Mesh s_plumeMesh;

        private void EnsureRig()
        {
            BlockVisuals.HideHostMesh(gameObject);
            BuildPlume();
            if (_nozzle != null) return;

            _nozzle = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Nozzle", PrimitiveType.Cube);
            _nozzle.localScale = new Vector3(0.6f, 0.6f, 0.9f);

            // Glowing cone at the back. Cylinder long axis defaults to +Y;
            // rotate 90° so it lies along the thruster's Z (back) axis.
            Transform flame = BlockVisuals.GetOrCreatePrimitiveChild(_nozzle, "Flame", PrimitiveType.Cylinder);
            flame.localPosition = new Vector3(0f, 0f, -0.7f);
            flame.localRotation = Quaternion.Euler(90f, 0f, 0f);
            flame.localScale = new Vector3(0.5f, 0.4f, 0.5f);

            MeshRenderer fmr = flame.GetComponent<MeshRenderer>();
            if (fmr != null)
            {
                if (s_nozzleMaterial == null)
                {
                    s_nozzleMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = _nozzleColor };
                }
                fmr.sharedMaterial = s_nozzleMaterial;
            }
        }

        // Procedural particle plume parented to the thruster's transform
        // so it follows chassis motion. World-space simulation: as the
        // chassis advances, particles trail behind realistically instead
        // of dragging along.
        private void BuildPlume()
        {
            Transform existing = transform.Find("Plume");
            GameObject plumeGo;
            if (existing != null)
            {
                plumeGo = existing.gameObject;
                _plumePs = plumeGo.GetComponent<ParticleSystem>();
            }
            else
            {
                plumeGo = new GameObject("Plume");
                plumeGo.transform.SetParent(transform, worldPositionStays: false);
                // Plume emits along -Z (out the rear). Rotate the whole
                // GO so its local +Z (cone "forward") points backwards.
                plumeGo.transform.localPosition = new Vector3(0f, 0f, -0.55f);
                plumeGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _plumePs = plumeGo.AddComponent<ParticleSystem>();
                ConfigurePlumeSystem(_plumePs);
            }

            _plumeEmission = _plumePs.emission;
            _plumeEmission.rateOverTime = 0f;
            _plumeBound = true;
        }

        private static void ConfigurePlumeSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.36f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Robogame.Core.RuntimePalette.HotCore,
                Robogame.Core.RuntimePalette.Hazard);
            main.maxParticles = 64;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // driven from CurrentThrottle

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.12f;
            shape.length = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Robogame.Core.RuntimePalette.HotCore, 0f),
                    new GradientColorKey(Robogame.Core.RuntimePalette.Hazard,  0.5f),
                    new GradientColorKey(Robogame.Core.RuntimePalette.SmokeDark, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 1.0f),
                    new Keyframe(0.4f, 1.2f),
                    new Keyframe(1f, 0.4f)));

            ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            rend.renderMode = ParticleSystemRenderMode.Mesh;

            if (s_plumeMesh == null)
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_plumeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
            }
            rend.mesh = s_plumeMesh;

            if (s_plumeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Particles/Standard Unlit")
                                ?? Shader.Find("Sprites/Default");
                s_plumeMaterial = new Material(shader) { name = "ThrusterPlumeMat" };
                if (s_plumeMaterial.HasProperty("_Surface")) s_plumeMaterial.SetFloat("_Surface", 1f);
                if (s_plumeMaterial.HasProperty("_Blend"))   s_plumeMaterial.SetFloat("_Blend",   1f); // additive
            }
            rend.sharedMaterial = s_plumeMaterial;
        }
    }
}
