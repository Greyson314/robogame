using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// A jump spring. Mounted on a chassis face (typically the underside),
    /// it fires a single cooldown-gated impulse on the jump input (Space),
    /// shoving the chassis off whatever its outward face braces against.
    /// For an underside-mounted spring that's straight up — a "jump button"
    /// for ground-bound bots. Side / top mounts dash the chassis along the
    /// corresponding axis, so the same block generalises to lateral hops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standalone <see cref="MonoBehaviour"/> in the <see cref="HoverBladeBlock"/>
    /// mould: no extra Rigidbody / joint (invariants #4, #5), all force goes
    /// through the existing chassis Rigidbody via <see cref="Rigidbody.AddForceAtPosition"/>.
    /// Zero per-frame allocation (invariant #6): refs cached in
    /// <see cref="OnEnable"/>, steady-state FixedUpdate is arithmetic only.
    /// </para>
    /// <para>
    /// Launch direction is <c>-transform.up</c>. <see cref="BlockGrid.PlaceBlock"/>
    /// rotates a block so its local +Y (<c>transform.up</c>) points along its
    /// mount-up = chassis-OUTWARD direction; the spring pushes the chassis the
    /// opposite way (inward, off the braced surface), so an underside spring
    /// (mount-up = chassis-down) launches the chassis up. Works on flat and
    /// spherical arenas because it's derived from the chassis pose, not world
    /// axes.
    /// </para>
    /// <para>
    /// Input is read by latching the rising edge of <see cref="IInputSource.Vertical"/>
    /// in our own FixedUpdate (no <see cref="IInputSource"/> change, no
    /// WasPressedThisFrame-in-FixedUpdate timing footgun). The Space-bound
    /// "Jump" action already feeds <c>Vertical = +1</c>; on a pure ground bot
    /// nothing else consumes it, so Space reads cleanly as "jump".
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class SpringBlock : MonoBehaviour
    {
        // Above this |Vertical| we treat the jump input as held. The "Jump"
        // action is a 0/1 button via Vertical = up - down, so 0.5 cleanly
        // separates pressed from released without catching analog drift.
        private const float VerticalThreshold = 0.5f;

        // Coil squash on launch + restore rate (visual only).
        private const float CoilCompressedScaleY = 0.45f;
        private const float CoilRestorePerSecond = 6f;

        private BlockBehaviour _block;
        private Rigidbody _chassisRb;
        private IInputSource _input;
        private SpringTuningConfig _tuning = SpringTuningConfig.Default;

        private float _cooldownRemaining;
        private bool _wasVerticalPositive;
        private bool _active = true;

        private Transform _coil;
        private float _coilRestScaleY = 1f;

        /// <summary>True while the spring is recharged and able to launch.</summary>
        public bool IsReady => _active && _cooldownRemaining <= 0f;

        private void Awake()
        {
            _block = GetComponent<BlockBehaviour>();
            BlockVisuals.HideHostMesh(gameObject);
            EnsureRig();
        }

        private void OnEnable()
        {
            _chassisRb = GetComponentInParent<Rigidbody>();
            _input = GetComponentInParent<IInputSource>();
            if (_block != null) _block.Destroyed += HandleDestroyed;
            ResolveTuning();
            Tweakables.Changed += ResolveTuning;
        }

        private void OnDisable()
        {
            if (_block != null) _block.Destroyed -= HandleDestroyed;
            Tweakables.Changed -= ResolveTuning;
        }

        private void ResolveTuning()
        {
            _tuning = SpringTuningConfig.Default;
            DevTuningOverride.ApplySpring(ref _tuning);
        }

        private void HandleDestroyed(BlockBehaviour _) => _active = false;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (_cooldownRemaining > 0f) _cooldownRemaining -= dt;
            RestoreCoil(dt);

            if (!_active || _chassisRb == null || _input == null)
            {
                _wasVerticalPositive = false;
                return;
            }

            bool verticalPositive = _input.Vertical > VerticalThreshold;
            bool risingEdge = verticalPositive && !_wasVerticalPositive;
            _wasVerticalPositive = verticalPositive;

            if (!risingEdge || _cooldownRemaining > 0f) return;

            Launch();
        }

        private void Launch()
        {
            // Push the chassis off the surface the spring braces against:
            // the chassis-INWARD direction of the mount face = -transform.up.
            Vector3 launchDir = -transform.up;
            float impulse = SpringSolver.ResolveImpulse(_block != null ? _block.ConfigValue : 0f, _tuning.DefaultImpulse);

            _chassisRb.AddForceAtPosition(launchDir * impulse, transform.position, ForceMode.Impulse);
            _cooldownRemaining = _tuning.Cooldown;

            // VFX + audio (invariant #8). Burst fires along the launch axis;
            // a sharp 8-bit "boing" localises to the spring's world position.
            VfxSpawner.Spawn(VfxKind.SpringBurst, transform.position, launchDir);
            AudioRouter.PlayOneShot(AudioCue.SpringLaunch, transform.position);

            // Snap the coil into a compressed pose; RestoreCoil eases it back
            // out, reading as the spring extending after the kick.
            if (_coil != null)
            {
                Vector3 s = _coil.localScale;
                _coil.localScale = new Vector3(s.x, _coilRestScaleY * CoilCompressedScaleY, s.z);
            }
        }

        private void RestoreCoil(float dt)
        {
            if (_coil == null) return;
            Vector3 s = _coil.localScale;
            if (Mathf.Approximately(s.y, _coilRestScaleY)) return;
            float y = Mathf.MoveTowards(s.y, _coilRestScaleY, CoilRestorePerSecond * _coilRestScaleY * dt);
            _coil.localScale = new Vector3(s.x, y, s.z);
        }

        // -----------------------------------------------------------------
        // Visual rig: a stubby coil (scaled cylinder) on the chassis-outward
        // face plus a small foot plate. Block-local +Y is chassis-outward
        // (same convention HoverBladeBlock documents), so an underside spring
        // reads as a coil hanging beneath the chassis pointing at the ground.
        // -----------------------------------------------------------------
        private void EnsureRig()
        {
            if (_coil == null)
            {
                _coil = BlockVisuals.GetOrCreatePrimitiveChild(transform, "Coil", PrimitiveType.Cylinder);
                _coil.localRotation = Quaternion.identity;
                _coil.localPosition = new Vector3(0f, 0.35f, 0f);
                _coil.localScale = new Vector3(0.45f, 0.35f, 0.45f);
                _coilRestScaleY = _coil.localScale.y;
                TintRenderer(_coil, RuntimePalette.SlateLight);
            }

            Transform foot = BlockVisuals.GetOrCreatePrimitiveChild(_coil, "Foot", PrimitiveType.Cylinder);
            foot.localRotation = Quaternion.identity;
            // Cylinder default is 2 units tall along Y; place the foot at the
            // outward end and flatten it into a contact plate.
            foot.localPosition = new Vector3(0f, 1.0f, 0f);
            foot.localScale = new Vector3(1.4f, 0.12f, 1.4f);
            TintRenderer(foot, RuntimePalette.Caution);
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
            mpb.SetColor(s_baseColorId, colour);
            mpb.SetColor(s_albedoColorId, colour);
            mpb.SetColor(s_legacyColorId, colour);
            mr.SetPropertyBlock(mpb);
        }
    }
}
