using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Process-wide accessor for the active <see cref="INetworkContext"/>.
    /// Every gameplay system that has a server-only action queries
    /// <c>NetworkContext.Instance.IsServer</c> instead of hard-coding
    /// "always run". Until the Network module registers a real context,
    /// <see cref="Instance"/> returns the offline stub
    /// (<c>IsServer=IsClient=true, IsHost=IsOnline=false</c>) so
    /// singleplayer behaviour is byte-identical.
    /// </summary>
    /// <remarks>
    /// Statics survive domain reload but a registered MonoBehaviour-backed
    /// context does not. <see cref="ResetStatics"/> clears the registration
    /// on <c>SubsystemRegistration</c> so a stale (fake-null) Network
    /// bootstrap from a prior play session never lingers — mirrors the
    /// <see cref="GravityField"/> pattern.
    /// </remarks>
    public static class NetworkContext
    {
        private sealed class OfflineContext : INetworkContext
        {
            public bool IsServer => true;
            public bool IsClient => true;
            public bool IsHost => false;
            public bool IsOnline => false;
        }

        private static readonly OfflineContext s_offline = new();
        private static INetworkContext s_active;

        /// <summary>The active context, or the offline stub if none registered.</summary>
        public static INetworkContext Instance => s_active ?? s_offline;

        /// <summary>True when a Network bootstrap has registered a real context.</summary>
        public static bool HasActiveContext => s_active != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_active = null;

        /// <summary>Called by the Network bootstrap once NGO is running.
        /// Idempotent re-registration of the same instance is a no-op.</summary>
        public static void Register(INetworkContext context)
        {
            if (context == null)
            {
                Debug.LogError("[NetworkContext] Register(null) ignored.");
                return;
            }
            s_active = context;
        }

        /// <summary>Called by the Network bootstrap on teardown so queries
        /// fall back to the offline stub.</summary>
        public static void Unregister(INetworkContext context)
        {
            if (ReferenceEquals(s_active, context)) s_active = null;
        }
    }
}
