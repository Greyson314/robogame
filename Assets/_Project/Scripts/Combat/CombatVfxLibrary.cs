using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Runtime-accessible bag of references to combat VFX prefabs (explosions,
    /// hit decals, etc.). The single instance lives at
    /// <c>Resources/CombatVfxLibrary.asset</c> so any runtime code can fetch
    /// it via <see cref="Load"/> without a scene-time wire-up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We don't put third-party FX prefabs (Cartoon FX Remaster, etc.) in
    /// <c>Resources/</c> directly — we'd lose the ability to rebuild the
    /// import path safely. Instead, this SO holds <see cref="GameObject"/>
    /// references to those prefabs; the SO is in <c>Resources/</c>, the
    /// prefabs live wherever the asset pack put them, and Unity drags both
    /// into the build automatically when the SO is loaded.
    /// </para>
    /// <para>
    /// The asset is created and populated by <c>CombatVfxWizard</c> in the
    /// Editor. At runtime, <see cref="Load"/> caches the result so the
    /// Resources lookup is a one-time cost.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Robogame/Combat VFX Library", fileName = "CombatVfxLibrary")]
    public sealed class CombatVfxLibrary : ScriptableObject
    {
        public const string ResourcePath = "CombatVfxLibrary";

        [Header("Bomb explosion")]
        [Tooltip("Particle prefab spawned at the bomb impact point. Should auto-destroy itself (e.g. CFXR particles with auto-destroy).")]
        [SerializeField] private GameObject _bombExplosion;

        public GameObject BombExplosion => _bombExplosion;

        // -----------------------------------------------------------------

        private static CombatVfxLibrary s_cached;

        public static CombatVfxLibrary Load()
        {
            if (s_cached != null) return s_cached;
            s_cached = Resources.Load<CombatVfxLibrary>(ResourcePath);
            if (s_cached == null)
            {
                Debug.LogWarning($"[Robogame] CombatVfxLibrary not found at Resources/{ResourcePath}. " +
                                 "Run Robogame → Scaffold → Create Combat VFX Library.");
            }
            return s_cached;
        }
    }
}
