using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Server/world-canonical armour-interaction constants: spike ramming
    /// bonus and wedge glancing-hit deflection. Same pattern and rationale
    /// as <see cref="ImpactConfig"/>.
    /// </summary>
    /// <remarks>
    /// <b>Why this is not a Tweakable.</b> Armour multipliers are
    /// gameplay-observable outcomes (hard invariant #1). These are world
    /// constants — identical for every player in a match — so they live in
    /// a server-authoritative asset. A missing
    /// <c>Resources/ArmorConfig.asset</c> falls back to a
    /// default-constructed instance (behaviour-identical defaults).
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Armor Config", fileName = "ArmorConfig")]
    public sealed class ArmorConfig : ScriptableObject
    {
        public const string ResourcePath = "ArmorConfig";

        [Tooltip("Ring-0 damage multiplier applied to a chassis that rams into an enemy spike block.")]
        [SerializeField, Min(1f)] private float _spikeDamageMultiplier = 2.0f;

        [Tooltip("Incidence angle (deg from head-on) at which wedge deflection starts. Below this, full damage.")]
        [SerializeField, Range(0f, 89f)] private float _wedgeDeflectStartDeg = 30f;

        [Tooltip("Damage multiplier at a perfect graze (90 deg). Deflection lerps from 1 at the start angle down to this.")]
        [SerializeField, Range(0f, 1f)] private float _wedgeMinMultiplier = 0.25f;

        public float SpikeDamageMultiplier => _spikeDamageMultiplier;
        public float WedgeDeflectStartDeg  => _wedgeDeflectStartDeg;
        public float WedgeMinMultiplier    => _wedgeMinMultiplier;

        // -----------------------------------------------------------------

        private static ArmorConfig s_cached;

        /// <summary>
        /// The active armour config. Loads <c>Resources/ArmorConfig.asset</c>
        /// once; if absent, returns a default instance. Cache cleared on
        /// domain reload because statics survive it but the object does not.
        /// </summary>
        public static ArmorConfig Instance
        {
            get
            {
                if (s_cached != null) return s_cached;
                s_cached = Resources.Load<ArmorConfig>(ResourcePath);
                if (s_cached == null) s_cached = CreateInstance<ArmorConfig>();
                return s_cached;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => s_cached = null;
    }
}
