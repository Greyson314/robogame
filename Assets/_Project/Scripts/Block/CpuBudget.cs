using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Single source of truth for the chassis CPU budget. The cap shape is
    /// "each CPU block grants <see cref="BudgetPerCpuBlock"/> budget" — the
    /// pillar question the budget feature was meant to settle. The garage HUD
    /// reads <see cref="UsedCpu"/> / <see cref="Capacity"/> live; the arena
    /// spawn path enforces it with <see cref="TrimToFit"/> at match start
    /// (server-authoritative — see the caller).
    /// </summary>
    public static class CpuBudget
    {
        /// <summary>Budget granted per CPU-category block on the chassis.
        /// One CPU per bot (the second-CPU placement rule), so this is the
        /// per-bot CPU ceiling.</summary>
        public const int BudgetPerCpuBlock = 1000;

        // -----------------------------------------------------------------
        // Blueprint-entry queries (used by the spawn-time enforcer)
        // -----------------------------------------------------------------

        public static int UsedCpu(IReadOnlyList<ChassisBlueprint.Entry> entries, BlockDefinitionLibrary lib)
        {
            if (entries == null || lib == null) return 0;
            int used = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                BlockDefinition def = lib.Get(entries[i].BlockId);
                if (def != null) used += EffectiveCpuCost(entries[i], def);
            }
            return used;
        }

        /// <summary>
        /// A block's CPU cost including its chosen concoction's surcharge. For a
        /// block with no concoction (every block today except a Bomb/Mortar with
        /// a chosen recipe) this is just <see cref="BlockDefinition.CpuCost"/> —
        /// zero feature cost when unused (INV-5). The surcharge is read from the
        /// session <see cref="ConcoctionRegistry"/>; an empty/unknown id adds
        /// nothing. Used by both the garage spend bar and spawn-time TrimToFit so
        /// the player and the server agree on the price. See ADR-0004.
        /// </summary>
        public static int EffectiveCpuCost(in ChassisBlueprint.Entry entry, BlockDefinition def)
        {
            if (def == null) return 0;
            int baseCost = Mathf.Max(0, def.CpuCost);
            string cid = entry.EffectiveConcoctionId;
            if (cid.Length == 0 || !ConcoctionRegistry.IsConcoctableBlock(entry.BlockId)) return baseCost;
            return ConcoctionRegistry.TryGet(cid, out Concoction c) ? baseCost + c.CpuSurcharge(baseCost) : baseCost;
        }

        public static int Capacity(IReadOnlyList<ChassisBlueprint.Entry> entries, BlockDefinitionLibrary lib)
        {
            if (entries == null || lib == null) return 0;
            int cpus = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                BlockDefinition def = lib.Get(entries[i].BlockId);
                if (def != null && def.Category == BlockCategory.Cpu) cpus++;
            }
            return cpus * BudgetPerCpuBlock;
        }

        public static bool IsOverBudget(IReadOnlyList<ChassisBlueprint.Entry> entries, BlockDefinitionLibrary lib)
            => UsedCpu(entries, lib) > Capacity(entries, lib);

        // -----------------------------------------------------------------
        // Spawn-time enforcement
        // -----------------------------------------------------------------

        /// <summary>
        /// Return a blueprint-entry set trimmed to fit the CPU cap. Removes
        /// the blocks furthest from a CPU block first (peeling from the
        /// periphery so connectivity is preserved), then drops anything left
        /// disconnected from the CPU. CPU blocks are never removed — they
        /// supply the budget. Returns the input unchanged when already within
        /// budget or when the chassis has no CPU block (a CPU-less blueprint
        /// is invalid for other reasons; stripping it to nothing would hide
        /// that).
        /// </summary>
        public static ChassisBlueprint.Entry[] TrimToFit(
            ChassisBlueprint.Entry[] entries, BlockDefinitionLibrary lib, out int removedCount)
        {
            removedCount = 0;
            if (entries == null || entries.Length == 0 || lib == null) return entries;

            int cap = Capacity(entries, lib);
            int used = UsedCpu(entries, lib);
            if (cap <= 0 || used <= cap) return entries;

            // Occupancy + CPU seed.
            var occupied = new HashSet<Vector3Int>();
            var cpuSeeds = new List<Vector3Int>();
            foreach (ChassisBlueprint.Entry e in entries)
            {
                occupied.Add(e.Position);
                BlockDefinition def = lib.Get(e.BlockId);
                if (def != null && def.Category == BlockCategory.Cpu) cpuSeeds.Add(e.Position);
            }
            if (cpuSeeds.Count == 0) return entries; // no budget source → don't strip

            Dictionary<Vector3Int, int> dist = BfsDistances(occupied, cpuSeeds);

            // Rank removable (non-CPU) entries: furthest from CPU first, then
            // most expensive first so we drop under budget in the fewest cuts.
            var removable = new List<int>(); // indices into entries
            for (int i = 0; i < entries.Length; i++)
            {
                BlockDefinition def = lib.Get(entries[i].BlockId);
                if (def == null || def.Category == BlockCategory.Cpu) continue;
                removable.Add(i);
            }
            removable.Sort((a, b) =>
            {
                int da = dist.TryGetValue(entries[a].Position, out int va) ? va : int.MaxValue;
                int db = dist.TryGetValue(entries[b].Position, out int vb) ? vb : int.MaxValue;
                if (da != db) return db.CompareTo(da); // furthest first
                int ca = EffectiveCpuCost(entries[a], lib.Get(entries[a].BlockId));
                int cb = EffectiveCpuCost(entries[b], lib.Get(entries[b].BlockId));
                return cb.CompareTo(ca); // pricier first (surcharge included)
            });

            var dropped = new HashSet<int>();
            for (int k = 0; k < removable.Count && used > cap; k++)
            {
                int idx = removable[k];
                used -= EffectiveCpuCost(entries[idx], lib.Get(entries[idx].BlockId));
                dropped.Add(idx);
            }

            // Drop anything now disconnected from a CPU block.
            var survivingOccupied = new HashSet<Vector3Int>();
            for (int i = 0; i < entries.Length; i++)
                if (!dropped.Contains(i)) survivingOccupied.Add(entries[i].Position);
            Dictionary<Vector3Int, int> reachable = BfsDistances(survivingOccupied, cpuSeeds);
            for (int i = 0; i < entries.Length; i++)
                if (!dropped.Contains(i) && !reachable.ContainsKey(entries[i].Position))
                    dropped.Add(i);

            if (dropped.Count == 0) return entries;

            var result = new List<ChassisBlueprint.Entry>(entries.Length - dropped.Count);
            for (int i = 0; i < entries.Length; i++)
                if (!dropped.Contains(i)) result.Add(entries[i]);

            removedCount = dropped.Count;
            return result.ToArray();
        }

        /// <summary>
        /// Clone <paramref name="src"/> (all blueprint fields preserved) with
        /// its entries trimmed to the CPU cap. Never mutates the shared
        /// source asset.
        /// </summary>
        public static ChassisBlueprint TrimmedClone(
            ChassisBlueprint src, BlockDefinitionLibrary lib, out int removedCount)
        {
            removedCount = 0;
            if (src == null) return null;
            ChassisBlueprint.Entry[] trimmed = TrimToFit(src.Entries, lib, out removedCount);
            if (removedCount == 0) return src;
            ChassisBlueprint clone = Object.Instantiate(src);
            clone.SetEntries(trimmed);
            return clone;
        }

        // 6-neighbour BFS over an occupancy set, seeded from every CPU block.
        private static readonly Vector3Int[] s_neighbours =
        {
            new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
        };

        private static Dictionary<Vector3Int, int> BfsDistances(
            HashSet<Vector3Int> occupied, List<Vector3Int> seeds)
        {
            var dist = new Dictionary<Vector3Int, int>(occupied.Count);
            var queue = new Queue<Vector3Int>();
            foreach (Vector3Int s in seeds)
            {
                if (occupied.Contains(s) && !dist.ContainsKey(s)) { dist[s] = 0; queue.Enqueue(s); }
            }
            while (queue.Count > 0)
            {
                Vector3Int p = queue.Dequeue();
                int d = dist[p];
                for (int n = 0; n < s_neighbours.Length; n++)
                {
                    Vector3Int q = p + s_neighbours[n];
                    if (!occupied.Contains(q) || dist.ContainsKey(q)) continue;
                    dist[q] = d + 1;
                    queue.Enqueue(q);
                }
            }
            return dist;
        }
    }
}
