using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// One per-network-tick sample of a robot's bounding sphere
    /// (NETCODE_PLAN §9 lag compensation, variant C). Plain value type so
    /// the per-robot ring buffer is allocation-free in steady state.
    /// </summary>
    public struct RobotBoundsSnapshot
    {
        public Vector3 Pos;
        public float Radius;
        public uint Tick;
    }

    /// <summary>
    /// Per-robot ring buffer of <see cref="RobotBoundsSnapshot"/>s for
    /// Phase-6 lag compensation (NETCODE_PLAN §9, variant C: bounding-volume
    /// rewind). Server-only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// At 50 Hz network tick rate, <see cref="Capacity"/> = 25 entries covers
    /// 500 ms of history — the upper end of the lag-comp budget called out
    /// in the plan. The radius is set once at chassis build time (the chassis
    /// is rigid, so the bounding sphere doesn't grow during play — only
    /// shrinks as blocks detach, and a slightly-over-sized historical sphere
    /// is the safe over-cover direction for variant C). Position is captured
    /// every server FixedUpdate.
    /// </para>
    /// <para>
    /// <b>Telemetry-only in Phase 6.</b> The historical bounds are read by
    /// <see cref="NetworkRobotCombat"/>'s lag-comp check; that check logs
    /// disagreements with <c>ProjectileWorld</c>'s live sweep but does NOT
    /// apply damage. Slow projectile weapons (SMG pellet ≈ 80 m/s, cannon,
    /// bomb) keep their leadable / dodgeable feel — the live sweep stays
    /// authoritative. If a hitscan weapon type ever ships, flipping
    /// telemetry → authoritative is a one-method change in NetworkRobotCombat.
    /// </para>
    /// <para>
    /// <b>Late join / mid-match join.</b> No special handling required —
    /// the ring buffer fills up on each peer's server-side instance as the
    /// match progresses, and lag-comp queries within the past 500 ms always
    /// find data once a robot has been alive that long.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LagCompHistory : NetworkBehaviour
    {
        /// <summary>500 ms at the 50 Hz tick rate.</summary>
        public const int Capacity = 25;

        private readonly RobotBoundsSnapshot[] _buffer = new RobotBoundsSnapshot[Capacity];
        private int _head;
        private int _count;
        private float _cachedRadius;

        /// <summary>Number of samples currently in the buffer (caps at
        /// <see cref="Capacity"/>).</summary>
        public int Count => _count;

        /// <summary>The chassis bounding-sphere radius supplied via
        /// <see cref="SetChassisBounds"/>. Used by sampling; cached once at
        /// chassis build time rather than recomputed per tick.</summary>
        public float ChassisRadius => _cachedRadius;

        public override void OnNetworkSpawn()
        {
            if (IsServer) LagCompRegistry.Register(NetworkObjectId, this);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) LagCompRegistry.Unregister(NetworkObjectId);
            _head = 0;
            _count = 0;
            _cachedRadius = 0f;
        }

        /// <summary>Set the chassis bounding radius. Called once by
        /// <see cref="NetworkRobotCombat"/> after the chassis is built.</summary>
        public void SetChassisBounds(float radius)
        {
            if (radius > 0f) _cachedRadius = radius;
        }

        /// <summary>Append a snapshot at <paramref name="pos"/> /
        /// <paramref name="tick"/> using the cached radius. Cheap O(1).</summary>
        public void Sample(Vector3 pos, uint tick)
        {
            _buffer[_head] = new RobotBoundsSnapshot
            {
                Pos = pos,
                Radius = _cachedRadius,
                Tick = tick,
            };
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>Find the snapshot whose <see cref="RobotBoundsSnapshot.Tick"/>
        /// is closest to <paramref name="targetTick"/>. Returns false if the
        /// buffer is empty OR the closest sample's tick is more than
        /// <see cref="Capacity"/> ticks away (= outside the 500 ms rewind
        /// window) — preventing accidental rewind onto stale data.</summary>
        public bool TryQueryAt(uint targetTick, out RobotBoundsSnapshot result)
        {
            result = default;
            if (_count == 0) return false;

            int bestDist = int.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < _count; i++)
            {
                int dist = (int)System.Math.Abs((long)_buffer[i].Tick - (long)targetTick);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0 || bestDist > Capacity) return false;
            result = _buffer[bestIdx];
            return true;
        }

        /// <summary>Clear the buffer (used between rounds / on robot
        /// despawn). Does not zero the cached radius.</summary>
        public void Reset()
        {
            _head = 0;
            _count = 0;
        }
    }
}
