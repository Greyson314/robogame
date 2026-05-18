namespace Robogame.Core
{
    /// <summary>
    /// The single network-aware type gameplay code is allowed to import.
    /// Gameplay asmdefs must NEVER reference <c>Robogame.Network</c>
    /// (NETCODE_PLAN §14); they ask this interface "am I the authoritative
    /// instance?" instead. The Network module implements it; in
    /// singleplayer / offline an always-authoritative stub answers so the
    /// same query returns "yes" everywhere with no other code change.
    /// </summary>
    public interface INetworkContext
    {
        /// <summary>True on the authoritative simulation (server or host's
        /// server-side process). Always true offline.</summary>
        bool IsServer { get; }

        /// <summary>True on a machine that has a local player view.
        /// Always true offline.</summary>
        bool IsClient { get; }

        /// <summary>True when this process is both server and a client
        /// (listen-server / host). False offline (offline is not a
        /// networked host — it is the degenerate "everything local" case).</summary>
        bool IsHost { get; }

        /// <summary>True when a real NGO session is running. False offline
        /// — code that must only run in true multiplayer gates on this.</summary>
        bool IsOnline { get; }
    }
}
