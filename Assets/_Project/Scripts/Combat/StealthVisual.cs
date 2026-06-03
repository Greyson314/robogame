using System.Collections.Generic;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Transient helper that fades a robot's mesh renderers to a near-invisible
    /// ghost for the invisibility module, then restores them exactly. Caches
    /// each renderer's original <c>sharedMaterial</c> and points it at one
    /// shared translucent cloak material — a per-renderer <i>reference</i> swap,
    /// not a mutation of the shared block material, so it never bleeds onto
    /// other bots and allocates nothing per renderer (invariant #6; fires at
    /// most once per cooldown anyway).
    /// </summary>
    /// <remarks>
    /// Only <see cref="MeshRenderer"/>s are swapped — line renderers (ropes)
    /// and particle systems are left alone so the cloak can't break their
    /// specialised materials. The combined static-mesh child built by
    /// <c>ChassisInstancedRenderer</c> is a MeshRenderer, so the bulk hull is
    /// covered.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StealthVisual : MonoBehaviour
    {
        private readonly List<(MeshRenderer renderer, Material original)> _cached = new();
        private static Material s_cloakMaterial;

        /// <summary>Fade <paramref name="robot"/> to the cloak ghost. Returns the live helper.</summary>
        public static StealthVisual Activate(Robot robot)
        {
            StealthVisual sv = robot.GetComponent<StealthVisual>();
            if (sv == null) sv = robot.gameObject.AddComponent<StealthVisual>();
            sv.Apply(robot);
            return sv;
        }

        private void Apply(Robot robot)
        {
            _cached.Clear();
            Material cloak = CloakMaterial;
            foreach (MeshRenderer mr in robot.GetComponentsInChildren<MeshRenderer>(includeInactive: false))
            {
                if (mr == null) continue;
                _cached.Add((mr, mr.sharedMaterial));
                mr.sharedMaterial = cloak;
            }
        }

        /// <summary>Restore the original materials and remove the helper.</summary>
        public void Deactivate()
        {
            for (int i = 0; i < _cached.Count; i++)
            {
                MeshRenderer mr = _cached[i].renderer;
                if (mr != null) mr.sharedMaterial = _cached[i].original;
            }
            _cached.Clear();
            Destroy(this);
        }

        private static Material CloakMaterial
        {
            get
            {
                if (s_cloakMaterial != null) return s_cloakMaterial;
                // Near-colourless faint film at ~5% alpha — reads as "barely
                // there", not a bright turquoise ghost. The transparent setup
                // (keyword + ZWrite off) is what actually makes it see-through.
                var c = new Color(0.7f, 0.85f, 0.95f, 0.05f);
                s_cloakMaterial = RuntimeMaterials.UnlitTransparent(c);
                s_cloakMaterial.name = "CloakGhostMat";
                return s_cloakMaterial;
            }
        }

        // Statics survive domain reload but GameObjects don't — drop the cached
        // material so the first cloak after a reload rebuilds it cleanly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_cloakMaterial = null;
    }
}
