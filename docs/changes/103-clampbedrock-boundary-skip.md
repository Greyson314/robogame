# 103 — Fix: ClampBedrock counted the watertight boundary shell

> Status: **Shipped.** One-line-class bugfix to the session-100 bedrock
> feature. Resolves 5 failing PlayMode tests (the session-100 comfort-pass
> WIP regressions). No new surface.

## What broke

Session 100 added `DigZone.ClampBedrock(chunk)` to re-solidify the bottom
`_bedrockCells` rows after every brush and **subtract the restored count
from the brush's gross `changedCount`** so a brush that only hit bedrock
returns 0 net (preserving max-fold idempotency for the drill-glide gate).

The clamp scanned `y ∈ [1, bedrockCells]` over **all** x/z and restored
any cell that wasn't `sbyte.MinValue`. But the seed
(`InitializeHeightmapSurface` / `InitializeHalfSpace`) gives the zone's
outer face planes (`globalX/Z == 0` or `== totalCells`) **precedence over
bedrock** — they're the watertight exterior shell, seeded `MaxValue`.
ClampBedrock saw those `MaxValue` boundary cells as "needs restoring",
flipped them to `MinValue`, and counted them.

For the bottom chunk row that's ~96–384 phantom "restored" cells *per
brush* (the dim×dim edge ring × bedrock layers). A brush carving real
dirt above bedrock returned `gross − phantom < 0`, so:

- `ApplyBrush`/`ApplyBrushDeferred` reported `changed ≤ 0` → the op never
  hit `_opLog`, `_chunkChanged` stayed false, the renderer-cull never
  flipped the chunk on.
- The first brush also corrupted the bottom boundary shell
  (`MaxValue → MinValue`), so a replayed op-log diverged from the source.

### Failing tests (all one root cause)

| Test | Symptom |
|---|---|
| `DeferredBrush_MutatesSdfNow_RemeshesOnFlush` | `changed` not > 0 |
| `UndugChunkRenderers_CulledUntilCarved` | carved chunk's renderer stayed off |
| `ApplyBrush_SphereSubtractAtChunkCentre_MutatesSdfInsideBrush` | `changed` not > 0 |
| `DigZone_Checkpoint_DropsOpsAtOrBeforeSnapshotTick` | only 4/5 ops logged (op 1 swallowed) |
| `DigZone_ReplayLog_OnFreshZone_ConvergesToOriginal` | 125 bytes differ (op 1's carve missing from log) |

## The fix

`ClampBedrock` now skips the zone-boundary face cells, mirroring the seed's
`isZoneBoundary` precedence: a subtract brush can never lift a `MaxValue`
cell, so those cells never need restoring and must not be counted. Only
genuine interior bedrock cells the brush actually lifted are restored and
subtracted. (`DigZone.cs`, `ClampBedrock`.)

This was a production regression, not stale test expectations — the tests
correctly assert that carving dirt above bedrock reports `changed > 0`,
logs the op, and replays byte-identically. The live arena was also
silently solidifying its bottom-edge shell on the first dig near the floor;
the fix removes that too.

## Tests

PlayMode 100/101 passed, 0 failed, 0 inconclusive (was 93/101, 5 failed,
2 inconclusive). No test edits — the fix is entirely in production code.
`Bedrock_BottomCellsStaySolid_AcrossBrushes` still green (its probe cell is
interior, untouched by the boundary skip).

## Files

- `Assets/_Project/Scripts/Voxel/DigZone.cs` — `ClampBedrock` boundary skip.
