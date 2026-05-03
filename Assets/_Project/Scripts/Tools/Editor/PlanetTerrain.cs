using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Procedural height field for spherical planets. Sample
    /// <see cref="SampleHeight"/> with a unit-length world direction and
    /// get back the radial offset (in metres) to displace that vertex by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Designed to be called from <see cref="IcosphereBuilder.Build"/>'s
    /// optional displacement callback so the planet mesh, its
    /// <see cref="MeshCollider"/>, and any decor placed via the same
    /// sampler all land on the exact same surface — there is no separate
    /// "physics height" vs "render height".
    /// </para>
    /// <para>
    /// <b>Composition</b> (each layer additive in metres):
    /// <list type="number">
    ///   <item><description><b>Continents</b> — single-octave low-frequency Perlin
    ///         FBM. Smooth rolling base, ±<c>continentAmplitude</c>.</description></item>
    ///   <item><description><b>Mountains</b> — ridged multifractal (the
    ///         <c>1 - |2n-1|</c> trick) masked by a sharp
    ///         <see cref="Smoothstep01"/> on the continent layer so they only
    ///         erupt over high ground. Adds up to <c>mountainAmplitude</c>.</description></item>
    ///   <item><description><b>Canyons</b> — same ridged trick at a different
    ///         seed, subtracted with a soft mask so they cut into low ground.</description></item>
    ///   <item><description><b>Spawn flatness mask</b> — multiplies the entire
    ///         result by 0 at the north pole and ramps to 1 by ~30°
    ///         latitude, so the chassis spawn cap stays driveable while
    ///         the locomotion subsystem still assumes flat ground.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The defaults aim for the "Astroneer / Mario Galaxy stylized" look:
    /// hills are gentle (~3% of radius), mountains are sharp (~6% of
    /// radius), canyons cut to ~3% below baseline. All values are
    /// editor-tunable from <see cref="EnvironmentBuilder.BuildPlanetArenaEnvironment"/>.
    /// </para>
    /// </remarks>
    internal static class PlanetTerrain
    {
        // -----------------------------------------------------------------
        // Tunables. Public-ish so EnvironmentBuilder can override.
        // -----------------------------------------------------------------

        public struct Settings
        {
            /// <summary>Planet radius in metres. All amplitudes are quoted as fractions of this.</summary>
            public float Radius;

            /// <summary>Peak hill height above baseline (m).</summary>
            public float ContinentAmplitude;
            /// <summary>Spatial frequency of continents. Higher = more islands.</summary>
            public float ContinentFrequency;
            /// <summary>FBM octaves for continents. 3-4 gives fractal detail without noise hash.</summary>
            public int   ContinentOctaves;

            /// <summary>Peak mountain height above baseline (m).</summary>
            public float MountainAmplitude;
            /// <summary>Frequency of mountain ridges. Higher = denser ranges.</summary>
            public float MountainFrequency;
            /// <summary>Continent height (0..1) above which mountains start to appear.</summary>
            public float MountainMaskThreshold;
            /// <summary>Sharpness of the mountain mask. Higher = harder cutoff.</summary>
            public float MountainMaskSharpness;

            /// <summary>Peak canyon depth below baseline (m).</summary>
            public float CanyonAmplitude;
            /// <summary>Frequency of canyon networks.</summary>
            public float CanyonFrequency;

            /// <summary>Latitude (deg from north pole) where the spawn flatness mask hits 0 (totally flat).</summary>
            public float SpawnFlatRadiusDeg;
            /// <summary>Latitude (deg from north pole) where the mask reaches 1 (full terrain).</summary>
            public float SpawnFalloffEdgeDeg;

            /// <summary>RNG seed, mixed into every noise lookup so re-rolling the planet is one number change.</summary>
            public int Seed;

            public static Settings Default(float radius)
            {
                return new Settings
                {
                    Radius                = radius,
                    ContinentAmplitude    = radius * 0.03f,   // ~72 m at 2400
                    // Frequencies are in cycles across the unit sphere. Mathf.PerlinNoise's
                    // gradient lattice sits on integer coordinates, so values < ~3 produce
                    // a single smooth blob across the whole planet (visually flat). 8 gives
                    // ~16 lattice cells across each axis, enough for FBM to read as continents.
                    ContinentFrequency    = 8f,
                    ContinentOctaves      = 4,

                    // Lower amplitude + lower frequency + softer mask gives broad, rolling
                    // peaks instead of the tight SM64-style spikes the old tuning produced.
                    MountainAmplitude     = radius * 0.035f,  // ~84 m at 2400 (was 144)
                    MountainFrequency     = 12f,              // wider peaks (was 22)
                    MountainMaskThreshold = 0.50f,
                    MountainMaskSharpness = 3f,               // softer mask edge (was 8)

                    CanyonAmplitude       = radius * 0.04f,   // ~96 m at 2400
                    CanyonFrequency       = 14f,

                    // Geometric horizon from a chassis at 1.5 m on r=2400 m is
                    // sqrt(2*1.5*2400) ≈ 85 m ≈ 2.0° of arc — the player must be able
                    // to see terrain BEYOND that, so the fully-flat cap has to be small.
                    SpawnFlatRadiusDeg    = 3f,
                    SpawnFalloffEdgeDeg   = 9f,

                    Seed                  = 1337,
                };
            }

            /// <summary>
            /// All-zero amplitudes — <see cref="SampleHeight"/> returns 0 for
            /// every direction. Lets callers keep the same call sites while
            /// disabling terrain entirely.
            /// </summary>
            public static Settings Flat(float radius)
            {
                return new Settings
                {
                    Radius                = radius,
                    ContinentAmplitude    = 0f,
                    ContinentFrequency    = 1f,
                    ContinentOctaves      = 1,
                    MountainAmplitude     = 0f,
                    MountainFrequency     = 1f,
                    MountainMaskThreshold = 1f,
                    MountainMaskSharpness = 1f,
                    CanyonAmplitude       = 0f,
                    CanyonFrequency       = 1f,
                    SpawnFlatRadiusDeg    = 0f,
                    SpawnFalloffEdgeDeg   = 1f,
                    Seed                  = 0,
                };
            }
        }

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Radial displacement (metres) for a vertex at unit direction
        /// <paramref name="unitDir"/>. Caller multiplies the planet's
        /// outward normal by (Radius + this) to place the vertex.
        /// </summary>
        public static float SampleHeight(Vector3 unitDir, in Settings s)
        {
            // 1. Continent base.
            float continent = Fbm(unitDir * s.ContinentFrequency, s.ContinentOctaves, s.Seed);
            // continent is in [-1, 1]-ish; remap to [0, 1] for masking convenience.
            float continent01 = continent * 0.5f + 0.5f;
            float continentH = continent * s.ContinentAmplitude;

            // 2. Mountains: ridged noise, masked to high continent zones.
            float ridge = RidgedNoise(unitDir * s.MountainFrequency, s.Seed + 7919);
            float mountainMask = Smoothstep01(s.MountainMaskThreshold,
                s.MountainMaskThreshold + (1f / Mathf.Max(0.0001f, s.MountainMaskSharpness)),
                continent01);
            float mountainH = ridge * mountainMask * s.MountainAmplitude;

            // 3. Canyons: ridged noise, subtracted, masked away from mountain zones.
            float canyonRidge = RidgedNoise(unitDir * s.CanyonFrequency, s.Seed + 31337);
            float canyonMask = 1f - mountainMask;                  // can't cut where mountains rise
            // Only deepen above some threshold so canyons feel like discrete cuts not a noisy haze.
            canyonRidge = Mathf.Max(0f, canyonRidge - 0.45f) / 0.55f;
            float canyonH = -canyonRidge * canyonMask * s.CanyonAmplitude;

            float total = continentH + mountainH + canyonH;

            // 4. Spawn flatness mask. y = unitDir.y = cos(latitude_from_north_pole).
            //    cos(SpawnFlatRadiusDeg) gives the y-threshold above which mask = 0.
            float yMaskHi = Mathf.Cos(s.SpawnFlatRadiusDeg * Mathf.Deg2Rad);
            float yMaskLo = Mathf.Cos(s.SpawnFalloffEdgeDeg * Mathf.Deg2Rad);
            // Above yMaskHi (closer to pole) → mask=0; below yMaskLo → mask=1.
            float flatness = Smoothstep01(yMaskHi, yMaskLo, unitDir.y);
            // Smoothstep01 is rising; we want falling from pole, so the args
            // are reversed (yMaskHi > yMaskLo, which Smoothstep01 handles by
            // returning monotone-in-the-second-arg-direction).
            return total * flatness;
        }

        // -----------------------------------------------------------------
        // Noise primitives
        // -----------------------------------------------------------------

        /// <summary>
        /// Fractal Brownian Motion of a 3D-style Perlin sample. Unity's
        /// <see cref="Mathf.PerlinNoise"/> is 2D; we sample three orthogonal
        /// planes and average to fake a smooth 3D field. Cheap, branch-free,
        /// good enough for visible-scale displacement (we're not doing
        /// per-pixel shader work).
        /// </summary>
        private static float Fbm(Vector3 p, int octaves, int seed)
        {
            float sum = 0f;
            float amp = 1f;
            float norm = 0f;
            float seedOffset = seed * 0.13f;
            for (int i = 0; i < octaves; i++)
            {
                sum  += amp * Noise3(p + Vector3.one * seedOffset);
                norm += amp;
                amp  *= 0.5f;
                p    *= 2f;
            }
            // Normalise so the result is in roughly [-1, 1].
            return (sum / norm) * 2f - 1f;
        }

        private const float NoiseOriginShift = 1000f; // push inputs well into positive Perlin domain

        private static float Noise3(Vector3 p)
        {
            // Average three Perlin samples on the three orthogonal planes to fake 3D.
            // Each sample is in [0, 1]. The big positive offsets avoid Mathf.PerlinNoise's
            // mirror-symmetry across 0 (which otherwise produces a visible north/south
            // reflection at the small unit-sphere domain we're sampling).
            float ox = p.x + NoiseOriginShift;
            float oy = p.y + NoiseOriginShift + 31.7f;
            float oz = p.z + NoiseOriginShift + 71.1f;
            float xy = Mathf.PerlinNoise(ox, oy);
            float yz = Mathf.PerlinNoise(oy + 11.3f, oz);
            float zx = Mathf.PerlinNoise(oz + 53.9f, ox);
            return (xy + yz + zx) * (1f / 3f);
        }

        /// <summary>
        /// Ridged noise: <c>1 - |2n - 1|</c>. Output in [0, 1] with sharp
        /// ridges at value 1 — the canonical trick for mountain spines and
        /// canyon walls.
        /// </summary>
        private static float RidgedNoise(Vector3 p, int seed)
        {
            float n = Noise3(p + Vector3.one * (seed * 0.13f));
            float ridged = 1f - Mathf.Abs(2f * n - 1f);
            // Mild sharpening (pow ~1.4) keeps a hint of ridge without the
            // knife-edge SM64 silhouette that ridged*ridged produces.
            return Mathf.Pow(ridged, 1.4f);
        }

        /// <summary>
        /// Smoothstep that gracefully handles a > b (returns the inverse
        /// ramp), so callers don't have to special-case "ramp from x to y"
        /// vs "ramp from y to x".
        /// </summary>
        private static float Smoothstep01(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1)) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
