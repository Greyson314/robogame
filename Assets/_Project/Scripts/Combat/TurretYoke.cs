using Robogame.Core;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Shared turret aiming for the yaw/pitch weapon blocks. Yaws the base
    /// about the local "up" (surface-up on planet arenas, world-up on flat
    /// ones), pitches a yoke at the aim point, and points the muzzle.
    /// Extracted from <see cref="WeaponBlock"/> / <see cref="CannonBlock"/> /
    /// <see cref="GrappleMagnetBlock"/> (full look-at <see cref="Track"/>) and
    /// <see cref="MortarBlock"/> (yaw only — it drives its own lob pitch), so
    /// the spherical-aim fix lives in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bug it fixes (audit #8 / ADR-0003 phase C): every copy yawed with
    /// <c>Quaternion.LookRotation(flatXZ, Vector3.up)</c> after zeroing the
    /// world-Y of the aim vector. On the spherical / planet arena "up" is the
    /// surface normal, not world-Y, so the turret base swung toward a
    /// world-horizontal projection of the target instead of a surface-level
    /// one. The fix resolves a local up from <see cref="GravityField"/> and
    /// projects the aim onto the plane perpendicular to it.
    /// </para>
    /// <para>
    /// Pure math where it matters: <see cref="UpAt"/>,
    /// <see cref="TryYawTargetLocal"/> and <see cref="PitchDegrees"/> are
    /// static and side-effect free so the spherical regression is unit
    /// testable without a play session.
    /// </para>
    /// </remarks>
    public readonly struct TurretYoke
    {
        private const float Eps = 0.0001f;

        private readonly Transform _block;
        private readonly Transform _yoke;
        private readonly Transform _muzzle;
        private readonly float _yawSpeed;
        private readonly float _pitchSpeed;
        private readonly float _minPitch;
        private readonly float _maxPitch;

        public TurretYoke(Transform block, Transform yoke, Transform muzzle,
                          float yawSpeed, float pitchSpeed, float minPitch, float maxPitch)
        {
            _block = block;
            _yoke = yoke;
            _muzzle = muzzle;
            _yawSpeed = yawSpeed;
            _pitchSpeed = pitchSpeed;
            _minPitch = minPitch;
            _maxPitch = maxPitch;
        }

        // TRACE[AUDIT-3]: local up = opposite gravity (was Vector3.up). No longer
        // the turret yaw axis — superseded by MountUp (LOG-131) — kept for
        // gravity-frame consumers and the pinned spherical regression tests.
        /// <summary>
        /// The local "up" at <paramref name="worldPos"/>: opposite the sampled
        /// gravity, or <see cref="Vector3.up"/> where gravity is ~0. On flat
        /// arenas <see cref="GravityField.SampleAt"/> returns
        /// <c>Physics.gravity</c>, so this is exactly <c>Vector3.up</c> — the
        /// fix is a no-op off the planet.
        /// </summary>
        public static Vector3 UpAt(Vector3 worldPos)
        {
            Vector3 g = GravityField.SampleAt(worldPos);
            return g.sqrMagnitude > 1e-6f ? (-g).normalized : Vector3.up;
        }

        // TRACE[LOG-131]: turrets ride the chassis — yaw about the block's
        // authored mount axis in the parent frame, not gravity-up. Rolling
        // the bot rolls the gun with it.
        /// <summary>
        /// The block's mount "up" in world space: the up axis of its authored
        /// rest orientation (<paramref name="restLocalRotation"/>, captured
        /// before the first yaw write), rotated into the current parent
        /// (chassis) frame. Turrets yaw about this axis so they stay attached
        /// to their block through chassis rolls. On a planet a grounded bot's
        /// chassis up tracks the surface normal, so the audit #8 spherical
        /// behavior is preserved for the case it targeted; <see cref="UpAt"/>
        /// (gravity up) kept turrets world-level even on a rolled chassis,
        /// which read as the weapon detaching from its block.
        /// </summary>
        public static Vector3 MountUp(Transform block, Quaternion restLocalRotation)
        {
            Quaternion parentRot = block.parent != null ? block.parent.rotation : Quaternion.identity;
            return parentRot * (restLocalRotation * Vector3.up);
        }

        /// <summary>
        /// Target <em>local</em> yaw rotation that faces the block's forward at
        /// the aim point, level with <paramref name="localUp"/>. Returns false
        /// when the aim is directly along the up axis (degenerate) — the caller
        /// keeps the current rotation.
        /// </summary>
        public static bool TryYawTargetLocal(Vector3 blockPos, Quaternion parentRotation,
                                             Vector3 aimPoint, Vector3 localUp, out Quaternion targetLocal)
        {
            // Project onto the plane perpendicular to localUp (was: flat.y = 0,
            // which only works when up == world-Y).
            Vector3 flat = Vector3.ProjectOnPlane(aimPoint - blockPos, localUp);
            if (flat.sqrMagnitude <= Eps)
            {
                targetLocal = Quaternion.identity;
                return false;
            }
            Quaternion world = Quaternion.LookRotation(flat, localUp);
            targetLocal = Quaternion.Inverse(parentRotation) * world;
            return true;
        }

        /// <summary>
        /// Pitch (Unity X-rotation degrees, positive = nose down) to point the
        /// yoke at <paramref name="aimPoint"/>, evaluated in the block's
        /// post-yaw local frame. Frame-relative, so it inherits the corrected
        /// yaw and needs no up vector. Caller clamps to the block's limits.
        /// </summary>
        public static float PitchDegrees(Transform block, Vector3 aimPoint, Vector3 yokeLocalPos)
        {
            Vector3 localAim = block.InverseTransformPoint(aimPoint) - yokeLocalPos;
            float horiz = new Vector2(localAim.x, localAim.z).magnitude;
            return Mathf.Atan2(-localAim.y, horiz) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Yaw the base toward the aim about <paramref name="localUp"/>. Used by
        /// every turret (the lob mortar calls only this, then drives its own
        /// pitch).
        /// </summary>
        public void Yaw(Vector3 aimPoint, Vector3 localUp, float dt)
        {
            if (_block == null) return;
            Quaternion parentRot = _block.parent != null ? _block.parent.rotation : Quaternion.identity;
            if (!TryYawTargetLocal(_block.position, parentRot, aimPoint, localUp, out Quaternion targetLocal)) return;
            _block.localRotation = _yawSpeed <= 0f
                ? targetLocal
                : Quaternion.Slerp(_block.localRotation, targetLocal, 1f - Mathf.Exp(-_yawSpeed * dt));
        }

        /// <summary>
        /// Full look-at track: yaw the base, pitch the yoke at the aim, point
        /// the muzzle. For the direct-fire turrets (SMG / cannon / grapple).
        /// </summary>
        public void Track(Vector3 aimPoint, Vector3 localUp, float dt)
        {
            if (_block == null || _yoke == null || _muzzle == null) return;

            Yaw(aimPoint, localUp, dt);

            // Pitch is read after the yaw assignment (same order as the forked
            // bodies), so it uses this frame's just-slerped block frame.
            float pitchDeg = Mathf.Clamp(PitchDegrees(_block, aimPoint, _yoke.localPosition), _minPitch, _maxPitch);
            Quaternion targetPitch = Quaternion.Euler(pitchDeg, 0f, 0f);
            _yoke.localRotation = _pitchSpeed <= 0f
                ? targetPitch
                : Quaternion.Slerp(_yoke.localRotation, targetPitch, 1f - Mathf.Exp(-_pitchSpeed * dt));

            // Muzzle: precise world look-at for cross-block convergence. Roll
            // reference is localUp so a planet-arena barrel doesn't twist.
            Vector3 dir = aimPoint - _muzzle.position;
            if (dir.sqrMagnitude > Eps)
                _muzzle.rotation = Quaternion.LookRotation(dir, localUp);
        }
    }
}
