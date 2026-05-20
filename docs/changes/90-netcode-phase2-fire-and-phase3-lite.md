# 90 — Netcode Phase 1 close-out (FireCommand + tracer) + Phase 3 lite (owner local sim)

> Status: **two phase advances, both `dotnet`-build-green, both tested
> green in EditMode (236/239 — 2 pre-existing failures unchanged).**
> Phase 1's deferred items shipped; Phase 3 ships a deliberately
> conservative cut that hits the §15 exit criterion ("controls feel
> local at 150 ms RTT") without the manual-physics-step gymnastics
> a full Fiedler replay needs. User-side MPPM verification (the
> qualitative feel test) remains the gate.

## Why this session

User asked for the FireCommand+tracer close-out (session 87's
explicit "first task of the next netcode phase") and Phase 3 CSP, run
autonomously as a road test of session 88/89's hook + subagent setup.

## What shipped — Phase 1 close-out (NETCODE_PLAN §9 / §13)

- New `static event Action<ProjectileSpec> Spawned` on `ProjectileWorld`
  (Combat-tier hook the prior session deferred). Reset in
  `ResetStatics`, fired at the tail of `SpawnInternal`. Zero baseline
  cost when no subscriber (singleplayer).
- New wire structs `FireCommand` + `ProjectileSpawnPayload`
  (`Network/Robot/FireCommand.cs`).
- New `FireCooldownTable` — pure C# per-position cooldown ledger,
  per-block-keyed by `Vector3Int` to scale to Phase-4 per-block fire
  commands without an API churn. 8 EditMode tests drafted by
  test-drafter, all passing.
- `NetworkRobotCombat` rewritten end-to-end: server subscribes to
  `Spawned`, filters to own owner, fans out
  `ProjectileSpawnEventClientRpc` to every client; non-server clients
  (owner included — their firers are disabled) spawn a `Damage=0`
  cosmetic projectile + emit muzzle flash + audio. Owner sends
  `FireCommandServerRpc` at the SMG fire rate while held; server
  cooldown-validates and increments `RejectedFireCount` on breach
  (observation only — the per-block `_nextFireTime` is what actually
  rate-limits the fire today; the ServerRpc is the §13 telemetry
  surface, Phase 4+ can promote it to a true gate).

Aim-bounds validation is a stub always-pass: the owner's input is in
look-input (yaw/pitch) space while `FireCommand.AimDir` is world-space
muzzle-forward — coord-space reconciliation belongs in Phase 3 with
CSP. The aim fields are still on the wire so the flip is RPC-shape
stable.

## What shipped — Phase 3 lite (NETCODE_PLAN §8, partial)

The planner produced a thorough full-Fiedler plan (command buffer +
replay + mesh-offset smoothing + prefab edit + UTP latency HUD). I
cut hard:

- **Owner non-server**: `Rigidbody.isKinematic = false` and the local
  `NetworkTransform` is `enabled = false`. The chassis runs free under
  local `PlayerController` + `RobotDrive` — input is immediate. This
  alone is the load-bearing change for "controls feel local."
- **Anti-drift**: server sends a `RobotPoseSnapshot` every 5 physics
  ticks (10 Hz) to the owner only via targeted ClientRpc. Owner
  hard-snaps Rigidbody to server state only when drift > 1 m;
  otherwise trusts local prediction.
- **Non-owner remote**: unchanged from Phase 1 (kinematic +
  `NetworkTransform`-driven). No prefab edit required.
- **Host**: unchanged (server-owns-physics directly, zero latency).

New helper types: `RobotPoseSnapshot`, `ServerCommandQueue`
(handles out-of-order owner commands + tracks
`LastAppliedTick`). The `Tick` field on `InputCommand` is wired but
the full replay buffer + reconciliation smoother are explicitly
deferred — I deleted the speculative `ClientCommandBuffer.cs` /
`ReconciliationSmoother.cs` I had drafted rather than leave them as
dead code (per the project's "no speculative RPCs" discipline from
session 86).

**What this is NOT.** Not full Fiedler reconciliation — no command
buffer replay after a snapshot, no visual-offset mesh smoothing. Both
are designed-but-unimplemented; the staged cut lets owner feel land
in this session without the manual-physics-step gymnastics that real
replay needs across a shared `Physics.Simulate`. Phase 3.5 work.

## Subagent road-test (sessions 88/89 tooling)

- **planner** dispatched twice (Phase 1 close-out + Phase 3): caught
  the timing-race around fire gating, surfaced the cooldown-table
  granularity choice, identified the coord-space mismatch that
  motivated aim-validation deferral, flagged the rotor hub / replay
  divergence risk that motivated the Phase 3 scope cut. High value.
- **test-drafter** drafted 8 `FireCooldownTable` tests covering the
  per-position cooldown invariants. Assumed signature recorded in
  a header comment matched the implementer. Useful.
- **qa-verifier** ran the final pass + filtered output. PASS verdict
  in ~30 s. Mid-session also caught the pre-existing failures cleanly
  surfaced as "not introduced this session."
- **perf-checker** skipped (zero new physics objects per its skip rule).
- **design-pilot** not relevant to this work.

Test-rig hit one transient `EPERM` on the first run (`PackageCache`
locked by main Unity); retry succeeded. Worth noting.

## Plan corrections recorded in netcode.md §15

- Phase 1 row gets the §9 deferred bullet retired.
- Phase 3 row split into "lite" (this session, owner-local-sim +
  anti-drift) and "3.5 — full Fiedler" (deferred — command buffer
  replay + reconciliation smoother + latency-injection HUD).

## Files

New: `Network/Robot/FireCommand.cs`, `Network/Robot/FireCooldownTable.cs`,
`Network/Snapshot/RobotPoseSnapshot.cs`,
`Network/Prediction/ServerCommandQueue.cs`,
`Tests/EditMode/Network/FireCooldownTableTests.cs`.
Edited: `Combat/ProjectileWorld.cs`, `Network/Robot/NetworkRobotCombat.cs`,
`Network/Robot/NetworkInputSource.cs`,
`Network/Robot/NetworkRobotMovement.cs`,
`docs/subsystems/netcode.md` (§15 ticks), this log + README index.
