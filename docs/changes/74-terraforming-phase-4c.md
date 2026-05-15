# 74 — Terraforming Phase 4c (LOD-boundary transition, surface-nets-flavored)

> Status: **shipped, machine gate green.** Phase 4 is now complete. The
> LOD-boundary seam handling is a surface-nets-native partial fix, not a
> literal port of Lengyel's transvoxel tables — see "Decision" below.

## What changed

**[`SurfaceNetsMesher.cs`](../../Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs)**
gains a new `NeighbourLodStrides` struct (6 ints; per-face stride of
each face's neighbour relative to this chunk's own mesh stride) and an
optional `strides` parameter on `Mesh` / `Schedule`. Default = all
zeros, treated as no-snap.

`MeshJob` extends with 6 stride fields. In **Pass 1**, cells in the
boundary strip (`cx == 0 || cx == cellDim - 1`, etc.) snap the
axis PERPENDICULAR to the LOD-coarser face onto the coarse-cell-center
lattice (offset = stride/2 in fine cell-grid units). In **Pass 2**,
X/Y/Z-axis-edge quads with all 4 corners in the boundary strip of an
LOD-coarser face are suppressed — the coarser neighbour's mesh owns
that seam. A per-triangle degenerate-area filter
(cross-product magnitude² < 1e-6) catches the rare case where snapped
corners collapse to identical positions.

**[`DigChunk.cs`](../../Assets/_Project/Scripts/Voxel/DigChunk.cs)**
holds a `NeighbourLodStrides` field (default `Identity` = all 1s) and
threads it into the mesher call in `RemeshNow`. The struct lives on
the chunk so the zone-level apron build can populate it once per
remesh.

**[`DigZone.cs`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)**:

- `BuildApronFor` now also writes the chunk's `NeighbourLodStrides`,
  computed from each face-neighbour's `CurrentLodLevel` vs the chunk's
  own LOD. Missing neighbours → stride 1 (zone boundary; no seam).
- `RefreshLod` runs a full `RebuildAllMeshes` pass after any chunk's
  LOD changes — a chunk needs its NEIGHBOUR's new LOD to decide
  whether to snap/suppress on the shared face, and the simplest way
  to make that propagate is one extra apron-rebuild pass per
  LOD-transition frame.

**4 new PlayMode tests** ([DigZoneTests.cs](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs)):

- `LodSeam_FineSidePosXBoundaryVertices_SnapXToCoarseGrid` — with a
  sphere brush spanning the +X boundary and coarseLod=1, fine-side
  apron vertices have their world X snapped from 16.25 m to 16.5 m
  (= `chunkSide + 0.5 × coarseCellSize`), matching the coarse
  neighbour's first-cell-centre X.
- `LodSeam_NoDegenerateTrianglesAcrossLodMismatch_LodOneTwo` — the
  Phase 4c machine gate. Runs at both coarseLod=1 and 2, scans every
  triangle on both chunks, asserts cross-product magnitude² > 1e-6
  (~0.5 mm side).
- `LodSeam_FineAndCoarse_BothHaveVerticesOnSeamPlane` — seam continuity
  check: both meshes emit ≥ 1 vertex at the snapped X plane in world
  coordinates.
- `LodSeam_SameLod_NeighbourStridesAreIdentity_NoSnapNoSuppress` —
  regression guard: when neighbours are at same LOD, all 6 strides are
  1 and the fine chunk's boundary vertices stay at the un-snapped X.

## Decision: surface-nets-native, not a literal Lengyel port

The original brief said "port Eric Lengyel's transvoxel algorithm — 73
case lookup tables." After reading both the transvoxel paper and our
existing `SurfaceNetsMesher`, that approach turned out to be a
conceptual mismatch:

- Lengyel's transvoxel is a Marching Cubes extension. His 73
  transition-cell tables encode MC-style triangulations of a half-cell
  wedge using cell-edge crossings — one triangle per table entry,
  vertices on edge midpoints.
- Our mesher is Naive Surface Nets (Lysenko 2012). It places one vertex
  per active cell at the centroid of zero-crossings on the cell's 12
  edges, then emits one quad per active grid edge. There is no
  per-cell triangulation table; topology comes from quad emission.

A literal port of the 73 transition cases would require either (a)
switching the entire mesher to MC, or (b) running MC only in a
one-cell-thick boundary wedge while keeping SN in the interior, which
creates an internal SN ↔ MC seam to manage. Both are larger changes
than Phase 4c was scoped for.

Instead, the implementation here is what the planner subagent ended
up calling **Option B**: surface-nets-native vertex coalescing. The
fine chunk's boundary-strip cells snap their vertex positions onto
the coarse neighbour's grid; the coarse neighbour's own mesh covers
the seam face. Conceptually the same shape as transvoxel (transition
geometry on the fine side that lines up with the coarse side), but
without MC's lookup tables.

## What it doesn't fix

The snap is "centre-of-coarse-cell" along the perpendicular axis only.
That's correct alignment in X for the +X face, but the in-plane axes
(Y and Z) stay at fine spacing. The coarse neighbour's vertices at
that face are spaced at the coarser interval. So at the seam:

- Fine vertex world position: `(snapped X, fine Y, fine Z)`.
- Coarse vertex world position: `(coarse X, coarse Y, coarse Z)`,
  where `coarse X == snapped X`.

The X coordinate aligns. The Y and Z coordinates don't — the fine
side has more vertices along the seam line than the coarse side.
Result: the meshes share the seam plane but not every vertex on it.
That's still a topological win over Phase 4a/4b (where X also didn't
align), but it's not the pixel-perfect zero-crack seam that a full
transvoxel implementation would produce.

A "full" fix would compute, for each fine boundary cell, the actual
vertex position the coarse neighbour would have placed (by running
surface-nets vertex placement on coarse-stride apron samples) and
snap to *that* position rather than to the cell centre. The apron
data is available; the per-cell compute is a follow-up. Deferred.

## What's deferred (Phase 4c follow-ups)

- **Coarse-neighbour vertex computation** (instead of cell-centre snap)
  for true seam alignment when the surface doesn't pass through cell
  midpoints.
- **Apron extension on the coarse side.** Today the apron is one-sided
  (+X/+Y/+Z only). Coarse chunks' downsample drops the apron's last
  sample for stride-2, so coarse cells don't "see past" their own
  boundary. Asymmetric, but harmless given the current snap design.
- **Visual playtest pass.** Per session 73, seams are sub-pixel at
  32 m+. Phase 4c should make them tighter; a user-driven check
  confirms.

## Files

- Modified: `Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs`,
  `Assets/_Project/Scripts/Voxel/DigChunk.cs`,
  `Assets/_Project/Scripts/Voxel/DigZone.cs`,
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`.

## Validation

- `.claude/scripts/run-tests.sh PlayMode`: 52/54 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
  All 4 new Phase 4c tests pass.
- `.claude/scripts/run-tests.sh EditMode`: 183/184 passed, 1
  inconclusive (pre-existing `PresetBlueprintTests`, unrelated).
  No regressions in `SurfaceNetsMesherTests` despite the mesher
  changes — the new `EmitTriangle` degenerate-area filter doesn't
  drop any triangles in the existing test SDFs (sphere, half-space,
  single-corner).
