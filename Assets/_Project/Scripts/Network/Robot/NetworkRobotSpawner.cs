using System.Collections.Generic;
using Robogame.Block;
using Robogame.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Phase-1 MPPM glue: when a peer connects, the server spawns a robot
    /// for it (including its own, when hosting). This is the minimal
    /// "press Host → a robot exists" driver that closes the loop without
    /// the full NetworkSceneManager arena handshake (that is a later
    /// phase). Auto-bootstrapped so no scene wiring is required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Phase-1 simplification (intentional):</b> the server spawns every
    /// robot from its <em>own</em> <c>GameStateController.CurrentBlueprint</c>.
    /// A real client uploads its own blueprint before spawn (NETCODE_PLAN
    /// §10 step 3); for a same-build loopback test both robots being the
    /// local default chassis is enough to validate spawn / drive / shoot /
    /// damage replication. Wiring the client→server blueprint upload is a
    /// later-phase task.
    /// </para>
    /// <para>
    /// Spawn poses are spread on X by client id so the two robots don't
    /// interpenetrate. Tune <see cref="SpawnBase"/> / <see cref="SpawnStride"/>
    /// to your arena.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetworkRobotSpawner : MonoBehaviour
    {
        public static readonly Vector3 SpawnBase = new(0f, 2f, 0f);
        public static readonly Vector3 SpawnStride = new(6f, 0f, 0f);

        private static GameObject s_root;

        /// <summary>Auto-bootstrapped singleton (NetworkBootstrap drives
        /// <see cref="ServerSpawnAllConnected"/> on arena-loaded).</summary>
        public static NetworkRobotSpawner Instance { get; private set; }

        private readonly HashSet<ulong> _spawned = new();
        private readonly List<NetworkRobot> _robots = new();
        private NetworkManager _nm;
        private bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_root = null; Instance = null; }

        private void Awake() => Instance = this;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_root != null) return;
            s_root = new GameObject("[NetworkRobotSpawner]");
            DontDestroyOnLoad(s_root);
            s_root.AddComponent<NetworkRobotSpawner>();
        }

        // NetworkManager.Singleton is created by NetworkBootstrap.Awake;
        // auto-boot order is not guaranteed, so hook lazily once it exists.
        private void Update()
        {
            if (_hooked) return;
            _nm = NetworkManager.Singleton;
            if (_nm == null) return;

            _nm.OnServerStarted += HandleServerStarted;
            _nm.OnClientConnectedCallback += HandleClientConnected;
            _nm.OnClientDisconnectCallback += HandleClientDisconnected;
            _hooked = true;
        }

        private void OnDestroy()
        {
            if (!_hooked || _nm == null) return;
            _nm.OnServerStarted -= HandleServerStarted;
            _nm.OnClientConnectedCallback -= HandleClientConnected;
            _nm.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private bool _arenaSpawned;

        private void HandleServerStarted()
        {
            // NOTE: do NOT spawn here. The §10 flow connects in the MainMenu
            // and only spawns once the server has loaded the arena (see
            // ServerSpawnAllConnected, driven by NetworkBootstrap's
            // scene-load handshake) — spawning in the menu would put robots
            // in the wrong scene.
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!_nm.IsServer || !_arenaSpawned) return;

            // A client that joins AFTER the arena is already up: re-send
            // every existing robot's payload targeted to it, then spawn its
            // own. (For the basic 1v1 both peers connect before the arena
            // loads, so ServerSpawnAllConnected covers them.)
            for (int i = 0; i < _robots.Count; i++)
                if (_robots[i] != null) _robots[i].ServerSendConfigTo(clientId);

            SpawnFor(clientId);
        }

        /// <summary>Server-only: spawn a robot for every connected client.
        /// Called once the arena scene has loaded + synchronized on all
        /// peers (NETCODE_PLAN §10). Idempotent per client.</summary>
        public void ServerSpawnAllConnected()
        {
            if (_nm == null || !_nm.IsServer) return;
            _arenaSpawned = true;
            foreach (ulong id in _nm.ConnectedClientsIds)
                SpawnFor(id);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _spawned.Remove(clientId);
        }

        private void SpawnFor(ulong clientId)
        {
            if (!_nm.IsServer) return;
            if (!_spawned.Add(clientId)) return; // already has a robot

            ChassisBlueprint blueprint = GameStateController.Instance != null
                ? GameStateController.Instance.CurrentBlueprint
                : null;
            if (blueprint == null)
            {
                Debug.LogError("[NetworkRobotSpawner] No CurrentBlueprint — cannot spawn.");
                _spawned.Remove(clientId);
                return;
            }

            var bootstrap = Robogame.Network.Bootstrap.NetworkBootstrap.Instance;
            NetworkObject prefab = bootstrap != null ? bootstrap.RobotPrefab : null;
            if (prefab == null)
            {
                Debug.LogError("[NetworkRobotSpawner] Robot prefab not registered — cannot spawn.");
                _spawned.Remove(clientId);
                return;
            }

            Vector3 pos = SpawnBase + SpawnStride * clientId;
            byte team = (byte)(clientId == _nm.LocalClientId ? 1 : 2);
            NetworkRobot nr = NetworkRobot.ServerSpawn(
                prefab, blueprint, clientId, team, pos, Quaternion.identity);
            if (nr != null) _robots.Add(nr);
        }
    }
}
