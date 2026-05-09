using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Pure helpers for ordering blueprint entries. The single chokepoint
    /// every mutation flows through is <see cref="ChassisBlueprint.SetEntries"/>,
    /// which calls <see cref="SortCanonical"/> so on-disk JSON, mid-edit grid
    /// syncs, and runtime spawns all see entries in the same order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order itself is arbitrary; what matters is that it's
    /// deterministic. We sort lexicographically on <c>(z, y, x)</c> with
    /// <see cref="ChassisBlueprint.Entry.BlockId"/> as a tie-break — two
    /// entries at the same cell are an authoring error caught by the
    /// validator, but the tie-break keeps the sort total even when the
    /// pipeline sees malformed input so downstream determinism survives.
    /// </para>
    /// <para>
    /// This is the netcode contract from <c>docs/NETCODE_PLAN.md</c> §6:
    /// every client + server arrives at the same <c>(cell → blockIndex)</c>
    /// mapping when they receive the same blueprint payload, so
    /// <c>BlockHitEvent.blockIndex</c> resolves to the same block on every
    /// machine without sending a per-block string id over the wire.
    /// </para>
    /// </remarks>
    public static class BlockEntries
    {
        /// <summary>
        /// Total order over <see cref="ChassisBlueprint.Entry"/> values.
        /// Compares position lex by <c>(z, y, x)</c>, then BlockId ordinal.
        /// </summary>
        public static int Compare(in ChassisBlueprint.Entry a, in ChassisBlueprint.Entry b)
        {
            int cmp = a.Position.z.CompareTo(b.Position.z);
            if (cmp != 0) return cmp;
            cmp = a.Position.y.CompareTo(b.Position.y);
            if (cmp != 0) return cmp;
            cmp = a.Position.x.CompareTo(b.Position.x);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.BlockId, b.BlockId);
        }

        /// <summary>
        /// In-place stable sort of <paramref name="entries"/> into canonical
        /// order. Idempotent — sorting an already-canonical array is cheap
        /// (Array.Sort detects nothing-to-swap quickly). Safe on null /
        /// empty / single-entry arrays.
        /// </summary>
        public static void SortCanonical(ChassisBlueprint.Entry[] entries)
        {
            if (entries == null || entries.Length < 2) return;
            Array.Sort(entries, s_canonical);
        }

        /// <summary>
        /// True if <paramref name="entries"/> is already in canonical order.
        /// Cheaper than calling <see cref="SortCanonical"/> when you only
        /// want to assert ordering (e.g. in tests).
        /// </summary>
        public static bool IsCanonical(ChassisBlueprint.Entry[] entries)
        {
            if (entries == null || entries.Length < 2) return true;
            for (int i = 1; i < entries.Length; i++)
            {
                if (Compare(entries[i - 1], entries[i]) > 0) return false;
            }
            return true;
        }

        // Static singleton avoids the per-call Comparison<T> delegate
        // allocation Array.Sort would do under the lambda overload.
        private static readonly IComparer<ChassisBlueprint.Entry> s_canonical = new CanonicalComparer();

        private sealed class CanonicalComparer : IComparer<ChassisBlueprint.Entry>
        {
            public int Compare(ChassisBlueprint.Entry a, ChassisBlueprint.Entry b)
                => BlockEntries.Compare(in a, in b);
        }
    }
}
