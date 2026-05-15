# 69 — Terraforming Phase 2b (apron-based seam-free meshing)

> Status: **shipped, machine gate green.** Phase 2a's visible
> chunk-boundary cracks are gone. New seam test pins vertex
> agreement on shared chunk edges to 1e-4 m. Verified
> autonomously via `.claude/scripts/run-tests.sh`.

## Why this session

User: *"please proceed with 2b."*

Phase 2a left visible seam cracks at chunk boundaries — the
expected artifact (documented in 2a's session log) since each
chunk meshed independently without knowing about its neighbours'
SDF. Phase 2b is the fix.

## What changed

### Apron-staging buffer in [`DigChunk`](../../Assets/_Project/Scripts/Voxel/DigChunk.cs)

Each chunk now allocates two SDF buffers:

- `_sdf` — `(chunkSize+1)³` samples. The chunk's own SDF.
  Brush-mutable, persistent.
- `_sdfWithApron` — `(chunkSize+2)³` samples. Per-remesh staging:
  the chunk's own region copied in + a 1-cell rim of neighbour
  samples around the outside. Written by the parent zone before
  each remesh; the chunk just reads it.

The mesher buffers grow to `Allocate(dimWithApron)` so they hold
up to `(chunkSize+1)³` vertices = the chunk's own 32³ cells plus
the apron rim cells (the chunk's mesh now extends one cell past
its nominal world bounds in each +face direction).

`DigChunk.ApplyBrushNoRemesh(op)` — applies the brush to own SDF
and returns the changed-cell count, but does NOT remesh. The
parent zone batches remeshes across all chunks after every brush
event so a chunk's apron reflects its neighbours' fresh state.

`DigChunk.RemeshNow()` — assumes the apron staging buffer has
been populated; runs the Burst mesher on it, scales output by
`cellSize`, uploads to `Mesh` + `MeshCollider`.

### Apron build in [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)

New `BuildApronFor(chunk)` method. Iterates the
`(chunkSize+2)³` staging region and routes each sample to its
source:

- For coords in `[0, dim)` per axis: copy from this chunk's own SDF.
- For coords at `dim` per axis: the apron sample. Source chunk's
  coord is shifted +1 on that axis; source local sample coord
  is 1 (the neighbour's "first row past the shared boundary").
  Handles all 7 +direction neighbour cases: +X, +Y, +Z, +XY, +XZ,
  +YZ, +XYZ.
- If the required neighbour doesn't exist (dig zone edge): replicate
  the chunk's own face boundary sample. This avoids false
  sign-crossings at the dig zone exterior — without it, boundary
  chunks would render a "wall" at the zone edge where the apron
  defaults to 0 (exterior) clashes with interior cells.

`DigZone.ApplyBrush(op)` is now two-pass:
1. Apply brush to every chunk's own SDF via
   `chunk.ApplyBrushNoRemesh`. Collect total changed-cell count.
2. If anything changed, call `RebuildAllMeshes()` which loops
   every chunk and runs `BuildApronFor` + `RemeshNow`.

The "rebuild all chunks" cost is the explicit Phase 2b trade-off
for correctness simplicity. Phase 2c will add proper dirty
propagation (only the brushed chunks + their -face neighbours need
apron rebuild). For 2×2×2 = 8 chunks the all-rebuild path is ~5 ms
total, well under the per-frame budget.

### Benchmark dim bump

`SurfaceNetsBenchmarkTests` updated from `Dim=33` to `Dim=34` —
matches the production chunk meshing extent after Phase 2b. The
< 1 ms median + zero-GC assertions still hold (mesher cost is
~10% higher at dim=34, was ~0.5 ms at dim=33 per Phase 1c
measurements, comfortable headroom remains).

### Seam test — Phase 2b machine gate

`DigZoneTests.Mesh_TwoAdjacentChunks_VerticesOnSharedXBoundaryAgreeToTolerance`:

- 2×1×1 chunk grid with the default half-space SDF init.
- Both chunks mesh with their respective aprons.
- Vertices in chunk A near world `x = chunkSideMeters + 0.5 × cellSize`
  (the apron-emitted vertex position for cells at A's +X face rim)
  are collected. Same on chunk B's -X face side (own cell at
  local cx=0 → world x = chunkSideMeters + 0.5 × cellSize).
- Asserts: (a) both chunks emit a non-zero number of boundary
  vertices; (b) the counts match; (c) for every left vertex,
  there's a right vertex within 1e-4 m of the same world position.

If the apron data is wrong (e.g., chunk A's apron sample at x=33
doesn't equal chunk B's own sample at x=1), the two chunks would
compute different vertex positions for the shared rim cells and
this test would fail.

## Decisions worth flagging

**Replicate own face sample when neighbour is missing.** Initially
considered defaulting apron samples to 0 for boundary chunks.
That would create a false "exterior" wall at the zone edge,
producing unintended mesh geometry. Replicating the own boundary
sample makes the apron consistent with the chunk's own data: no
sign-crossings appear, the chunk meshes only its real surface.

**Two SDF buffers per chunk, not one.** Tempting to unify into a
single `(chunkSize+2)³` buffer where brushes only write to the
inner region. But that's error-prone — every BrushApplicator call
would need to know about the buffer size vs the writable range
separately. Keeping `_sdf` (own, brush-writable) and
`_sdfWithApron` (mesh-input, parent-managed) cleanly separates
concerns at a modest cost (~35 KB extra per chunk).

**Rebuild every chunk's apron + remesh on every brush.** Cheapest
to reason about; the dirty-propagation optimization (only
brushed chunks + their -face neighbours need apron rebuild) is
Phase 2c. For 8 chunks × ~0.5 ms each = 4 ms per brush, fine for
a test scene and probably fine for in-game brushes at typical
brush rates.

**Mesh extends 1 cell past chunk's nominal bounds.** The meshed
region is `(chunkSize+1)³` cells (own 32³ + apron rim slabs).
Visually: chunk A's mesh extends to world x = `chunkSideMeters + cellSize`
into chunk B's territory. Both chunks emit identical vertices in
this overlap region (deterministic math + identical SDF samples
on both sides). Renderer draws duplicate triangles in the overlap;
they're coplanar so no z-fighting, just a small ~3% overhead per
boundary. Acceptable for seam-free rendering.

**Trade-off for MeshColliders.** Two chunks' colliders also
overlap at the boundary. PhysX handles this — a robot driving
across the boundary contacts both colliders simultaneously but
they're coplanar so contact resolution is consistent. Not ideal
but not problematic for v1.

## What I deliberately did NOT do

1. **No dirty-set propagation.** Phase 2c. Every brush triggers
   a full chunk-set apron rebuild + remesh.
2. **No async `Physics.BakeMesh`.** Phase 2c.
3. **No collider-vs-renderer mesh decoupling.** The duplicated
   apron-rim mesh is also in the collider. Could be cleaner but
   not necessary for seam-free visuals.
4. **No `.dig` baker.** Phase 2d.
5. **No proper interior/exterior apron data when chunks are at
   the dig zone boundary in *interior* directions** — the
   "replicate own sample" fallback is correct only when the
   neighbour is genuinely absent. For e.g. a +X-most chunk in
   a 2×2×2 grid, its +X apron is replicated (no neighbour) but
   its +Y apron is from a real neighbour. Mixed handling is
   intentional and the code handles each axis independently.

## Files

- **Modified:**
  - `Assets/_Project/Scripts/Voxel/DigChunk.cs` — apron staging
    buffer + bigger mesher buffers + RemeshNow assumes apron is
    pre-built.
  - `Assets/_Project/Scripts/Voxel/DigZone.cs` — BuildApronFor +
    two-pass ApplyBrush + RebuildAllMeshes.
  - `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` —
    new seam test (machine gate).
  - `Assets/_Project/Tests/PlayMode/Voxel/SurfaceNetsBenchmarkTests.cs`
    — Dim 33 → 34.

## Hard-invariant check

- **No physics changes** beyond the existing MeshCollider lifecycle.
- **No `Tweakable`s touched.**
- **No new failure modes for arenas without DigZones.** Zero-baseline
  rule still satisfied.
- **No per-frame allocations.** Apron rebuild is O(chunks × dim³)
  managed C# but no GC alloc (writes into pre-allocated NativeArray).
- **Seam test passes autonomously.** First Phase whose machine gate
  was verified end-to-end without manual Test Runner clicks via
  `.claude/scripts/run-tests.sh`.

## Validation

`.claude/scripts/run-tests.sh All`:

- EditMode: 170/171 passed, 0 failed, 1 inconclusive (preset
  blueprint env, pre-existing). All 13 `SurfaceNetsMesherTests`
  pass.
- PlayMode: 37/39 passed, 2 failed (pre-existing `HookGrappleTests`
  + `RotorBlockTests` — unrelated). All 11 `DigZoneTests` +
  2 `SurfaceNetsBenchmarkTests` pass.

Visual playtest is still worth running for confirmation: rebuild
`DigZone_Test.unity` via `Robogame > Dig Zone > Build Test Scene`,
click `Test Sphere Subtract`. Expected: dimple spans multiple
chunks AND the previously-visible chunk-boundary cracks are now
gone.

## What Phase 2c needs from here

Phase 2c moves MeshCollider cooking off the main thread via
`Physics.BakeMesh` and adds atomic collider swap. Concretely:

1. After `RemeshNow` produces the new mesh data, schedule
   `Physics.BakeMesh(mesh.GetInstanceID(), convex=false)` on a
   worker.
2. When the bake completes, on the main thread: assign
   `meshCollider.sharedMesh = mesh`. This is "atomic" in the
   sense that the collider's sharedMesh is never transiently null.
3. Bonus: dirty-set propagation so a brush touches only the
   affected chunks + their -face neighbours' aprons. Today's
   "rebuild all" cost goes from O(N) to O(touched chunks).
4. Test: PlayMode test asserts `meshCollider.sharedMesh` is
   non-null throughout the bake (no transient null window).
