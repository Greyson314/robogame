using Robogame.Movement;
using Robogame.Network.Prediction;
using Robogame.Network.Snapshot;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Robogame.Network.Robot
{
    /// <summary>
    /// Phase-3.5 movement replication — full client-side prediction +
    /// reconciliation (NETCODE_PLAN §8). Owner runs a local sim with a
    /// command-stamped <see cref="ClientCommandBuffer"/>; on every server
    /// <see cref="RobotPoseSnapshot"/> the owner snaps its Rigidbody to the
    /// authoritative state at <c>LastProcessedCommandTick</c> and replays
    /// each unacked command through <see cref="RobotDrive.ApplyMovement"/>
    /// + <see cref="Physics.Simulate"/>, ending the FixedUpdate at the
    /// predicted-current state. Server still owns authoritative physics and
    /// broadcasts pose to non-owner remotes via stock
    /// <see cref="NetworkTransform"/> as in Phase 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Wire shape.</b> Owner sends a redundant triple of the last three
    /// commands per FixedUpdate via <see cref="SubmitInputBundleServerRpc"/>;
    /// the server's <see cref="ServerCommandQueue"/> dedupes against its
    /// <c>LastAppliedTick</c>. Server snapshots at
    /// <see cref="SnapshotIntervalTicks"/> via
    /// <see cref="ReceiveSnapshotClientRpc"/> targeted to the owner only.
    /// </para>
    /// <para>
    /// <b>Replay semantics.</b> A FixedUpdate that processes a snapshot does
    /// N + 1 physics steps (N replay + 1 normal end-of-frame). At 25 Hz
    /// snapshot rate against a 50 Hz physics tick, N is typically 2 — so
    /// three <see cref="Physics.Simulate"/> calls per snapshot-FixedUpdate
    /// on the owner. Replay is capped at <see cref="MaxReplayDepth"/> ticks
    /// to prevent a frame-blocking storm after a long stall; beyond the cap
    /// the snap stands and normal sim catches up.
    /// </para>
    /// <para>
    /// <b>Input delegation.</b> The owner's <see cref="NetworkInputSource"/>
    /// is the first IInputSource on the chassis root (added by NetworkRobot
    /// before Assemble). Outside replay it delegates to the bound
    /// <c>PlayerInputHandler</c> — chassis components read live input as
    /// before. During replay, <see cref="NetworkInputSource.EnterReplay"/>
    /// pins the historical command so <see cref="RobotDrive.ApplyMovement"/>
    /// (and weapon firers reading <c>FireHeld</c>) see the same values they
    /// saw originally. <see cref="NetworkInputSource.ExitReplay"/> restores
    /// live delegation before the FixedUpdate continues.
    /// </para>
    /// <para>
    /// <b>Phase 3.6 adds</b> a runtime latency / jitter / loss HUD
    /// (<see cref="Diagnostics.NetcodeFakeLatencyController"/>) and a
    /// determinism-guard PlayMode test. Visual mesh-offset smoothing
    /// (<c>ReconciliationSmoother</c>) is still deferred — replay plus
    /// Rigidbody interpolation keep the snap invisible at the cost of
    /// a touch of visible jitter under high RTT; build the smoother only
    /// if MPPM testing under §16's matrix surfaces a jarring snap.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [DisallowMultipleComponent]
    public sealed class NetworkRobotMovement : NetworkBehaviour
    {
        // Server snapshot send cadence in physics ticks.
        private const int SnapshotIntervalTicks = 2; // 25 Hz at 50 Hz physics

        // Cap on replay depth — beyond this we just trust the snap and let
        // normal sim catch up. Prevents a frame-blocking storm.
        private const int MaxReplayDepth = 64;

        private NetworkRobot _net;
        private NetworkTransform _netTransform;
        private Rigidbody _rb;
        private RobotDrive _drive;
        private NetworkInputSource _netInput;

        // Owner state.
        private bool _isOwnerPredictor;
        private int _localTick;
        private ClientCommandBuffer _buffer;
        private RobotPoseSnapshot _pendingSnap;
        private bool _hasPendingSnap;
        private InputCommand _prevCmd0 = new InputCommand { Tick = -1 };
        private InputCommand _prevCmd1 = new InputCommand { Tick = -1 };

        // Server state.
        private ServerCommandQueue _serverQueue;
        private bool _serverDrivesRemote;
        private int _ticksUntilSnapshot;
        private ulong[] _ownerTarget;

        private void Awake()
        {
            _net = GetComponent<NetworkRobot>();
            _netTransform = GetComponent<NetworkTransform>();
        }

        public override void OnNetworkSpawn() => _net.WhenBuilt(OnChassisBuilt);

        public override void OnNetworkDespawn()
        {
            _serverQueue?.Reset();
            _buffer?.Reset();
            _hasPendingSnap = false;
        }

        private void OnChassisBuilt(NetworkRobot _)
        {
            if (_net.Handle?.Robot == null) return;
            _rb = _net.Handle.Robot.Rigidbody;
            _drive = GetComponent<RobotDrive>();
            _netInput = GetComponent<NetworkInputSource>();

            if (IsServer && !IsOwner)
            {
                _serverDrivesRemote = true;
                _serverQueue = new ServerCommandQueue();
                _ownerTarget = new ulong[] { OwnerClientId };
                _ticksUntilSnapshot = SnapshotIntervalTicks;
                // Server keeps Rigidbody dynamic.
            }
            else if (IsOwner && !IsServer)
            {
                _isOwnerPredictor = _netInput != null && _drive != null;
                _buffer = new ClientCommandBuffer();
                if (_rb != null) _rb.isKinematic = false;
                if (_netTransform != null) _netTransform.enabled = false;
            }
            else if (!IsServer && !IsOwner)
            {
                // Non-owner remote — kinematic + NetworkTransform-driven.
                if (_rb != null) _rb.isKinematic = true;
            }
            // else: host (IsServer && IsOwner) — Phase-1 behaviour unchanged.
        }

        private void FixedUpdate()
        {
            // Owner: snapshot reconciliation + send current command.
            if (_isOwnerPredictor && IsSpawned)
            {
                if (_hasPendingSnap)
                {
                    ReconcileAndReplay(in _pendingSnap);
                    _hasPendingSnap = false;
                }

                // Build cmd from live input. NetworkInputSource delegates
                // to PlayerInputHandler outside replay mode (we just exited
                // replay above if we replayed).
                var cmd = new InputCommand
                {
                    Tick = _localTick,
                    Move = _netInput.Move,
                    Look = _netInput.Look,
                    Vertical = _netInput.Vertical,
                    FireHeld = _netInput.FireHeld,
                    FirePressed = _netInput.FirePressed,
                    ReloadPressed = _netInput.ReloadPressed,
                };
                _buffer.Store(in cmd);

                // Send last 3 commands — UDP loss redundancy. Server's
                // queue dedupes against LastAppliedTick.
                SubmitInputBundleServerRpc(cmd, _prevCmd0, _prevCmd1);
                _prevCmd1 = _prevCmd0;
                _prevCmd0 = cmd;
                _localTick++;
            }

            // Server: drain commands + periodic snapshot to owner.
            if (_serverDrivesRemote && _serverQueue != null && _netInput != null)
            {
                if (_serverQueue.TryDrainNext(out InputCommand cmd))
                    _netInput.Apply(cmd);

                if (_rb != null && --_ticksUntilSnapshot <= 0)
                {
                    _ticksUntilSnapshot = SnapshotIntervalTicks;
                    var snap = new RobotPoseSnapshot
                    {
                        ServerTick = NetworkManager.Singleton != null
                            ? NetworkManager.Singleton.LocalTime.Tick
                            : 0,
                        LastProcessedCommandTick = _serverQueue.LastAppliedTick,
                        Position = _rb.position,
                        Rotation = _rb.rotation,
                        LinearVelocity = _rb.linearVelocity,
                        AngularVelocity = _rb.angularVelocity,
                    };
                    ReceiveSnapshotClientRpc(snap, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = _ownerTarget },
                    });
                }
            }
        }

        private void ReconcileAndReplay(in RobotPoseSnapshot snap)
        {
            if (_rb == null || _buffer == null || _drive == null || _netInput == null) return;

            // Snap Rigidbody to authoritative state at the last-processed
            // owner tick. The Rigidbody jumps; visible interpolation
            // smooths between FixedUpdate states for the renderer.
            _rb.position = snap.Position;
            _rb.rotation = snap.Rotation;
            _rb.linearVelocity = snap.LinearVelocity;
            _rb.angularVelocity = snap.AngularVelocity;

            // Replay every command strictly after the server's last-applied
            // tick up to (but not including) the current localTick — the
            // current tick is applied normally by PlayerController later
            // this FixedUpdate.
            int firstReplay = snap.LastProcessedCommandTick + 1;
            int lastReplay = _localTick - 1;
            if (lastReplay < firstReplay) return;

            int depth = lastReplay - firstReplay + 1;
            if (depth > MaxReplayDepth) return;

            float dt = Time.fixedDeltaTime;
            for (int tick = firstReplay; tick <= lastReplay; tick++)
            {
                if (!_buffer.TryGet(tick, out InputCommand cmd)) continue;
                _netInput.EnterReplay(in cmd);
                _drive.ApplyMovement(cmd.Move, cmd.Vertical, dt);
                Physics.Simulate(dt);
            }
            _netInput.ExitReplay();
        }

        [ServerRpc]
        private void SubmitInputBundleServerRpc(InputCommand c0, InputCommand c1, InputCommand c2)
        {
            if (_serverQueue == null) return;
            // Enqueue oldest-first; tick-monotonic dedupe in the queue
            // drops anything older than LastAppliedTick (incl. the
            // Tick = -1 sentinels for early-frame redundancy).
            _serverQueue.Enqueue(in c2);
            _serverQueue.Enqueue(in c1);
            _serverQueue.Enqueue(in c0);
        }

        [ClientRpc]
        private void ReceiveSnapshotClientRpc(RobotPoseSnapshot snap, ClientRpcParams _ = default)
        {
            // Targeted to OwnerClientId but ClientRpc is broadcast-shaped;
            // guard explicitly.
            if (!IsOwner || IsServer) return;
            _pendingSnap = snap;
            _hasPendingSnap = true;
        }
    }
}
