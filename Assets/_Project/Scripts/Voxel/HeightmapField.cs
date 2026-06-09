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
    /// <para>
    /// Two layers of shape, summed:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Detail</b> — two octaves of <see cref="Mathf.PerlinNoise"/>
    /// centred on zero (the original gentle rolling hills), windowed by a
    /// central-flat falloff (flat spawn) and an edge falloff (flat under
    /// the wall ring).</item>
    /// <item><b>Structure</b> — the "Sunken Crossing" arena layout
    /// (session 119): two diagonal ridges forming an X with an open centre
    /// gap, a shallow east-west valley along z≈0 that keeps the depot
    /// sightline low, and a symmetric pair of raised base bowls at z≈±BowlZ
    /// so each team fights out of a mild depression. All structural terms
    /// are even in z (mirror-symmetric north↔south) so the two sides play
    /// fairly. Every feature stays ≤ ~13 m so the diggable surface fits the
    /// single-chunk-tall voxel volume (TERRAFORMING_PLAN §7 budget); taller
    /// drama lives in non-diggable backdrop peaks beyond the wall ring.</item>
    /// </list>
    /// <para>
    /// The structural amplitudes default to 0, so a <see cref="HeightmapParams"/>
    /// that only sets the legacy Perlin fields behaves exactly as before —
    /// existing tests and any other consumer are unaffected until the
    /// arena settings opt in.
    /// </para>
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

        // -----------------------------------------------------------------
        // Structural terms (session 119 — "Sunken Crossing"). Default 0 →
        // pure legacy rolling-hills behaviour.
        // -----------------------------------------------------------------

        [Tooltip("Crown height (m) of the two diagonal ridges that form the arena's X. 0 = no ridges.")]
        public float RidgeAmp;

        [Tooltip("Depth (m) of the shallow east-west valley along z≈0 (the no-man's-land that keeps the depot sightline low). 0 = no valley.")]
        public float ValleyDepth;

        [Tooltip("Rim height (m) of the symmetric base bowls at z≈±BowlZ. 0 = no bowls. The bowl floor sits near y=0 so depot pads land flush.")]
        public float BowlAmp;

        /// <summary>Disabled params — Sample() returns 0 (a flat plane at y=0).</summary>
        public static HeightmapParams Disabled => default;
    }

    public static class HeightmapField
    {
        // -----------------------------------------------------------------
        // Structural geometry constants (session 119). These define WHERE
        // the ridges / valley / bowls sit; the per-feature heights are the
        // tunable HillsSettings knobs (RidgeAmp / ValleyDepth / BowlAmp).
        // Authored for the ±170 m combat arena (360 m grass mesh).
        // -----------------------------------------------------------------

        // Diagonal ridges run along the lines z = x and z = -x.
        private const float RidgeWidth   = 14f;   // Gaussian half-width across a ridge line (m) — narrow → steep cliff-like faces
        private const float RidgeInnerR  = 56f;   // ridges fade IN beyond this radius (open centre gap + flat mid-field combat box keeps spawned targets grounded)
        private const float RidgeInnerW  = 20f;   // feather of the inner fade
        private const float RidgeOuterR  = 150f;  // ridges fade OUT by here (clear flank corridors at the walls)
        private const float RidgeOuterW  = 26f;   // feather of the outer fade
        private const float Inv2Sqrt     = 0.70710678f; // 1/√2, signed distance to a 45° line

        // Central east-west valley along z = 0.
        private const float ValleyWidth  = 26f;   // Gaussian half-width along z (m)

        // Symmetric base bowls centred at (0, ±BowlZ).
        private const float BowlZ        = 92f;   // matches the team depots at z = ±90, nudged out
        private const float BowlRimSigma = 48f;   // rim spread
        private const float BowlDipSigma = 27f;   // central depression spread (< rim → a bowl, not a dome)
        private const float BowlFloor    = 0.5f;  // net height (m) at the bowl centre, so depot pads sit ~flush

        /// <summary>
        /// World-space ground height at <paramref name="x"/>,
        /// <paramref name="z"/>. Returns 0 when <paramref name="p"/> is not
        /// <see cref="HeightmapParams.Enabled"/>.
        /// </summary>
        public static float Sample(in HeightmapParams p, float x, float z)
        {
            if (!p.Enabled) return 0f;

            float r = Mathf.Sqrt(x * x + z * z);

            // Inner falloff: 0 inside the spawn zone, ramps to 1 by rampOuter.
            float inner = Smoothstep(p.FlatRadius, p.RampOuter, r);
            // Outer falloff: 1 in the playable region, ramps back to 0 by
            // edgeFlatEnd so the boundary sits on flat ground.
            float outer = 1f - Smoothstep(p.EdgeFlatStart, p.EdgeFlatEnd, r);
            float field = inner * outer;

            // ---- Detail: the original two-octave rolling hills -----------
            float n1 = Mathf.PerlinNoise((x + p.NoiseOffset.x) * p.HillFreqLow,
                                          (z + p.NoiseOffset.y) * p.HillFreqLow) - 0.5f;
            float n2 = Mathf.PerlinNoise((x - p.NoiseOffset.y) * p.HillFreqHigh,
                                          (z + p.NoiseOffset.x) * p.HillFreqHigh) - 0.5f;
            float detail = n1 * p.HillAmpLow * 2f + n2 * p.HillAmpHigh * 2f;

            // Fast-out for the legacy (structure-free) configuration so
            // existing consumers pay nothing for the new terms.
            if (p.RidgeAmp == 0f && p.ValleyDepth == 0f && p.BowlAmp == 0f)
                return detail * field;

            // ---- Structure: ridges + valley + bowls ---------------------

            // `detailMask` thins the rolling detail toward the structural
            // features so neither a ridge crown nor a bowl floor fights the
            // big readable shape (and so crowns don't stack detail on top of
            // the ridge and saturate flat against the voxel ceiling).
            float detailMask = 1f;

            // Two diagonal ridges (z = x and z = -x), each a Gaussian across
            // the line, windowed to leave an open centre gap and clear flank
            // corridors near the walls. The X never touches x = 0 or z = 0
            // at full height, so depot-to-depot sightlines stay open.
            float ridge = 0f;
            if (p.RidgeAmp != 0f)
            {
                float dPos = (z - x) * Inv2Sqrt; // signed distance to z =  x
                float dNeg = (z + x) * Inv2Sqrt; // signed distance to z = -x
                float gate = Smoothstep(RidgeInnerR, RidgeInnerR + RidgeInnerW, r)
                           * (1f - Smoothstep(RidgeOuterR - RidgeOuterW, RidgeOuterR, r));
                float shape = Gauss(dPos, RidgeWidth) + Gauss(dNeg, RidgeWidth);
                ridge = p.RidgeAmp * shape * gate;
                // Thin detail toward the crown → clean peak, not a clamped
                // mesa. Keep ~25 % so the crown still has surface texture.
                detailMask *= 1f - 0.75f * Mathf.Clamp01(shape) * gate;
            }

            // Shallow east-west valley along z = 0. Windowed by `field` so it
            // never digs below the flat spawn pad at the centre.
            float valley = 0f;
            if (p.ValleyDepth != 0f)
                valley = -p.ValleyDepth * Gauss(z, ValleyWidth) * field;

            // Symmetric base bowls at (0, ±BowlZ): a raised rim minus a
            // central dip → a shallow concave bowl whose floor sits at
            // BowlFloor so each team's depot pad lands ~flush. Using |z|
            // mirrors the bowl to both sides.
            float bowl = 0f;
            if (p.BowlAmp != 0f)
            {
                float bz = Mathf.Abs(z) - BowlZ;
                float d2 = x * x + bz * bz;
                float dip = p.BowlAmp - BowlFloor; // floor = rim-peak coefficient − dip ≈ BowlFloor at centre
                bowl = (p.BowlAmp * GaussSq(d2, BowlRimSigma)
                      - dip * GaussSq(d2, BowlDipSigma)) * field;
                // Suppress rolling detail toward each bowl centre so the
                // depot ground is predictable (the pad is a flat disc).
                detailMask *= 1f - GaussSq(d2, BowlDipSigma);
            }

            float h = detail * field * detailMask + ridge + valley + bowl;

            // Keep the diggable surface inside the voxel volume's headroom
            // (zone top ≈ y+16; leave a safety margin). Backdrop drama lives
            // beyond the walls as non-diggable static geometry.
            return Mathf.Clamp(h, -10f, 13.5f);
        }

        // exp(-d² / w²) — a unit-peak Gaussian of a signed distance.
        private static float Gauss(float d, float w)
        {
            float t = d / w;
            return Mathf.Exp(-t * t);
        }

        // exp(-d² / (2σ²)) given d² directly (saves a sqrt for radial terms).
        private static float GaussSq(float d2, float sigma)
        {
            return Mathf.Exp(-d2 / (2f * sigma * sigma));
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-5f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
