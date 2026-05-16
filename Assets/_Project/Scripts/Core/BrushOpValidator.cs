using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Stateless server-side validation rules for a candidate
    /// <see cref="BrushOp"/>. Phase 6 of TERRAFORMING_PLAN — the
    /// "server is authoritative" boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each rule is a single bool check, composable into the larger
    /// validation pipeline the netcode layer will run when a client
    /// submits a candidate. The current rule set is the universal half:
    /// kind in the allowed enum, radius positive and bounded, brush
    /// volume intersects the target dig zone's bounding box. The
    /// chassis-relative checks (drill owned by the claiming chassis,
    /// drill tip within ChassisAimReachableBounds) live one layer up
    /// because they need chassis context this layer doesn't carry.
    /// </para>
    /// <para>
    /// Stateless + immutable inputs by design — this is the code path
    /// the server invokes at ~30 Hz per drilling player, and it has to
    /// stay zero-alloc and trivially threadable.
    /// </para>
    /// </remarks>
    public static class BrushOpValidator
    {
        /// <summary>
        /// Hard ceiling on brush radius regardless of weapon spec. A
        /// 16m brush would carve a 32m crater — bigger than a chunk —
        /// and is almost certainly a malformed op or a cheat attempt.
        /// </summary>
        public const float MaxRadiusMeters = 16f;

        /// <summary>
        /// Run all universal validation rules against <paramref name="op"/>.
        /// Returns true if the op is wire-acceptable for the given
        /// <paramref name="zoneBounds"/>. The caller is responsible for
        /// chassis-relative rules layered on top.
        /// </summary>
        public static bool Validate(in BrushOp op, in Bounds zoneBounds)
        {
            return ValidateKind(op)
                && ValidateRadius(op)
                && ValidateZoneOverlap(op, zoneBounds);
        }

        /// <summary>Kind must be a defined non-None brush type.</summary>
        public static bool ValidateKind(in BrushOp op)
            => op.kind == BrushKind.SphereSubtract
            || op.kind == BrushKind.CapsuleSubtract;

        /// <summary>Radius positive and within <see cref="MaxRadiusMeters"/>.</summary>
        public static bool ValidateRadius(in BrushOp op)
        {
            if (op.radiusFixed == 0) return false;
            float r = op.RadiusMeters;
            return r > 0f && r <= MaxRadiusMeters;
        }

        /// <summary>
        /// Brush volume's bounding sphere intersects the dig-zone AABB.
        /// Rejects "drilling at world (10000, 0, 0)" abuses outright.
        /// Note: a brush that grazes the zone boundary is accepted —
        /// the per-cell apply handles out-of-zone cells as no-ops, so
        /// edge-grazing is harmless.
        /// </summary>
        public static bool ValidateZoneOverlap(in BrushOp op, in Bounds zoneBounds)
        {
            Vector3 p0 = op.p0.ToVector3();
            Vector3 p1 = op.p1.ToVector3();
            Vector3 center = 0.5f * (p0 + p1);
            float halfLength = Vector3.Distance(p0, p1) * 0.5f;
            float boundingRadius = halfLength + op.RadiusMeters;

            Vector3 closest = zoneBounds.ClosestPoint(center);
            float dist = Vector3.Distance(closest, center);
            return dist <= boundingRadius;
        }
    }
}
