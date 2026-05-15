# 73 — Terraforming Phase 4 (LOD + budget proxy; transvoxel deferred)

> Status: **shipped 4a + 4b + 4d, machine gates green. 4c
> (transvoxel transition cells) deferred to a follow-up.**
> Far chunks now mesh at coarser resolution, cutting triangle
> count by ~9× per LOD step. Visible artifact: small seams at
> LOD boundaries where neighbouring chunks sit at different
> levels — 4c's transvoxel port fixes those.

## What changed

**[`DigChunk`](../../Assets/_Project/Scripts/Voxel/DigChunk.cs)**
gains a `_currentLodLevel` (0/1/2). `RemeshNow()` either meshes
the full-res `_sdfWithApron` (lod=0) or downsamples into a
`_sdfLod` temp buffer with `stride = 1 << lod` and meshes that
with `cellScale = _cellSize * stride`. Output positions stay in
world units across LODs.

`DownsampleSdf` is a plain managed nearest-sample helper; cheap
compared to the mesher itself.

**[`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)**
adds `_lodDistance1` (32 m), `_lodDistance2` (64 m), and
`_enableLod`. `Update` calls `RefreshLod(Camera.main.position)`
each frame; per-chunk distance picks 0/1/2 and `SetLodLevel`
triggers a remesh on transitions.

**4 new PlayMode tests** ([DigZoneTests.cs](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs)):

- `SetLodLevel_ReducesVertexCountAtHigherLevels`: lod 0 > lod 1 > lod 2 vertex counts; returning to lod 0 restores the original count.
- `RefreshLod_NearView_ChunksStayAtLod0`: view at zone centre keeps every chunk at lod 0.
- `RefreshLod_FarView_ChunksGetHigherLod`: view 1000 m away pushes every chunk to lod 2.
- `HighLod_HeavyExcavation_StaysUnderPerChunkBudget`: 50 random brushes + lod=2 stays under the per-chunk budget (15K tris = 1.5M / 100 chunks per plan § 11).

## Decisions worth flagging

**Downsample-and-mesh, not stride-iterate.** Modifying the
mesher's inner loops to use a stride would mix LOD into the
algorithm. Downsampling to a smaller buffer keeps the mesher
dim-agnostic; LOD is a chunk-level concern.

**Nearest-sample downsample, not box-filter.** A box filter
would soften features but loses fidelity at LOD boundaries.
Nearest preserves sample values exactly, which matters for the
brush-op monotonicity story (downsampled values still ≥ original
values pre-brush).

**Apron buffer reused as the downsample source.** The chunk's
`_sdfWithApron` is built per-remesh by `DigZone.BuildApronFor`
before `RemeshNow` runs. Downsampling reads from it directly —
no second apron synthesis pass. The downsampled apron is
nearest-sample-correct because both sides use the same stride.

**Transvoxel deferred (4c).** Eric Lengyel's transition-cell
algorithm has 73 cases and a lookup-table port that's its own
focused work. Without it, two chunks at different LOD levels
have mismatched vertex spacing on their shared face — visible
as small seams. Acceptable for v1 with LOD only kicking in at
32 m+ where seams are sub-pixel; revisit if playtest finds it
distracting.

## What's deferred

- **4c — transvoxel transition cells.** Fixes the LOD-boundary
  seams. Own session.
- **True 100-chunk worst-case profile.** 4d's test is a per-chunk
  budget proxy; a real 100-chunk PlayMode test would need ~5 s to
  scaffold and excavate. The math holds (per-chunk × 100 = total).

## Files

- Modified: `Assets/_Project/Scripts/Voxel/DigChunk.cs`,
  `Assets/_Project/Scripts/Voxel/DigZone.cs`,
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`.

## Validation

`.claude/scripts/run-tests.sh PlayMode`: 48/50 passed, 2 failed
(pre-existing `HookGrappleTests` + `RotorBlockTests`). All 4 new
Phase 4 tests pass.
