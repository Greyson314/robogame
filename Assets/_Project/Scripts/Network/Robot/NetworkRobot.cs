using System;
using Robogame.Block;
using Robogame.Gameplay;
using Robogame.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Thin Net sibling that owns spawn / blueprint reconstruction /
    /// ownership for one robot (NETCODE_PLAN §5 pattern). The gameplay
    /// <c>Robot</c> / <c>BlockGrid</c> / <c>RobotDrive</c> stay untouched —
    /// this only carries the wire and then hands off to the singleplayer
    /// construction chokepoint, <see cref="ChassisAssembler.Assemble"/>,
    /// 1:1 (handoff §2.2 / NETCODE_PLAN §6 Bucket B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>NetworkObject</c> per robot; blocks are plain children, never
    /// NetworkObjects (NETCODE_PLAN §7). The robot network prefab is a bare
    /// GameObject (NetworkObject + the Net* siblings only) — blocks are
    /// reconstructed at runtime from the replicated
    /// <see cref="SpawnRobotPayload"/> blob. Authoring that prefab and
    /// registering it on the NetworkManager is the one editor step that
    /// cannot be done headless; it rides the Phase-1 MPPM exit (handoff
    /// §2.4 / §6).
    /// </para>
    /// <para>
    /// The server builds the chassis immediately in
    /// <see cref="ServerSpawn"/> (it runs the authoritative physics);
    /// non-server peers build from the post-spawn
    /// <see cref="ConfigureClientRpc"/>. The host is the server, so its
    /// RPC handler early-outs to avoid a double build.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class NetworkRobot : NetworkBehaviour
    {
        /// <summary>The assembled chassis bundle. Null until the build
        /// runs (server: in <see cref="ServerSpawn"/>; client: on the
        /// configure RPC). Sibling Net components read Robot/Grid here.</summary>
        public ChassisHandle Handle { get; private set; }

        /// <summary>Owning player's client id, replicated for the gameplay
        /// tier (team/scoring). Server-set before the configure RPC.</summary>
        public ulong OwnerPlayerId { get; private set; }

        // Server keeps the spawn payload so it can re-send it, targeted, to
        // a client that connects AFTER this robot spawned (every client is
        // a late-joiner relative to the host's own robot — a fire-and-
        // forget broadcast at spawn never reaches them).
        private SpawnRobotPayload _payload;

        /// <summary>True once <see cref="Handle"/> is assembled.</summary>
        public bool IsBuilt => Handle != null;

        /// <summary>Raised once the chassis finishes assembling (server: in
        /// <see cref="ServerSpawn"/> after spawn; client: on the configure
        /// RPC). Sibling Net components hook gameplay events here because
        /// the chassis does not exist yet at <c>OnNetworkSpawn</c> time.</summary>
        public event Action<NetworkRobot> Built;

        /// <summary>Invoke <paramref name="callback"/> as soon as the chassis
        /// is built — immediately if it already is, otherwise on
        /// <see cref="Built"/>. The single hook every sibling uses.</summary>
        public void WhenBuilt(Action<NetworkRobot> callback)
        {
            if (callback == null) return;
            if (IsBuilt) callback(this);
            else Built += callback;
        }

        // -----------------------------------------------------------------
        // Server spawn
        // -----------------------------------------------------------------

        /// <summary>
        /// Server-only. Instantiate the registered robot network prefab,
        /// spawn it with ownership, build the chassis locally, and tell
        /// every other peer to build the same chassis from the blob.
        /// Returns the spawned <see cref="NetworkRobot"/>, or null if not
        /// the server / inputs are invalid.
        /// </summary>
        public static NetworkRobot ServerSpawn(
            NetworkObject prefab,
            ChassisBlueprint blueprint,
            ulong ownerClientId,
            byte teamId,
            Vector3 position,
            Quaternion rotation)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                Debug.LogError("[NetworkRobot] ServerSpawn called off-server.");
                return null;
            }
            if (prefab == null) { Debug.LogError("[NetworkRobot] ServerSpawn: prefab is null."); return null; }
            if (blueprint == null) { Debug.LogError("[NetworkRobot] ServerSpawn: blueprint is null."); return null; }

            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            var nr = instance.GetComponent<NetworkRobot>();
            if (nr == null)
            {
                Debug.LogError("[NetworkRobot] Robot prefab is missing a NetworkRobot component.");
                UnityEngine.Object.Destroy(instance.gameObject);
                return null;
            }

            nr.OwnerPlayerId = ownerClientId;
            instance.SpawnWithOwnership(ownerClientId);

            // Server runs the authoritative sim — build here, now.
            nr.BuildFromBlueprint(blueprint, teamId, position, rotation);

            var payload = new SpawnRobotPayload
            {
                PlayerId = ownerClientId,
                TeamId = teamId,
                SpawnPosition = position,
                SpawnRotation = rotation,
                BlueprintBlob = BlueprintBlob.Encode(blueprint),
            };
            nr._payload = payload;
            nr.ConfigureClientRpc(payload);            // already-connected clients
            return nr;
        }

        /// <summary>Server-only: (re)send this robot's build payload to one
        /// specific client — used when a client connects after the robot
        /// already spawned (e.g. the host's own robot).</summary>
        public void ServerSendConfigTo(ulong clientId)
        {
            if (!IsServer) return;
            var p = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            ConfigureClientRpc(_payload, p);
        }

        [ClientRpc]
        private void ConfigureClientRpc(SpawnRobotPayload payload, ClientRpcParams rpcParams = default)
        {
            // Host is the server and already built in ServerSpawn.
            if (IsServer) return;

            OwnerPlayerId = payload.PlayerId;
            if (!BlueprintBlob.TryDecode(payload.BlueprintBlob, out ChassisBlueprint bp, out string error))
            {
                Debug.LogError($"[NetworkRobot] Could not decode spawn blueprint: {error}");
                return;
            }
            BuildFromBlueprint(bp, payload.TeamId, payload.SpawnPosition, payload.SpawnRotation);
        }

        // -----------------------------------------------------------------
        // Shared build path — the singleplayer chokepoint, reused 1:1
        // -----------------------------------------------------------------

        private void BuildFromBlueprint(ChassisBlueprint blueprint, byte teamId,
            Vector3 position, Quaternion rotation)
        {
            if (Handle != null) return; // idempotent — never build twice

            BlockDefinitionLibrary library = GameStateController.Instance != null
                ? GameStateController.Instance.Library
                : null;
            if (library == null)
            {
                Debug.LogError("[NetworkRobot] No BlockDefinitionLibrary (GameStateController missing) — cannot build.");
                return;
            }

            transform.SetPositionAndRotation(position, rotation);

            // Per-peer rig: the owning client's copy is a full Player build
            // (local PlayerInputHandler drives the camera + sources the
            // wire commands); every other copy — incl. the server's
            // authoritative copy of a remote player's robot — is a Bot
            // build whose IInputSource is a NetworkInputSource the server
            // feeds from the owner's input RPC (NETCODE_PLAN §5).
            //
            // Phase 3.5: the OWNER also gets a NetworkInputSource —
            // added BEFORE Assemble so it lands at component index 0 (the
            // first IInputSource on the root). RobotDrive / weapon blocks
            // resolve their IInputSource via GetComponentInParent, which
            // picks the first match — that lands on NetworkInputSource,
            // which delegates to the local PlayerInputHandler outside of
            // replay (BindLive after Assemble) and replays historical
            // commands during CSP reconciliation.
            if (GetComponent<NetworkInputSource>() == null)
                gameObject.AddComponent<NetworkInputSource>();

            AssemblyOptions options;
            if (IsOwner)
            {
                options = AssemblyOptions.Player(
                    GameStateController.Instance != null
                        ? GameStateController.Instance.InputActions
                        : null);
            }
            else
            {
                options = AssemblyOptions.Bot();
            }

            Handle = ChassisAssembler.Assemble(gameObject, blueprint, library, options);

            if (Handle == null)
            {
                Debug.LogError("[NetworkRobot] ChassisAssembler returned null — build failed.");
                return;
            }
            if (Handle.Robot != null)
                Handle.Robot.ConfigureTeam((TeamId)teamId);

            // Phase 3.5: bind the owner's live PlayerInputHandler into the
            // NetworkInputSource so its delegating properties resolve live
            // input outside of replay. Done after Assemble (which is what
            // creates the PlayerInputHandler).
            if (IsOwner)
            {
                var net = GetComponent<NetworkInputSource>();
                var live = GetComponent<Robogame.Input.PlayerInputHandler>();
                if (net != null && live != null) net.BindLive(live);
            }

            Built?.Invoke(this);

            // The owning client's view: hand the local camera/HUDs to this
            // networked robot (gameplay listens via the Core bridge so it
            // never references Robogame.Network).
            if (IsOwner && IsClient)
                Robogame.Core.NetworkPlayerBridge.RaiseLocalOwnerRobotReady(gameObject);
        }
    }
}
