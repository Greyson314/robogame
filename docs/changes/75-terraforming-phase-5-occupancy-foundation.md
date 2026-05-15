# 75 — Terraforming Phase 5 foundation: occupancy grid + A*

> Status: **shipped, EditMode machine gate green.** Pure data structure
> + algorithm; no POI authoring, no AI integration yet. Sets up the
> AI-pathing scaffold from TERRAFORMING_PLAN.md § 8 so the actual AI
> systems land on a solid substrate when they come online.

## What changed

**New: [`OccupancyGrid`](../../Assets/_Project/Scripts/Voxel/OccupancyGrid.cs)**.
A managed-byte[] 3D grid, one cell per (4×4×4) voxel-cell block — 2 m
on a side at the default 0.5 m voxel size. Three states per cell:

- `Solid` (0): impassable terrain.
- `OpenWithFloor` (1): open air with solid terrain directly below
  in -Y. Ground AI can stand here.
- `OpenNoFloor` (2): open air with no floor below. Only flying AI
  may traverse.

API surface:

- `BuildFromChunkSdf(chunkCoord, chunkSize, sdf, sdfDim)` — rebuilds
  the 8×8×8-cell slice for one chunk. Two-pass: pass 1 classifies
  Solid vs Open by counting interior SDF samples in the 5×5×5 window
  covering the occupancy cell (threshold > 50% interior); pass 2
  walks the chunk's region plus the row immediately above (so a
  fresh excavation re-evaluates `OpenWithFloor` ↔ `OpenNoFloor`
  designations on the cell above).
- `TryFindPath(start, goal, connectivity, allowFlying, outPath)` —
  A* with `Cardinal6` (±X/±Y/±Z) or `Full26` (3×3×3 minus center)
  neighbour topology. Euclidean heuristic, Euclidean edge costs;
  admissible. Open set is a linear-scan list (O(N²) worst case but
  N stays small in practice — fine for a 100-chunk zone).

