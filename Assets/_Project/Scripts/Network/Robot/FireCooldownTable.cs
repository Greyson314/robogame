using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Per-weapon-position cooldown ledger used by <see cref="NetworkRobotCombat"/>
    /// to validate owner <c>FireCommandServerRpc</c> arrivals
    /// (NETCODE_PLAN §9 step 2 / §13).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase-2 uses the helper coarsely (a single chassis-wide
    /// <see cref="Vector3Int"/> key); the per-position capability is here so
    /// the Phase-4 per-block fire-command refactor doesn't need an API churn.
    /// </para>
    /// <para>
    /// Pure C# — no Unity object refs, no per-call allocation in steady state.
    /// The dictionary grows once per distinct weapon position; that bound is
    /// the small block count.
    /// </para>
    /// </remarks>
    public sealed class FireCooldownTable
    {
        private readonly Dictionary<Vector3Int, float> _lastFire = new(16);

        /// <summary>Total rejections recorded. Visible to combat for the
        /// debug counter / telemetry surface; never gates gameplay.</summary>
        public int RejectedCount { get; private set; }

        /// <summary>
        /// Validate a fire attempt at <paramref name="weaponPos"/> at server
        /// time <paramref name="now"/> against <paramref name="cooldown"/>.
        /// Returns <c>true</c> on acceptance (and updates the last-fire time);
        /// <c>false</c> on rejection (and increments <see cref="RejectedCount"/>).
        /// Boundary is inclusive-accept: <c>now - last == cooldown</c> passes.
        /// </summary>
        public bool TryAccept(Vector3Int weaponPos, float now, float cooldown)
        {
            if (_lastFire.TryGetValue(weaponPos, out float last) && now - last < cooldown)
            {
                RejectedCount++;
                return false;
            }
            _lastFire[weaponPos] = now;
            return true;
        }

        /// <summary>Clear all bookkeeping. Used by tests and on robot teardown.</summary>
        public void Reset()
        {
            _lastFire.Clear();
            RejectedCount = 0;
        }
    }
}
