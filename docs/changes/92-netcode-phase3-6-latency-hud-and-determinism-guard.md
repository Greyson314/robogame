# 92 — Netcode Phase 3.6: latency HUD + determinism guard

> Status: **Phase 3.6 shipped (HUD + test).** ReconciliationSmoother
> still deferred (architecture decision: build only if MPPM under §16
> matrix surfaces a jarring snap). EditMode baseline 236/239 unchanged;
> new PlayMode test `Replay_IdenticalInput_DriftsLessThanBudget` passes.

## Why this session

Session 91 shipped full-Fiedler CSP but deferred Phase 3.5's two
qualitative-validation tools to "Phase 3.6": a latency-injection HUD
(blocked by UTP 2.x's `SetDebugSimulatorParameters` being `[Obsolete]`
and Multiplayer Tools not being in the manifest) and a determinism-
guard PlayMode test. Both land this session. The visible-mesh
`ReconciliationSmoother` was explicitly scope-cut after surfacing the
architectural cost upfront — every block prefab would have to split
collider-on-root from renderer-on-child, or we'd need a renderer-offset
LateUpdate pass. The doc itself says "only if MPPM surfaces a jarring
snap," so I won't pay that cost until evidence demands it.

## What landed

**Package.** `com.unity.multiplayer.tools` 2.2.8 — added to
`Packages/manifest.json`. The package ships
`Unity.Multiplayer.Tools.NetworkSimulator.Runtime.NetworkSimulator`,
which binds to UTP via the global `NetworkAdapters` registry, so the
simulator does *not* need to co-locate with `UnityTransport`. Convenient.

**Latency controller.** New file at
`Assets/_Project/Scripts/Network/Debug/NetcodeFakeLatencyController.cs`.
Editor + DEVELOPMENT_BUILD only (same gate as `NetDevHud`). Wraps the
package's `NetworkSimulator` MonoBehaviour with a 4-preset matrix that
matches §16's qualitative test rows: LAN baseline (0/0/0), 100 ms RTT
(50 ms one-way), 200 ms RTT (100 ms one-way), and 200 ms + 30 ms jitter
+ 5% loss. Static `Instance` singleton; `CyclePreset` walks the array.

**HUD wiring.** `NetDevHud.cs` now `EnsureAttached`s the controller on
its DontDestroyOnLoad root, binds **F5** to cycle, and adds an IMGUI
status line showing the active preset name. Header bumped to
"Netcode Dev (Phase 3.6)".

**Determinism guard.** New PlayMode test at
`Assets/_Project/Tests/PlayMode/Network/PredictionDeterminismTest.cs`.
Spins up a minimal chassis (Rigidbody + RobotDrive + NetworkInputSource
+ a 30-line test-only `ForwardThruster` IDriveSubsystem that turns
`Move.y` into a constant chassis-forward force), runs 50 identical
`InputCommand` ticks via `EnterReplay` → `ApplyMovement` →
`Physics.Simulate` twice with a reset between, asserts drift < 0.5 m / s
of identical input. `Physics.simulationMode = Script` for the duration
so the only integrator stepping the chassis is our explicit
`Physics.Simulate(dt)` — otherwise FixedUpdate's auto-sim would double-
tick. `RobotDrive.AimPointOverride` is set so the camera-ray aim path
can't introduce a hidden per-tick raycast.

## What didn't change

**ReconciliationSmoother.** Deferred. The architectural cost is real
(prefab refactor OR a renderer-offset LateUpdate that touches every
child block), and Rigidbody interpolation already smooths the renderer
between FixedUpdate states — so the visible snap *during* a replay
cycle is one frame, not the full reconciliation delta. If MPPM at
200 ms + jitter shows a jarring snap that interpolation can't hide,
this is the follow-up. Until then, the doc text in netcode.md §15
Phase 3.6 still lists it as conditional.

## A note on the package import

The Unity Package Manager local server lost its connection partway
through the session (MCP couldn't reach it). The manifest edit was the
right move but Unity's editor didn't see the change until a focus
event triggered domain reload. Future me: if you add a package via
manifest edit and `Library/PackageCache/<package>@*` never appears,
Alt-Tab to Unity; if that doesn't work, restart the editor — that
unblocks the local server. Also worth knowing: batch-mode Unity in
`.claude/scripts/run-tests.sh` will trip on EPERM if the main editor
is holding a lock on the shared package cache. Workaround was to
manually copy the resolved package directory from `Library/PackageCache`
into the test-rig's `Library/PackageCache` before re-running. Logged
here so the next package add doesn't burn the same time.

## Subagent road-test (continued)

- **qa-verifier** dispatched; first run hit the package-resolution
  EPERM and a stale csproj. After cache-copy + Unity focus, second
  pass confirmed compile + tests.
- **perf-checker** skipped — zero new physics objects; the simulator
  injects packet-level lag below the physics layer.

## Files

New: `Network/Debug/NetcodeFakeLatencyController.cs`,
`Tests/PlayMode/Network/PredictionDeterminismTest.cs`. Edited:
`Packages/manifest.json` (added multiplayer.tools 2.2.8),
`Network/Robogame.Network.asmdef` (+NetworkSimulator.Runtime ref),
`Network/Bootstrap/NetDevHud.cs` (F5 cycle + status line),
`Network/Robot/NetworkRobotMovement.cs` (doc-comment update only —
removed the "deferred" note for the latency HUD),
`Tests/PlayMode/Robogame.Tests.PlayMode.asmdef` (+Robogame.Network ref),
`docs/subsystems/netcode.md` (§15 Phase 3.6 ticked), this log + README index.
