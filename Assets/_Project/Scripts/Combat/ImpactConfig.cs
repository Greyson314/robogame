using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Server/world-canonical ramming-impact constants. These drive the
    /// kinetic-energy damage curve in <see cref="MomentumImpactHandler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not a Tweakable.</b> Ramming damage is a
    /// gameplay-observable outcome (PHYSICS_PLAN §1.5 / §5, hard invariant
    /// #1). A per-machine local-JSON slider that changes how much damage a
    /// collision deals desyncs the moment a second client exists. These
    /// values are world physics — the same for every player in a match —
    /// so they live in a server-authoritative asset, not in
    /// <c>Tweakables</c>. When netcode lands this asset is a
    /// content-hash-checked "Bucket A" world constant (NETCODE_PLAN §6):
    /// every machine loads the identical asset; the server is the source
    /// of truth.
    /// </para>
    /// <para>
    /// Field defaults equal the pre-migration Tweakable defaults, so a
    /// missing <c>Resources/ImpactConfig.asset</c> falls back to a
    /// default-constructed instance with byte-identical behaviour — same
    /// tolerance pattern as <see cref="Robogame.Core.AudioCueLibrary"/>.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Impact Config", fileName = "ImpactConfig")]
    public sealed class ImpactConfig : ScriptableObject
    {
        public const string ResourcePath = "ImpactConfig";

        [Tooltip("HP dealt per kJ of normal-relative collision kinetic energy.")]
        [SerializeField, Min(0f)] private float _damagePerKj = 5.0f;

        [Tooltip("m/s normal-relative speed below which a collision deals no ramming damage.")]
        [SerializeField, Min(0f)] private float _minSpeed = 2.0f;

        [Tooltip("Direct-hit cell damage multiplier.")]
        [SerializeField, Min(0f)] private float _ring0Scale = 1.00f;

        [Tooltip("1-step neighbour cell damage multiplier.")]
        [SerializeField, Min(0f)] private float _ring1Scale = 0.30f;

        [Tooltip("2-step neighbour cell damage multiplier.")]
        [SerializeField, Min(0f)] private float _ring2Scale = 0.10f;

        public float DamagePerKj => _damagePerKj;
        public float MinSpeed    => _minSpeed;
        public float Ring0Scale  => _ring0Scale;
        public float Ring1Scale  => _ring1Scale;
        public float Ring2Scale  => _ring2Scale;

        // -----------------------------------------------------------------

        private static ImpactConfig s_cached;

        /// <summary>
        /// The active impact config. Loads <c>Resources/ImpactConfig.asset</c>
        /// once; if absent, returns a default instance whose values equal
        /// the historical Tweakable defaults (behaviour-identical). Cached
        /// for the process; the cache is cleared on domain reload because
        /// statics survive it but the loaded object does not.
        /// </summary>
        public static ImpactConfig Instance
        {
            get
            {
                if (s_cached != null) return s_cached;
                s_cached = Resources.Load<ImpactConfig>(ResourcePath);
                if (s_cached == null) s_cached = CreateInstance<ImpactConfig>();
                return s_cached;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => s_cached = null;
    }
}
