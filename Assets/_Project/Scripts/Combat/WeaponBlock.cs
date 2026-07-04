using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Lives on a <see cref="BlockBehaviour"/> of category <c>Weapon</c>.
    /// Builds a small turret rig: the block itself yaws (Y-axis only) so the
    /// chassis-mounted base turns, and a child "Yoke" pitches (X-axis only)
    /// so the barrel can elevate. Pairs with a <see cref="ProjectileGun"/> on
    /// the same GameObject for firing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class WeaponBlock : MonoBehaviour
    {
        [Header("Rig layout (block-local)")]
        [Tooltip("Local position of the pitch yoke pivot — sits on top of the block.")]
        [SerializeField] private Vector3 _yokeLocalOffset = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Local position of the muzzle relative to the yoke (down the barrel).")]
        [SerializeField] private Vector3 _muzzleLocalOffset = new Vector3(0f, 0f, 0.55f);

        [Header("Aim limits")]
        [Tooltip("Pitch clamp (degrees). Negative = look up, positive = look down (Unity convention).")]
        [SerializeField] private float _minPitch = -60f;
        [SerializeField] private float _maxPitch = 30f;

        [Header("Smoothing")]
        [Tooltip("How quickly the block yaws to face the aim point. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _yawSpeed = 18f;

        [Tooltip("How quickly the yoke pitches. 0 = snap.")]
        [SerializeField, Range(0f, 30f)] private float _pitchSpeed = 22f;

        [Header("Wiring (auto if blank)")]
        [SerializeField] private Transform _yoke;
        [SerializeField] private Transform _muzzle;
        [SerializeField] private WeaponMount _mount;
        [SerializeField] private ProjectileGun _gun;

        public Transform Muzzle => _muzzle;

        private Robogame.Block.BlockBehaviour _block;
        private Quaternion _restLocalRotation = Quaternion.identity;

        private void Awake()
        {
            // Rest rotation before the first Track overwrites it — the yaw
            // axis is the authored mount orientation, not gravity.
            _restLocalRotation = transform.localRotation;
            // _block must resolve before EnsureRig so the rig can read the
            // WeaponDefinition's optional turret model.
            _block = GetComponent<Robogame.Block.BlockBehaviour>();
            EnsureRig();
            if (_mount == null) _mount = GetComponentInParent<WeaponMount>();
            if (_gun == null) _gun = GetComponent<ProjectileGun>();
            if (_gun == null) _gun = gameObject.AddComponent<ProjectileGun>();
            _gun.SetMuzzle(_muzzle);
        }

        // Stylized turret model (Fatty pack) when the definition supplies one;
        // returns false to fall through to the procedural barrel below.
        private bool TryBuildTurretModel()
        {
            WeaponDefinition def = _block != null && _block.Definition != null
                ? _block.Definition.GetComponentData<WeaponDefinition>() : null;
            if (def == null || def.TurretModel == null) return false;
            if (!WeaponModelRig.TryBuild(this, def.TurretModel, def.TurretModelScale,
                    def.TurretModelOffset, out Transform yoke, out Transform muzzle))
                return false;
            _yoke = yoke;
            _muzzle = muzzle;
            return true;
        }

        private void LateUpdate()
        {
            if (_yoke == null || _muzzle == null) return;

            Vector3 aim = _mount != null
                ? _mount.AimPoint
                : transform.position + transform.forward * 30f;

            // TRACE[ADR-0003]: shared yaw/pitch/muzzle track (phase C); yaw
            // axis is the chassis-frame mount up since LOG-131, so the turret
            // follows the block through rolls.
            Vector3 up = TurretYoke.MountUp(transform, _restLocalRotation);
            new TurretYoke(transform, _yoke, _muzzle, _yawSpeed, _pitchSpeed, _minPitch, _maxPitch)
                .Track(aim, up, Time.deltaTime);
        }

        // -----------------------------------------------------------------
        // Rig construction
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
            // Prefer the authored turret model; fall back to the procedural
            // barrel when the definition has no model assigned.
            if (TryBuildTurretModel()) return;

            // Yoke pivot. New yokes need a barrel + initial offset.
            bool yokeIsNew = transform.Find("Yoke") == null;
            _yoke = BlockVisuals.GetOrCreateChild(transform, "Yoke");
            if (yokeIsNew)
            {
                _yoke.localPosition = _yokeLocalOffset;

                // Visible barrel — cylinder default points +Y, rotate 90° on
                // X to lay it down the +Z barrel axis.
                Transform barrel = BlockVisuals.GetOrCreatePrimitiveChild(_yoke, "Barrel", PrimitiveType.Cylinder);
                barrel.localPosition = new Vector3(0f, 0f, 0.4f);
                barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
                barrel.localScale = new Vector3(0.15f, 0.4f, 0.15f);
            }

            // Muzzle: child of yoke so it inherits both yaw + pitch.
            bool muzzleIsNew = _yoke.Find("Muzzle") == null;
            _muzzle = BlockVisuals.GetOrCreateChild(_yoke, "Muzzle");
            if (muzzleIsNew) _muzzle.localPosition = _muzzleLocalOffset;
        }

        /// <summary>Editor / scaffolder helper.</summary>
        public void Bind(WeaponMount mount)
        {
            _mount = mount;
        }
    }
}
