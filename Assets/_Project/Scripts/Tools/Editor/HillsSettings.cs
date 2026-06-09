using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Authoring knobs for <see cref="HillsGround"/>'s procedural arena
    /// terrain. Edited in the inspector; consumed at scaffold time.
    /// </summary>
    /// <remarks>
    /// Lives at <c>Assets/_Project/ScriptableObjects/HillsSettings.asset</c>.
    /// <see cref="HillsGround.Build"/> auto-creates the asset on first
    /// run with the constants previously hard-coded in code, so existing
    /// projects upgrade transparently.
    /// </remarks>
    [CreateAssetMenu(fileName = "HillsSettings", menuName = "Robogame/Hills Settings", order = 50)]
    public sealed class HillsSettings : ScriptableObject
    {
        // -----------------------------------------------------------------
        // Mesh resolution
        // -----------------------------------------------------------------

        [Header("Mesh")]
        [Tooltip("World-space size of the ground (matches a primitive Plane scaled 22 → 220 m).")]
        [Min(10f)]
        public float size = 220f;

        [Tooltip("Vertex grid resolution per side. 121 → 14641 verts, 28800 tris (≈1.83 m sample spacing). " +
                 "Bumping past ~250 will overflow 16-bit indices — only do it if you also handle index format.")]
        [Range(33, 251)]
        public int resolution = 121;

        // -----------------------------------------------------------------
        // Hill profile (two octaves of Perlin noise)
        // -----------------------------------------------------------------

        [Header("Hills (low-frequency, big rolling shape)")]
        [Tooltip("Peak height of the broad rolling hills, in metres.")]
        [Min(0f)]
        public float hillAmpLow = 4f;

        [Tooltip("Spatial frequency of the broad hills. Lower = wider hills. " +
                 "0.025 ≈ 40 m wavelength.")]
        [Min(0.001f)]
        public float hillFreqLow = 0.025f;

        [Header("Detail (high-frequency, small bumps on top)")]
        [Tooltip("Peak height of the small detail bumps, in metres.")]
        [Min(0f)]
        public float hillAmpHigh = 1f;

        [Tooltip("Spatial frequency of the detail bumps. 0.080 ≈ 12 m wavelength.")]
        [Min(0.001f)]
        public float hillFreqHigh = 0.08f;

        // -----------------------------------------------------------------
        // Falloffs
        // -----------------------------------------------------------------

        [Header("Central flat zone (spawn + obstacle course)")]
        [Tooltip("Radius (m) inside which the ground is dead flat. The obstacle course extends to ~16 m, so 25 m gives a comfortable shoulder.")]
        [Min(0f)]
        public float flatRadius = 25f;

        [Tooltip("Radius (m) at which hills reach full height. Smoothstep falloff between flatRadius and rampOuter.")]
        [Min(0f)]
        public float rampOuter = 55f;

        [Header("Boundary flatten (so wall ring sits on level ground)")]
        [Tooltip("Radius (m) at which hills start ramping back down toward zero.")]
        [Min(0f)]
        public float edgeFlatStart = 80f;

        [Tooltip("Radius (m) at which the ground is fully flat again. The wall ring is at ±100 m, so this should match.")]
        [Min(0f)]
        public float edgeFlatEnd = 100f;

        // -----------------------------------------------------------------
        // Noise determinism
        // -----------------------------------------------------------------

        [Header("Noise seed")]
        [Tooltip("Stable offset into Mathf.PerlinNoise's domain. Change to roll a different hill layout. " +
                 "Two builds with the same offset always produce the same hills.")]
        public Vector2 noiseOffset = new Vector2(137.31f, 91.47f);

        // -----------------------------------------------------------------
        // Structure — the "Sunken Crossing" arena layout (session 119).
        // These add big readable shapes on top of the rolling detail above:
        // two diagonal ridges, a central no-man's-land valley, and a
        // symmetric pair of base bowls. Geometry (where each sits) is fixed
        // in HeightmapField; these knobs only scale the heights. All stay
        // ≤ ~13 m so the diggable surface fits the voxel volume.
        // -----------------------------------------------------------------

        [Header("Structure (Sunken Crossing layout)")]
        [Tooltip("Crown height (m) of the two diagonal ridges forming the arena's X. 0 = flat / no ridges.")]
        [Range(0f, 13f)]
        public float ridgeAmp = 13f;

        [Tooltip("Depth (m) of the shallow east-west valley along z≈0 — the no-man's-land that keeps the depot sightline low. 0 = no valley.")]
        [Range(0f, 8f)]
        public float valleyDepth = 2.5f;

        [Tooltip("Rim height (m) of the symmetric base bowls at z≈±90 (the team depots). Floor stays near y=0 so depot pads land flush. 0 = no bowls.")]
        [Range(0f, 12f)]
        public float bowlAmp = 6.5f;
    }
}
