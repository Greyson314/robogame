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

        private void Awake()
        {
            EnsureRig();
            if (_mount == null) _mount = GetComponentInParent<WeaponMount>();
            if (_gun == null) _gun = GetComponent<ProjectileGun>();
            if (_gun == null) _gun = gameObject.AddComponent<ProjectileGun>();
            _gun.SetMuzzle(_muzzle);
        }

        private void LateUpdate()
        {
            if (_yoke == null || _muzzle == null) return;

            Vector3 aim = _mount != null
                ? _mount.AimPoint
                : transform.position + transform.forward * 30f;

            // ---- Yaw: rotate the whole block around Y to face aim. ----
            Vector3 flat = aim - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
            {
                Quaternion targetWorldYaw = Quaternion.LookRotation(flat, Vector3.up);
                Quaternion parentInv = transform.parent != null
                    ? Quaternion.Inverse(transform.parent.rotation)
                    : Quaternion.identity;
                Quaternion targetLocal = parentInv * targetWorldYaw;

                transform.localRotation = _yawSpeed <= 0f
                    ? targetLocal
                    : Quaternion.Slerp(transform.localRotation, targetLocal,
                        1f - Mathf.Exp(-_yawSpeed * Time.deltaTime));
            }

            // ---- Pitch: rotate yoke around its local X. ----
            // Compute the elevation angle in the post-yaw block frame so yaw
            // and pitch are properly orthogonal.
            Vector3 localAim = transform.InverseTransformPoint(aim) - _yoke.localPosition;
            float horiz = new Vector2(localAim.x, localAim.z).magnitude;
            // Unity X-rot: positive = look down. Atan2(-y, horiz) => up = negative.
            float pitchDeg = Mathf.Atan2(-localAim.y, horiz) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Clamp(pitchDeg, _minPitch, _maxPitch);

            Quaternion targetPitch = Quaternion.Euler(pitchDeg, 0f, 0f);
            _yoke.localRotation = _pitchSpeed <= 0f
                ? targetPitch
                : Quaternion.Slerp(_yoke.localRotation, targetPitch,
                    1f - Mathf.Exp(-_pitchSpeed * Time.deltaTime));

            // ---- Muzzle: precise lookat for cross-block convergence. ----
            Vector3 dir = aim - _muzzle.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                _muzzle.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        // -----------------------------------------------------------------
        // Rig construction
        // -----------------------------------------------------------------

        private void EnsureRig()
        {
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
