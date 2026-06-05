using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Runtime in-memory lookup of the concoctions available this session,
    /// keyed by <see cref="Concoction.Id"/>. Populated at match/garage load
    /// from <see cref="ConcoctionLibrary"/>; queried at fire time by explosive
    /// blocks and at CPU-budget time by <c>CpuBudget</c>. Server-authoritative
    /// in the netcode sense: the server populates it and every entry is clamped
    /// on registration (INV-3). See ADR-0004.
    /// </summary>
    /// <remarks>
    /// Static cache → MUST reset on domain reload (statics survive it,
    /// GameObjects don't — the project's recurring failure mode). The
    /// <see cref="ResetStatics"/> hook clears it at SubsystemRegistration.
    /// Zero per-frame cost: lookups are dictionary hits, populated once per
    /// session load, not per frame.
    /// </remarks>
    public static class ConcoctionRegistry
    {
        private static readonly Dictionary<string, Concoction> s_byId = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_byId.Clear();

        public static int Count => s_byId.Count;

        /// <summary>
        /// Whether a block type accepts a concoction. Phase-1 scope: the two
        /// explosive weapons with a splash radius (Bomb, Mortar). Single source
        /// of truth shared by the variant-panel dropdown, the CPU surcharge, and
        /// the fire-time application — widen this one predicate to add a block.
        /// </summary>
        public static bool IsConcoctableBlock(string blockId)
            => blockId == BlockIds.BombBay || blockId == BlockIds.Mortar;

        /// <summary>Register (or overwrite) one concoction. Clamped on the way in.</summary>
        public static void Register(Concoction concoction)
        {
            if (concoction == null || string.IsNullOrEmpty(concoction.Id)) return;
            concoction.Validate();
            s_byId[concoction.Id] = concoction;
        }

        public static void RegisterAll(IEnumerable<Concoction> concoctions)
        {
            if (concoctions == null) return;
            foreach (Concoction c in concoctions) Register(c);
        }

        /// <summary>Clear then repopulate from the player's on-disk library.</summary>
        public static void ReloadFromLibrary()
        {
            s_byId.Clear();
            List<ConcoctionLibrary.Record> records = ConcoctionLibrary.LoadAll();
            for (int i = 0; i < records.Count; i++) Register(records[i].Concoction);
        }

        /// <summary>
        /// Resolve a concoction by id. Returns false (and null) for an empty id
        /// or an unknown id — callers fall back to the weapon's baseline stats.
        /// </summary>
        public static bool TryGet(string id, out Concoction concoction)
        {
            concoction = null;
            if (string.IsNullOrEmpty(id)) return false;
            return s_byId.TryGetValue(id, out concoction);
        }

        /// <summary>Snapshot of all registered concoctions (for the variant dropdown). Allocates a list — call off the hot path.</summary>
        public static List<Concoction> GetAll()
        {
            var list = new List<Concoction>(s_byId.Count);
            foreach (KeyValuePair<string, Concoction> kvp in s_byId) list.Add(kvp.Value);
            return list;
        }

        public static void Clear() => s_byId.Clear();
    }
}
