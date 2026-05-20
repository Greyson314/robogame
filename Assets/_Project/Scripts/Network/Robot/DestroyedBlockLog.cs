using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Server-side cumulative log of every block destroyed on a single
    /// networked robot since spawn (NETCODE_PLAN §7c late-join).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The log records the canonical blueprint-index (the same
    /// <c>ushort</c> the wire uses in <see cref="BlockHitEvent.BlockIndex"/>)
    /// so a late-joining client can replay the cumulative destruction set
    /// after it has reconstructed the initial blueprint locally — running
    /// the SAME destruction path every other peer ran, converging on the
    /// same post-destruction grid.
    /// </para>
    /// <para>
    /// <b>Capacity.</b> Fixed at <see cref="Capacity"/> entries. A chassis
    /// with more than 512 blocks would silently lose early destruction
    /// history if we used an evicting ring, so on overflow we log a warning
    /// and stop appending — the cumulative log is still useful, just
    /// incomplete past the cap. Current presets cap at &lt;100 blocks; if a
    /// future preset ever hits the warning, raise the cap rather than
    /// silently truncating.
    /// </para>
    /// <para>
    /// <b>Late-join wiring is reserved, not connected.</b> Per
    /// NETCODE_PLAN §10, late-join v1 is disabled (lobby locks at round
    /// start). This class exists today so the late-join v2 work in a later
    /// session can replay through the existing scene-loaded handshake
    /// without revisiting the per-block index encoding.
    /// </para>
    /// </remarks>
    public sealed class DestroyedBlockLog
    {
        public const int Capacity = 512;

        private readonly ushort[] _indices = new ushort[Capacity];
        private int _count;
        private bool _overflowWarned;

        /// <summary>Number of recorded destructions (capped at <see cref="Capacity"/>).</summary>
        public int Count => _count;

        /// <summary>Append <paramref name="blockIndex"/>. Beyond the cap, logs
        /// once then becomes a no-op (caller does not need to gate).</summary>
        public void Record(ushort blockIndex)
        {
            if (_count >= Capacity)
            {
                if (!_overflowWarned)
                {
                    Debug.LogWarning(
                        $"[DestroyedBlockLog] Overflow at {Capacity} entries — " +
                        "raise the cap or audit why a chassis is shedding so many blocks. " +
                        "Subsequent destructions are dropped from the late-join log.");
                    _overflowWarned = true;
                }
                return;
            }
            _indices[_count++] = blockIndex;
        }

        /// <summary>Copy the log into <paramref name="destination"/>; returns
        /// the number of entries written. Destination must be at least
        /// <see cref="Count"/> long; otherwise nothing is copied and the
        /// method returns -1.</summary>
        public int CopyTo(ushort[] destination)
        {
            if (destination == null || destination.Length < _count) return -1;
            for (int i = 0; i < _count; i++) destination[i] = _indices[i];
            return _count;
        }

        /// <summary>Allocate and return a fresh <c>ushort[]</c> snapshot of
        /// the log — for ClientRpc payloads. Length equals <see cref="Count"/>.</summary>
        public ushort[] ToArray()
        {
            var arr = new ushort[_count];
            for (int i = 0; i < _count; i++) arr[i] = _indices[i];
            return arr;
        }

        /// <summary>Reset back to empty (used on robot despawn).</summary>
        public void Reset()
        {
            _count = 0;
            _overflowWarned = false;
        }
    }
}
