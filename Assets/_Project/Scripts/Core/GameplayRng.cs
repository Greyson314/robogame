using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Seeded RNG for gameplay-observable rolls (best-practices §12.4:
    /// no <c>UnityEngine.Random</c> where the roll decides damage,
    /// trajectories, or anything another player can observe). One shared
    /// <see cref="System.Random"/> stream, reseedable for future
    /// replay / lockstep work. Cosmetic randomness (particle jitter,
    /// VFX throttles) should keep using <c>UnityEngine.Random</c>.
    /// </summary>
    public static class GameplayRng
    {
        private const int DefaultSeed = 0x5EED;

        private static System.Random s_rng = new System.Random(DefaultSeed);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_rng = new System.Random(DefaultSeed);

        /// <summary>Restart the stream (replay / server-sync hook).</summary>
        public static void Reseed(int seed) => s_rng = new System.Random(seed);

        /// <summary>Uniform [0, 1).</summary>
        public static float Value => (float)s_rng.NextDouble();

        /// <summary>Uniform point inside the unit circle (polar sampling —
        /// sqrt on the radius keeps area density uniform).</summary>
        public static Vector2 InsideUnitCircle
        {
            get
            {
                float r = Mathf.Sqrt(Value);
                float theta = Value * 2f * Mathf.PI;
                return new Vector2(r * Mathf.Cos(theta), r * Mathf.Sin(theta));
            }
        }

        /// <summary>Uniform point inside the unit sphere (cbrt radius ×
        /// uniform direction).</summary>
        public static Vector3 InsideUnitSphere
        {
            get
            {
                float z = Value * 2f - 1f;                  // cos(polar) uniform
                float phi = Value * 2f * Mathf.PI;
                float s = Mathf.Sqrt(1f - z * z);
                Vector3 dir = new Vector3(s * Mathf.Cos(phi), s * Mathf.Sin(phi), z);
                float r = Mathf.Pow(Value, 1f / 3f);
                return dir * r;
            }
        }
    }
}
