using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Pure math: samples wave height (and, when needed, surface normal)
    /// at any world-space (x, z) and time. The buoyancy sampler in
    /// <see cref="BuoyancyController"/> calls this per-block; future
    /// visual mesh / shader code will read the same function so the
    /// physics and the rendered surface can never drift.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Model: a sum of three Gerstner waves with hard-coded directions
    /// (0°, 60°, 135°) and lengths derived as <c>λ, λ/1.6, λ/2.6</c>
    /// from the <see cref="Tweakables.WaveLength"/> base. The base
    /// amplitude / speed / steepness all come from
    /// <see cref="Tweakables"/> so the player can tune the sea state in
    /// Settings ▸ Tweaks ▸ Water without a recompile.
    /// </para>
    /// <para>
    /// Why Gerstner instead of plain sine? Gerstner displaces particles
    /// in BOTH x and y, so wave crests *peak* and troughs *flatten* the
    /// way real ocean waves do. Steepness=0 collapses the formula to
    /// pure sine (used to compute height-only when we don't need the
    /// horizontal displacement, e.g. a fast height check).
    /// </para>
    /// <para>
    /// Cost: 3 sin/cos pairs per sample. For a 100-block chassis at
    /// 50 Hz that's ~30 k trig ops/sec, well below the noise floor of
    /// the existing PhysX <c>AddForceAtPosition</c> calls. No caching is
    /// necessary at Pass-A scale; if profiling shows a hotspot later we
    /// can hoist the per-frame constants out of the inner loop.
    /// </para>
    /// </remarks>
    public static class WaterSurface
    {
        // Directional unit vectors (cosθ, sinθ) in the XZ plane. Hard-
        // coded so each wave train has a stable identity — the player
        // can't reorient the sea, just change its severity. Wind heading
        // becomes a tweakable in Phase 4.
        private static readonly Vector2[] s_dir =
        {
            new Vector2( 1.0000f,  0.0000f),  //   0°  (along +X)
            new Vector2( 0.5000f,  0.8660f),  //  60°
            new Vector2(-0.7071f,  0.7071f),  // 135°
        };

        // Per-train wavelength scale relative to the base length, and
        // amplitude scale relative to base amplitude. Smaller waves
        // ride on top of larger ones for visual + physical complexity.
        private static readonly float[] s_lenScale = { 1.00f, 0.625f, 0.385f };
        private static readonly float[] s_ampScale = { 1.00f, 0.55f,  0.30f };

        /// <summary>
        /// Sample the water surface height at world-space (<paramref name="x"/>,
        /// <paramref name="z"/>) at the given <paramref name="time"/>. The
        /// returned value is the absolute world Y of the surface — i.e.
        /// includes <see cref="WaterVolume.SurfaceY"/>.
        /// </summary>
        /// <summary>
        /// The four wave tweakables, hoisted once per frame/tick. Each
        /// <see cref="Tweakables.Get(string)"/> is a string-keyed dictionary
        /// probe; the per-vertex mesh loop (65×65 verts) plus the per-block
        /// buoyancy loop were paying 4 probes per call — ~17k per rendered
        /// frame (perf doc §8.9). Hot loops call <see cref="Sample"/> and
        /// pass the params through.
        /// </summary>
        public readonly struct WaveParams
        {
            public readonly float Amplitude, Length, Speed, Steepness;
            public WaveParams(float amplitude, float length, float speed, float steepness)
            { Amplitude = amplitude; Length = length; Speed = speed; Steepness = steepness; }

            public static WaveParams Sample() => new WaveParams(
                Tweakables.Get(Tweakables.WaveAmplitude),
                Mathf.Max(0.01f, Tweakables.Get(Tweakables.WaveLength)),
                Tweakables.Get(Tweakables.WaveSpeed),
                Mathf.Clamp01(Tweakables.Get(Tweakables.WaveSteepness)));
        }

        public static float SampleHeight(WaterVolume water, float x, float z, float time)
            => SampleHeight(water, WaveParams.Sample(), x, z, time);

        /// <summary>Hot-loop overload — caller hoists <see cref="WaveParams.Sample"/> once.</summary>
        public static float SampleHeight(WaterVolume water, in WaveParams p, float x, float z, float time)
        {
            if (water == null) return 0f;

            float baseY     = water.SurfaceY;
            float amplitude = p.Amplitude;
            if (amplitude <= 0f) return baseY;

            float length    = p.Length;
            float speed     = p.Speed;
            float steepness = p.Steepness;

            float displacementY = 0f;
            for (int i = 0; i < s_dir.Length; i++)
            {
                Vector2 d = s_dir[i];
                float lambda = length * s_lenScale[i];
                float k = (2f * Mathf.PI) / lambda;
                // Deep-water dispersion: ω² = g·k. We want speed in m/s so
                // we use ω = k·c where c is the user-supplied phase speed
                // (lets the player crank speed without amplitude blowing up).
                float omega = k * speed;
                float amp = amplitude * s_ampScale[i];

                float phase = k * (d.x * x + d.y * z) - omega * time;

                // Plain sin contributes pure height. The Gerstner term
                // (steepness · cos) only changes the X/Z particle path,
                // not Y, but we mix it in as a peakiness multiplier so
                // higher steepness produces sharper crests and flatter
                // troughs (common shorthand for "Gerstner-flavoured" sin).
                float crestiness = 1f + steepness * Mathf.Cos(phase) * 0.5f;
                displacementY += amp * Mathf.Sin(phase) * crestiness;
            }

            return baseY + displacementY;
        }

        /// <summary>
        /// Approximate surface normal at (<paramref name="x"/>,
        /// <paramref name="z"/>). Computed by central differences on
        /// <see cref="SampleHeight"/> so it always agrees with whatever
        /// the height sampler is doing (one source of truth).
        /// </summary>
        public static Vector3 SampleNormal(WaterVolume water, float x, float z, float time, float epsilon = 0.5f)
        {
            float hL = SampleHeight(water, x - epsilon, z, time);
            float hR = SampleHeight(water, x + epsilon, z, time);
            float hD = SampleHeight(water, x, z - epsilon, time);
            float hU = SampleHeight(water, x, z + epsilon, time);
            // Cross-product of tangents (along +X and +Z) = surface normal.
            Vector3 tx = new Vector3(2f * epsilon, hR - hL, 0f);
            Vector3 tz = new Vector3(0f, hU - hD, 2f * epsilon);
            return Vector3.Cross(tz, tx).normalized;
        }
    }
}
