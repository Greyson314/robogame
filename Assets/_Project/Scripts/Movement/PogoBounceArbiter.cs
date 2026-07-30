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

        /// <summary>
        /// True exactly once per <paramref name="window"/> seconds; the
        /// caller that gets true applies the chassis bounce.
        /// </summary>
        public bool TryClaim(float now, float window)
        {
            if (now - _lastBounceTime < window) return false;
            _lastBounceTime = now;
            return true;
        }
    }
}
