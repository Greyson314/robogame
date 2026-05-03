using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Anything in the scene that can pull rigidbodies toward it. The
    /// canonical implementation is <see cref="PlanetBody"/>; future
    /// designs (gravity wells, anti-grav volumes) implement the same
    /// interface and register with <see cref="GravityField"/>.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Robogame.Gameplay</c> rather than <c>Robogame.Core</c>
    /// for v1 — see [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md)
    /// §6 "New Components". Promote to Core once flat arenas also need
    /// to query it (i.e. once the substitution work in Phase A starts).
    /// </remarks>
    public interface IGravitySource
    {
        /// <summary>Acceleration vector (m/s²) acting on a body at <paramref name="worldPosition"/>. Zero outside the source's SOI.</summary>
        Vector3 GetGravityAt(Vector3 worldPosition);

        /// <summary>True when <paramref name="worldPosition"/> is inside the source's sphere of influence.</summary>
        bool ContainsPoint(Vector3 worldPosition);
    }

    /// <summary>
    /// Process-wide registry of <see cref="IGravitySource"/>s. Anything
    /// that needs a "down" vector calls <see cref="SampleAt"/> instead
    /// of using <c>Vector3.down</c> or <c>Physics.gravity</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Singleplayer-safe: scenes with no registered source fall back to
    /// the standard flat-world gravity (<c>Physics.gravity</c>), so flat
    /// arenas keep working unchanged. The Planet arena scene is the only
    /// one in v1 that registers a source.
    /// </para>
    /// <para>
    /// This is a v1 sketch — the full design (with summing across
    /// multiple sources, dominant-source queries, and core-namespace
    /// hosting) lives in
    /// [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md)
    /// §3 and §6 and is enacted by the Phase A rollout.
    /// </para>
    /// <para>
    /// <b>File layout note:</b> only the interface and this static
    /// registry live here. <see cref="PlanetBody"/> and
    /// <see cref="PlanetGravityBody"/> sit in their own files because
    /// Unity won't serialize a MonoBehaviour whose filename doesn't
    /// match the class name — the script GUID lookup fails silently
    /// and the component is dropped on save.
    /// </para>
    /// </remarks>
    public static class GravityField
    {
        private static readonly List<IGravitySource> s_sources = new();

        public static event Action<IGravitySource> SourceAdded;
        public static event Action<IGravitySource> SourceRemoved;

        public static void Register(IGravitySource source)
        {
            if (source == null || s_sources.Contains(source)) return;
            s_sources.Add(source);
            SourceAdded?.Invoke(source);
        }

        public static void Unregister(IGravitySource source)
        {
            if (source == null) return;
            if (s_sources.Remove(source)) SourceRemoved?.Invoke(source);
        }

        /// <summary>
        /// Sum of every active source's gravity at <paramref name="worldPosition"/>.
        /// Returns <see cref="Physics.gravity"/> if no sources are
        /// registered (the flat-world fallback).
        /// </summary>
        public static Vector3 SampleAt(Vector3 worldPosition)
        {
            if (s_sources.Count == 0) return Physics.gravity;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < s_sources.Count; i++)
            {
                IGravitySource src = s_sources[i];
                if (src == null) continue;
                sum += src.GetGravityAt(worldPosition);
            }
            // If sources are registered but none contain this point, fall
            // back to flat gravity — a chassis flung outside every SOI
            // shouldn't drift in zero-G forever (see SPHERICAL_ARENAS.md
            // §12 "What if a robot leaves the SOI?").
            return sum.sqrMagnitude > 0.0001f ? sum : Physics.gravity;
        }

        /// <summary>
        /// The single source whose SOI currently contains
        /// <paramref name="worldPosition"/>, or <c>null</c> if none. Used
        /// by cameras / UI for "which planet am I on?" queries.
        /// </summary>
        public static IGravitySource DominantAt(Vector3 worldPosition)
        {
            for (int i = 0; i < s_sources.Count; i++)
            {
                IGravitySource src = s_sources[i];
                if (src != null && src.ContainsPoint(worldPosition)) return src;
            }
            return null;
        }

        /// <summary>Editor / test convenience.</summary>
        public static int SourceCount => s_sources.Count;
    }
}
