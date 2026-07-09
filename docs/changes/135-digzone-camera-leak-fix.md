# 135 — DigZone glide failures: camera-leak cascade, not a drill regression

Investigates the 6 PlayMode failures surfaced during session 134's
UI-only runs (5 `DigZoneTests` glide tests + `PerfRenderProbe`).

## Diagnosis

The suspected commit (bec7da48, drill dig-axis orientation bake) is
**exonerated** — it changed only the FBX, a visual offset, and editor
wiring; the failing tests build bare `AddComponent<DrillBlock>()` bots
with no BlockDefinition, so none of that loads.

Actual chain, two independent rots meeting:

1. **PerfRenderProbe rotted at session 132.** The inventor model wiring
   hides host `Block_*` meshes (`BlockVisuals.HideHostMesh`) and puts the
   visible renderers on `BlockModel` FBX children, so the probe's
   `Block_*` name filter found 0 renderers and asserted — *before* its
   own camera-disable step, in a class with no teardown. The Arena scene
   and its tagged main camera stayed loaded.
2. **DrillBlock's cone-aim follows `Camera.main`** (since May's cone-aim
   commit 2b082ee3). With the leaked arena camera alive, every glide
   test's bit tilted along the camera's forward and the kinematic glide
   dug *downward* (−0.37…−0.45 m vs the expected +0.2 climb). Why it
   never bit before: a *passing* probe disables all tagged cameras and
   its own `PerfProbeCam` is untagged (never becomes `Camera.main`) —
   only a *failing* probe leaks a live main camera. Alphabetical suite
   order (Perf < Voxel) put the leak upstream of every glide test.

## Fixes

- `PerfRenderProbe`: chassis renderers gathered by `BlockBehaviour`
  parentage instead of the rotted `Block_*` name prefix.
- `DigZoneTests.SetUp`: disables all stray cameras — isolation holds
  whether an upstream suite leaks a camera by failing or by succeeding.
- `.claude/scripts/run-tests.sh`: failure listing rewritten grep/awk-only
  (machine has no Python; the Windows App Execution Alias shim silently
  ate the old `python3` heredoc, which is why failure names never
  printed).

## Verification

PlayMode 120/121, 0 failed (121st is the documented `[Ignore]`
`MatchFlowTests.SpawnBot`). Parser dry-run reproduced all six failure
lines from the stale XML before the fix run.
