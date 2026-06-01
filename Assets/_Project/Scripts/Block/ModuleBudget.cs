using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// The per-chassis module cap. A bot may carry at most
    /// <see cref="MaxModules"/> Module-category blocks; the garage rejects the
    /// next placement (see <c>BlockEditor</c>) and the spawn path trims any
    /// excess server-side (see <c>ArenaController</c>) so a hand-authored or
    /// imported blueprint can never field more than the cap.
    /// </summary>
    public static class ModuleBudget
    {
        /// <summary>Maximum module blocks per chassis (MOBA ability bar slots).</summary>
        public const int MaxModules = 4;

        /// <summary>Count module blocks among <paramref name="entries"/>.</summary>
        public static int Count(IReadOnlyList<ChassisBlueprint.Entry> entries)
        {
            if (entries == null) return 0;
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
                if (ModuleKinds.IsModuleId(entries[i].BlockId)) n++;
            return n;
        }

        /// <summary>True when the chassis already fields the maximum modules.</summary>
        public static bool IsAtLimit(IReadOnlyList<ChassisBlueprint.Entry> entries)
            => Count(entries) >= MaxModules;

        /// <summary>
        /// Drop module blocks beyond <see cref="MaxModules"/>, keeping the
        /// first ones in canonical array order so the surviving set is stable.
        /// Non-module blocks are untouched; connectivity is unaffected because
        /// modules are leaf carriers. Returns the input array unchanged when
        /// already within the cap.
        /// </summary>
        public static ChassisBlueprint.Entry[] TrimToFit(
            ChassisBlueprint.Entry[] entries, out int removedCount)
        {
            removedCount = 0;
            if (entries == null || entries.Length == 0) return entries;
            if (Count(entries) <= MaxModules) return entries;

            var result = new List<ChassisBlueprint.Entry>(entries.Length);
            int kept = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (ModuleKinds.IsModuleId(entries[i].BlockId))
                {
                    if (kept >= MaxModules) { removedCount++; continue; }
                    kept++;
                }
                result.Add(entries[i]);
            }
            return removedCount == 0 ? entries : result.ToArray();
        }

        /// <summary>
        /// Clone <paramref name="src"/> with its module blocks trimmed to the
        /// cap. Never mutates the shared source asset. Returns the input when
        /// already within the cap.
        /// </summary>
        public static ChassisBlueprint TrimmedClone(
            ChassisBlueprint src, out int removedCount)
        {
            removedCount = 0;
            if (src == null) return null;
            ChassisBlueprint.Entry[] trimmed = TrimToFit(src.Entries, out removedCount);
            if (removedCount == 0) return src;
            ChassisBlueprint clone = Object.Instantiate(src);
            clone.SetEntries(trimmed);
            return clone;
        }
    }
}