**Modified: [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)**.
Owns an `OccupancyGrid` whose size is derived from the chunk grid +
chunk size. `EnsureInitialised` allocates it after chunks spawn and
before `RebuildAllMeshes` runs (so it's ready for the first pass).
`RebuildAllMeshes` calls `BuildFromChunkSdf` per chunk alongside the
existing apron rebuild + remesh + bake — every brush apply, every
LOD transition, and every snapshot load updates occupancy.

**8 new EditMode tests** ([OccupancyGridTests.cs](../../Assets/_Project/Tests/EditMode/Voxel/OccupancyGridTests.cs)):

- `Build_HalfSpace_BottomFourLayersAreSolid_TopFourAreOpen` — basic
  classification: voxel y < 16 (interior) collapses to occupancy gy ≤ 3
  Solid; voxel y ≥ 16 (exterior) → gy ≥ 4 Open.
- `Build_HalfSpace_FirstOpenLayerIsOpenWithFloor_HigherLayersOpenNoFloor`
  — pass-2 floor designation: gy = 4 above Solid gy = 3 → `OpenWithFloor`;
  gy ≥ 5 → `OpenNoFloor`.
- `Build_GridBelowZoneFloor_HasNoFloor_DefaultsToOpenNoFloor` — gy = 0
  cells with no -Y neighbour default to `OpenNoFloor` (can't stand on
  the void at the zone's bottom edge).
- `WorldToGrid_GridToWorld_RoundTripsThroughCellCenter` — coordinate
  conversion sanity.
- `FindPath_AlongFloorRow_ReturnsContiguousPath` — A* walk across the
  floor row. Asserts start/end match, Manhattan distance = 1 between
  consecutive steps (Cardinal6 invariant), and every cell is
  `OpenWithFloor`.
- `FindPath_NonFlyer_ThroughTunnelCarvedInSolid_ReturnsPath` — the
  machine gate. Carves a horizontal slab through the half-space's
  solid half at sample y=9..12 (= voxel y=8..11 = occupancy gy=2),
  asserts the floor below stays Solid, the tunnel is `OpenWithFloor`,
  and a ground bot finds a path from one X end to the other.
- `FindPath_NoPath_ReturnsFalse_EmptyPath` — A* fails closed when
  start is inside Solid material; out-list stays empty.
- `FindPath_Flyer_AllowsOpenNoFloor_NonFlyer_Does_Not` — `allowFlying`
  flag opens up `OpenNoFloor` cells for flyers; a non-flyer can't
  even start in one.
- `BuildFromChunkSdf_OneChunk_DoesNotTouchOtherChunksCells` —
  incremental-update isolation. Builds chunk (0,0,0) from a half-space,
  snapshots its 8×8×8 slice, then builds chunk (1,0,0) from an
  all-exterior SDF, and asserts every cell of (0,0,0)'s slice still
  matches the snapshot. Confirms the per-chunk pass doesn't bleed
  across the boundary.

## Decisions worth flagging

**Classification rule = sample-count majority.** The 4×4×4 voxel-cell
block of an occupancy cell is covered by 5×5×5 = 125 SDF samples. If
more than half are interior (sdf < 0), the cell is Solid. This is
more robust at surface-straddling cells than a single-sample probe
would be (an SDF whose surface passes through the geometric centre of
a 4×4×4 block doesn't accidentally classify it Solid or Open based
on one sample's sign). Cost: ~64 K sample reads per chunk per
rebuild — well under a millisecond for plain managed C#.

**Floor direction hardcoded to -Y.** Flat-arena assumption. Spherical
arenas (radial gravity) would need a per-cell gravity lookup — that's
a Phase 5 follow-up alongside the actual AI integration, not v1.

**A* with linear-scan open list.** For a 100-chunk zone the grid has
~50 K cells. Worst-case open-set size is bounded by what A* expands;
in practice rarely more than a few hundred for typical paths. The
linear scan is O(open_size) per pop. A binary-heap priority queue is
the next optimisation, but Phase 5 foundation is about correctness
+ shape, not throughput.

**Managed `byte[]` storage, not `NativeArray<byte>`.** The grid
allocates once per zone (lifetime = match), not per frame, so no GC
pressure. Burst port would only help on A* expansion hot loops, which
plain managed C# handles fine for grids of this size. The NativeArray
upgrade is straightforward when profiles call for it.

**Pass-2 floor lookahead.** Pass 2 walks the chunk's gy range plus
one extra layer above (`gy = end`). This is so that a chunk
excavation that exposes new open air below cell at gy = end correctly
re-promotes that cell from `OpenNoFloor` to `OpenWithFloor` (or vice
versa). Without the extra layer, the cell above stays stale until its
own chunk is rebuilt. Same logic applies to dig-affecting-the-cell-
below the chunk's bottom row, but dig-only means a chunk's top can't
*become* solid retroactively — only the lookahead direction matters.

## What's deferred

- **Spherical-arena floor direction.** Hardcoded -Y for flat arenas;
  spherical needs per-cell gravity lookup via `GravityField`.
- **Binary-heap A* open set.** Linear scan today; upgrade when a
  Profiler capture motivates it.
- **NativeArray + Burst.** Plain managed today; Burst for
  fan-out parallel A* would only help at 1000+ concurrent
  pathfinders, which is far beyond v1.
- **POI authoring.** Phase 5 itself per the plan; foundation lands
  separately so AI integration has a substrate to plug into.
- **Underground enemies.** Same — Phase 5's visual playtest gate.
- **Incremental-update propagation across chunks.** If a chunk's dig
  empties out a cell that's now under an OpenNoFloor cell in the
  +Y neighbour chunk, the neighbour's pass-2 designation is stale
  until that neighbour rebuilds. Acceptable for v1 — the +Y neighbour
  will rebuild on its next remesh (any chunk's brush triggers a full
  `RebuildAllMeshes` today). Optimise later if dirty-set propagation
  becomes finer-grained.

## Files

- New: `Assets/_Project/Scripts/Voxel/OccupancyGrid.cs`,
  `Assets/_Project/Tests/EditMode/Voxel/OccupancyGridTests.cs`.
- Modified: `Assets/_Project/Scripts/Voxel/DigZone.cs`
  (occupancy field + per-chunk rebuild call).

## Validation

- `.claude/scripts/run-tests.sh EditMode`: 192/193 passed, 1
  inconclusive (pre-existing `PresetBlueprintTests`). All 9 new
  Phase 5 tests pass.
- `.claude/scripts/run-tests.sh PlayMode`: 52/54 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
  No regressions from the `DigZone` integration.
