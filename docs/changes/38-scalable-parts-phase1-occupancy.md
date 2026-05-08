# Session 38 — Scalable parts Phase 1: swept-volume occupancy

> Status: **shipped, untested in-engine.** Phase 1 of the
> [Scalable Parts Plan](../SCALABLE_PARTS_PLAN.md). Adds a swept-volume
> overlap check so a span-2 wing can't be authored on top of a
> neighbouring armour cube (or a future weapon block, etc.). No
> player-visible UX yet beyond placement rejection: a 2× wing now
> blocks the cell its span pokes into. Slider-clamp UI for
> scaling already-placed blocks is deferred to Phase 1.b — see
> follow-ups.

## Why this session

Per the Phase 0 audit, the `Dims` data path was solid end-to-end but
nothing was rejecting placements that interpenetrated. Authors could
freely produce blueprints where a span-2 wing visibly overlapped the
armour cube next to it — the visual was wrong and would be wronger
once dims start affecting collision shapes.

## What changed

### Static dispatcher: [`BlockOccupancy`](../../Assets/_Project/Scripts/Block/BlockOccupancy.cs)

New file. Three responsibilities:

- **`ComputeSweptBoundsLocal(blockId, gridPos, up, dims, cellSize)`** —
  switches on stable block id and delegates to type-specific math.
  Today: `BlockIds.Aero` / `BlockIds.AeroFin` route to
  `AeroSurfaceBlock.ComputeFoilSweptBoundsLocal`; everything else
  falls back to a 1×1×1 cell-centred AABB.
- **`StrictOverlap(a, b)`** — strict less-than on every axis, so two
  unit cubes at adjacent integer cells correctly report no overlap.
  `Bounds.Intersects` is inclusive and would falsely flag every
  face-adjacent default-block pair, so the predicate is hand-rolled.
- **`WouldOverlapInGrid(grid, ...)`** — the placement-time predicate
  the build editor calls.

Static rather than per-component because the placement check runs
*before* the candidate block exists; a MonoBehaviour interface
can't answer the question for a candidate. Lower allocation
overhead too — every helper is O(1) per entry, no heap.

### Foil bounds math: inlined in `BlockOccupancy`

Mirrors the geometry produced by `AeroSurfaceBlock.ApplyOrientationToVisual`
and `ComputeWingShift` so the placement check sees exactly what
the player sees. Uses the abs-summed-rotated-axes trick to turn
the foil-local OBB into a chassis-local AABB — correct for any
`OrientationFromUp(up)` rotation, not just the +Y case, even
though build mode only ever produces +Y today.

The math lives in `BlockOccupancy` rather than next to
`AeroSurfaceBlock` because the asmdef graph won't let it live
in Movement: `Robogame.Block` (where `BlockOccupancy` and the
validator live) doesn't reference `Robogame.Movement` (and
flipping that creates a cycle, since Movement depends on Block).
Foil constants (`FoilDefaultSpan` / `Thickness` / `Chord`) are
duplicated in `BlockOccupancy` from `AeroSurfaceBlock.Default*`,
and a `FoilDefaults_StayInSyncWithAeroSurfaceBlock` test asserts
the values match — change one, the test fails until you change
the other.

### `BlockBehaviour.Up` is now stored per-instance

Previously the per-block `up` direction was used to set the cell's
`transform.localRotation` and then forgotten — `BlockBehaviour`
held only `GridPosition`, `Definition`, and `Dims`. The placement
check needs to know the `up` of every existing block to compute
its swept bounds, so `up` now lives on the behaviour with a
`Vector3Int.zero → Vector3Int.up` legacy fallback (mirroring
`ChassisBlueprint.Entry.EffectiveUp`).

`BlockGrid.PlaceBlock` threads it through to `Initialize`. The
authored-blueprint path was already passing `entry.EffectiveUp` to
`PlaceBlock` (`ChassisFactory`, `RepairPad`), so non-+Y placements
in helicopter / bomber presets just work.

This also unblocks a small fix in
[`BlockEditor.SyncBlueprintFromGrid`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs):
the sync used to hard-write `Vector3Int.up` for every entry with a
TODO comment about the gap. It now writes `b.Up`, so build-mode
edits to a chassis with non-+Y blocks no longer silently coerce
those blocks back to +Y on save.

