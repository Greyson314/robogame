using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Static registry of live chassis-root Rigidbodies. Lets low-level
    /// systems (Movement blocks, pickups) iterate "every chassis in the
    /// scene" without a physics overlap query or an asmdef reference to
    /// <c>Robogame.Robots</c> (Movement deliberately does not reference
    /// Robots). <c>Robot</c> registers its root body OnEnable and
    /// unregisters OnDisable.
    /// </summary>
    /// <remarks>
    /// Born from the magnet pull-field fix: an OverlapSphere with mask ~0
    /// fills its fixed buffer with the owner's own block colliders, so a
    /// real target chassis could be silently dropped. Iterating this
    /// registry by distance is both cheaper and saturation-proof.
    /// </remarks>
    public static class ChassisRegistry
    {
        private static readonly List<Rigidbody> s_active = new(16);

        /// <summary>Live chassis root bodies. Index with a for-loop (no enumerator boxing).</summary>
        public static IReadOnlyList<Rigidbody> Active => s_active;

        // Statics survive domain reload with Enter-Play-Mode options on.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_active.Clear();

        public static void Register(Rigidbody chassisRoot)
        {
            if (chassisRoot != null && !s_active.Contains(chassisRoot)) s_active.Add(chassisRoot);
        }

        public static void Unregister(Rigidbody chassisRoot)
        {
            if (chassisRoot != null) s_active.Remove(chassisRoot);
        }
    }
}
