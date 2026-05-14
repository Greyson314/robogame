using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-chassis ammo + reload tracker. Phase 5–6 of
    /// <c>docs/SCRAP_LOOP_PLAN.md</c>: every weapon block consults this
    /// component before firing and decrements one pool slot on each
    /// shot. Pools are <i>per-weapon-type</i> — every SMG on the chassis
    /// shares one pool, every cannon shares another. Pool capacity scales
    /// with weapon-block count: <c>max = ClipSize × instances</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why per-type, not per-instance?</b> The plan considered both.
    /// Per-instance pools mean a 4-SMG bot has to track 4 separate
    /// reload states in the HUD; per-type collapses that into one stat
    /// per weapon family and reads cleanly. See SCRAP_LOOP_PLAN § 6.
    /// </para>
    /// <para>
    /// <b>Reload paths.</b>
    /// <list type="bullet">
    ///   <item><b>Auto-on-empty</b>: firing the last round in a pool
    ///         schedules a reload after the per-type <c>AutoReloadDelay</c>
    ///         (≈0.3 s grace). The pool is locked until the reload
    ///         completes.</item>
    ///   <item><b>Manual (R)</b>: the player's <see cref="IInputSource.ReloadPressed"/>
    ///         starts a reload on every pool that isn't full and isn't
    ///         already reloading. Bots never press R — they rely on
    ///         auto-reload.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Pool init.</b> Walks the chassis <see cref="BlockGrid"/> at
    /// OnEnable to count weapon blocks per <see cref="BlockDefinition.Id"/>.
    /// Subscribes to <c>BlockPlaced</c> / <c>BlockRemoving</c> so a chassis
    /// losing weapons mid-fight shrinks the pool to match — current ammo
    /// clamps to the new max. Adding a block at runtime expands the pool
    /// and refills the new slots.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockGrid))]
    public sealed class WeaponAmmoState : MonoBehaviour
    {
        // Per-weapon-type pool. Struct so the dictionary stays small
        // and lookups are zero-alloc.
        private struct Pool
        {
            public int Current;
            public int Max;
            public int ClipPerInstance;
            public int Instances;
            public float ReloadDuration;
            public float AutoReloadDelay;
            // 0 = not reloading. When Time.time >= this, reload completes.
            public float ReloadEndsAt;
            // 0 = no pending reload. When Time.time >= this, reload starts.
            public float ReloadStartsAt;
        }

        private readonly Dictionary<string, Pool> _pools = new();
        private BlockGrid _grid;
        private IInputSource _input;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void OnEnable()
        {
            _grid = GetComponent<BlockGrid>();
            _input = GetComponentInParent<IInputSource>();
            if (_grid != null)
            {
                _grid.BlockPlaced += HandleBlockPlaced;
                _grid.BlockRemoving += HandleBlockRemoving;
            }
            RecomputePoolsFromGrid();
        }

        private void OnDisable()
        {
            if (_grid != null)
            {
                _grid.BlockPlaced -= HandleBlockPlaced;
                _grid.BlockRemoving -= HandleBlockRemoving;
            }
        }

        private void Update()
        {
            // Manual reload (R): player only; bot ReloadPressed is always
            // false. Don't double-trigger if the pool is already in a
            // reload (handled inside RequestReloadAll).
            if (_input != null && _input.ReloadPressed)
            {
                RequestReloadAll();
            }
            TickReloads();
        }

        // -----------------------------------------------------------------
        // Public API (called from weapon block Update paths)
        // -----------------------------------------------------------------

        /// <summary>True when this weapon type has at least one round loaded and isn't currently in a reload.</summary>
        public bool CanFire(string blockId)
        {
            if (!_pools.TryGetValue(blockId, out Pool p)) return true; // no pool = no gate (defensive)
            if (p.Max <= 0) return false;
            return p.Current > 0 && p.ReloadEndsAt <= Time.time;
        }

        /// <summary>
        /// Consume one round from the pool. Returns true on success.
        /// On hitting zero, schedules an auto-reload after the pool's
        /// <c>AutoReloadDelay</c>.
        /// </summary>
        public bool Consume(string blockId, int amount = 1)
        {
            if (!_pools.TryGetValue(blockId, out Pool p)) return true;
            if (p.Max <= 0) return false;
            if (p.Current < amount) return false;
            if (p.ReloadEndsAt > Time.time) return false;
            p.Current -= amount;
            if (p.Current <= 0 && p.ReloadStartsAt <= 0f)
            {
                // Auto-reload-on-empty schedule.
                p.ReloadStartsAt = Time.time + Mathf.Max(0f, p.AutoReloadDelay);
            }
            _pools[blockId] = p;
            return true;
        }

        /// <summary>Force-start a reload on this pool (if it's not already at full and not already reloading).</summary>
        public void RequestReload(string blockId)
        {
            if (!_pools.TryGetValue(blockId, out Pool p)) return;
            if (p.Max <= 0) return;
            if (p.Current >= p.Max) return;
            if (p.ReloadEndsAt > Time.time) return;
            p.ReloadStartsAt = Time.time;
            _pools[blockId] = p;
        }

        /// <summary>R-key path: kick a reload on every non-full pool.</summary>
        public void RequestReloadAll()
        {
            // Materialise the keys so we can mutate the dictionary inside
            // the loop. Allocation is paid once per R press — not on the
            // hot fire path.
            List<string> keys = new(_pools.Keys);
            for (int i = 0; i < keys.Count; i++) RequestReload(keys[i]);
        }

        /// <summary>Current loaded ammo for this weapon type, or 0 if no pool.</summary>
        public int GetCurrent(string blockId) => _pools.TryGetValue(blockId, out Pool p) ? p.Current : 0;

        /// <summary>Maximum ammo (capacity) for this weapon type.</summary>
        public int GetMax(string blockId) => _pools.TryGetValue(blockId, out Pool p) ? p.Max : 0;

        /// <summary>
        /// True while a reload is in progress for the pool, with
        /// <paramref name="progress"/> filled in [0..1] (0 = just started,
        /// 1 = about to finish).
        /// </summary>
        public bool IsReloading(string blockId, out float progress)
        {
            progress = 0f;
            if (!_pools.TryGetValue(blockId, out Pool p)) return false;
            if (p.ReloadEndsAt <= Time.time) return false;
            float total = Mathf.Max(0.001f, p.ReloadDuration);
            progress = 1f - Mathf.Clamp01((p.ReloadEndsAt - Time.time) / total);
            return true;
        }

        /// <summary>Enumerate every pool — used by the HUD to render an ammo row per weapon type.</summary>
        public IEnumerable<KeyValuePair<string, (int current, int max, int instances, bool reloading, float reloadProgress)>> EnumeratePools()
        {
            foreach (var kvp in _pools)
            {
                Pool p = kvp.Value;
                bool reloading = p.ReloadEndsAt > Time.time;
                float progress = reloading
                    ? 1f - Mathf.Clamp01((p.ReloadEndsAt - Time.time) / Mathf.Max(0.001f, p.ReloadDuration))
                    : 0f;
                yield return new KeyValuePair<string, (int, int, int, bool, float)>(
                    kvp.Key, (p.Current, p.Max, p.Instances, reloading, progress));
            }
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private void TickReloads()
        {
            // Two-stage reload: ReloadStartsAt waits for the post-empty
            // grace window to expire; then ReloadEndsAt locks the pool
            // for the per-type duration; on completion the pool refills
            // to Max. We avoid LINQ here so the per-frame walk is alloc-free.
            float now = Time.time;
            List<string> keys = null;
            foreach (var kvp in _pools)
            {
                Pool p = kvp.Value;
                bool changed = false;
                if (p.ReloadStartsAt > 0f && now >= p.ReloadStartsAt && p.ReloadEndsAt <= now)
                {
                    p.ReloadStartsAt = 0f;
                    p.ReloadEndsAt = now + Mathf.Max(0.05f, p.ReloadDuration);
                    AudioRouter.PlayUI(AudioCue.ReloadStart);
                    changed = true;
                }
                else if (p.ReloadEndsAt > 0f && now >= p.ReloadEndsAt)
                {
                    p.ReloadEndsAt = 0f;
                    p.Current = p.Max;
                    AudioRouter.PlayUI(AudioCue.ReloadComplete);
                    changed = true;
                }
                if (changed)
                {
                    if (keys == null) keys = new List<string>();
                    keys.Add(kvp.Key);
                    _pendingPoolWrites[kvp.Key] = p;
                }
            }
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    string k = keys[i];
                    _pools[k] = _pendingPoolWrites[k];
                }
                _pendingPoolWrites.Clear();
            }
        }

        // Scratch dictionary for in-flight pool writes during TickReloads.
        // Reused to avoid per-frame allocation when no pool changes.
        private readonly Dictionary<string, Pool> _pendingPoolWrites = new();

        // ----- Block-grid event handlers ----------

        private void HandleBlockPlaced(BlockBehaviour block)
        {
            if (block == null || block.Definition == null) return;
            if (!IsWeaponBlock(block)) return;
            RecomputePoolsFromGrid();
        }

        private void HandleBlockRemoving(BlockBehaviour block)
        {
            if (block == null || block.Definition == null) return;
            if (!IsWeaponBlock(block)) return;
            // BlockRemoving fires BEFORE the grid drops it, so we count
            // the block-being-removed and subtract its contribution
            // afterwards. RecomputePoolsFromGrid runs at the post-event
            // state if we defer by one frame, but a synchronous recount
            // with "ignore this block" is simpler.
            string id = block.Definition.Id;
            if (_pools.TryGetValue(id, out Pool p))
            {
                p.Instances = Mathf.Max(0, p.Instances - 1);
                p.Max = p.Instances * p.ClipPerInstance;
                p.Current = Mathf.Min(p.Current, p.Max);
                if (p.Instances <= 0) _pools.Remove(id);
                else _pools[id] = p;
            }
        }

        private static bool IsWeaponBlock(BlockBehaviour b)
        {
            string id = b.Definition.Id;
            return id == BlockIds.Weapon
                || id == BlockIds.BombBay
                || id == BlockIds.Cannon;
        }

        private void RecomputePoolsFromGrid()
        {
            if (_grid == null) return;
            // Count instances per weapon block id.
            Dictionary<string, int> counts = _instanceCountScratch;
            counts.Clear();
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                if (!IsWeaponBlock(b)) continue;
                string id = b.Definition.Id;
                counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            }
            // Build / update pools.
            // 1. Drop pools whose weapon type no longer has any instances.
            _removeScratch.Clear();
            foreach (var kvp in _pools)
            {
                if (!counts.ContainsKey(kvp.Key)) _removeScratch.Add(kvp.Key);
            }
            for (int i = 0; i < _removeScratch.Count; i++) _pools.Remove(_removeScratch[i]);
            // 2. Add / update pools for live weapon types.
            foreach (var kvp in counts)
            {
                string id = kvp.Key;
                int instances = kvp.Value;
                BlockDefinition def = ResolveDefinition(id);
                if (def == null) continue;
                (int clipSize, float reloadDur, float autoDelay) = ResolveAmmoConfig(def);
                int newMax = clipSize * instances;
                if (_pools.TryGetValue(id, out Pool existing))
                {
                    existing.Instances = instances;
                    existing.ClipPerInstance = clipSize;
                    existing.ReloadDuration = reloadDur;
                    existing.AutoReloadDelay = autoDelay;
                    // Grow the pool: top up to new max with the extra
                    // slots' worth of ammo. Shrink: clamp current down
                    // to the new max.
                    if (newMax > existing.Max) existing.Current += (newMax - existing.Max);
                    existing.Max = newMax;
                    if (existing.Current > existing.Max) existing.Current = existing.Max;
                    _pools[id] = existing;
                }
                else
                {
                    _pools[id] = new Pool
                    {
                        Current = newMax, // start full on first author
                        Max = newMax,
                        ClipPerInstance = clipSize,
                        Instances = instances,
                        ReloadDuration = reloadDur,
                        AutoReloadDelay = autoDelay,
                        ReloadEndsAt = 0f,
                        ReloadStartsAt = 0f,
                    };
                }
            }
        }

        private static readonly Dictionary<string, int> _instanceCountScratch = new();
        private static readonly List<string> _removeScratch = new();

        private BlockDefinition ResolveDefinition(string id)
        {
            // Walk the grid to find any live block of this id and pull
            // its definition reference. Cheaper than wiring a library
            // ref through the chassis — and one block of any id is
            // sufficient (definitions are shared SOs).
            foreach (var kvp in _grid.Blocks)
            {
                BlockBehaviour b = kvp.Value;
                if (b == null || b.Definition == null) continue;
                if (b.Definition.Id == id) return b.Definition;
            }
            return null;
        }

        // Per-weapon-type ammo config resolution. Each weapon kind
        // attaches its tuning SO to BlockDefinition.ComponentData; we
        // try-cast against the known types.
        private static (int clipSize, float reloadDuration, float autoReloadDelay) ResolveAmmoConfig(BlockDefinition def)
        {
            WeaponDefinition wd = def.GetComponentData<WeaponDefinition>();
            if (wd != null) return (wd.ClipSize, wd.ReloadDuration, wd.AutoReloadDelay);
            BombDefinition bd = def.GetComponentData<BombDefinition>();
            if (bd != null) return (bd.ClipSize, bd.ReloadDuration, bd.AutoReloadDelay);
            CannonDefinition cd = def.GetComponentData<CannonDefinition>();
            if (cd != null) return (cd.ClipSize, cd.ReloadDuration, cd.AutoReloadDelay);
            // Defensive defaults. Shipped weapon assets that haven't been
            // re-saved against the Phase 5 fields default to a 10-round
            // clip + 1.5 s reload — playable, even if not tuned.
            return (10, 1.5f, 0.3f);
        }
    }
}
