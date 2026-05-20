# 93 — Netcode Phase 4: block-destruction hardening

> Status: **Phase 4 shipped.** Five concrete hardenings (orphan-RPC,
> tick-sequence dedup, late-join destruction log, aim-bounds gate, real
> aim sampling). EditMode 240+/3 (4 new Phase 4 tests pass; baseline
> unchanged). PlayMode unchanged (no new network-stress simulation —
> see "What's not tested" below). The 4v4 MPPM playtest remains the
> qualitative exit gate per the plan.

## Why this session

Session 92 closed the Phase 3 stack. The user asked for backend
completion next. Phase 4 owns the destruction half of the wire: the
"I shot it but it's still alive on my screen" failure mode that block-
builder MP games trip on first.

## Architecture decisions made under the plan

The planner laid out 6 questions; the planning pass answered them. The
load-bearing calls:

**1. Orphan list as a separate `OrphanBatchClientRpc`, not piggybacked.**
`Robot.RunConnectivityNextFrame` runs one Unity frame *after* the
destroying block was removed — fires from a `yield return null`
coroutine. By the time the orphans are computed, the hit batch for the
triggering destruction has already gone out on the network tick the
destruction happened in. Piggybacking would require buffering hit
batches an extra frame; a second RPC is strictly simpler.

**2. Per-batch monotonic seq, no reorder buffer.**
NGO's reliable channel is strictly ordered; duplicates only happen on
reconnect edge cases. A `uint _batchSeq` per server, `_lastAppliedSeq`
per client, and a single `<=` compare on receive is enough. We're not
trying to migrate to an unreliable channel here — that's a Phase-6+
concern once lag-comp is in.

**3. Aim threshold at 90° per accepted command.**
The planner argued correctly that at 12 fires/sec a tracking human can
shift aim 60°+ between intervals. 90° catches only impossible
teleporting-aim cases without false-positive risk. Phase 6 lag-comp
gets a server-side aim-at-time-T record; that's when we tighten.

**4. Real aim from `RobotDrive.AimPoint`, not the placeholder.**
The planner missed this — the `FireCommand.AimDir` field was
`Vector3.forward` placeholder from session 90, which made any aim
bounds check vacuous (angle from forward to forward is 0). Owner now
samples `(RobotDrive.AimPoint - chassis.position).normalized`, the same
vector the local firers use. Falls back to `transform.forward` if the
drive isn't built yet or the aim degenerates onto the muzzle.

## What landed

**`Robot.OrphansDetached` event** (`Scripts/Robot/Robot.cs`). Raised
after `RunConnectivityNextFrame` has finished detaching all orphan
cells, with the pre-detach `Vector3Int` positions. Gameplay-side; no
network types referenced. The grid positions are captured *before*
`DetachAsDebris` reparents the BlockBehaviours, so the snapshot is
stable even after detach.

**`DestroyedBlockLog`** (`Scripts/Network/Robot/DestroyedBlockLog.cs`).
Fixed-capacity 512-entry ring of canonical blueprint indices. Records
every destruction the server sees (direct hit and structural orphan).
On overflow, logs a single warning and stops appending — never silently
truncates. Late-join replay scaffold; `NetworkBlockGrid.ServerSendDestructionLogTo`
is the reserved entry point but no scene-lifecycle wiring yet (v1
locks lobbies at round start per §10).

**`NetworkBlockGrid` Phase-4 hardening** — sequence number on
`BlockHitBatchClientRpc`, orphan subscription via
`Robot.OrphansDetached` → `OrphanBatchClientRpc(ushort[])` driving
each named block to zero HP through the same local destruction path,
shared `ReplayBlocksToZeroOnClient` helper so the late-join replay
ClientRpc reuses the same code, destruction log writes from both the
direct-hit and orphan handlers.

**`NetworkRobotCombat` aim gate + real aim** — replaced the always-pass
stub with `ValidateAim` over `MaxAimDeltaDeg = 90f`, owner now samples
real aim from `RobotDrive.AimPoint`, `RejectedFireCount` aggregates
cooldown + aim rejections, public `ServerValidateAim` and
`ServerProcessFireCommand` test hooks for EditMode coverage without
standing up NGO.

## Tests

Four EditMode tests in `Tests/EditMode/Network/Phase4HardeningTests.cs`:
- `DestroyedBlockLog` round-trip, ToArray snapshot, overflow warning
  fires exactly once, Reset rearms the warning.
- Aim validation: first-command-accepted (seeds the validator),
  under-threshold accepted, over-threshold rejected and counter
  increments, degenerate-zero accepted without poisoning state.

## What's not tested (honest scope)

**No automated network-stress test.** The planner sketched a "spawn 8
synthetic networked robots, 300 ticks of damage, assert no desync." In
practice that needs a full NGO host spinup, prefab registration,
`ChassisAssembler.Assemble` per robot, and a way to drive damage from
the server while reading per-client mirror state — easily 200+ LOC of
test scaffolding for a property the MPPM 4v4 playtest gates anyway. I
skipped it. The §15 Phase-4 exit criterion *is* the qualitative
playtest; the EditMode tests pin the regression surfaces that would
silently fail (DestroyedBlockLog, aim validation).

**No PlayMode orphan-event test.** Same reasoning — a hand-built
chassis with a bridge layout would need a CPU block, real BlockGrid
hookup, real Robot bootstrap. ~50 LOC to verify one `Invoke`. The
event firing is read-confirmable; integration is the MPPM gate.

## Carry-forward / open threads

- **CPU-loss convergence** (planner's question 4): already correct per
  audit. Server's local `Robot.OnCpuDestroyed` fires `Destroyed`;
  `NetworkRobotState` writes `IsAlive = false` and tier = Dead via
  NetworkVariable. Client's path: BlockHitBatch destroys CPU block →
  same local `Robot.OnCpuDestroyed` fires → tier mirror catches up via
  the replicated NetworkVariable anyway. No race possible (writes are
  reliable ordered).
- **Late-join activation** — `ServerSendDestructionLogTo` is reserved
  but not wired into a scene-lifecycle callback. That's the v2 mid-
  match join work (§10 "v2 candidate").
- **Per-block fire commands** — `FireCommandServerRpc` still uses one
  coarse chassis-wide cooldown key (`Vector3Int.zero`). Per-block
  granularity stays a Phase-4+ refactor.

## Subagent road-test (continued)

- **planner** dispatched at the top of the session. Plan held up well;
  one missed question (aim is a placeholder Vector3.forward, so the
  bounds gate is vacuous without wiring real aim) caught in-flight.
- **qa-verifier** dispatched after implementation.
- **perf-checker** skipped — zero new physics objects; the new code is
  event-time RPC dispatch, not per-frame work.

## Files

New: `Scripts/Network/Robot/DestroyedBlockLog.cs`,
`Tests/EditMode/Network/Phase4HardeningTests.cs`. Edited:
`Scripts/Robot/Robot.cs` (+OrphansDetached event + position snapshot
before detach), `Scripts/Network/Robot/NetworkBlockGrid.cs`
(seq + orphan RPC + destruction-log wiring + late-join replay
scaffold), `Scripts/Network/Robot/NetworkRobotCombat.cs` (real aim +
aim-bounds gate + test hooks), `docs/subsystems/netcode.md` (§15
Phase 4 ticked), this log + README index.
