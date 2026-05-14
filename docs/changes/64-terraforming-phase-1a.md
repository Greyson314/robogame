# 64 — Terraforming Phase 1a (Surface Nets algorithm + EditMode tests)

> Status: **algorithm + tests written, validation pending Unity refresh.**
> New `Robogame.Voxel` asmdef. No runtime behaviour wired up yet
> (no `DigZone` component, no scene) — Phase 1b lands that.

## Why this session

Phase 1 of [TERRAFORMING_PLAN.md](../TERRAFORMING_PLAN.md) is budgeted
at 1–2 weeks. Pushed back on doing it in one session and proposed
a three-way split:

- **1a (this session)** — pure C# Surface Nets algorithm + EditMode
  tests. No Unity integration. Exit: tests green.
- **1b** — `DigZone` component + test scene scaffolder + editor
  brush button. Exit: clicking applies a brush in a scene.
- **1c** — Burst port + profiling. Exit: < 1 ms remesh per chunk;
  full plan §12 exit criterion met.

The riskiest unknown in Phase 1 is algorithm correctness — Surface
Nets is small but easy to get wrong on edge cases (winding,
apron-boundary handling, vertex-index sentinels). EditMode tests
can drive the algorithm to correctness in CLI without Unity playtests.

## What changed

### New asmdef: `Robogame.Voxel`

[`Assets/_Project/Scripts/Voxel/Robogame.Voxel.asmdef`](../../Assets/_Project/Scripts/Voxel/Robogame.Voxel.asmdef)

Mirrors the per-feature asmdef pattern (Block, Combat, Movement,
etc.). References `Robogame.Core` only. `allowUnsafeCode: false`
for now; Phase 1c will flip that when Burst lands.

### [`SurfaceNetsMesher`](../../Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs)

Naive Surface Nets per Lysenko's writeup. One static method:

```csharp
public static void Mesh(
    sbyte[] sdf, int dim,
    Buffers buffers,
    out int vertexCount, out int indexCount);
```

Buffers are caller-owned: tests use `Allocate(dim)`, production
(Phase 1b+) reuses one set across remeshes. The mesher resets the
`CellToVertex` sentinel array internally on every call, so buffer
reuse is safe without external bookkeeping.

Output positions are in **cell-grid units** (Vector3 components in
`[0, dim-1]`). Caller scales by `cellSize` and adds chunk origin.
Keeps the algorithm unit-agnostic and avoids baking world-space
assumptions in.

Two-pass structure:
- **Pass 1:** for each cell, read its 8 corner samples. Sign mask
  = 0x00 (all exterior) or 0xFF (all interior) → inactive, skip.
  Otherwise, scan the 12 edges; for each sign-crossing edge,
  compute the parametric zero-crossing and accumulate. Vertex
  position = average of crossings. Stored in `CellToVertex`.
- **Pass 2:** for each axis-aligned grid edge whose two endpoints
  differ in sign, find the 4 incident cells (skip if any are
  outside the chunk), and emit two triangles with axis-correct
  winding. Winding flips based on which side of the edge is
  interior, so the outward normal always points toward the
  exterior side.

### [`SurfaceNetsMesherTests`](../../Assets/_Project/Tests/EditMode/Voxel/SurfaceNetsMesherTests.cs)

12 tests, organised by what class of bug each catches:

- **Degenerate cases** (`AllInterior`, `AllExterior`) — uniform
  input must emit nothing. Catches mask-computation regressions
  where uniform corners look "active".
- **Half-space pinning** (along X, Y, Z, plus a flipped-sign
  variant) — exact active-cell count (49 for dim=8), exact vertex
  position (3.5 on the split axis), exact triangle count (216 =
  36 quads × 6 indices), and outward-normal direction via
  cross-product. The 3 axes catch axis-swap winding mistakes;
  the flipped variant catches both-branches-same-orientation
  regressions.
