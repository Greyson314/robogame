using System;
using Robogame.Core;
using Robogame.Network.Robot;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Network.Bootstrap
{
    /// <summary>
    /// Owns the process <see cref="NetworkManager"/> + <see cref="UnityTransport"/>
    /// and bridges NGO state to the gameplay tier via <see cref="INetworkContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auto-bootstraps (mirrors <c>ProjectileWorld</c> / <c>VerletRopeSimulator</c>)
    /// so the project runs headless / without hand-authoring the Bootstrap
    /// scene. It is <em>only</em> the active <see cref="NetworkContext"/> while
    /// an NGO session is live: it registers on session start and unregisters
    /// on shutdown, so offline / singleplayer falls back to the offline stub
    /// (<c>IsServer == true</c>) and behaviour stays byte-identical. Without
    /// that gating, a parked <see cref="NetworkManager"/> reports
    /// <c>IsServer == false</c> and every Step-9 server-gated action would
    /// stop running in singleplayer — the one trap to get right here.
    /// </para>
    /// <para>
    /// Tick rate is pinned to 50 Hz (architect decision, handoff §5.4) to
    /// match the 50 Hz physics <c>FixedUpdate</c>; Phase 3 owns final tuning.
    /// Connection approval carries the Bucket-A content hash
    /// (<see cref="ContentHashGuard"/>, NETCODE_PLAN §6/§13) so a mismatched
    /// client is rejected before it can spawn.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetworkBootstrap : MonoBehaviour, INetworkContext
    {
        public const ushort DefaultPort = 47777;
        public const uint TickRateHz = 50; // handoff §5.4 — matches physics FixedUpdate

        /// <summary>Arena scene the server loads over the NGO handshake
        /// once a remote client has connected (§10).</summary>
        public const string ArenaScene = "Arena";

        private bool _arenaRequested;
        private bool _sceneHooked;

        private static NetworkBootstrap s_instance;
        private static GameObject s_root;

        public static NetworkBootstrap Instance => s_instance;

        private NetworkManager _nm;
        private UnityTransport _transport;

        // INetworkContext — reflects real NGO state. Only consulted while
        // this instance is the registered context (i.e. a session is live).
        public bool IsServer => _nm != null && _nm.IsServer;
        public bool IsClient => _nm != null && _nm.IsClient;
        public bool IsHost   => _nm != null && _nm.IsHost;
        public bool IsOnline => _nm != null && _nm.IsListening;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_root = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_instance != null) return;
            s_root = new GameObject("[NetworkBootstrap]");
            DontDestroyOnLoad(s_root);
            s_instance = s_root.AddComponent<NetworkBootstrap>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            _nm = NetworkManager.Singleton;
            if (_nm == null)
            {
                var nmGo = new GameObject("[NetworkManager]");
                DontDestroyOnLoad(nmGo);
                _nm = nmGo.AddComponent<NetworkManager>();
            }

            _transport = _nm.GetComponent<UnityTransport>();
            if (_transport == null) _transport = _nm.gameObject.AddComponent<UnityTransport>();

            _nm.NetworkConfig ??= new NetworkConfig();
            _nm.NetworkConfig.NetworkTransport = _transport;
            _nm.NetworkConfig.TickRate = TickRateHz;
            _nm.NetworkConfig.ConnectionApproval = true;
            // §10 flow: connect in the MainMenu, then the SERVER drives the
            // arena load over NGO's synchronized scene handshake so every
            // peer transitions together and ArenaController.Start runs with
            // IsOnline already true (its guards then skip local SP spawn).
            _nm.NetworkConfig.EnableSceneManagement = true;
            _nm.NetworkConfig.PlayerPrefab = null; // robots spawned explicitly

            _nm.OnServerStopped += HandleSessionStopped;
            _nm.OnClientStopped += HandleSessionStopped;
            _nm.OnClientConnectedCallback += HandleClientConnected;
            _nm.OnClientDisconnectCallback += id =>
                Debug.LogWarning($"[NetworkBootstrap] Client {id} disconnected: '{_nm.DisconnectReason}'");
        }

        private void OnDestroy()
        {
            if (_nm != null)
            {
                // Force the transport to release its UDP socket if Play
                // stopped mid-session — otherwise the bound port can linger
                // and the next StartHost fails with "socket itself failed".
                if (_nm.IsListening) _nm.Shutdown();
                _nm.OnServerStopped -= HandleSessionStopped;
                _nm.OnClientStopped -= HandleSessionStopped;
                _nm.OnClientConnectedCallback -= HandleClientConnected;
                _nm.ConnectionApprovalCallback = null;
                if (_sceneHooked && _nm.SceneManager != null)
                    _nm.SceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            NetworkContext.Unregister(this);
            if (s_instance == this) s_instance = null;
        }

        // -----------------------------------------------------------------
        // Session control (driven by NetDevHud in Phase 1)
        // -----------------------------------------------------------------

        /// <summary>
        /// Resources path of the robot network prefab (a bare GameObject
        /// with NetworkObject + the Net* siblings). Loaded + registered at
        /// runtime so the NetworkManager prefab list never has to be
        /// hand-edited; server and client register the identical prefab.
        /// </summary>
        public const string RobotPrefabResource = "RobotNetPrefab";

        /// <summary>The registered robot prefab, or null if missing.</summary>
        public Unity.Netcode.NetworkObject RobotPrefab { get; private set; }

        private void RegisterRobotPrefab()
        {
            if (RobotPrefab != null) return;
            var prefab = Resources.Load<Unity.Netcode.NetworkObject>(RobotPrefabResource);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[NetworkBootstrap] No robot prefab at Resources/{RobotPrefabResource}. " +
                    "Create it (bare GameObject + NetworkObject + the Net* components) " +
                    "in an Assets/.../Resources folder with that exact name.");
                return;
            }
            RobotPrefab = prefab;
            _nm.AddNetworkPrefab(prefab.gameObject);
        }

        // -----------------------------------------------------------------
        // §10 scene flow: connect in menu → server loads arena → spawn
        // -----------------------------------------------------------------

        private void HandleClientConnected(ulong id)
        {
            // Server, first remote client in: load the arena for everyone
            // over NGO's synchronized handshake. Both peers transition
            // together; ArenaController.Start then runs with IsOnline true.
            if (!_nm.IsServer || _arenaRequested || id == _nm.LocalClientId) return;
            _arenaRequested = true;

            if (!_sceneHooked)
            {
                _nm.SceneManager.OnSceneEvent += HandleSceneEvent;
                _sceneHooked = true;
            }
            _nm.SceneManager.LoadScene(ArenaScene, LoadSceneMode.Single);
        }

        private void HandleSceneEvent(SceneEvent evt)
        {
            // Arena finished loading + synchronizing on every peer — now
            // the server spawns each connected player's robot (§10).
            if (_nm.IsServer && evt.SceneEventType == SceneEventType.LoadEventCompleted)
                NetworkRobotSpawner.Instance?.ServerSpawnAllConnected();
        }

        public bool StartHost(ushort port = DefaultPort)
        {
            if (_nm.IsListening) { Debug.LogWarning("[NetworkBootstrap] Already listening."); return false; }
            RegisterRobotPrefab();
            // Host = server + an internal loopback client. The client
            // connects to the *connect* address, so it must be a real
            // reachable address (127.0.0.1), NOT 0.0.0.0 — connecting to
            // 0.0.0.0 fails the UTP socket. 0.0.0.0 is only valid as the
            // *listen* (bind-all) address.
            _transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            _nm.ConnectionApprovalCallback = ContentHashGuard.ApproveConnection;
            ContentHashGuard.PrepareLocalConnectionData(_nm);
            bool ok = _nm.StartHost();
            if (ok) NetworkContext.Register(this);
            else Debug.LogError("[NetworkBootstrap] StartHost failed.");
            return ok;
        }

        public bool StartClient(string ip = "127.0.0.1", ushort port = DefaultPort)
        {
            if (_nm.IsListening) { Debug.LogWarning("[NetworkBootstrap] Already listening."); return false; }
            RegisterRobotPrefab();
            _transport.SetConnectionData(ip, port);
            ContentHashGuard.PrepareLocalConnectionData(_nm);
            bool ok = _nm.StartClient();
            if (ok) NetworkContext.Register(this);
            else Debug.LogError("[NetworkBootstrap] StartClient failed.");
            return ok;
        }

        public void StopSession()
        {
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
        }

        private void HandleSessionStopped(bool _)
        {
            // Host fires both server- and client-stopped; only fall back to
            // the offline stub once nothing is listening any more.
            if (_nm == null || !_nm.IsListening) NetworkContext.Unregister(this);
        }
    }
}
