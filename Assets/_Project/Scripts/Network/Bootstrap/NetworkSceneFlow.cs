using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Network.Bootstrap
{
    /// <summary>Replicated round phase (NETCODE_PLAN §10).</summary>
    public enum RoundPhaseValue : byte
    {
        Lobby = 0,
        InProgress = 1,
        Postgame = 2,
    }

    /// <summary>
    /// Wraps NGO's <c>NetworkSceneManager</c> for the connection / scene
    /// sequence (NETCODE_PLAN §10). The synchronized load handshake means a
    /// client cannot spawn into the arena before the server says
    /// "scene-loaded" — robots are spawned server-side off
    /// <see cref="ServerArenaLoaded"/>, never speculatively on the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Bootstrap stays loaded; the arena is loaded additively on top
    /// (NETCODE_PLAN §10). <see cref="RoundPhase"/> is a server-written
    /// <see cref="NetworkVariable{T}"/> so a client reflects Lobby /
    /// InProgress / Postgame without its own <c>MatchController</c> (match
    /// state replication proper is a later phase — Phase 1 only needs the
    /// phase gate so the client doesn't spawn early).
    /// </para>
    /// <para>
    /// <b>MPPM-exit wiring (cannot be done headless):</b> the server-side
    /// hookup that, on <see cref="ServerArenaLoaded"/>, instantiates each
    /// player's <c>NetworkRobot</c> from the registered robot prefab +
    /// their blueprint is finalised in the editor alongside the prefab and
    /// the Bootstrap-scene NetworkManager (handoff §2.4 / §6). This class
    /// provides the seam (the event + the server load API); the concrete
    /// per-player spawn loop is that final integration.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetworkSceneFlow : NetworkBehaviour
    {
        private readonly NetworkVariable<byte> _roundPhase =
            new((byte)RoundPhaseValue.Lobby);

        private bool _sceneEventsHooked;

        /// <summary>Replicated round phase. Authoritative on the server.</summary>
        public RoundPhaseValue RoundPhase => (RoundPhaseValue)_roundPhase.Value;

        /// <summary>Fires on the server once the arena scene has finished
        /// loading + synchronizing on every client — the safe point to
        /// spawn each player's NetworkRobot.</summary>
        public event Action<string> ServerArenaLoaded;

        /// <summary>Fires on every machine when the replicated phase changes.</summary>
        public event Action<RoundPhaseValue> RoundPhaseChanged;

        public override void OnNetworkSpawn()
        {
            _roundPhase.OnValueChanged += HandlePhaseReplicated;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null)
            {
                nm.SceneManager.OnSceneEvent += HandleSceneEvent;
                _sceneEventsHooked = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            _roundPhase.OnValueChanged -= HandlePhaseReplicated;
            if (_sceneEventsHooked && NetworkManager.Singleton != null &&
                NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            _sceneEventsHooked = false;
        }

        /// <summary>Server-only: additively load the arena with NGO's
        /// synchronized handshake (clients follow automatically).</summary>
        public void LoadArena(string sceneName)
        {
            if (!IsServer)
            {
                Debug.LogError("[NetworkSceneFlow] LoadArena called off-server.");
                return;
            }
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
        }

        /// <summary>Server-only: advance the replicated round phase.</summary>
        public void SetPhase(RoundPhaseValue phase)
        {
            if (!IsServer) return;
            _roundPhase.Value = (byte)phase;
        }

        private void HandleSceneEvent(SceneEvent evt)
        {
            // The server fires the spawn seam once the load has completed
            // for everyone (LoadEventCompleted = all clients synchronized).
            if (IsServer && evt.SceneEventType == SceneEventType.LoadEventCompleted)
                ServerArenaLoaded?.Invoke(evt.SceneName);
        }

        private void HandlePhaseReplicated(byte _, byte now)
            => RoundPhaseChanged?.Invoke((RoundPhaseValue)now);
    }
}
