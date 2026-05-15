# 67 — Terraforming Phase 2a (multi-chunk DigZone container)

> Status: **code written, validation pending Unity refresh + test run.**
> Phase 2a refactors DigZone from a single-chunk owner to a multi-chunk
> container. New `DigChunk` MonoBehaviour holds the per-chunk SDF +
> mesh + colliders. No apron yet (boundaries seam — Phase 2b fixes).
> No async cook yet (Phase 2c). No `.dig` baker yet (Phase 2d).

## Why this session

User: *"All tests pass, well done, please continue."*

Phase 1c's machine gate (Burst meshing < 1 ms, zero GC) shipped clean
last session. Phase 2 in the plan is multi-chunk + apron + async cook
+ `.dig` baker — 1 week budgeted total, too big for one session.

Same split shape we used for Phase 1:

- **2a (this session)** — multi-chunk container, no optimizations yet.
- **2b** — apron data flow + seam test.
- **2c** — async `Physics.BakeMesh` + atomic collider swap.
- **2d** — `.dig` binary format + baker + loader + content hash.

Plan updated in §12 with the sub-split and per-sub-phase machine gates.

## What changed

### New [`DigChunk`](../../Assets/_Project/Scripts/Voxel/DigChunk.cs)

A MonoBehaviour that owns one chunk's `NativeArray<sbyte> Sdf`,
mesher buffers, `Mesh`, and Mesh-Filter/Renderer/Collider components.
Most of Phase 1c's `DigZone` body moved here.

Explicit lifecycle: instantiated by the parent `DigZone` with
`SetActive(false)` first, then configured via `Initialize(chunkCoord,
cellSize, chunkSizeCells)`, then activated. No Awake auto-init —
chunks only work after the parent has handed them their coordinate.

API the parent (and tests) consume:
- `Initialize(...)` — allocate SDF, mesher buffers, Mesh.
- `Sdf` — `NativeArray<sbyte>` accessor (trusted; the parent uses it
  for half-space seeding).
- `ApplyBrush(BrushOp)` — delegates to `BrushApplicator` with this
  chunk's world origin, triggers `RemeshNow()` if anything changed.
- `RemeshNow()` — Burst mesh + zero-alloc Mesh upload (same shape
  as Phase 1c).
- `CurrentMesh`, `ChunkCoord`, `WorldBounds` — diagnostics + tests.

### [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs) rewritten as a container

`DigZone` now implements `IDigZone` for the aggregate volume but no
longer owns any rendering. Configuration via three serialised fields:
`_cellSize`, `_chunkSizeCells`, `_chunkGridSize` (Vector3Int, default
2×2×2 = 8 chunks). Plus `_chunkMaterial` for renderer assignment.

`EnsureInitialised` (called from Awake / OnEnable / lazy):
1. Cleans up any leaked DigChunk children from a prior domain reload.
2. Spawns `gridSize.x * gridSize.y * gridSize.z` chunk GameObjects as
   `HideFlags.DontSave` children (scene file persists only the DigZone;
   chunks rebuild fresh on every scene load).
3. Each chunk gets its local position from its grid coord, then is
   activated. Initialize fires; SDF + buffers + Mesh allocated.
4. `InitializeHalfSpace()` writes a global half-space SDF: lower half
   of the entire zone is solid, upper half exterior. Each chunk's
   own +face boundary samples are shared with neighbours (computed
   identically on both sides because the half-space function is
   deterministic).
5. Every chunk does its first `RemeshNow()`.

`ApplyBrush(BrushOp)` dispatches to all chunks unconditionally —
each chunk's `BrushApplicator` clips to its own AABB and returns 0
if the brush misses. Cost for a non-touched chunk is one AABB
rejection (microseconds at chunk count ≤ ~100).

`ChunkGridSize`, `ChunkSizeCells`, `CellSize` have property setters
that throw if the zone is already initialised. Tests configure these
on an inactive GameObject before `SetActive(true)` fires Awake.

`WorldBounds` is the aggregate of all chunks; `ContainsPoint` uses
the aggregate bounds. `DigField.ZoneAt(worldPoint)` resolves the zone
just like Phase 1c.

### [`DigZoneSceneScaffolder`](../../Assets/_Project/Scripts/Tools/Editor/DigZoneSceneScaffolder.cs) updated

The DigZone GameObject no longer needs MeshFilter / MeshRenderer /
MeshCollider — those moved to chunks. Scaffolder uses
`SerializedObject` to set the new fields (chunkGridSize, chunkMaterial)
before activating the GameObject. Default scaffolded zone is 2×2×2
= 8 chunks. Camera + ground plane resized to frame the larger
32 m × 32 m × 32 m volume. Test Sphere Subtract radius bumped to
4 m so it visibly spans multiple chunks.

### [`DigZoneTests`](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs) refactored

Old tests assumed a single-chunk DigZone with direct `Sdf` accessor.
Now tests use a `MakeZone(chunkGridSize, ...)` helper that creates
an inactive GameObject, sets `ChunkGridSize` (etc.), activates, and
returns the zone with its chunks already spawned.

Most existing tests run with `chunkGridSize = (1,1,1)` to preserve
single-chunk regression coverage with minimal change. The new Phase 2a
machine-gate test runs with `(2,1,1)`:

- `ApplyBrush_AtChunkBoundary_MutatesBothChunks` — brush of radius 3 m
  centred at the boundary plane (x = chunkSideMeters). Asserts:
  (a) total changed cells > 0;
  (b) SDF cells just inside the left chunk's +X face are now exterior;
  (c) SDF cells just inside the right chunk's -X face are exterior;
  (d) both chunks' meshes have remeshed (vertex count differs).

