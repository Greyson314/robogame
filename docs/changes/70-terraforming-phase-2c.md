# 70 — Terraforming Phase 2c (async MeshCollider bake + atomic swap)

> Status: **shipped, machine gate green via autonomous runner.**
> `Physics.BakeMesh` now runs on a Unity Job System worker; the
> collider's `sharedMesh` reference stays pinned at the chunk's
> single Mesh object throughout, so it's never transiently null.

## Why this session

User: *"yes, let's move onto phase 2c."*

Phase 2b's RemeshNow did a synchronous `null → mesh` reassign on
the MeshCollider, which both blocked the main thread during cook
AND created a (one-frame-internal) null window in `sharedMesh`.
Phase 2c eliminates both.

## What changed

### Async bake job in [`DigChunk`](../../Assets/_Project/Scripts/Voxel/DigChunk.cs)

New private `BakeMeshJob : IJob` that calls
`Physics.BakeMesh(meshEntityId, convex: false)` in its `Execute()`.
Plain managed `IJob` (not Burst) — the bake itself is native code,
and Burst doesn't add value over a single Unity API dispatch.

`DigChunk.RemeshNow()` now:
1. Drains any previously-scheduled bake (rare case: brush fires
   faster than the worker; `JobHandle.Complete()` blocks the main
   thread until done, then reassigns the collider's sharedMesh to
   pick up the prior bake's cooked data).
2. Runs the Burst mesher synchronously on the main thread (same as
   Phase 2b — mesher is fast).
3. Uploads vertices/indices to the Mesh.
4. Schedules a new bake on a worker. Stores the JobHandle.
5. Returns immediately — no waiting on bake completion.

New `DigChunk.PollBakeAndSwap()`:
- If no bake is pending, no-op.
- If pending but worker hasn't finished, no-op (poll again next frame).
- If pending and complete, calls `JobHandle.Complete()` to drain,
  reassigns `meshCollider.sharedMesh = _mesh` so the collider picks
  up the fresh cooked data, clears the pending flag.

`DigChunk.HasPendingBake` exposed for tests + diagnostics.

`DigChunk.Initialize()` now assigns the empty Mesh to the collider
immediately. Throughout the chunk's lifetime, `sharedMesh` points
at the same Mesh object — only the collider's *cached cooked data*
swaps when a bake completes.

`DigChunk.OnDestroy()` drains any in-flight bake before disposing
the Mesh (the worker holds a reference to the Mesh by entity ID;
disposing under the worker would crash).

### Polling in [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)

New `DigZone.Update()` iterates every chunk and calls
`PollBakeAndSwap`. `[ExecuteAlways]` was already on the DigZone so
this works in Edit Mode too — the test scene's collider refreshes
between Scene-View frames.

### Phase 2c machine gate test

`DigZoneTests.ApplyBrush_AsyncBake_SharedMeshNonNullThroughout`
(now `[UnityTest]` returning `IEnumerator`):

- Single chunk; pre-condition checks `sharedMesh` is already pointing
  at `chunk.CurrentMesh`.
- Apply a brush. RemeshNow runs, schedules an async bake. `sharedMesh`
  is *still* `chunk.CurrentMesh` (assignment happens via
  `PollBakeAndSwap`, not `RemeshNow`).
- Yield up to 60 frames while `chunk.HasPendingBake`. Every frame:
  assert `sharedMesh` is non-null AND still `AreSame(chunk.CurrentMesh, sharedMesh)`.
- Assert bake actually completed within the timeout.

If the collider's sharedMesh ever transiently nulled during the
bake-in-flight window, the per-frame assertion catches it.

## Decisions worth flagging

**Same Mesh object, swapping cooked data.** The alternative —
double-buffer two Mesh objects per chunk and swap references on
completion — doubles per-chunk Mesh memory and complicates the
MeshFilter/MeshCollider lifecycle. The current scheme is simpler:
one Mesh per chunk forever, mutated in place, with the collider
re-cooking on a worker. The "atomic swap" is the cached-cooked-data
swap inside the collider, not a Mesh reference swap.

**Plain `IJob`, not Burst.** `Physics.BakeMesh` is a native call.
Burst-compiling a job that does nothing but dispatch one native call
adds no value (and `Physics.BakeMesh` isn't in Burst's whitelist
anyway).

**Drain-on-RemeshNow for back-to-back brushes.** If a second brush
arrives before the first bake completes, we synchronously
`Complete()` the first bake. This loses the latency win for that
specific frame but guarantees we don't mutate Mesh contents while a
worker is reading them. Brush rates in practice (one menu click, or
~10–30 drill ticks/sec) are well below the bake throughput, so this
drain path is rarely hit. Phase 3+'s drill workload may want a
queue-based scheme.

**Used `EntityId`-overload of `Physics.BakeMesh`.** The `int`-based
overload is `[Obsolete]` in Unity 6 (will be removed in a future
release). `_mesh.GetEntityId()` is the modern lookup, blittable, and
job-friendly.

## What I deliberately did NOT do

1. **No dirty-set propagation.** Plan §12 lists this as a Phase 2c
   "bonus" but the machine gate is just async bake. ApplyBrush still
   rebuilds aprons + remeshes every chunk. Optimizing to "only
   brushed chunks + their -face neighbours" is the obvious next
   improvement and a clean session on its own.
2. **No bake-thread profiling.** The plan §6 budget is "< 5 ms/chunk
   worker time, < 3 ms main-thread spike on bomb crater." The
   benchmark suite doesn't measure these yet — that needs a longer
   profiler-driven test that's outside the machine-gate format.
3. **No multi-frame bake budgeting.** All in-flight bakes complete
   whenever the worker finishes them, with no per-frame cap. For
   eight chunks at < 5 ms each, this is fine; for 100+ chunks
   simultaneously dirtied, we'd want to amortize.

## Files

- **Modified:**
  - `Assets/_Project/Scripts/Voxel/DigChunk.cs` — async bake job,
    PollBakeAndSwap, immediate collider assign in Initialize.
  - `Assets/_Project/Scripts/Voxel/DigZone.cs` — Update polls chunks.
  - `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` — new
    `[UnityTest]` machine-gate test.

## Hard-invariant check

- **No physics changes** to gameplay code (chassis, projectiles).
  Only the dig-zone collider lifecycle.
- **No `Tweakable`s touched.**
- **No new failure modes for arenas without DigZones.** Zero-baseline
  rule still satisfied.
- **No per-frame allocations.** The job struct is a value type; the
  JobHandle is a value type. No managed allocations per RemeshNow.

## Validation

`.claude/scripts/run-tests.sh PlayMode`:

- 38/40 passed, 2 failed (pre-existing `HookGrappleTests` +
  `RotorBlockTests`).
- All 12 `DigZoneTests` (including the new async-bake test) pass.
- All 2 `SurfaceNetsBenchmarkTests` pass.

EditMode unchanged from Phase 2b (170/171, 0 failures, 1 inconclusive).

## What Phase 2d needs from here

Phase 2d: `.dig` binary format + editor baker + loader + content hash
for the Phase 6 netcode handshake.

1. Define a `.dig` binary asset format. Header (magic + version +
   dim + cellSize + chunkGridSize) + payload (per-chunk SDF byte
   arrays). Probably RLE-compressed.
2. Editor baker: a menu tool that voxelises an authored mesh into a
   `.dig` asset. The baker either uses CPU rasterization or runs
   the SDF generator against the mesh's signed distance field.
3. Loader: reads the `.dig` asset at scene load and seeds each
   `DigChunk._sdf` from the on-disk data instead of
   `InitializeHalfSpace()`.
4. Content hash (SHA-256 or similar over the binary content) stored
   in the asset header. Phase 6's NetCode handshake will compare
   client + server hashes to detect tampering.
5. Test: EditMode bake-and-load round-trip — bake a known SDF into
   a `.dig`, load it back, assert byte-identical and content hash
   stable.

Phase 2d completes the Phase 2 milestone and unblocks Phase 3
(drill + bomb integration).
