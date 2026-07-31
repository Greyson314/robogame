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
    /// against an isolated prediction body (ADR-0002), ending the FixedUpdate
    /// at the predicted-current state. Server still owns authoritative physics
    /// and broadcasts pose to non-owner remotes via stock
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
    /// <b>Replay semantics.</b> A snapshot-FixedUpdate does N isolated
    /// prediction steps (<c>PredictionScene.PhysicsScene.Simulate</c>, N
    /// replay) plus the one normal end-of-frame step of the live scene. At
    /// 25 Hz snapshot rate against a 50 Hz physics tick, N is typically 2.
    /// The prediction steps advance ONLY the owner's mirror body, never the
    /// live arena (ADR-0002 — fixes the audit-#1 global double-step). Replay
    /// is capped at <see cref="MaxReplayDepth"/> ticks to prevent a
    /// frame-blocking storm after a long stall; beyond the cap the snap
    /// stands and normal sim catches up. If the mirror is unavailable the
    /// snap stands rather than falling back to a global step.
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
        // Prediction proxy (ADR-0002): colliderless body in the owner's
        // isolated PhysicsScene. Replay re-steps THIS, not the live arena.
        private Rigidbody _mirrorRb;
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
            if (_mirrorRb != null)
            {
                PredictionScene.ReleaseMirrorBody(_mirrorRb);
                _mirrorRb = null;
            }
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
                // Spin up the isolated prediction body so replay can re-step
                // this chassis without advancing the live arena (ADR-0002).
                if (_isOwnerPredictor && _rb != null)
                    _mirrorRb = PredictionScene.CreateMirrorBody(_rb);
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
                    ModuleMask = InputCommand.PackModuleMask(_netInput),
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
                // One command per tick in the steady state. A jitter burst
                // delivers several fresh ticks at once, and one-in/one-out
                // would keep that backlog (and its added input latency)
                // forever — catch up by applying a few extra commands this
                // tick until the queue is back under a small tolerance.
                // Each drained command goes through Apply so held input
                // converges on the newest; the physics step sees the last.
                const int BacklogTolerance = 2;
                const int MaxCatchUpPerTick = 4;
                int applied = 0;
                while (_serverQueue.TryDrainNext(out InputCommand cmd))
                {
                    _netInput.Apply(cmd);
                    applied++;
                    if (applied >= MaxCatchUpPerTick) break;
                    if (_serverQueue.PendingCount <= BacklogTolerance) break;
                }

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

            // ADR-0002: re-step ONLY this chassis, isolated in the owner's
            // prediction PhysicsScene, instead of a global Physics.Simulate
            // that would advance every dynamic body in the live arena. If the
            // mirror is missing the snap simply stands and normal sim catches
            // up — never fall back to the global double-step.
            if (_mirrorRb == null || !PredictionScene.IsCreated) return;
            PhysicsScene predScene = PredictionScene.PhysicsScene;

            // Mass distribution may have shifted since spawn (blocks shed).
            // The ~25 Hz snapshot cadence means PhysX has long recomputed the
            // chassis inertia, so this re-sync is always current and cheap.
            PredictionScene.SyncMassProperties(_rb, _mirrorRb);

            // Seed the mirror from the authoritative state.
            _mirrorRb.position = snap.Position;
            _mirrorRb.rotation = snap.Rotation;
            _mirrorRb.linearVelocity = snap.LinearVelocity;
            _mirrorRb.angularVelocity = snap.AngularVelocity;

            // Redirect the drive subsystems onto the mirror so PhysX itself
            // integrates their forces — including the torque an off-COM
            // AddForceAtPosition induces, which GetAccumulatedTorque does NOT
            // surface (so a force/torque transfer would translate the chassis
            // but never turn it). The real body is never stepped here.
            // TRACE[AUDIT-1]: re-step only the owner chassis in isolation — never global Physics.Simulate
            // TRACE[ADR-0002]: forces go to the prediction mirror via the subsystem redirect
            float dt = Time.fixedDeltaTime;
            _drive.SetReplayForceTarget(_mirrorRb);
            try
            {
                for (int tick = firstReplay; tick <= lastReplay; tick++)
                {
                    if (!_buffer.TryGet(tick, out InputCommand cmd)) continue;

                    // Keep the chassis transform on the evolving predicted pose
                    // so the subsystems compute force directions / application
                    // points and grounded raycasts from the right place. Velocity-
                    // dependent terms read the mirror directly via the redirect,
                    // so only the transform needs syncing here.
                    _rb.position = _mirrorRb.position;
                    _rb.rotation = _mirrorRb.rotation;
                    Physics.SyncTransforms();

                    _netInput.EnterReplay(in cmd);
                    _drive.ApplyMovement(cmd.Move, cmd.Vertical, dt);
                    predScene.Simulate(dt);
                }
            }
            finally
            {
                _drive.SetReplayForceTarget(null);
                _netInput.ExitReplay();
            }

            // Land the real chassis at the predicted-current state. The live
            // command for _localTick is applied normally later this FixedUpdate.
            _rb.position = _mirrorRb.position;
            _rb.rotation = _mirrorRb.rotation;
            _rb.linearVelocity = _mirrorRb.linearVelocity;
            _rb.angularVelocity = _mirrorRb.angularVelocity;
            Physics.SyncTransforms();
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
