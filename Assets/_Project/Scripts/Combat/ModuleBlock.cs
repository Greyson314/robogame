using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-block carrier for one module ability. Its <see cref="Kind"/> is the
    /// block type itself (resolved from <see cref="BlockDefinition.Id"/> via
    /// <see cref="ModuleKinds.ForBlockId"/>); its per-instance power rides
    /// <see cref="BlockBehaviour.ConfigValue"/>. Holds no cooldown state — that
    /// lives on the chassis-root <see cref="ModuleSystem"/> (the
    /// server-authoritative location). This component's jobs: expose the
    /// resolved tuning + a per-fire origin, own the per-kind visual (the spring
    /// coil + its squash), report contextual availability (spring needs
    /// ground), and be the <i>destructible</i> thing whose death disables the
    /// slot — mirrors how a destroyed weapon block stops firing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class ModuleBlock : MonoBehaviour
    {
        // Spring can only fire with ground close enough to push off. Without
        // this gate you could re-fire mid-air and fly forever. Probe runs along
        // gravity (works on spherical arenas) and is short enough that a launch
        // carries the spring out of range until it falls back.
        private const float GroundProbeDistance = 2.5f;
        private static readonly RaycastHit[] s_groundHits = new RaycastHit[8];

        // Spring coil squash on launch + restore rate (visual only).
        private const float CoilCompressedScaleY = 0.45f;
        private const float CoilRestorePerSecond = 6f;

        private BlockBehaviour _bb;
        private ModuleSystem _system;
        private Rigidbody _chassisRb;

        private Transform _coil;
        private float _coilRestScaleY = 1f;

        /// <summary>The ability this block grants (its block type).</summary>
        public ModuleKind Kind { get; private set; }

        /// <summary>Per-instance power (slider value); 0 = use the kind default.</summary>
        public float Power => _bb != null ? _bb.ConfigValue : 0f;

        /// <summary>Resolved per-fire numbers at the current power.</summary>
        public ModuleTuning.Resolved Tuning => ModuleTuning.Resolve(Kind, Power);

        /// <summary>False once the carrier block is destroyed/disabled.</summary>
        public bool IsOperational => isActiveAndEnabled;

        /// <summary>
        /// Contextual gate beyond cooldown: spring needs ground to push off;
        /// every other module is always context-available. Drives the HUD's
        /// "greyed while airborne" state for the spring tile.
        /// </summary>
        public bool ContextAvailable => Kind != ModuleKind.Spring || IsGrounded();

        private void Awake()
        {
            _bb = GetComponent<BlockBehaviour>();
            Kind = ResolveKind();
            if (Kind == ModuleKind.Spring) BuildSpringCoil();
        }

        private void OnEnable()
        {
            _chassisRb = GetComponentInParent<Rigidbody>();
            _system = GetComponentInParent<ModuleSystem>();
            if (_system != null) _system.Register(this);
            if (_bb != null) _bb.Destroyed += HandleBlockDestroyed;
        }

        private void OnDisable()
        {
            if (_bb != null) _bb.Destroyed -= HandleBlockDestroyed;
            if (_system != null) _system.Unregister(this);
        }

        private void Update()
        {
            if (_coil != null) RestoreCoil(Time.deltaTime);
        }

        private void HandleBlockDestroyed(BlockBehaviour _)
        {
            // Go dark the instant the carrier dies, before connectivity tears
            // the GameObject down a frame or two later.
            if (_system != null) _system.Unregister(this);
        }

        private ModuleKind ResolveKind()
        {
            string id = _bb != null && _bb.Definition != null ? _bb.Definition.Id : null;
            ModuleKind? k = id != null ? ModuleKinds.ForBlockId(id) : null;
            return k ?? ModuleKind.EmpBurst;
        }

        /// <summary>
        /// True when ground is within <see cref="GroundProbeDistance"/> along
        /// gravity — the spring has something to brace against. Mirrors the
        /// wheel / hover self-filtered downward cast so own chassis blocks
        /// don't count as ground.
        /// </summary>
        private bool IsGrounded()
        {
            Vector3 gravity = GravityField.SampleAt(transform.position);
            if (gravity.sqrMagnitude < 1e-4f) return false;
            Vector3 down = gravity.normalized;
            int count = Physics.RaycastNonAlloc(
                transform.position, down, s_groundHits, GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_groundHits[i].collider.attachedRigidbody == _chassisRb) continue; // own chassis
                return true;
            }
            return false;
        }

        /// <summary>Squash the spring coil on launch; <see cref="RestoreCoil"/> eases it back.</summary>
        public void PlaySpringSquash()
        {
            if (_coil == null) return;
            Vector3 s = _coil.localScale;
            _coil.localScale = new Vector3(s.x, _coilRestScaleY * CoilCompressedScaleY, s.z);
        }

        // -----------------------------------------------------------------
        // Spring coil visual (lifted from the retired SpringBlock). Block-local
        // +Y is chassis-outward, so an underside spring reads as a coil hanging
        // beneath the chassis pointing at the ground.
        // -----------------------------------------------------------------
        private void BuildSpringCoil()
        {
            BlockVisuals.HideHostMesh(gameObject);
            _coil = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Coil", PrimitiveType.Cylinder);
            _coil.localRotation = Quaternion.identity;
            _coil.localPosition = new Vector3(0f, 0.35f, 0f);
            _coil.localScale = new Vector3(0.45f, 0.35f, 0.45f);
            _coilRestScaleY = _coil.localScale.y;
            TintRenderer(_coil, RuntimePalette.SlateLight);

            Transform foot = BlockVisuals.GetOrCreatePrimitiveChild(_coil, "Foot", PrimitiveType.Cylinder);
            foot.localRotation = Quaternion.identity;
            foot.localPosition = new Vector3(0f, 1.0f, 0f);
            foot.localScale = new Vector3(1.4f, 0.12f, 1.4f);
            TintRenderer(foot, RuntimePalette.Caution);
        }

        private void RestoreCoil(float dt)
        {
            Vector3 s = _coil.localScale;
            if (Mathf.Approximately(s.y, _coilRestScaleY)) return;
            float y = Mathf.MoveTowards(s.y, _coilRestScaleY, CoilRestorePerSecond * _coilRestScaleY * dt);
            _coil.localScale = new Vector3(s.x, y, s.z);
        }

        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_albedoColorId = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");

        private static void TintRenderer(Transform t, Color colour)
        {
            if (t == null) return;
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetColor(s_baseColorId, colour);
            mpb.SetColor(s_albedoColorId, colour);
            mpb.SetColor(s_legacyColorId, colour);
            mr.SetPropertyBlock(mpb);
        }
    }
}
