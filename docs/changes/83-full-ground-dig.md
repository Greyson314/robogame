# 83 — The whole arena floor is diggable

> Status: **code complete, machine gate pending a green test run.**
> Visual playtest + scene regen are user follow-ups (see end).
>
> User intent: stop the dig being a tiny authored cube. The *entire*
> rolling ground is now carveable terrain with real depth. The surface
> keeps its Fluff grass; cut faces and tunnels read as dirt; grass
> disappears exactly where you've dug.

## What shipped

**P1 — full-footprint zone.** `EnvironmentBuilder.BuildArenaDigZone`
is now a 6×1×6 grid of 32-cell chunks at 1.0 m = **192 × 32 × 192 m**
centred on the arena (`transform = (-96,-16,-96)` → surface ≈ y=0,
~16 m of dig depth). LOD on (32/64 m) for the triangle budget;
perimeter wireframe off (it'd box the whole map). Worst-case ≈ 36 ×
~20 K ≈ 0.7 M tris, under the 1.5 M target (the naive 0.5 m approach
was ~15 M — ruled out). The POI chamber moved to world `(77,-8,77)`,
~8 m *under* the surface, so the `VoxelChaserBot` is a genuine
dig-down-to-reach-it objective.

**P2 — heightmap-seeded surface.** New runtime
[`HeightmapField`](../../Assets/_Project/Scripts/Voxel/HeightmapField.cs)
(`HeightmapParams` struct + pure sampler) is the single source of
truth for ground height. `HillsGround.SampleHeight` now delegates to
it, so the baked grass mesh and the voxel SDF surface use
byte-identical math — that's what keeps the two layers aligned (the
flagged alignment risk). `DigZone.InitializeHeightmapSurface()` seeds
each column to `Sample(x,z) − _surfaceSinkMeters`; the sink (0.25 m)
keeps the opaque grass mesh just above the voxels so there's no
coplanar z-fight while undug. `EnvironmentBuilder` pushes the live
`HillsSettings` in via `HillsGround.LoadHeightmapParams()`.

**P3 — dirt material.** New triplanar URP shader
[`DigZoneEarth.shader`](../../Assets/_Project/Art/Shaders/DigZoneEarth.shader)
(Surface-Nets meshes have no UVs, so it samples a baked tiled dirt
texture by world position weighted by normal) +
[`DigZoneEarthMaterial`](../../Assets/_Project/Scripts/Tools/Editor/DigZoneEarthMaterial.cs)
builder (mirrors `GroundMaterial`; flat-earth fallback if the shader
is missing). Chunks render this, never Fluff — TERRAFORMING_PLAN §7 /
T8 still holds (Fluff stays on the *separate* grass mesh, not the
voxel surface).

**P4 — grass clip-mask.** The grass mesh kept its full collider-free
self as a decoupled visual layer; the voxel chunks are the sole ground
collider (`HillsGround.Build(addCollider:false)`) so a dug column
actually drops the chassis. `DigZone` maintains an `RFloat` "metres
dug below the seeded surface" texture (one texel/column) and pushes it
+ a world-XZ→UV mapping as **global** uniforms. Two `// [robogame mod]`
edits to `Grass.hlsl` (globals after `CBUFFER_END`; an early `discard`
in `Fragment`) clip grass over dug columns. Globals default inert so
every other scene's Fluff is untouched. Documented in
[PACKAGE_MODIFICATIONS.md](../PACKAGE_MODIFICATIONS.md). Clip depth
2.0 m > 1 m cell so voxel quantisation never false-clips undug grass.

**P5 — close-out.** Reverted the temp `DrillBlock._debugReadout`
default (was `TEMP:` ON since 5d3caf04). Tests below.

## Tests (machine gate)

- EditMode `HeightmapFieldTests` (7): disabled→0, flat spawn zone,
  edge-flat ramp, mid-band relief + determinism, amplitude monotonicity,
  `HillsGround` projection round-trip.
- PlayMode `DigZoneHeightmapTests` (5): solid-below/exterior-above
  seeding, **surface-follows-heightmap** (top-solid column ranking
  tracks `Sample`), full-arena footprint + containment, dig-anywhere
  far from centre, worst-case per-chunk triangle proxy × 36 < 1.5 M.

## Known follow-ups (user / visual)

1. **Scene regen required.** `EnvironmentBuilder` only takes effect
   after re-running **Robogame > Build Everything** (it rewrites the
   arena `.unity`). Not done headlessly here.
2. **Visual playtest.** Confirm: no z-fight on undug ground; grass
   vanishes cleanly over a fresh drill/bomb hole; the 0.25 m sink
   isn't a visible lip at dig edges; dirt triplanar scale (3 m) reads
   right; the bot is reachable by drilling down to `(77,-8,77)`.
## Perf pass (same session, follow-up commit)

Playtest showed idle FPS 300→120 and 120→60 while digging. Two holes,
both fixed:

- **LOD-on regression (idle).** The old 4-chunk arena ran
  `_enableLod=false`; I'd flipped it to `true` for the triangle budget
  the budget doesn't actually need (0.7 M < 1.5 M). With LOD on,
  `DigZone.Update()`→`RefreshLod()` triggered a **full 36-chunk**
  `RebuildAllMeshes` every time the follow-camera crossed a 32/64 m
  band on the 192 m zone. Reverted to `_enableLod=false`.
- **Full-zone remesh per dig (dig dip).** `ApplyBrush` called
  `RebuildAllMeshes` (all 36 chunks remesh + occupancy + full 192²
  mask) on every brush op. New `RebuildChangedChunks` scopes the
  remesh to the chunks a brush actually mutated plus their −Δ apron
  consumers (a drill hits 1–2 of 36); occupancy + dig-mask update only
  the genuinely changed chunks. `WriteMaskSlab`/`PushDigMask` factor
  the mask so the seed path keeps the full min-merge and the per-dig
  path overwrites just the touched slabs. Pre-sized scratch buffers,
  zero per-op allocation. New `DigChunk.RemeshCount` + machine-gate
  test `InteriorBrush_OnlyRemeshesTouchedChunks` pins the scoping.

## Perf pass 2 (three coalescing/throughput wins)

- **Deferred dirty flush.** New `DigZone.ApplyBrushDeferred` (the drill
  uses it; bombs keep immediate `ApplyBrush` so craters feel instant).
  The SDF + op-log mutate every tick (state stays authoritative) but
  the remesh/bake/occupancy/mask coalesce to a throttled
  `FlushPendingDirty` in `Update` (`_flushInterval`, ~25 Hz). A 30 Hz
  drill now remeshes ~25 Hz instead of per-tick. `HasPendingDirty` +
  `DeferredBrush_MutatesSdfNow_RemeshesOnFlush` test.
- **Analytic normals.** The Burst mesher emits per-vertex normals from
  the SDF gradient (8 corner samples already in registers), so
  `DigChunk.RemeshNow` drops the main-thread `Mesh.RecalculateNormals`
  entirely. `Buffers.Normals` + `Mesh_AnalyticNormals_HalfSpaceAlongY_PointUp`
  test; existing winding tests unaffected.
- **Dig-mask upload throttle.** Slab CPU data still recomputes every
  flush, but the full-texture `Texture2D.Apply` + globals are
  rate-limited (`_maskUploadInterval`, ~10 Hz) with a trailing upload
  in `Update` so the cosmetic grass cut always converges.