- **Single negative corner** (boundary case + interior case) —
  the smallest non-trivial mesh. Boundary case asserts 0 quads
  (active edges fall on the chunk boundary, can't form quads).
  Interior case asserts a watertight cube-topology mesh (8
  vertices, 36 indices).
- **Sphere** — sanity on the smooth case the algorithm exists
  for. Doesn't pin exact counts (grid-alignment-dependent) but
  pins `|vertex_radius - target_radius| < 1 cell` and
  closedness (every directed edge has a matching reverse).
- **Determinism** — same SDF input ↦ same output across two
  separate `Mesh()` calls. TERRAFORMING_PLAN §2 commutativity
  argument depends on this; netcode Phase 6 needs it as the
  desync canary.
- **Buffer reuse** — running the mesher twice against the same
  `Buffers` instance with different SDFs produces the same
  output as a fresh allocation. Catches the failure mode where
  the `CellToVertex` reset is missing or partial and stale
  entries leak from run 1 into run 2's quad emission.

The closedness check (`AssertSurfaceIsClosed`) builds a directed
edge frequency table and asserts for every (a→b) edge that the
reverse (b→a) appears with the same frequency. Open boundaries
break this. Used for the interior-corner and sphere tests.

## Decisions worth flagging

**Caller-owned buffers via a `Buffers` struct.** The alternative
was internal allocation per `Mesh()` call, which violates the
zero-allocation rule once Phase 1b wires this up to repeated
remeshes. The `Buffers` struct is forward-compatible with NativeArray
(Phase 1c will swap `int[]` / `Vector3[]` for NativeArray and
the call site stays the same shape).

**Cell-grid-unit output, not world-space.** The mesher knows
nothing about chunk origins or cell sizes. Keeps the algorithm
testable without a full Unity context (no `Transform`, no
`MonoBehaviour`). Phase 1b's `DigZone` does the world-space
transform on upload to `Mesh`.

**No apron-as-data-flow.** Phase 1a meshes a single SDF buffer
of size `dim³`. Whether the outer layer came from a neighbour
chunk or local data is invisible to the algorithm. Apron
plumbing is a Phase 2 concern when multi-chunk dig zones land.

**Conservative index capacity (`18 * cellCount`).** Real chunks
emit far fewer indices than the upper bound, but Phase 1a doesn't
optimise memory — that's a Phase 1c concern when NativeArray
allocations actually matter. For tests at dim ≤ 17 the
over-allocation is < 100 KB.

**No `Vector3Fixed` use in the mesher.** Brush ops use fixed-point
positions for determinism (TERRAFORMING_PLAN §2); the mesher
operates on already-discretised `sbyte` SDF values, so float math
in the vertex-position computation is local to each cell and
deterministic given identical inputs. The determinism test
verifies this.

## What I deliberately did NOT do

1. **No `DigZone` component.** Phase 1b. The mesher takes raw
   `sbyte[]` input — wiring it to a MonoBehaviour with chunk
   lifecycle, SDF mutation, and `Mesh` upload is the next session.
2. **No Burst.** Phase 1c. The algorithm is plain managed C#
   today. Expected dim=33 remesh time is 3–8 ms (per
   TERRAFORMING_PLAN §5); Phase 1c targets sub-millisecond.
3. **No editor menu or scene.** Phase 1b.
4. **No VFX / audio hooks.** Phase 3 (drill block + bomb
   integration) is the first phase that produces gameplay-visible
   effects worth wiring cues to.
5. **No architecture.md update.** Deferred to Phase 1c when the
   full voxel pipeline lands and the module list becomes coherent
   to document.

## Files

- **Added:**
  - `Assets/_Project/Scripts/Voxel/Robogame.Voxel.asmdef`
  - `Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs`
  - `Assets/_Project/Tests/EditMode/Voxel/SurfaceNetsMesherTests.cs`
- **Modified:**
  - `Assets/_Project/Tests/EditMode/Robogame.Tests.EditMode.asmdef`
    — added `Robogame.Voxel` reference.

## Hard-invariant check

- **No runtime behaviour added.** The mesher is unreferenced by
  any production code path. Arenas play identical to session 63.
- **No `Tweakable`s touched.** No gameplay-outcome changes.
- **No per-frame allocations once Phase 1b wires it up.**
  The mesher's only allocation is at `Allocate()` time
  (caller-controlled); the steady-state `Mesh()` call is
  alloc-free given a reused `Buffers`.

## What Phase 1b needs from here

When Phase 1b starts:

1. Add a `DigZone : MonoBehaviour` (probably in
   `Assets/_Project/Scripts/Voxel/`) implementing `IDigZone`.
2. Owner of the SDF buffer for one hard-coded chunk (dim=33,
   cellSize=0.5m). Calls `DigField.Register` in `OnEnable`.
3. Owns one `SurfaceNetsMesher.Buffers` allocated at chunk
   creation; reuses across remeshes.
4. `RemeshNow()`: calls `SurfaceNetsMesher.Mesh`, transforms
   cell-grid positions to world via `Mesh.SetVertices` /
   `SetIndices`, swaps the `MeshCollider.sharedMesh`.
5. Editor scaffolder builds `DigZone_Test.unity` with one
   `DigZone` + ground + camera.
6. Editor menu `Robogame > Dig Zone > Test Sphere Subtract`
   mutates the SDF at a fixed test point and triggers remesh.

No Phase 1a API changes anticipated; the `Mesh()` signature is
what Phase 1b consumes.
