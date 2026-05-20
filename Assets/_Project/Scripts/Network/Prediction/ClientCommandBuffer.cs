using Robogame.Network.Robot;

namespace Robogame.Network.Prediction
{
    /// <summary>
    /// Owner-side ring buffer of <see cref="InputCommand"/>s keyed by
    /// <see cref="InputCommand.Tick"/>. Holds the last
    /// <see cref="Capacity"/> ticks of input so reconciliation can replay
    /// any unacked range on top of a server snapshot (NETCODE_PLAN §8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Capacity is sized at 128 (≈2.5 s at the 50 Hz physics tick). Snapshot
    /// delivery faster than this is normal; longer gaps mean prediction has
    /// already lost — the hard-snap fallback covers it.
    /// </para>
    /// <para>
    /// Pure data class. The backing array is allocated once in the
    /// constructor; per-call ops are array index + assignment.
    /// </para>
    /// </remarks>
    public sealed class ClientCommandBuffer
    {
        public const int Capacity = 128;

        private readonly InputCommand[] _ring = new InputCommand[Capacity];

        /// <summary>Most-recent tick stored (-1 if empty).</summary>
        public int HighestTick { get; private set; } = -1;

        /// <summary>Store a command at its own <see cref="InputCommand.Tick"/>
        /// (overwrites any prior entry at that slot — caller is responsible
        /// for storing monotonically).</summary>
        public void Store(in InputCommand cmd)
        {
            _ring[Mod(cmd.Tick)] = cmd;
            if (cmd.Tick > HighestTick) HighestTick = cmd.Tick;
        }

        /// <summary>Returns the command at <paramref name="tick"/> if it is
        /// still within the ring-buffer window. Returns false if the slot
        /// has been overwritten by a newer tick — defensive against caller
        /// asking for a tick beyond the buffer's reach.</summary>
        public bool TryGet(int tick, out InputCommand cmd)
        {
            if (tick < 0 || tick > HighestTick || HighestTick - tick >= Capacity)
            {
                cmd = default;
                return false;
            }
            cmd = _ring[Mod(tick)];
            if (cmd.Tick != tick)
            {
                cmd = default;
                return false;
            }
            return true;
        }

        /// <summary>Clear all entries. Used on robot teardown.</summary>
        public void Reset()
        {
            for (int i = 0; i < _ring.Length; i++) _ring[i] = default;
            HighestTick = -1;
        }

        private static int Mod(int tick)
        {
            int r = tick % Capacity;
            return r < 0 ? r + Capacity : r;
        }
    }
}
