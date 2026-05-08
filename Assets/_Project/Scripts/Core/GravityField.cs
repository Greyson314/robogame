using System;
using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Anything in the scene that can pull rigidbodies toward it. The
    /// canonical implementation is <c>PlanetBody</c> (in Robogame.Gameplay);
    /// future designs (gravity wells, anti-grav volumes) implement the same
    /// interface and register with <see cref="GravityField"/>.
    /// </summary>
    /// <remarks>
    /// Promoted to <c>Robogame.Core</c> in session 34 — the original v1
    /// home was <c>Robogame.Gameplay</c>, but chassis-level systems
    /// (FlipController et al.) live in lower asmdef tiers and need to
    /// sample gravity. See [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md)
    /// §6 "New Components" — this is the move that doc anticipated.
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
    /// Statics survive domain reload but registered MonoBehaviour-backed
    /// sources do not — the registry is reset by
    /// <see cref="ResetStatics"/> on <c>SubsystemRegistration</c> so the
    /// list never carries stale entries from a prior play session.
    /// </para>
    /// </remarks>
    public static class GravityField
    {
        private static readonly List<IGravitySource> s_sources = new();

        public static event Action<IGravitySource> SourceAdded;
        public static event Action<IGravitySource> SourceRemoved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_sources.Clear();
            SourceAdded = null;
            SourceRemoved = null;
        }

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
