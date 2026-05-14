using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// A bounded volume of voxel terrain that brush ops (bomb sphere subtract,
    /// drill capsule subtract) can carve into. One per authored dig zone in
    /// an arena.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="IGravitySource"/>: arenas that don't register any
    /// dig zones pay zero terraforming cost. See
    /// [docs/TERRAFORMING_PLAN.md](../../../../../docs/TERRAFORMING_PLAN.md)
    /// §3 "Storage model" for the chunk layout and §2 "Dig-only invariant"
    /// for why ContainsPoint never widens over a match.
    /// </remarks>
    public interface IDigZone
    {
        /// <summary>Axis-aligned world-space extent of the dig zone.</summary>
        Bounds WorldBounds { get; }

        /// <summary>Edge length of one SDF cell in metres. 0.5 in v1.</summary>
        float CellSize { get; }

        /// <summary>Cells per chunk side. 32 in v1 (32³ = 32K cells, 64 KB).</summary>
        int ChunkSizeCells { get; }

        /// <summary>True when <paramref name="worldPosition"/> is inside the zone's authored bounds.</summary>
        bool ContainsPoint(Vector3 worldPosition);
    }

    /// <summary>
    /// Process-wide registry of <see cref="IDigZone"/>s. Server-side brush-op
    /// validation calls <see cref="ZoneAt"/> to confirm a capsule is inside a
    /// registered zone before applying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero-baseline: arenas with no registered zones cost nothing. The
    /// terraforming feature is opt-in per arena (PHYSICS_PLAN.md §1.2).
    /// </para>
    /// <para>
    /// Statics survive domain reload but registered MonoBehaviour-backed
    /// zones do not — the registry is reset by <see cref="ResetStatics"/> on
    /// <c>SubsystemRegistration</c> so the list never carries stale entries
    /// from a prior play session.
    /// </para>
    /// </remarks>
    public static class DigField
    {
        private static readonly List<IDigZone> s_zones = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_zones.Clear();
        }

        public static void Register(IDigZone zone)
        {
            if (zone == null || s_zones.Contains(zone)) return;
            s_zones.Add(zone);
        }

        public static void Unregister(IDigZone zone)
        {
            if (zone == null) return;
            s_zones.Remove(zone);
        }

        /// <summary>
        /// The first registered zone whose bounds contain
        /// <paramref name="worldPosition"/>, or <c>null</c> if none. Zones
        /// are not authored to overlap; multi-zone arenas use disjoint
        /// volumes.
        /// </summary>
        public static IDigZone ZoneAt(Vector3 worldPosition)
        {
            for (int i = 0; i < s_zones.Count; i++)
            {
                IDigZone zone = s_zones[i];
                if (zone != null && zone.ContainsPoint(worldPosition)) return zone;
            }
            return null;
        }

        /// <summary>Editor / test convenience.</summary>
        public static int ZoneCount => s_zones.Count;
    }
}
