# 0002 — Isolate CSP replay in a prediction PhysicsScene

- **Status.** Accepted
- **Date.** 2026-06-03

## Context

Client-side prediction (CSP) reconciliation in
`NetworkRobotMovement.ReconcileAndReplay` replays each unacked input
command by calling `RobotDrive.ApplyMovement` (which `AddForce`s the
owner chassis Rigidbody) followed by the global `Physics.Simulate(dt)`.
The global step advances **every** dynamic Rigidbody in the active
scene, not just the owner's. At the shipped 25 Hz snapshot / 50 Hz
physics rate, a snapshot-FixedUpdate does N + 1 global steps (N replay +
1 normal), so every non-owner dynamic body — debris, projectiles,
physics props, eventually other players' chassis — is integrated N extra
times per snapshot and desyncs from its own authoritative state. This is
audit finding **#1 CRITICAL** (session [109](../changes/109-full-app-code-review.md)).

The bug is latent today: multiplayer is not started, so this path only
runs in loopback / MPPM testing. But the moment a second dynamic body
shares the scene with a predicting owner, replay corrupts it. The fix
must re-simulate the owner's predicted chassis **in isolation**.

Unity's mechanism for isolated stepping is a `Scene` opened with
`LocalPhysicsMode.Physics3D`: such a scene is excluded from the global
`Physics.Simulate` / `FixedUpdate` step and is advanced only by an
explicit `scene.GetPhysicsScene().Simulate(dt)`. The user selected this
fully-correct approach over the cheaper alternatives (hand-integration;
documenting the limitation) on 2026-06-03.

## Decision

Replay re-simulates the owner chassis in a dedicated prediction scene,
not the live arena scene.

A scene-lifetime `PredictionScene` (owner-client only) creates one
`LocalPhysicsMode.Physics3D` scene per arena session and holds a single
**mirror Rigidbody** — a colliderless, renderless body whose mass,
centre of mass, inertia tensor, and damping are copied from the live
owner chassis Rigidbody. `ReconcileAndReplay` seeds the mirror from the
authoritative snapshot, then for each replayed command **redirects the
drive subsystems onto the mirror** (`RobotDrive.SetReplayForceTarget`) and
calls `predictionPhysicsScene.Simulate(dt)` once, finally writing the
mirror's pose and velocity back onto the real chassis Rigidbody. The live
arena scene is never stepped during replay.

**Force delivery — redirect, not transfer.** The first implementation
attempt read the net `GetAccumulatedForce`/`GetAccumulatedTorque` off the
real body and re-applied it to the mirror. That fails: Unity's
`GetAccumulatedTorque` does **not** surface the torque an
`AddForceAtPosition` induces (only explicit `AddTorque` calls), so the
mirror translated but never turned — fatal for a game where every chassis
steers via off-COM forces. The shipped mechanism instead points each
subsystem's force target at the mirror (a `Body` indirection added to
`IDriveSubsystem`); PhysX then integrates the force-at-position natively on
the mirror, torque included. The chassis transform is synced to the
evolving mirror pose each tick so subsystems compute force points and
grounded raycasts correctly. Only the five `IDriveSubsystem`s reached by
`RobotDrive.ApplyMovement` are redirected — block-driven forces that run in
their own `FixedUpdate` (wheels, hover blades, aero) were never part of
replay under the old global `Physics.Simulate` either, so parity holds.

This requires a **carve-out to invariant #4 (single Rigidbody per
chassis)**: the mirror is a second Rigidbody representing the same
chassis. The carve-out is bounded — the mirror (a) is prediction-only,
(b) carries no gameplay authority and is never networked, (c) lives in a
separate physics scene with no colliders that touch any gameplay body,
(d) exists only on the owner client, and (e) is created at chassis
spawn and destroyed on despawn. The exemption is strictly **one mirror
body per prediction scene on the owner client** — it is not a general
licence for multiple Rigidbodies on a chassis in the main scene.

## Alternatives considered

**Hand-integrate the owner Rigidbody only (semi-implicit Euler from
`GetAccumulatedForce`/`Torque`).** Cheapest correct-ish fix, no second
scene. Rejected: it resolves no collision contacts during replay (the
chassis tunnels through ground/walls for the replay window), the angular
integration through the off-diagonal inertia tensor is error-prone, and
it changes replay feel. The PhysicsScene approach gets real contact
resolution for free against any geometry we choose to place in the
prediction scene later.

**Document the limitation, gate replay as loopback-only, defer the real
fix to the MP build.** Lowest effort, zero behaviour change, and
defensible because the bug can't bite before MP exists. Rejected by the
user in favour of doing the correct fix now rather than carrying the
debt.

**Mirror the full arena collision tree into the prediction scene.**
Would give the mirror walls and floors to collide against during replay.
Rejected as out of scope and architecturally inappropriate for a
non-lockstep server-authoritative design: copying and syncing the arena
collision tree is expensive, and the short replay window (~40 ms, 2–3
ticks) means the next authoritative snapshot corrects any contact the
mirror missed. Source-engine and Overwatch CSP make the same tradeoff.

## Consequences

**New invariant carve-out.** Invariant #4 gains an explicit exception
for the prediction mirror body, recorded above and to be cross-linked
from `invariants.md` on acceptance. Any future reviewer who sees two
Rigidbodies tied to one chassis must check it is the prediction mirror
and nothing else.

**Correctness.** Replay stops corrupting non-owner dynamic bodies. The
replay step also gets cheaper — one body integrated instead of the whole
scene.

**Known gaps carried as debt** (documented in the plan, to be tracked in
the session log, not silently accepted):
- The mirror has no arena geometry, so a replay that should hit a wall
  is predicted without the wall for the replay window; the next snapshot
  corrects it.
- Spherical (planet) arenas: `GravityField` lives in the main scene, so
  the mirror uses default world gravity unless we feed the same gravity
  bias per replay tick. Must be addressed before planet-arena MP goes
  past loopback.
- RotorBlock foils are driven by a kinematic hub in the main-scene step,
  so foil pose during replay stays approximate; rotor chassis accumulate
  slightly more prediction error.

**Zero cost off the owner path.** Server, host, and non-owner clients
create no prediction scene and no mirror body. Invariant #5 (zero
baseline cost) holds.

**Delivery is phased** (scene plumbing → replay rewrite → tests/docs) so
each phase lands independently. Does not trigger the Verlet rope
migration (physics.md §2): this is loopback-only and changes no
shipped-MP surface.

## Notes

- Implementation plan produced by the planner subagent, session
  [110](../changes/110-audit-remediation-queue.md) follow-up.
- Replaces the global-`Physics.Simulate` reconciliation described in
  `netcode.md §8`, which must be re-marked as the pre-fix behaviour.
- Subsystem-force delivery to the mirror uses the per-subsystem redirect
  (a `Body` indirection on `IDriveSubsystem` + `RobotDrive.SetReplayForceTarget`),
  not the originally-planned `GetAccumulatedForce/Torque` transfer — the
  latter cannot carry `AddForceAtPosition` torque (see Decision). The
  equivalence is pinned by `PredictionMirrorTest` (off-COM thruster, mirror
  vs global-simulate baseline).
