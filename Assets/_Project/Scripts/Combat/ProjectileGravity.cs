using Robogame.Core;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// One source of truth for the gravity a launched projectile integrates
    /// under. Sampled at the muzzle via <see cref="GravityField"/> so the
    /// same call is correct on flat and planet arenas.
    /// </summary>
    public static class ProjectileGravity
    {
        // TRACE[AUDIT-15]: unified muzzle gravity — cannon/mortar were chassis-relative (-parent.up), banked launchers got sideways gravity; bomb was already correct
        /// <summary>
        /// World gravity vector at <paramref name="muzzle"/>. Replaces the
        /// chassis-relative <c>-transform.parent.up * Physics.gravity.magnitude</c>
        /// that gave a banked launcher a sideways gravity component. Returns
        /// <see cref="Physics.gravity"/> if the muzzle is null.
        /// </summary>
        public static Vector3 ForMuzzle(Transform muzzle)
            => muzzle != null ? GravityField.SampleAt(muzzle.position) : Physics.gravity;
    }
}
