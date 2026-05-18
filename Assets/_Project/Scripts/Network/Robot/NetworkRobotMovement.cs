using Robogame.Input;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Phase-1 movement replication (NETCODE_PLAN §15 Phase 1 — explicitly
    /// NO client-side prediction; CSP is Phase 3). The server simulates
    /// every robot's physics and stock <see cref="NetworkTransform"/>
    /// (server-authoritative by default) replicates the pose; client
    /// rigidbodies are made kinematic so they don't fight the replicated
    /// transform (NETCODE_PLAN §4). The owning client samples its local
    /// input each <c>FixedUpdate</c> and ships an <see cref="InputCommand"/>
    /// to the server, which feeds the robot's <see cref="NetworkInputSource"/>
    /// so <c>RobotDrive</c> runs unchanged.
    /// </summary>
    /// <remarks>
    /// Feel will be laggy — that is the accepted Phase-1 outcome
    /// ("playable, laggy, ugly 1v1"). The host's own robot is server-owned:
    /// it drives directly off its local <c>PlayerInputHandler</c> with no
    /// RPC and no kinematic switch (the host has zero latency).
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [DisallowMultipleComponent]
    public sealed class NetworkRobotMovement : NetworkBehaviour
    {
        private NetworkRobot _net;
        private IInputSource _localInput;       // owner client: the local reader
        private NetworkInputSource _serverSink; // server copy of a remote robot
        private bool _sampleLocal;

        private void Awake() => _net = GetComponent<NetworkRobot>();

        public override void OnNetworkSpawn() => _net.WhenBuilt(OnChassisBuilt);

        private void OnChassisBuilt(NetworkRobot _)
        {
            if (_net.Handle == null) return;

            // Clients don't simulate — the server owns physics and the
            // NetworkTransform drives the pose. The server (incl. host)
            // keeps the dynamic body ChassisAssembler set up.
            if (!IsServer && _net.Handle.Robot != null && _net.Handle.Robot.Rigidbody != null)
                _net.Handle.Robot.Rigidbody.isKinematic = true;

            if (IsOwner && !IsServer)
            {
                // Owner Player build: PlayerInputHandler is the IInputSource
                // (no NetworkInputSource was added for owner builds).
                _localInput = GetComponent<IInputSource>();
                _sampleLocal = _localInput != null;
            }
            else if (IsServer && !IsOwner)
            {
                // Server's copy of a remote player's robot is a Bot build;
                // its IInputSource is the NetworkInputSource we feed.
                _serverSink = GetComponent<NetworkInputSource>();
            }
        }

        private void FixedUpdate()
        {
            if (!_sampleLocal || !IsSpawned || _localInput == null) return;

            var cmd = new InputCommand
            {
                Move = _localInput.Move,
                Look = _localInput.Look,
                Vertical = _localInput.Vertical,
                FireHeld = _localInput.FireHeld,
                FirePressed = _localInput.FirePressed,
                ReloadPressed = _localInput.ReloadPressed,
            };
            SubmitInputServerRpc(cmd);
        }

        [ServerRpc]
        private void SubmitInputServerRpc(InputCommand cmd)
        {
            // Server applies the owner's intent to its authoritative copy.
            _serverSink?.Apply(cmd);
        }
    }
}
