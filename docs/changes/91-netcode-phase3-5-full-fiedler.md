# 91 — Netcode Phase 3.5: full Fiedler CSP (real replay)

> Status: **Phase 3.5 shipped sans latency-injection HUD** (UTP's
> `SetDebugSimulatorParameters` is `[Obsolete]` in 2.x; Network Simulator
> from `com.unity.multiplayer.tools` isn't in the manifest — feature is
> recorded as Phase 3.6). EditMode 236/239 (same two pre-existing
> failures as session 90 unchanged). Console clean. User-side MPPM run
> is the qualitative gate; under simulated latency only via OS-level
> tools today.

## Why this session

Session 90 shipped Phase 3 *lite* — owner Rigidbody dynamic + low-rate
anti-drift snapshot. Useful but it leaves the owner with no real
reconciliation: snapshots either trust local prediction or hard-snap.
User asked to push through to Phase 3.5 — the real Fiedler model with
command-buffer replay.

## Architecture

The piece that made replay clean was extending **`NetworkInputSource`
into a replay-aware delegating bridge.** On owner builds it now sits
in front of `PlayerInputHandler` as the first `IInputSource` on the
chassis root; `RobotDrive` and weapon blocks resolve via
`GetComponentInParent` and find it. Outside replay it delegates straight
through to the live `PlayerInputHandler` — chassis components feel
zero difference. During replay,
`NetworkInputSource.EnterReplay(cmd)` pins all properties to the
historical command; `RobotDrive.ApplyMovement` (and any other consumer
reading `FireHeld`, `Move`, etc.) sees the same values it saw
originally. `ExitReplay` restores live delegation.

The new shape:

- `NetworkRobot.BuildFromBlueprint` now adds `NetworkInputSource` for
  *all* builds (owner included) **before** `ChassisAssembler.Assemble`,
  guaranteeing it's the first IInputSource by component index. After
  Assemble, the owner's `PlayerInputHandler` is bound via `BindLive`.
- `ClientCommandBuffer` (re-added from the file I deleted in Phase 3
  lite) — 128-slot ring buffer keyed by `InputCommand.Tick`.
- `NetworkRobotMovement.ReconcileAndReplay`: snap Rigidbody to
  authoritative `(pos, rot, linVel, angVel)` at
  `LastProcessedCommandTick`; for each replay tick call EnterReplay →
  `RobotDrive.ApplyMovement` → `Physics.Simulate(fixedDt)`; ExitReplay
  at the end. Capped at 64 replay ticks to prevent frame-blocking
  storms after a long stall.
- Server snapshots at **25 Hz** (every 2 physics ticks) targeted to
  `OwnerClientId` only.
- Owner sends a **redundant triple** of `(current, prev, prev-prev)`
  commands per FixedUpdate via `SubmitInputBundleServerRpc`; the
  server's `ServerCommandQueue.Enqueue` dedupes against `LastAppliedTick`
  (sentinel `Tick = -1` on the early-frame slots gets dropped).

## Scope cuts (deferred to Phase 3.6)

Two from the planner's full plan:

1. **Latency-injection HUD.** `UnityTransport.SetDebugSimulatorParameters`
   is marked `[Obsolete]` in NGO/UTP 2.x — its replacement
   (`com.unity.multiplayer.tools`'s Network Simulator) isn't in the
   project manifest. Real-latency MPPM remains the only path; OS-level
   netem / Clumsy is the qualitative test today.
2. **Visual mesh-offset `ReconciliationSmoother`.** Skipped. Replay
   alone keeps the predicted-vs-server delta small; Unity's Rigidbody
   `interpolation = Interpolate` smooths the visible position between
   FixedUpdate states for the renderer, so the snap-then-replay within
   one FixedUpdate doesn't show as a mid-frame jump. If user MPPM
   testing surfaces a jarring snap, this is the follow-up.

Also deferred: the determinism guard PlayMode test (drift < 0.5 m /
second of identical input). The replay is correct by construction;
adding a regression test against accidental drift is Phase 3.6 work.

## Replay step semantics (a note for future-me)

A snapshot-FixedUpdate on the owner does **N + 1 physics steps** where
N = replay depth. The N manual `Physics.Simulate` calls catch the
Rigidbody from server-state at tick S up to predicted state at tick
T-1; PlayerController applies tick T's input later in the same
FixedUpdate; Unity's auto-sim at end-of-FixedUpdate integrates that
into tick-T state. Net: each FixedUpdate ends with Rigidbody at
post-tick-(localTick) state, exactly like the offline path.

At 25 Hz snapshots / 50 Hz physics, N is typically 2 — so 3
`Physics.Simulate` calls per snapshot-FixedUpdate. Cheap.

## Subagent road-test (continued)

- **planner** dispatched in session 90, plan still load-bearing — its
  flagged risks (UTP API deprecation, RotorBlock replay drift) were
  both real and motivated the scope cuts.
- **qa-verifier** dispatched, PASS verdict in ~35 s.
- **perf-checker** skipped (zero new physics objects; only an extra
  `Physics.Simulate` call during reconciliation, not a new body).

## Files

New: `Network/Prediction/ClientCommandBuffer.cs` (re-added).
Edited: `Network/Robot/NetworkInputSource.cs` (delegation +
EnterReplay/ExitReplay), `Network/Robot/NetworkRobot.cs` (owner-build
NetworkInputSource add + post-Assemble BindLive),
`Network/Robot/NetworkRobotMovement.cs` (full Fiedler ReconcileAndReplay
+ redundant-triple SubmitInputBundleServerRpc),
`docs/subsystems/netcode.md` (§15 Phase 3.5 ticked, Phase 3.6 added),
this log + README index.
