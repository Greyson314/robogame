using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Shared self-filtered physics probes for chassis-mounted blocks.
    /// A block's ray origin sits inside (or flush with) its own host cube
    /// collider, so a plain <c>Physics.Raycast</c> hits the caster's own
    /// chassis first. These helpers cast NonAlloc into a reused buffer and
    /// skip every hit whose <c>attachedRigidbody</c> is the caller's
    /// chassis body.
    /// </summary>
    /// <remarks>
    /// Extracted from four hand-copied implementations (WheelBlock,
    /// PogoBlock, HoverBladeBlock, ModuleBlock) — one implementation so a
    /// buffer-size or trigger-handling change lands everywhere at once.
    /// Buffer is 8 wide: enough for a dense chassis under one probe; hits
    /// beyond that are dropped by PhysX in arbitrary order, which errs
    /// toward "no ground" (safe for suspension/bounce consumers).
    /// </remarks>
    public static class ChassisRaycast
    {
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[8];

        /// <summary>
        /// Nearest hit along the ray that does not belong to
        /// <paramref name="selfBody"/>. Triggers are ignored.
        /// </summary>
        public static bool TryNearestIgnoring(
            Rigidbody selfBody, Vector3 origin, Vector3 dir, float maxDist,
            int layerMask, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(
                origin, dir, s_hitBuffer, maxDist, layerMask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = s_hitBuffer[i];
                if (h.collider.attachedRigidbody == selfBody) continue; // self
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// True when any non-self collider lies along the ray — an
        /// existence probe (e.g. "is there ground to brace against")
        /// where the exact hit doesn't matter. Triggers are ignored.
        /// </summary>
        public static bool AnyHitIgnoring(
            Rigidbody selfBody, Vector3 origin, Vector3 dir, float maxDist, int layerMask)
        {
            int count = Physics.RaycastNonAlloc(
                origin, dir, s_hitBuffer, maxDist, layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_hitBuffer[i].collider.attachedRigidbody == selfBody) continue; // self
                return true;
            }
            return false;
        }
    }
}
