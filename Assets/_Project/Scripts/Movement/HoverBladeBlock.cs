using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Raycast-based spring-damper hover propulsion. Variable footprint
    /// (N×N×1 cells, N ∈ {2,3,4}) via the variable-dim pattern. Applies
    /// upward force on the parent chassis Rigidbody at this blade's
    /// world position whenever ground is within range and the gap to
    /// ground is less than the target altitude. Force is clamped ≥ 0 so
    /// the blade can never propel the chassis above its target altitude
    /// or pull it downward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First non-joint propulsion block — see docs/subsystems/physics.md
    /// §6. Models the Robocraft hover blade: lift-only (no forward thrust,
    /// no native strafe), passive auto-leveling via attach-point torque,
    /// dramatic per-corner failure on destruction.
    /// </para>
    /// <para>
    /// Uses <see cref="GravityField.SampleAt"/> for "down", so the same
    /// code path works on flat and spherical arenas. No additional
    /// Rigidbody or joint — invariants #4 and #5 are honored structurally.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class HoverBladeBlock : MonoBehaviour
    {
        // Baseline numerics for N=2 live on HoverBladeTuningConfig.Default.
        // Lift and damping scale with N² per-instance; mass and CPU do NOT
        // scale in v1 (would require modifying Robot.RecalculateStats —
        // documented as a v2 followup in docs/changes/99-hoverblade-v1.md).
        // Size constants live on BlockOccupancy.HoverBladeMin/Max/DefaultSize
        // as the shared source of truth (variant panel + occupancy + this
        // file all read from there).
        private const float MaxRaycastDistance  = 4.0f;   // metres — beyond this the blade contributes zero

        // VFX emission: headline rate at peak lift (size-2 baseline). The
        // emission rate scales with both lift normalised against full
        // spring force AND with N² so a bigger blade visually blows more
        // dust. Capped to keep four-blade chassis under the VfxSpawner's
        // budget conventions even though each blade owns its own PS.
        private const float PlumeMaxRate = 90f;

        // Reusable hit buffer (no per-frame allocations — invariant #6).
        // Sized for the worst case of overlapping colliders on a stack of
        // chassis under one blade; same width as WheelBlock.s_hitBuffer.
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[8];

        [Header("Layers")]
        [Tooltip("Layers the hover blade's downward raycast can rest on. " +
                 "Default = all; self-chassis hits are filtered by rigidbody match.")]
        [SerializeField] private LayerMask _groundMask = ~0;

        private BlockBehaviour _block;
        private BlockGrid _grid;
        private Rigidbody _chassisRb;
        private HoverDriveSubsystem _drive;
        private bool _active = true;
        private bool _wasInRange;

        /// <summary>
        /// True when this blade's last <see cref="FixedUpdate"/> raycast
        /// hit ground inside the spring's max range. Read by
        /// <see cref="HoverDriveSubsystem"/> to gate native hover thrust
        /// — a blade in midair (no ground contact) can't propel the
        /// chassis laterally, only fall.
        /// </summary>
        public bool HasGroundContact => _active && _wasInRange;

        /// <summary>
        /// This blade's per-instance lift capacity, in "size-2 baseline
        /// units" (N/2)². Read by <see cref="HoverDriveSubsystem"/> to
        /// compute the chassis-wide max hover ceiling — more blades or
        /// bigger blades = higher ceiling.
        /// </summary>
        public float LiftScale => _liftScale;
        // Per-instance scale factor applied to spring + damping. N²/baseline²
        // = (N/2)². Recomputed on enable and when Dims changes.
        private float _liftScale = 1f;
        // Resolved tuning (baseline + dev override layer). Refreshed in
        // OnEnable and on every Tweakables.Changed pulse so slider drags
        // take effect live in editor / development builds.
        private HoverBladeTuningConfig _tuning = HoverBladeTuningConfig.Default;
        // Offset from transform.position to the AABB center, expressed in
        // both chassis-local (for physics/raycast) and block-local (for
        // visual placement of the disc + plume children). transform sits
        // at gridPos — the "near corner" of the N×N footprint — so without
        // this shift all four corner blades push from the same block-
        // local offset and the chassis tips toward one side.
        private Vector3 _aabbCenterShiftChassis;
        private Vector3 _aabbCenterShiftBlockLocal;

        // Audio — looped while the blade is producing lift, modulated by
        // lift magnitude. ContactLost one-shot fires when raycast goes
        // hitting → missing (cliff edge, terraformed pit).
        private AudioLoopHandle _loop;

        // VFX — persistent ParticleSystem child (ThrusterBlock plume
        // pattern). Toggling emission rate rather than spawning per-frame
        // keeps allocations zero and stays within the VfxSpawner budget
        // even on four-blade chassis. See planner Phase 5 note.
        private ParticleSystem _plumePs;
        private ParticleSystem.EmissionModule _plumeEmission;
        private bool _plumeBound;

        private void Awake()
        {
            _block = GetComponent<BlockBehaviour>();
            BlockVisuals.HideHostMesh(gameObject);
            EnsureRig();
        }

        private void OnEnable()
        {
            _chassisRb = GetComponentInParent<Rigidbody>();
            _grid = GetComponentInParent<BlockGrid>();
            _drive = GetComponentInParent<HoverDriveSubsystem>();
            if (_block != null)
            {
                _block.Destroyed += HandleDestroyed;
                _block.DimsChanged += HandleDimsChanged;
            }
            ResolveTuning();
            Tweakables.Changed += ResolveTuning;
            RecomputeLiftScale();
            // Self-register with the chassis-level drive so the ceiling
            // formula and shared target-altitude state include this blade.
            // The drive subsystem runs its own SeedBladesFromHierarchy at
            // OnEnable but the binder adds HoverBladeBlock components
            // AFTER the drive subsystem activates — by which time the seed
            // has already run with zero blades visible. Self-registration
            // is the canonical pickup path.
            if (_drive != null) _drive.RegisterBlade(this);

            // Start the loop voice now so it tracks live block lifetime.
            // ChassisWindAudio pattern: idempotent re-check via IsValid so
            // snapshot capture / re-enable doesn't double-allocate.
            if (_loop == null || !_loop.IsValid)
            {
                _loop = AudioRouter.PlayLoop(AudioCue.HoverBladeLoop, transform);
                if (_loop != null) _loop.SetBaseVolume(0f); // ramps up from FixedUpdate
            }
        }

        private void OnDisable()
        {
            if (_block != null)
            {
                _block.Destroyed -= HandleDestroyed;
                _block.DimsChanged -= HandleDimsChanged;
            }
            Tweakables.Changed -= ResolveTuning;
            if (_drive != null) _drive.UnregisterBlade(this);
            _loop?.Stop();
            _loop = null;
        }

        private void ResolveTuning()
        {
            _tuning = HoverBladeTuningConfig.Default;
            DevTuningOverride.ApplyHoverBlade(ref _tuning);
        }

        private void OnDestroy()
        {
            _loop?.Stop();
            _loop = null;
        }

        private void HandleDimsChanged(BlockBehaviour _) => RecomputeLiftScale();

        private void HandleDestroyed(BlockBehaviour _)
        {
            // Stop applying lift the instant the block dies. The visual
            // rig + the block GameObject itself are destroyed by
            // BlockGrid's removal flow; we just need to ensure the audio
            // loop is stopped before that happens (OnDestroy also calls
            // Stop, idempotent — this just avoids one extra audio frame).
            _active = false;
            if (_plumeBound) _plumeEmission.rateOverTime = 0f;
            _loop?.Stop();
            _loop = null;
        }

        private void RecomputeLiftScale()
        {
            int n = ResolveSize();
            // (N/baseline)² so size-2 = 1.0, size-3 = 2.25, size-4 = 4.0.
            float ratio = n / (float)BlockOccupancy.HoverBladeDefaultSize;
            _liftScale = ratio * ratio;

            // Lateral shift from transform (gridPos) to AABB center. Mirrors
            // BlockOccupancy.ComputeHoverBladeSweptBoundsLocal: (N-1)/2 in
            // each axis perpendicular to mount-up, zero on mount-up axis.
            Vector3Int upN = _block != null && _block.Up != Vector3Int.zero
                ? _block.Up
                : Vector3Int.up;
            int axMountUp = Mathf.Abs(upN.x) > 0 ? 0 : (Mathf.Abs(upN.y) > 0 ? 1 : 2);
            float half = (n - 1) * 0.5f;
            Vector3 shiftChassis = new Vector3(half, half, half);
            shiftChassis[axMountUp] = 0f;
            _aabbCenterShiftChassis = shiftChassis;
            // Block-local equivalent (the disc/plume children are parented
            // to this transform whose localRotation = OrientationFromUp).
            Quaternion blockToChassis = BlockGrid.OrientationFromUp(_block != null ? _block.Up : Vector3Int.up);
            _aabbCenterShiftBlockLocal = Quaternion.Inverse(blockToChassis) * shiftChassis;

            ResizeRig();
        }

        // World-space position where lift force is applied AND where the
        // raycast originates. Equal to the AABB center, NOT transform.position.
        // transform.position is the "near corner" of the N×N footprint —
        // using it directly would offset all four corner blades by the
        // same block-local vector, biasing lift toward one side and tipping
        // the chassis.
        //
        // _aabbCenterShiftChassis is in cell units; multiply by CellSize to
        // get a world-metre delta before applying the chassis transform.
        private Vector3 GetLiftWorldPosition()
        {
            if (_grid == null) return transform.position;
            return transform.position
                 + _grid.transform.TransformVector(_aabbCenterShiftChassis * _grid.CellSize);
        }

        /// <summary>
        /// Clamp the per-instance size (read from <see cref="BlockBehaviour.Dims"/>.x,
        /// rounded to nearest integer) into the valid range. Default size
        /// applies when Dims hasn't been initialised yet (zero vector).
        /// </summary>
        private int ResolveSize()
        {
            if (_block == null) return BlockOccupancy.HoverBladeDefaultSize;
            return BlockOccupancy.ResolveHoverBladeSize(_block.Dims);
        }

        private void FixedUpdate()
        {
            using var _scope = PerfMarkers.HoverBladeFixedUpdate.Auto();
            if (!_active || _chassisRb == null)
            {
                if (_plumeBound) _plumeEmission.rateOverTime = 0f;
                _loop?.SetBaseVolume(0f);
                return;
            }

            // Sample current gravity at this blade's world position. On
            // flat arenas this is world-down; on planet arenas it points
            // toward the planet center. Either way, lift opposes it.
            Vector3 gravity = GravityField.SampleAt(transform.position);
            if (gravity.sqrMagnitude < 1e-4f) return;
            Vector3 gravityDir = gravity.normalized;

            // Target altitude + max raycast may be driven by the chassis-
            // level HoverDriveSubsystem (so Space/Shift can raise/lower
            // the whole craft as a unit). When absent (test rigs, or any
            // chassis without a HoverDriveSubsystem present), fall back
            // to the baseline tuning constants. `_drive != null` uses
            // Unity's overloaded == so a destroyed-but-not-yet-cleared
            // reference reads as null.
            bool driveAlive = _drive != null;
            float targetAlt = driveAlive ? _drive.CurrentTargetAltitude : _tuning.TargetAltitude;
            float maxRay    = driveAlive ? _drive.EffectiveMaxRaycast   : MaxRaycastDistance;

            Vector3 origin = GetLiftWorldPosition();
            if (!RaycastIgnoringSelf(origin, gravityDir, maxRay, out RaycastHit hit))
            {
                // Ray missed (terraformed pit, off a cliff, above max
                // range). Zero lift; gravity does the rest.
                HandleContactState(false);
                ApplyIdleFeedback();
                return;
            }
            float gap = hit.distance;

            // Spring term, clamped ≥ 0. At gap >= targetAltitude the
            // blade contributes zero force — no suction toward the
            // ground, no propulsion above target altitude.
            float springForce = _tuning.SpringK * _liftScale * (targetAlt - gap);
            if (springForce <= 0f)
            {
                HandleContactState(true);
                ApplyIdleFeedback();
                return;
            }

            // Damping is GATED to active spring (only applies when the
            // spring is producing lift). This prevents the blade from
            // feeling like a drag when the chassis is being thrust up
            // through the spring range — once above target altitude, no
            // damping, just gravity.
            Vector3 pointVel = _chassisRb.GetPointVelocity(origin);
            // verticalVel > 0 means moving AWAY from ground (climbing).
            // SpringSolver subtracts damping × verticalVel from the spring
            // term (and clamps ≥ 0) so a climbing chassis is decelerated —
            // the shared spring math (session 104), behaviour-identical to
            // the prior inline springForce − dampForce.
            float verticalVel = Vector3.Dot(pointVel, -gravityDir);
            float liftMagnitude = SpringSolver.HookeDamped(
                _tuning.SpringK * _liftScale,
                _tuning.DampingC * _liftScale,
                gap, targetAlt, verticalVel);
            if (liftMagnitude <= 0f)
            {
                HandleContactState(true);
                ApplyIdleFeedback();
                return;
            }

            HandleContactState(true);
            _chassisRb.AddForceAtPosition(-gravityDir * liftMagnitude, origin);
            ApplyActiveFeedback(liftMagnitude, springForce);
        }

        // Track hit-state transitions so a contact-lost cue fires when
        // the blade goes from "had ground under it" to "doesn't anymore."
        private void HandleContactState(bool inRange)
        {
            if (_wasInRange && !inRange)
            {
                AudioRouter.PlayOneShot(AudioCue.HoverBladeContactLost, transform.position);
            }
            _wasInRange = inRange;
        }

        // Audio + VFX update for an actively-lifting frame. Headline volume
        // and emission rate scale with lift fraction (current lift /
        // theoretical max at this size).
        private void ApplyActiveFeedback(float liftMagnitude, float maxSpring)
        {
            float t = Mathf.Clamp01(liftMagnitude / Mathf.Max(1f, maxSpring));
            _loop?.SetBaseVolume(0.15f + 0.45f * t); // floor of 0.15 so a lightly-lifting blade is still audible
            _loop?.SetPitch(0.90f + 0.20f * t);
            if (_plumeBound)
            {
                // Rate also scales with N²-derived _liftScale so a size-4
                // blade visually blows ~4× the dust of a size-2 even at
                // identical normalised t.
                _plumeEmission.rateOverTime = PlumeMaxRate * _liftScale * t;
            }
        }

        // Audio + VFX silence for an idle / out-of-range frame.
        private void ApplyIdleFeedback()
        {
            _loop?.SetBaseVolume(0f);
            if (_plumeBound) _plumeEmission.rateOverTime = 0f;
        }

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(origin, dir, s_hitBuffer, maxDist, _groundMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = s_hitBuffer[i];
                if (h.collider.attachedRigidbody == _chassisRb) continue; // self
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        // -----------------------------------------------------------------
        // Visual rig
        //
        // Block-local +Y points chassis-OUTWARD (away from the host face),
        // mirroring WheelBlock's convention. For a bottom-mounted blade
        // (mount-up = chassis -Y), block-local +Y is world-down, so the
        // disc sits below the chassis face and the plume emits toward the
        // ground. For other mounts the relative geometry still tracks
        // mount-up, so a top-mounted blade points its plume away from
        // the chassis (correct for visuals; physics happens to also
        // work because the raycast follows gravity, not block axes).
        // -----------------------------------------------------------------

        private Transform _disc;
        private Transform _hub;
        private static readonly Color s_hubColour = new Color(0.45f, 0.46f, 0.48f, 1f);
        private static Material s_plumeMaterial;
        private static Mesh s_plumeMesh;

        private void EnsureRig()
        {
            // Flat disc as the lift surface. Cylinder default = 2 m along
            // Y, diameter 1. Scale into a disc that fills the N×N
            // footprint. The disc lives on the chassis-OUTWARD side
            // (block-local +Y) so a bottom-mounted blade reads as a fan
            // hanging under the chassis.
            if (_disc == null)
            {
                _disc = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Disc", PrimitiveType.Cylinder);
                _disc.localRotation = Quaternion.identity;
                _disc.localPosition = new Vector3(0f, 0.4f, 0f);
            }
            // Hub: small contrast cylinder centered on the disc, slightly
            // proud on the outward face. Reads as the fan housing.
            if (_hub == null)
            {
                _hub = BlockVisuals.GetOrCreatePrimitiveChild(_disc, "Hub", PrimitiveType.Cylinder);
                _hub.localRotation = Quaternion.identity;
                _hub.localPosition = new Vector3(0f, 0.1f, 0f);
                TintRenderer(_hub, s_hubColour);
            }

            ResizeRig();
            BuildPlume();
        }

        private void ResizeRig()
        {
            int n = ResolveSize();
            if (_disc != null)
            {
                _disc.localScale = new Vector3(n, 0.12f, n);
                // Position disc at the AABB center (block-local lateral
                // shift) PLUS the standard chassis-outward visual offset
                // (0.4 along block-local +Y = mount-up direction).
                _disc.localPosition = new Vector3(
                    _aabbCenterShiftBlockLocal.x,
                    0.4f + _aabbCenterShiftBlockLocal.y,
                    _aabbCenterShiftBlockLocal.z);
            }
            if (_hub != null)
            {
                float hubD = 0.5f / Mathf.Max(1, n);
                float hubH = 0.5f;
                _hub.localScale = new Vector3(hubD, hubH, hubD);
            }
            // Plume radius scales with footprint so a size-4 blade blows
            // dust across its whole bottom face.
            if (_plumeBound && _plumePs != null)
            {
                var shape = _plumePs.shape;
                shape.radius = Mathf.Max(0.35f, n * 0.45f);
                // Plume origin also tracks the AABB center, on the
                // outward side of the disc.
                _plumePs.transform.localPosition = new Vector3(
                    _aabbCenterShiftBlockLocal.x,
                    0.55f + _aabbCenterShiftBlockLocal.y,
                    _aabbCenterShiftBlockLocal.z);
            }
        }

        // Persistent particle plume parented to the blade. Emits in
        // block-local +Y (chassis-outward) — for a bottom-mounted blade
        // this is world-down, blowing dust into the ground. World-space
        // simulation lets the particles trail behind a moving chassis.
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
                plumeGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                plumeGo.transform.localRotation = Quaternion.identity;
                _plumePs = plumeGo.AddComponent<ParticleSystem>();
                ConfigurePlumeSystem(_plumePs);
            }

            _plumeEmission = _plumePs.emission;
            _plumeEmission.rateOverTime = 0f;
            _plumeBound = true;
        }

        private void ConfigurePlumeSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                RuntimePalette.DustLight,
                RuntimePalette.SmokeDark);
            main.maxParticles = 128;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 75f; // wide spray — air being blown across the ground
            shape.radius = 0.5f;
            shape.length = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(RuntimePalette.DustLight, 0f),
                    new GradientColorKey(RuntimePalette.SmokeDark, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0.0f,  1f),
                });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.8f),
                    new Keyframe(0.5f, 1.2f),
                    new Keyframe(1f, 1.4f)));

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
                s_plumeMaterial = new Material(shader) { name = "HoverBladeDustMat" };
                if (s_plumeMaterial.HasProperty("_Surface")) s_plumeMaterial.SetFloat("_Surface", 1f);
                if (s_plumeMaterial.HasProperty("_Blend"))   s_plumeMaterial.SetFloat("_Blend",   0f); // alpha (not additive — dust is dusty, not glowing)
            }
            rend.sharedMaterial = s_plumeMaterial;
        }

        private static readonly int s_baseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int s_albedoColorId = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");

        private static void TintRenderer(Transform t, Color colour)
        {
            if (t == null) return;
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetColor(s_baseColorId,   colour);
            mpb.SetColor(s_albedoColorId, colour);
            mpb.SetColor(s_legacyColorId, colour);
            mr.SetPropertyBlock(mpb);
        }
    }
}