Plus:
- `ApplyBrush_OutsideZone_NoChunksTouched` — brush far above the zone
  changes zero cells; SDF corner samples on both chunks are unchanged.
- `ChunkCount_MatchesGridSize` — 2×2×2 spawns 8 chunks, every grid
  coord resolves.

## Decisions worth flagging

**`HideFlags.DontSave` on chunk children.** The alternative — saving
chunk children in the scene file — would inflate scenes massively
(8 chunks × 33 KB+ of generated mesh data each) and create churn
every save. DontSave keeps the scene file as just "DigZone GameObject +
its serialised settings" and lets the runtime regenerate. The cost is
a per-scene-load remesh of every chunk, which is fine for a 1c-paced
Burst mesher (a few ms across 8 chunks).

**Initialize() instead of an `[ExecuteAlways] Awake` doing the work.**
DigChunk's Awake would have no way to know its chunk coord — the
parent has to tell it. Explicit Initialize() makes the lifecycle
unambiguous and lets the parent control timing
(SetActive(false) → AddComponent → Initialize → SetActive(true)
ensures Awake won't fire prematurely).

**Property setters on DigZone with init-time guard.** Tests need to
configure chunkGridSize per-test (smaller grids for fast iteration,
larger for boundary tests). Public setters throw if the zone is
already initialised, which prevents accidental post-init mutation
that would silently leak chunks.

**No apron in Phase 2a means visible seams at chunk boundaries.**
Accepted cost; the visual playtest will show the cracks. Phase 2b is
the fix. The seam test that asserts vertex agreement on shared edges
also lands in 2b.

**Dispatch to all chunks unconditionally, no spatial index.** The
brush AABB intersection check is cheap and chunks self-clip via
BrushApplicator. For 8 chunks this is trivially fast. If chunk count
grows past ~100 (large dig zone per the plan's authoring discipline,
§ 7), an Octree or grid lookup is the obvious upgrade — but a Phase 4+
concern, not Phase 2a.

## What I deliberately did NOT do

1. **No apron data flow.** Phase 2b.
2. **No async `Physics.BakeMesh`.** Phase 2c.
3. **No `.dig` baker / loader / content hash.** Phase 2d.
4. **No sparse chunk allocation** (uniform-Empty / uniform-Exterior
   states from plan § 3). Every chunk is currently full Mixed even
   if it has no surface. The optimization is worth ~80% memory in
   real arenas (most chunks are entirely solid or entirely empty)
   but doesn't affect Phase 2a correctness. Plan § 12 doesn't gate
   on it for 2a.
5. **No octree / spatial index for brush dispatch.** Iterate-and-clip
   is fine at chunk counts ≤ 100.

## Files

- **Added:**
  - `Assets/_Project/Scripts/Voxel/DigChunk.cs`
- **Modified:**
  - `Assets/_Project/Scripts/Voxel/DigZone.cs` — rewritten as container.
  - `Assets/_Project/Scripts/Tools/Editor/DigZoneSceneScaffolder.cs` — multi-chunk scaffold + larger framing.
  - `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` — adapted for multi-chunk, added 3 new tests.
  - `docs/TERRAFORMING_PLAN.md` — § 12 Phase 2 sub-split documented.

## Hard-invariant check

- **No physics changes** beyond the existing MeshCollider lifecycle.
- **No `Tweakable`s touched.**
- **No new failure modes for arenas without DigZones.** Zero-baseline
  rule satisfied; multi-chunk infrastructure adds zero cost when no
  DigZones are in the scene.
- **No per-frame allocations.** Chunks allocate NativeArrays once at
  Initialize; per-brush dispatch is `O(chunks)` aabb checks +
  `O(touched-cells)` SDF mutates + `O(touched-chunks)` remeshes; no
  managed allocation in steady state.

## Validation needed before commit

1. Focus Unity to regenerate csprojs and `.meta` for `DigChunk.cs`.
2. `dotnet build Robogame.Voxel.csproj` should be clean.
3. Run EditMode tests — `SurfaceNetsMesherTests` (13 tests) still
   pass; they don't touch DigZone.
4. Run PlayMode tests — `DigZoneTests` is now ~10 tests including
   the new multi-chunk + dispatch coverage; `SurfaceNetsBenchmarkTests`
   (2 tests) unaffected.
5. Re-run `Robogame > Dig Zone > Build Test Scene` — the existing
   `DigZone_Test.unity` from Phase 1b/1c needs rebuilding because the
   DigZone GameObject's layout changed (no MeshFilter/Collider on the
   parent any more). The scaffolder will overwrite the scene cleanly.
6. Click `Robogame > Dig Zone > Test Sphere Subtract` — should see a
   dimple at the zone centre that spans multiple chunks. **Expected
   visual artifact:** cracks at chunk boundaries because there's no
   apron yet. This is Phase 2b's fix.

## What Phase 2b needs from here

Phase 2b adds apron-based seam-free meshing. Concretely:

1. `SurfaceNetsMesher.Mesh` signature extends to accept a "neighbour
   sample lookup" — either as additional NativeArrays for the six
   face aprons, or as a callback predicate. The job reads its 32³
   own cells plus a 1-cell rim of neighbour cells when computing
   vertices on the +face boundaries.
2. `DigZone.RemeshChunk(coord)` orchestrates: gather the chunk's
   neighbour SDF aprons → mesher → upload.
3. Initial dirty-marking can stay naive (every chunk that received a
   brush remeshes). Phase 2c is when the apron change propagates to
   neighbours (a chunk's edit affects the +face boundary samples
   visible to its -X / -Y / -Z neighbours).
4. New EditMode test bakes a known SDF into two adjacent chunks with
   apron data, meshes both, asserts vertex positions on the shared
   chunk-edge agree to within 1e-4 m.
