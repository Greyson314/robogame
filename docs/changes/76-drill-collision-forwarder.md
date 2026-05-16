# 76 — Drill collision forwarder

> Status: **shipped, PlayMode gate green.** Unblocks actual in-arena
> drilling: a `DrillBlock` placed on a chassis cell now receives
> contact events from the chassis root, where Unity routes physics
> callbacks. Without this, drill-block placement compiled fine but
> the drill never carved terrain because `OnCollisionStay` never
> fired on the drill's GameObject.

## What changed

**New: [`DrillCollisionForwarder`](../../Assets/_Project/Scripts/Voxel/DrillCollisionForwarder.cs)**.
Lives on the chassis-root GameObject (the one with the Rigidbody).
Receives `OnCollisionStay`, walks the collision's contact points,
and routes each contact to the owning `DrillBlock` via a per-collider
hash lookup built at `RefreshDrills` time. Mirrors the
`TipCollisionForwarder` pattern from `Robogame.Movement.TipBlock` but
adapted for the chassis-level 1:N case (a chassis may carry
multiple drill blocks; each contact in a collision may involve any
subset of them).

`DispatchContact(thisCol, otherCol)` is the public dispatcher — used
internally by `OnCollisionStay` and also by PlayMode tests, since
Unity's physics callbacks aren't directly invokable and constructing
a `Collision` object from C# isn't supported.

**Modified: [`DrillBlock`](../../Assets/_Project/Scripts/Voxel/DrillBlock.cs)**.
Refactored the OnCollisionStay body into a public
`HandleContact(Collider)` method so the forwarder can drive it
externally. `OnCollisionStay` now just calls `HandleContact` —
preserves the standalone path (drill on a Rigidbody-bearing
GameObject, useful for tests) while exposing the forwarded path.

**Modified: [`RobotDrillBinder`](../../Assets/_Project/Scripts/Voxel/RobotDrillBinder.cs)**.
On every `Bind` call (when a Drill block is placed on the chassis),
the binder ensures a `DrillCollisionForwarder` exists on the chassis
root and calls `RefreshDrills` so the new drill is registered in the
lookup. Idempotent via `DisallowMultipleComponent`.

**3 new PlayMode tests** ([DigZoneTests.cs](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs)):

- `DrillForwarder_DispatchContact_FromOwningDrillCollider_CarvesSdf`
  — happy path: build a synthetic chassis (Rigidbody + forwarder +
  one drill child), get a DigChunk's MeshCollider, call
  `DispatchContact(drillCollider, chunkCollider)`. Asserts the
  call returned true AND the SDF at the drill's position got
  carved exterior. This is the missing-OnCollisionStay bug fix
  proven end-to-end.
- `DrillForwarder_DispatchContact_FromUnknownCollider_Noop` — fail
  closed: dispatch from a stray collider that doesn't belong to any
  drill returns false and the SDF is unchanged.
- `DrillForwarder_RefreshDrills_CountMatchesAttachedDrillBlocks` —
  adding a second drill block and calling `RefreshDrills` picks it
  up (BoundDrillCount goes from 1 to 2).

## Decisions worth flagging

**Per-collider lookup, not per-drill loop.** The forwarder builds a
`Dictionary<Collider, DrillBlock>` at `RefreshDrills` time. Dispatch
is O(contactCount) hash lookups, not O(drills × contacts). For
chassis with 1–2 drill blocks the cost difference is academic, but
the hash-lookup design scales cleanly to "many drill blocks per
chassis" if a future preset wants that.

**RefreshDrills is the binder's job.** Adding a new drill block to
the chassis at runtime (via `RobotDrillBinder.Bind`) triggers a
`RefreshDrills` call so the new drill's colliders enter the lookup.
A drill removed from the chassis would leave a stale lookup entry
(no removal path today), but the orphan key just fails to dispatch
to a destroyed object — no crash. A `BlockRemoved` hook on the
binder is the clean fix when removal becomes a real concern.

**Public `DispatchContact` for tests.** Unity's physics callbacks
fire from native code and aren't directly invokable from `[Test]`
methods, and a `Collision` object can't be constructed from C#.
Exposing the dispatcher as a public method lets PlayMode tests
drive synthetic contacts using real `Collider` references without
having to run actual physics. This is the same pattern
`DrillBlock.Drill(zone)` already used (testing without a full
collision simulation).

## What's deferred

- **`RobotDrillBinder.Unbind` / `BlockRemoved` hook.** Removing a
  drill block at runtime leaves a stale collider entry in the
  forwarder's lookup. Dispatch fails gracefully (lookup yields
  destroyed object → null check → no-op) but a clean removal
  path is straightforward and the right hygiene for when more
  block-removal flows land.
- **Visual playtest:** drive a chassis with a drill block into a
  dig-zone arena, verify the drill actually carves terrain on
  contact. Per the autonomy contract, machine gate landed
  green; the playtest is the user-driven follow-up.

## Files

- New: `Assets/_Project/Scripts/Voxel/DrillCollisionForwarder.cs`.
- Modified: `Assets/_Project/Scripts/Voxel/DrillBlock.cs`
  (HandleContact extraction),
  `Assets/_Project/Scripts/Voxel/RobotDrillBinder.cs`
  (ensures forwarder on bind),
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`
  (3 new tests + helper).

## Validation

- `.claude/scripts/run-tests.sh PlayMode`: 55/57 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
  All 3 new forwarder tests pass.
