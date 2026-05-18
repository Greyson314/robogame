using System.Collections.Generic;
using Robogame.Block;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Replicates per-block damage / destruction (NETCODE_PLAN §7b). The
    /// server watches the authoritative <see cref="BlockBehaviour.DamageDealt"/>
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
    /// Idempotent on receive: a hit for a block already gone is a no-op.
    /// <c>DamageDealt</c> fires with the post-damage <c>CurrentHealth</c>
    /// (0 ⇒ destroyed), so it is the single authoritative Phase-1 hook —
    /// it also covers splash (splash routes through <c>TakeDamage</c>).
    /// Structural detach replays deterministically because every peer runs
    /// the same removal logic; the server-authoritative orphan list for
    /// tie-breaks is a Phase-4 hardening (NETCODE_PLAN Phase 4), out of
    /// scope here.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkRobot))]
    [DisallowMultipleComponent]
    public sealed class NetworkBlockGrid : NetworkBehaviour
    {
        private NetworkRobot _net;
        private BlockGrid _grid;
        private ChassisBlueprint _blueprint;

        // Canonical entry position -> wire index (server emit side).
        private readonly Dictionary<Vector3Int, ushort> _posToIndex = new();
        // Latest hpAfter per block this tick (last write wins → converges
        // since hpAfter is absolute, not a delta).
        private readonly Dictionary<ushort, BlockHitEvent> _pending = new();
        private bool _hooked;
        private NetworkManager _nm;

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
                if (_nm != null && _nm.NetworkTickSystem != null)
                    _nm.NetworkTickSystem.Tick -= FlushBatch;
                _hooked = false;
            }
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
            if (_grid == null || _blueprint == null) return;

            ChassisBlueprint.Entry[] entries = _blueprint.Entries;
            _posToIndex.Clear();
            for (int i = 0; i < entries.Length && i <= ushort.MaxValue; i++)
                _posToIndex[entries[i].Position] = (ushort)i;

            BlockBehaviour.DamageDealt += HandleDamageDealt;
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

            _pending[index] = new BlockHitEvent
            {
                BlockIndex = index,
                HpAfter = (ushort)Mathf.Clamp(Mathf.CeilToInt(block.CurrentHealth), 0, ushort.MaxValue),
                HitFlags = 0,
            };
        }

        private void FlushBatch()
        {
            if (_pending.Count == 0) return;
            var batch = new BlockHitEvent[_pending.Count];
            int w = 0;
            foreach (KeyValuePair<ushort, BlockHitEvent> kv in _pending) batch[w++] = kv.Value;
            _pending.Clear();
            BlockHitBatchClientRpc(batch);
        }

        // -----------------------------------------------------------------
        // Client: replay the same damage so the same local logic runs
        // -----------------------------------------------------------------

        [ClientRpc]
        private void BlockHitBatchClientRpc(BlockHitEvent[] events)
        {
            if (IsServer) return;            // host already authoritative
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
    }
}
