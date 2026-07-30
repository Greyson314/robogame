using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Per-chassis bounce gate for <see cref="PogoBlock"/>. Every pogo on
    /// a chassis claims here before applying its bounce; only the first
    /// claim inside the window wins. Without this, N pogos touching down
    /// in the same physics step each read the PRE-bounce velocity and
    /// queue their full velocity-set — the corrections stack N× and a
    /// 10-pogo bot is a functional rocket (playtest: 500+ m). With it,
    /// extra pogos buy landing coverage and redundancy, not Δv.
    /// </summary>
    /// <remarks>
    /// Lives on the chassis-root GameObject (added lazily by the first
    /// pogo that needs it) so the latch is naturally per-Rigidbody, with
    /// no static state to reset across domain reloads. First-claim-wins:
    /// on a mixed-power chassis the winning foot is contact-order
    /// arbitrary — acceptable for the prototype; a max-request arbiter
    /// is the upgrade if mixed-power pogo builds become a real pattern.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PogoBounceArbiter : MonoBehaviour
    {
        private float _lastBounceTime = float.NegativeInfinity;
        private readonly List<PogoBlock> _feet = new List<PogoBlock>(8);

        /// <summary>
        /// True exactly once per <paramref name="window"/> seconds; the
        /// caller that gets true applies the chassis bounce.
        /// </summary>
        public bool TryClaim(float now, float window)
        {
            if (!CanClaim(now, window)) return false;
            _lastBounceTime = now;
            return true;
        }

        /// <summary>
        /// Read-only probe: would <see cref="TryClaim"/> succeed right now?
        /// Lets a foot skip the stack-count work while the window is closed
        /// and defer the actual latch until it knows the bounce will really
        /// apply (a no-op claim would deny sibling feet for the window).
        /// </summary>
        public bool CanClaim(float now, float window) => now - _lastBounceTime >= window;

        /// <summary>Feet enrol here so the stack count never allocates at bounce time.</summary>
        public void Register(PogoBlock foot)
        {
            if (foot != null && !_feet.Contains(foot)) _feet.Add(foot);
        }

        public void Unregister(PogoBlock foot) => _feet.Remove(foot);

        /// <summary>
        /// How many registered feet currently touch ground (each foot
        /// re-probes its own ray so the count is same-step accurate —
        /// sibling FixedUpdate order would otherwise leave half the feet
        /// one step stale at the landing instant). Floored at 1: the
        /// caller is a foot that just touched. Event-scoped cost only.
        /// </summary>
        public int CountLoadedFeet()
        {
            int n = 0;
            for (int i = 0; i < _feet.Count; i++)
            {
                PogoBlock p = _feet[i];
                if (p != null && p.isActiveAndEnabled && p.ProbeFootLoaded()) n++;
            }
            return Mathf.Max(1, n);
        }
    }
}
