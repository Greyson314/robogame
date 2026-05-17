using UnityEngine;

namespace Robogame.Voxel
{
    /// <summary>
    /// Runtime, allocation-free sampler for the arena's procedural hill
    /// surface. This is the single source of truth for "what world-Y is
    /// the ground at (x, z)" — both the visual grass mesh
    /// (<c>HillsGround</c>, editor-side) and the diggable voxel surface
    /// (<see cref="DigZone"/>, runtime) sample through here so the two
    /// layers align to the same height with no drift.
    /// </summary>
    /// <remarks>
    /// The math mirrors the original hard-coded <c>HillsGround.SampleHeight</c>
    /// exactly: two octaves of <see cref="Mathf.PerlinNoise"/> centred on
    /// zero, modulated by a central-flat falloff and an edge-flat falloff.
    /// Moving it here (a runtime asmdef the editor tools already reference)
    /// lets the voxel zone seed its SDF surface per-column to the same
    /// curve the grass mesh was baked from.
    /// </remarks>
    [System.Serializable]
    public struct HeightmapParams
    {
        [Tooltip("When false, Sample() returns 0 everywhere (flat). Lets a DigZone " +
                 "fall back to its half-space / full-solid seeding when no heightmap " +
                 "is wired in.")]
        public bool Enabled;

        public Vector2 NoiseOffset;
        public float HillFreqLow;
        public float HillAmpLow;
        public float HillFreqHigh;
        public float HillAmpHigh;
        public float FlatRadius;
        public float RampOuter;
        public float EdgeFlatStart;
        public float EdgeFlatEnd;

        /// <summary>Disabled params — Sample() returns 0 (a flat plane at y=0).</summary>
        public static HeightmapParams Disabled => default;
    }

    public static class HeightmapField
    {
        /// <summary>
        /// World-space ground height at <paramref name="x"/>,
        /// <paramref name="z"/>. Returns 0 when <paramref name="p"/> is not
        /// <see cref="HeightmapParams.Enabled"/>.
        /// </summary>
        public static float Sample(in HeightmapParams p, float x, float z)
        {
            if (!p.Enabled) return 0f;

            // Two-octave Perlin, both centred on 0 (Mathf.PerlinNoise is
            // [0,1] so we shift by -0.5 first).
            float n1 = Mathf.PerlinNoise((x + p.NoiseOffset.x) * p.HillFreqLow,
                                          (z + p.NoiseOffset.y) * p.HillFreqLow) - 0.5f;
            float n2 = Mathf.PerlinNoise((x - p.NoiseOffset.y) * p.HillFreqHigh,
                                          (z + p.NoiseOffset.x) * p.HillFreqHigh) - 0.5f;
            float h = n1 * p.HillAmpLow * 2f + n2 * p.HillAmpHigh * 2f;

            float r = Mathf.Sqrt(x * x + z * z);

            // Inner falloff: 0 inside the spawn zone, ramps to 1 by rampOuter.
            float inner = Smoothstep(p.FlatRadius, p.RampOuter, r);
            // Outer falloff: 1 in the playable region, ramps back to 0 by
            // edgeFlatEnd so the boundary sits on flat ground.
            float outer = 1f - Smoothstep(p.EdgeFlatStart, p.EdgeFlatEnd, r);

            return h * inner * outer;
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-5f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