### Validator + editor wire-up

- **[`BlueprintValidator`](../../Assets/_Project/Scripts/Block/BlueprintValidator.cs)**
  has a new rule (#4 in the Validate body): pre-compute every
  entry's swept bounds, run an O(n²) strict-overlap pass, emit a
  per-pair error. n is bounded by chassis size (≤200 cells in
  practice) so 20k comparisons per validation is fine.
- **[`BlockEditor.IsValidPlacement`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)**
  calls `BlockOccupancy.WouldOverlapInGrid` after the existing
  CPU / connectivity / 2-CPU checks. The candidate's dims come
  from the variant panel's next-placement cache.

Existing `BlueprintBuilder` got a fourth `Block` overload that
takes `(blockId, position, up, dims)` for tests that exercise
the new rule.

## Tests

[`BlockOccupancyTests.cs`](../../Assets/_Project/Tests/EditMode/Blueprints/BlockOccupancyTests.cs)
— 8 tests covering the dispatcher and the strict-overlap predicate:

- Default block bounds = unit cube at the cell.
- Adjacent unit cubes do NOT overlap (regression for the
  `Bounds.Intersects` inclusivity bug).
- Same-cell cubes DO overlap.
- Default-dim foils fit inside the host cell.
- Span-2 foil shifts outward by half-span on positive / negative X.
- Thin foils don't poke into Y-neighbours.
- Span-2 foil overlaps a neighbour cube in its extension direction.
- Vertical fin span extends along Y.
- Cell-size scale invariance.

[`BlueprintValidatorTests.cs`](../../Assets/_Project/Tests/EditMode/Blueprints/BlueprintValidatorTests.cs)
gets three new tests at the integration level:

- Span-2 wing poking into a neighbour fails validation.
- Default-dim wings at adjacent cells pass validation.
- A row of adjacent unit cubes passes validation. (If this fails,
  every shipped blueprint breaks — sentinel test.)

`PresetBlueprintTests.Preset_PassesValidation` already runs the
validator against every shipped preset. Default dims = `Vector3.zero`
on every entry, which the consumer treats as "use defaults", which
for foils means span=1 / chord=0.9 / thickness=0.08 — all under one
cell-edge, so no shipped preset can overlap. Verified analytically;
the test will catch any future preset that tries.

## Notes for the next session

- **Phase 1.b — select-and-modify UX is needed before slider-clamp.**
  The plan's Phase 1 finale was "the variant slider clamps at the
  largest dim that doesn't collide". `VariantConfigPanel` today
  only writes the *next-placement* cache; there's no path to
  re-edit an already-placed block's dims via the UI. The
  occupancy machinery is already there (it's the same
  `WouldOverlapInGrid` call with the existing block as `ignore`);
  what's missing is a build-mode "select existing block, show its
  dims in the variant panel, drag the slider, push the new dims
  back via `BlockBehaviour.SetDims`" flow. Defer to its own
  session — the placement-time check covers the common case and
  unblocks Phase 2 / Phase 1.5.

- **Phase 1.5 is next.** Lift formula in
  [`AeroSurfaceBlock.FixedUpdate`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
  needs to scale by `span * chord` so a 2× wing actually produces
  2× lift. Without it, Phase 3 cost balancing inverts the
  player's incentive (bigger = cheaper per lift unit). Default
  chassis numbers should be preserved by normalising against
  `DefaultSpan * DefaultChord`.

- **Up-storage migration is complete but quiet.** Existing scenes /
  saved blueprints don't break — `Vector3Int.zero` falls back to
  `+Y` everywhere it's read. The first non-`+Y` build-mode
  placement will be the visible smoke test; until then, authored
  helicopters / bombers with `EffectiveUp != +Y` already exercise
  the path through `ChassisFactory`.

- **Validator perf bound.** O(n²) overlap is fine at chassis sizes
  (≤200). If a 1000-cell mega-chassis lands later, sweep-and-prune
  is the upgrade path; not worth doing now.
