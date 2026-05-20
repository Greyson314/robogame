using System.Collections.Generic;
using Robogame.Block;
using Unity.Netcode;
using UnityEngine;
using GameRobot = Robogame.Robots.Robot;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Replicates per-block damage / destruction (NETCODE_PLAN §7b + §7c).
    /// The server watches the authoritative <see cref="BlockBehaviour.DamageDealt"/>
    /// for blocks on this robot, batches the results per network tick, and
    /// fans them out in one <c>ClientRpc</c>. Each client replays the same
    /// <see cref="BlockBehaviour.TakeDamage"/> call, so the existing local
    /// destruction + structural-integrity path runs identically on every
    /// peer — we replicate the <em>outcome</em>, not the physics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One ClientRpc per robot per tick, only when something was hit — an
    /// idle tick allocates nothing (invariant #6). The per-tick allocation
    /// on a tick that did have hits is bounded and event-time, the same
    /// tolerance as <c>BrushOpCodec.DecodeBatch</c>.
    /// </para>
    /// <para>
    /// <b>Phase 4 hardening.</b> Each batch carries a monotonic
    /// <c>_batchSeq</c> uint so the client can drop duplicates (the NGO
    /// reliable channel is ordered but reconnect edge cases can re-deliver).
    /// Structural orphans fire on a separate <c>OrphanBatchClientRpc</c>
    /// driven by <see cref="GameRobot.OrphansDetached"/>: the server runs
    /// the BFS, sends the authoritative orphan set, and clients skip their
    /// own BFS in favour of the server's list — tie-breaking on the rare
    /// case where local connectivity computes a different result. A
    /// <see cref="DestroyedBlockLog"/> records every destruction since
    /// spawn for late-join replay; v1 has late-join disabled per §10, so
    /// the late-join replay RPC is reserved but not yet triggered by the
    /// scene lifecycle.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkRobot))]
    [DisallowMultipleComponent]
    public sealed class NetworkBlockGrid : NetworkBehaviour
    {
        private NetworkRobot _net;
        private BlockGrid _grid;
        private ChassisBlueprint _blueprint;
        private GameRobot _robot;

        // Canonical entry position -> wire index (server emit side).
        private readonly Dictionary<Vector3Int, ushort> _posToIndex = new();
        // Latest hpAfter per block this tick (last write wins → converges
        // since hpAfter is absolute, not a delta).
        private readonly Dictionary<ushort, BlockHitEvent> _pending = new();
        private bool _hooked;
        private NetworkManager _nm;

        // Phase 4 — server tick sequence. Increments every flush that sends.
        private uint _batchSeq;
        // Phase 4 — client dedup against duplicate-delivered batches.
        private uint _lastAppliedSeq;
        // Phase 4 — cumulative destruction log for late-join replay.
        private readonly DestroyedBlockLog _destroyedLog = new();

        private void Awake() => _net = GetComponent<NetworkRobot>();

        public override void OnNetworkSpawn()
        {
            _nm = NetworkManager.Singleton;
            if (IsServer) _net.WhenBuilt(_ => HookServer());
            else _net.WhenBuilt(_ => CacheClient());
        }

        public override void OnNetworkDespawn()
        {
            if (_hooked)
            {
                BlockBehaviour.DamageDealt -= HandleDamageDealt;
                if (_robot != null) _robot.OrphansDetached -= HandleOrphansDetached;
                if (_nm != null && _nm.NetworkTickSystem != null)
                    _nm.NetworkTickSystem.Tick -= FlushBatch;
                _hooked = false;
            }
            _destroyedLog.Reset();
            _batchSeq = 0;
            _lastAppliedSeq = 0;
        }

        private void CacheClient()
        {
            if (_net.Handle == null) return;
            _grid = _net.Handle.Grid;
            _blueprint = _net.Handle.Blueprint;
        }

        private void HookServer()
        {
            if (_hooked || _net.Handle == null) return;
            _grid = _net.Handle.Grid;
            _blueprint = _net.Handle.Blueprint;
            _robot = _net.Handle.Robot;
            if (_grid == null || _blueprint == null) return;

            ChassisBlueprint.Entry[] entries = _blueprint.Entries;
            _posToIndex.Clear();
            for (int i = 0; i < entries.Length && i <= ushort.MaxValue; i++)
                _posToIndex[entries[i].Position] = (ushort)i;

            BlockBehaviour.DamageDealt += HandleDamageDealt;
            if (_robot != null) _robot.OrphansDetached += HandleOrphansDetached;
            if (_nm != null && _nm.NetworkTickSystem != null)
                _nm.NetworkTickSystem.Tick += FlushBatch;
            _hooked = true;
        }

        // -----------------------------------------------------------------
        // Server: accumulate authoritative hits, flush once per tick
        // -----------------------------------------------------------------

        private void HandleDamageDealt(BlockBehaviour block, float dealt)
        {
            if (block == null) return;
            // DamageDealt is global; keep only blocks on THIS robot's grid.
            if (!_grid.TryGetBlock(block.GridPosition, out BlockBehaviour mine) ||
                !ReferenceEquals(mine, block))
                return;
            if (!_posToIndex.TryGetValue(block.GridPosition, out ushort index))
                return;

            ushort hpAfter = (ushort)Mathf.Clamp(Mathf.CeilToInt(block.CurrentHealth), 0, ushort.MaxValue);
            _pending[index] = new BlockHitEvent
            {
                BlockIndex = index,
                HpAfter = hpAfter,
                HitFlags = 0,
            };
            // Record straight-from-hit destruction in the late-join log.
            // Structural-detach destructions are recorded by
            // HandleOrphansDetached on the same path through the wire.
            if (hpAfter == 0) _destroyedLog.Record(index);
        }

        private void HandleOrphansDetached(GameRobot _, IReadOnlyList<Vector3Int> positions)
        {
            // Map server-computed orphan positions to canonical wire indices.
            // The list arrives one Unity frame after the destruction event
            // (Robot.RunConnectivityNextFrame is a coroutine with yield null),
            // so this fans out as its own RPC on the next tick boundary — the
            // hit batch went out first; clients have already processed the
            // direct-hit destruction and only the structural cascade is new.
            if (positions == null || positions.Count == 0) return;

            int writeIdx = 0;
            ushort[] indices = new ushort[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                if (_posToIndex.TryGetValue(positions[i], out ushort idx))
                {
                    indices[writeIdx++] = idx;
                    _destroyedLog.Record(idx);
                }
            }
            if (writeIdx == 0) return;
            if (writeIdx < indices.Length)
            {
                // Tight-pack — orphan lists are tiny so the realloc is cheap.
                ushort[] trimmed = new ushort[writeIdx];
                System.Array.Copy(indices, trimmed, writeIdx);
                indices = trimmed;
            }
            OrphanBatchClientRpc(indices);
        }

        private void FlushBatch()
        {
            if (_pending.Count == 0) return;
            var batch = new BlockHitEvent[_pending.Count];
            int w = 0;
            foreach (KeyValuePair<ushort, BlockHitEvent> kv in _pending) batch[w++] = kv.Value;
            _pending.Clear();
            unchecked { _batchSeq++; }
            BlockHitBatchClientRpc(_batchSeq, batch);
        }

        // -----------------------------------------------------------------
        // Client: replay the same damage so the same local logic runs
        // -----------------------------------------------------------------

        [ClientRpc]
        private void BlockHitBatchClientRpc(uint seq, BlockHitEvent[] events)
        {
            if (IsServer) return;            // host already authoritative
            // Drop duplicate redelivery (rare on NGO reliable, but the
            // sequence makes the protection explicit and cheap).
            if (seq != 0 && seq <= _lastAppliedSeq) return;
            _lastAppliedSeq = seq;

            if (_grid == null || _blueprint == null) CacheClient();
            if (_grid == null || _blueprint == null || events == null) return;

            ChassisBlueprint.Entry[] entries = _blueprint.Entries;
            for (int i = 0; i < events.Length; i++)
            {
                BlockHitEvent e = events[i];
                if (e.BlockIndex >= entries.Length) continue;
                Vector3Int pos = entries[e.BlockIndex].Position;
                if (!_grid.TryGetBlock(pos, out BlockBehaviour block) || block == null)
                    continue; // already gone — idempotent no-op

                if (e.HpAfter == 0)
                {
                    // Drive HP to zero through the SAME path the server took:
                    // TakeDamage → Destroyed → grid removal → BlockRemoving
                    // (structural integrity + aggregates run locally too).
                    if (block.IsAlive) block.TakeDamage(block.CurrentHealth);
                }
                else
                {
                    float delta = block.CurrentHealth - e.HpAfter;
                    if (delta > 0f) block.TakeDamage(delta);
                }
            }
        }

        [ClientRpc]
        private void OrphanBatchClientRpc(ushort[] indices)
        {
            if (IsServer) return; // host already authoritative
            ReplayBlocksToZeroOnClient(indices);
        }

        // Single client-side replay implementation reused by the orphan
        // RPC and the late-join replay RPC. Cannot live in a ClientRpc
        // method body because ClientRpcs cannot call other ClientRpcs.
        private void ReplayBlocksToZeroOnClient(ushort[] indices)
        {
            if (_grid == null || _blueprint == null) CacheClient();
            if (_grid == null || _blueprint == null || indices == null) return;

            ChassisBlueprint.Entry[] entries = _blueprint.Entries;
            for (int i = 0; i < indices.Length; i++)
            {
                ushort idx = indices[i];
                if (idx >= entries.Length) continue;
                Vector3Int pos = entries[idx].Position;
                if (!_grid.TryGetBlock(pos, out BlockBehaviour block) || block == null)
                    continue; // already destroyed locally (BFS converged) — no-op
                if (block.IsAlive) block.TakeDamage(block.CurrentHealth);
            }
        }

        /// <summary>Late-join scaffold (NETCODE_PLAN §10 v2). Server only.
        /// Targets one client and replays the cumulative destruction log so
        /// the joiner converges on the current grid after rebuilding the
        /// blueprint locally. v1 lobbies lock at round start (see §10), so
        /// the scene-lifecycle does not call this yet — when v2 wires
        /// mid-match join it calls into here from the scene-loaded callback
        /// for the joining client.</summary>
        public void ServerSendDestructionLogTo(ulong clientId)
        {
            if (!IsServer) return;
            if (_destroyedLog.Count == 0) return;
            ushort[] payload = _destroyedLog.ToArray();
            ClientRpcParams p = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
            };
            DestroyedBlockReplayClientRpc(payload, p);
        }

        [ClientRpc]
        private void DestroyedBlockReplayClientRpc(ushort[] indices, ClientRpcParams _ = default)
        {
            if (IsServer) return; // host already authoritative
            ReplayBlocksToZeroOnClient(indices);
        }
    }
}
