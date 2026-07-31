# 158 — Perf / INV-6 pass (spring-cleaning review batch)

Verified per-frame allocation and wasted-work hotspots from the deep
review, including the two still-open HIGH items from the 109 audit.

## Fixes

- **Splash damage is allocation-free** (109-audit #8). `ProjectileWorld.
  DamageRobotInRadius` snapshots into a reused static list instead of
  `GetComponentsInChildren` per explosion. Side win: reparented grid
  blocks (rotor foils) are now splashable — the child walk missed them.
  The snapshot is still required: lethal `TakeDamage` removes the block
  from the grid synchronously.
- **Scoped LOD rebuild** (109-audit #7). A chunk crossing a LOD band now
  remeshes itself + face-neighbours only (new `SetLodLevelQuiet` sets
  levels first, so every remesh sees post-change `NeighbourLodStrides`),
  instead of a full 36-chunk synchronous pass — which also double-
  remeshed each transitioned chunk. `Camera.main` cached with a
  dead/disabled re-check.
- **`BlockGrid.BlocksNonAlloc`** — struct-enumerator view for hot loops;
  foreach over the `IReadOnlyDictionary`-typed `Blocks` boxed the
  enumerator (INV-6). Switched: BuoyancyController (per FixedUpdate),
  CenterOverlay, BlockEditor.GetChassisStats, and the new splash path.
- **Wave params hoisted** (§8.9). `WaterSurface.WaveParams.Sample()`
  reads the four wave tweakables once; the hot `SampleHeight(volume,
  in wave, …)` overload takes them by ref. WaterMeshAnimator (65×65
  verts ≈ 17k string-keyed probes/frame) and BuoyancyController hoist
  at loop entry. Fixed §8.9's wrong claim that the animator already did
  this.
- **Ammo HUD is event-driven.** `WeaponAmmoState` replaces the
  yield-iterator `EnumeratePools` (state-machine alloc per call, two
  HUDs × every frame) with `PoolCount`/`TryGetPoolAt` + a
  `PoolsVersion` stamp bumped on consume/reload/recompute. AimReticle
  and VehicleStatsHud dirty-gate on the stamp; the ammo string no
  longer rebuilds per frame.
- **ScrapPickup** iterates `Core.ChassisRegistry` by distance (the
  mask-~0 16-slot overlap could saturate on own-hull/debris colliders
  and miss a real collector) and throttles its ground probe to 4 Hz
  once settled (still re-probes — scrap can rest on a moving chassis).
  Note: attraction now keys on chassis ROOT distance, not
  nearest-collider distance — marginally tighter for huge hulls.
- **ObjectiveHud** late-bind scan throttled to 1 Hz (§12.5).

## Skipped

- `ChassisInstancedRenderer.Recombine` — already debounced to once per
  frame (the review's "no batching" claim was refuted); sustained-fire
  O(hull) recombines per damage-frame remain. Revisit if profiling
  shows mid-combat spikes at MP scale.

## Verification

- EditMode + PlayMode suites green (numbers in commit message).
- perf-checker dispatched post-batch per invariant #7 — verdict
  recorded in the commit/summary.
