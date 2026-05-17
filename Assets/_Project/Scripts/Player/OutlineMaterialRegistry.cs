using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Runtime lookup from an outlined block material (MK Toon
    /// <c>+ Outline</c> shader variant) to its plain (no-outline)
    /// counterpart. Authored by the editor scaffolder
    /// <c>BlockMaterialsPlain</c> and loaded once at runtime via
    /// <see cref="Resources"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why a registry and not a <c>BlockDefinition._materialPlain</c>
    /// field (the original plan): block runtime components don't surface
    /// their <c>BlockDefinition</c>, so a chassis can't ask a placed
    /// block "what's your plain material?". Keying off the renderer's
    /// current shared material instead keeps this fully decoupled from
    /// the block data model and trivially testable.
    /// </para>
    /// <para>
    /// The MK Toon outline is a *separate shader asset*
    /// (<c>… + Outline</c>), gated by validated keywords — not a runtime
    /// float — so a <see cref="MaterialPropertyBlock"/> override of
    /// <c>_Outline</c> cannot disable the pass. Swapping the whole shared
    /// material to the plain asset is the only mechanism that actually
    /// drops the outline draw.
    /// </para>
    /// </remarks>
    public sealed class OutlineMaterialRegistry : ScriptableObject
    {
        [System.Serializable]
        public struct Pair
        {
            public Material Outline;
            public Material Plain;
        }

        [SerializeField] private List<Pair> _pairs = new();

        public const string ResourcePath = "OutlineMaterialRegistry";

        private Dictionary<Material, Material> _lookup;

        private static OutlineMaterialRegistry s_instance;
        private static bool s_loaded;

        /// <summary>
        /// The project-wide registry, or null if none has been scaffolded
        /// (in which case the outline cull is simply inert — every chassis
        /// keeps its authored materials).
        /// </summary>
        public static OutlineMaterialRegistry Instance
        {
            get
            {
                if (!s_loaded)
                {
                    s_instance = Resources.Load<OutlineMaterialRegistry>(ResourcePath);
                    s_loaded = true;
                }
                return s_instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Statics survive domain reload; the cached instance + its
            // lookup must not leak across play sessions.
            s_instance = null;
            s_loaded = false;
        }

        /// <summary>
        /// The plain counterpart of <paramref name="outline"/>, or
        /// <paramref name="outline"/> itself if it isn't an outlined
        /// material (already non-outline → nothing to swap).
        /// </summary>
        public Material GetPlain(Material outline)
        {
            if (outline == null) return null;
            if (_lookup == null)
            {
                _lookup = new Dictionary<Material, Material>(_pairs.Count);
                foreach (Pair p in _pairs)
                    if (p.Outline != null && p.Plain != null)
                        _lookup[p.Outline] = p.Plain;
            }
            return _lookup.TryGetValue(outline, out Material plain) ? plain : outline;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only: replace the pair list (scaffolder use).</summary>
        public void EditorSetPairs(List<Pair> pairs)
        {
            _pairs = pairs;
            _lookup = null;
        }
#endif
    }
}
