using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Process-wide registry of live <see cref="LagCompHistory"/>
    /// instances keyed by <c>NetworkObject.NetworkObjectId</c>
    /// (NETCODE_PLAN §9 lag compensation, variant C).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Server-side only — <c>LagCompHistory.OnNetworkSpawn</c> gates the
    /// registration on <c>IsServer</c>. The registry is a plain static
    /// dictionary because (a) it is exclusively used inside RPC bodies and
    /// FixedUpdate methods that already run on the main thread, and (b)
    /// keeping it static lets <see cref="NetworkRobotCombat"/> resolve a
    /// peer's history without holding a reference per shooter.
    /// </para>
    /// <para>
    /// <b>Domain-reload safety.</b> The static dictionary outlives Play-mode
    /// domain reloads on its own, but the dictionary's <see cref="LagCompHistory"/>
    /// instances do NOT — they're MonoBehaviours that get destroyed when the
    /// scene unloads. The <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c>
    /// reset clears the stale entries before any new spawn can register.
    /// This is the canonical fix for the "statics survive domain reload,
    /// GameObjects don't" failure mode logged in <c>CLAUDE.md</c>.
    /// </para>
    /// </remarks>
    public static class LagCompRegistry
    {
        private static readonly Dictionary<ulong, LagCompHistory> s_byId =
            new Dictionary<ulong, LagCompHistory>(16);

        // Counts QueryAll invocations — a test seam so EditMode tests can
        // verify host-skip / no-op paths without standing up NGO.
        private static int s_queryAllInvocations;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_byId.Clear();
            s_queryAllInvocations = 0;
        }

        /// <summary>Number of currently-registered histories. Diagnostic.</summary>
        public static int Count => s_byId.Count;

        /// <summary>Test seam — how many times <see cref="QueryAll"/> has
        /// been called since the last domain reload. Used to verify that
        /// the host-skip guard in <c>NetworkRobotCombat</c> does not query
        /// the registry on the host's own shots.</summary>
        public static int QueryAllInvocationCount => s_queryAllInvocations;

        public static void Register(ulong networkObjectId, LagCompHistory history)
        {
            if (history == null) return;
            s_byId[networkObjectId] = history;
        }

        public static void Unregister(ulong networkObjectId)
        {
            s_byId.Remove(networkObjectId);
        }

        public static bool TryGet(ulong networkObjectId, out LagCompHistory history)
        {
            return s_byId.TryGetValue(networkObjectId, out history);
        }

        /// <summary>Fill <paramref name="results"/> with every registered
        /// (id, snapshot) pair whose history has data within the rewind
        /// window at <paramref name="targetTick"/>. Allocation-free — the
        /// caller owns the list and clears it as needed.</summary>
        public static void QueryAll(uint targetTick, List<(ulong, RobotBoundsSnapshot)> results)
        {
            s_queryAllInvocations++;
            if (results == null) return;
            foreach (KeyValuePair<ulong, LagCompHistory> kv in s_byId)
            {
                if (kv.Value == null) continue;
                if (kv.Value.TryQueryAt(targetTick, out RobotBoundsSnapshot snap))
                    results.Add((kv.Key, snap));
            }
        }
    }
}
