using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Pure hit-angle → damage-multiplier math for wedge armour. Lives in
    /// Combat so the block layer stays ignorant of damage rules — callers
    /// (ProjectileWorld, MomentumImpactHandler) thread the returned float
    /// into <see cref="BlockGrid.ApplySplashDamage"/>'s ring-0 multiplier.
    /// </summary>
    public static class ArmorDeflection
    {
        /// <summary>
        /// Damage multiplier for a hit on <paramref name="hitBlock"/>.
        /// Returns 1 for anything that is not a live wedge block. For a
        /// wedge, incidence is measured between the incoming travel
        /// direction and the surface normal: 0° = head-on (full damage),
        /// 90° = graze. Deflection lerps from 1 at
        /// <see cref="ArmorConfig.WedgeDeflectStartDeg"/> down to
        /// <see cref="ArmorConfig.WedgeMinMultiplier"/> at 90°.
        /// </summary>
        /// <remarks>
        /// Prototype note (wave-1 option A): the wedge keeps the standard
        /// axis-aligned box collider, so <paramref name="hitNormal"/> is
        /// whatever PhysX reports for the cube face — the sloped visual and
        /// the mechanic can disagree on ramp-face hits. Allocation-free.
        /// </remarks>
        public static float ComputeMultiplier(BlockBehaviour hitBlock, Vector3 hitNormal, Vector3 incomingDir)
        {
            if (hitBlock == null || hitBlock.Definition == null) return 1f;
            if (hitBlock.Definition.Id != BlockIds.WedgeArmor) return 1f;

            float sqrN = hitNormal.sqrMagnitude;
            float sqrD = incomingDir.sqrMagnitude;
            if (sqrN < 1e-6f || sqrD < 1e-6f) return 1f;

            // |cos| between travel dir and normal: 1 = head-on, 0 = graze.
            float cos = Mathf.Abs(Vector3.Dot(hitNormal, incomingDir)) / Mathf.Sqrt(sqrN * sqrD);
            float incidenceDeg = Mathf.Acos(Mathf.Clamp01(cos)) * Mathf.Rad2Deg;

            ArmorConfig cfg = ArmorConfig.Instance;
            if (incidenceDeg <= cfg.WedgeDeflectStartDeg) return 1f;

            float t = Mathf.InverseLerp(cfg.WedgeDeflectStartDeg, 90f, incidenceDeg);
            return Mathf.Lerp(1f, cfg.WedgeMinMultiplier, t);
        }
    }
}
