# 160 — Spring-cleaning main-report findings (15 items closed)

Fixes the 15 confirmed correctness findings from the multi-agent
spring-cleaning review (see 156–159 for the earlier batches). All
severity-ordered items from the review's main report are now closed.

## Fixed

1. **Robot.cs — stale COM/inertia on connected removals.** The
   connectivity coroutine yield-broke before `RecalculateAggregates`
   when zero orphans were found; only `rb.mass` was patched. Now the
   zero-orphan path recomputes, and removals that skip the connectivity
   pass entirely get a deferred `RecalculateAggregatesNextFrame`.
2. **FollowCamera.cs — Y-floor vs radial UpProvider.** The world-space
   Y clamp dragged the camera into the planet on the lower hemisphere.
   The floor is now measured along the sampled local up.
3. **Robot.cs — mass-loss destroy fired mid-edit.** New
   `Robot.SuppressMassLossDestruction`; `BuildModeController` sets it on
   Enter, clears + `ResetInitialAggregates` on Exit.
4. **ArenaController.RespawnPlayer — unregistered respawn.** Manual
   respawn now calls `RegisterChassis` (idempotent), so the next death
   keeps lives / match-end / scrap banking working.
5. **GameStateController.CloneBlueprint — tuning configs dropped.** The
   four tuning configs (plane/ground/damping/thruster) now deep-copy
   through the clone via a JsonUtility round-trip.
6. **RopeBlock — full rebuild on ANY tweakable change.** Rope tweakable
   values are snapshotted at Build; `OnTweakablesChanged` only rebuilds
   when one of them actually moved.
7. **ProjectileWorld — stale owner-collider cache + dead-key leak.**
   New `BlockGrid.StructureVersion` (bumped on place/remove/detach)
   stamps the cache; mismatches rebuild on next fire. Dead Robot keys
   are pruned on a 10 s cadence (`PruneDeadOwnerCache`).
8. **GrappleMagnetBlock — own-hull hit → instant retract.**
   `Physics.IgnoreCollision` doesn't affect queries; the flight cast is
   now `SphereCastNonAlloc` + nearest-non-own-collider filter.
9. **BuoyancyController — drag averaged over wet blocks only.** Drag
   now averages over the whole chassis; one submerged corner block no
   longer applies full water drag.
10. **Budget trim moved to ChassisAssembler.Assemble.** CPU + module
    trims now run at the shared chokepoint (server-side), covering
    flat/water/planet arenas and the networked server spawn. The
    duplicate in `ArenaController.SpawnPlayerChassis` was removed.
    TRACE[INV-3] added.
11. **Planet gravity for ropes + debris.** `VerletRopeSimulator` samples
    `GravityField.SampleAt` per chain (hub particle) instead of
    `Physics.gravity`; `Robot.DetachAsDebris` clones the chassis's
    `PlanetGravityBody` onto debris (loose-typed lookup — Robots →
    Gameplay stays out of the asmdef graph).
12. **ConcoctionId — mirror + wire.** Mirror placement copies the
    variant cache's ConcoctionId. `BlueprintBlob` bumped to wire v3:
    per-entry concoction string-table index (0xFFFF = none); v2 blobs
    still decode.
13. **RopeBlock tip re-adoption.** Adopted-tip mass is tracked
    (`_tipMassAdded`) and reversed on release/re-adopt; the collision
    forwarder is reused instead of stacked, so RepairPad regen cycles
    no longer inflate mass or NRE.
14. **BlockEditor eyedropper vs tune mode.** A middle-click pick now
    clears the instance-edit binding first (same rule as fresh
    placement), so a same-id pick can't overwrite the bound block's
    config through VariantChanged propagation.
15. **BlockConnectivity leaf gap** — `no_change_needed`; already fixed
    by ADR-0008 (session 159).

## Notes

- `BlueprintBlob` v3 changes `ContentHash` for all blueprints (wire
  version byte + entry stride). Fine pre-multiplayer; both peers must
  be on the same build anyway (the version check rejects mismatches).
- Observed while editing: `Teeter` is also absent from the blob wire.
  Left alone (visual-only today); flag for the netcode phase.

## Verification

- `run-tests.sh`: EditMode 495/496 (1 pre-existing inconclusive),
  PlayMode 122/123, 0 failed — baseline held.
- Unity console: 0 errors after recompile.
