# 111 — CSP replay isolated in a prediction PhysicsScene (audit #1)

> Continues the [110 audit-remediation queue](110-audit-remediation-queue.md).
> Quick wins (#18/#27/#19) shipped early in the session; the bulk of the
> work was the #1 CRITICAL fix. **The netcode change is NOT yet
> editor-compiled — Unity MCP was revoked all session.** Verify before
> trusting.

## What shipped

### Quick wins — committed `7323af0`

- **#18** `NameplateOverlay` memoizes the display name in a `List` parallel
  to `_robots` (was allocating a `(Clone)`-trim substring per `OnGUI`).
- **#27** `AudioRouter.Update` sweeps loop handles whose parent was
  destroyed without `Stop()` (mirrors the one-shot voice sweep).
- **#19** `BuoyancyController` exposes a parallel `List` (`IReadOnlyList`,
  HashSet stays the dedup source of truth); `WaterMeshAnimator` iterates by
  index — kills ~4 225 enumerator boxes/frame with wake foam on.

### #1 CRITICAL — CSP replay double-step → ADR-0002

`NetworkRobotMovement.ReconcileAndReplay` called global `Physics.Simulate(dt)`
per replay tick, advancing **every** dynamic body in the scene N extra
times per snapshot and desyncing all non-owner bodies. User chose the
fully-correct fix (over hand-integration / doc-only): re-simulate the owner
chassis in isolation.

[ADR-0002](../decisions/0002-prediction-scene-second-rigidbody.md) (Accepted)
carves out invariant #4 for a prediction-only **mirror Rigidbody**. Recorded
in [invariants.md §4](../invariants.md).

Implementation:

- **`Network/Prediction/PredictionScene.cs`** (new) — owner-client static
  manager. Lazily creates one `LocalPhysicsMode.Physics3D` scene; holds a
  colliderless mirror body (mass/COM/inertia/damping copied from the
  chassis); ref-counted by mirror count; `RuntimeInitializeOnLoadMethod`
  static reset for domain-reload safety.
- **`NetworkRobotMovement`** — owner path creates the mirror in
  `OnChassisBuilt`, releases it in `OnNetworkDespawn`. `ReconcileAndReplay`
  rewritten: seed mirror from snapshot, then per tick → sync real chassis to
  the evolving mirror state (so subsystems compute state-dependent forces
  from the right pose), `ApplyMovement`, transfer outcome to the mirror,
  step `predScene.Simulate(dt)` alone.

**Force delivery — redirect, after a transfer dead-end.** First attempt
transferred net `GetAccumulatedForce`/`GetAccumulatedTorque` to the mirror.
The equivalence test (`PredictionMirrorTest`) caught it: position matched
but **rotation drifted 10°+** — Unity's `GetAccumulatedTorque` returns only
explicit `AddTorque` calls, NOT the torque an `AddForceAtPosition` induces,
so the mirror translated but never turned. Fatal for a game where every
chassis steers via off-COM forces.

Shipped mechanism instead **redirects the drive subsystems onto the mirror**:
a `Body` indirection (`_replayBody ?? _rb`) added to `IDriveSubsystem`
(+ `RobotDrive.SetReplayForceTarget`), so `ApplyMovement` during replay
applies forces to the mirror and PhysX integrates the force-at-position
natively (torque included). The chassis transform is synced to the evolving
mirror pose each tick (for force points + grounded raycasts). Only the five
`IDriveSubsystem`s reached by `ApplyMovement` are redirected — block forces
that run in their own `FixedUpdate` (wheels, hover blades, aero) were never
in replay under the old global `Physics.Simulate` either, so parity holds.
Touches: `IDriveSubsystem`, the 5 subsystems (`_rb.` → `Body.`), `RobotDrive`.

### Tests + docs

- **`Tests/PlayMode/Network/PredictionMirrorTest.cs`** (new) — equivalence
  guard: redirect-path mirror trajectory matches a global-`Physics.Simulate`
  baseline (linear+gravity+drag over 50 ticks; off-COM torque over a short
  horizon). This test caught the transfer dead-end (rotation drift) before it
  shipped. The test chassis gets a real collider so its inertia tensor is
  geometry-derived (as a real chassis's is — production sets it explicitly via
  `Robot.RecalculateAggregates`).
- `PredictionDeterminismTest.ForwardThruster` gains the no-op `SetForceTarget`
  (interface grew a method).
- `netcode.md §8` + Phase 3.5/3.6 entries re-marked (global Simulate was the
  pre-fix behaviour); ADR-0002 mechanism note updated (redirect, not transfer).

## Known gaps (deliberate, from ADR-0002)

- Mirror has no arena geometry → a replay that should hit a wall won't, for
  the ~40 ms window; next snapshot corrects it.
- Planet arenas: `GravityField` lives in the main scene, so the mirror uses
  default gravity. **Must fix before planet-arena MP past loopback.**
- RotorBlock foils (main-scene kinematic hub) stay approximate during replay.

## Verification

- PlayMode suite green (113/114; the 1 unrun is a pre-existing skipped test):
  both `PredictionMirrorTest` equivalence cases pass, the existing
  `PredictionDeterminismTest` still passes, no compile errors.
- Cross-`PhysicsScene` float floor (documented, not a bug): under sustained
  off-COM spin the mirror tracks the global-simulate baseline to ~1.3°/~3 cm
  per 6 ticks (linear motion matches to <2 cm / 50 ticks). PhysX angular
  integration isn't bit-identical across scene instances; production replay
  is 2-3 ticks and the next 25 Hz snapshot corrects any residual.
- `perf-checker` still TODO (physics change; dispatch after commit).

## Outstanding (queue, see [110](110-audit-remediation-queue.md))

- Item 3 (doc-drift sweep) — **done**, commit `ac87cfd`.
- Item 4 (weapon-fork refactor — ADR first), item 5 (Continual Traces —
  confirm syntax) remain.
