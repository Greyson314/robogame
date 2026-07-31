using System.Collections.Generic;
using Robogame.Network.Robot;

namespace Robogame.Network.Prediction
{
    /// <summary>
    /// Server-side queue of an owner's incoming <see cref="InputCommand"/>s.
    /// One queue per remote-owned robot. Used by the server's
    /// <see cref="Robot.NetworkRobotMovement"/> to drain the next command on
    /// each <c>FixedUpdate</c> and apply it to the chassis's
    /// <see cref="Robot.NetworkInputSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stores commands in FIFO order; <see cref="DrainNext"/> returns the
    /// next-newer command than what was last applied, dropping any that are
    /// older (out-of-order arrivals on the redundancy bundle). The server
    /// also records the highest tick it has applied for the owner's
    /// reconciliation snapshot.
    /// </para>
    /// <para>
    /// Pure data class — no Unity references. The internal Queue allocates
    /// once on construction with a reasonable starting capacity.
    /// </para>
    /// </remarks>
    public sealed class ServerCommandQueue
    {
        private readonly Queue<InputCommand> _pending = new(16);

        /// <summary>Highest <see cref="InputCommand.Tick"/> applied by the
        /// server. Echoed back in <see cref="Snapshot.RobotPoseSnapshot.LastProcessedCommandTick"/>
        /// so the owner knows what to replay.</summary>
        public int LastAppliedTick { get; private set; } = -1;

        /// <summary>Enqueue an owner's command. Silently drops commands that
        /// are older than what the server has already processed (the owner's
        /// last-3-redundancy bundle frequently re-sends already-applied ticks).</summary>
        public void Enqueue(in InputCommand cmd)
        {
            if (cmd.Tick <= LastAppliedTick) return;
            _pending.Enqueue(cmd);
        }

        /// <summary>Commands still queued (stale entries included until a
        /// drain skips them). Used by the consumer's backlog catch-up.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Pop the next command (if any) for the server to apply this
        /// tick. Updates <see cref="LastAppliedTick"/>.</summary>
        public bool TryDrainNext(out InputCommand cmd)
        {
            // Loop to skip any stale entries that snuck past the Enqueue
            // guard (e.g. enqueued out-of-order from the redundancy bundle).
            while (_pending.TryDequeue(out cmd))
            {
                if (cmd.Tick > LastAppliedTick)
                {
                    LastAppliedTick = cmd.Tick;
                    return true;
                }
            }
            cmd = default;
            return false;
        }

        public void Reset()
        {
            _pending.Clear();
            LastAppliedTick = -1;
        }
    }
}
