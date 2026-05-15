# 66 — Terraforming Phase 1c (Burst port + zero-alloc remesh + benchmark gate)

> Status: **code written, validation pending Unity refresh + Burst JIT.**
> Phase 1c ports the Surface Nets mesher to Burst, switches `DigZone`
> to zero-allocation mesh upload, and pins the < 1 ms / zero-GC machine
> gate via a new PlayMode benchmark suite. First Burst system in the
> project — patterns captured in [`docs/BURST_NOTES.md`](../BURST_NOTES.md).

## Why this session

User: *"Please proceed to the next phase."* — Phase 1c per the plan
ordering. This phase exists to bring the meshing path under the perf
budget the rest of the system assumes; Phase 1b's managed C# version
was correct and demoable but at ~3–8 ms/remesh would frame-kill under
any sustained drilling.

## What changed

### Package adoption — `Packages/manifest.json`

Promoted three transitively-cached packages to direct dependencies:

- `com.unity.burst` 1.8.29
- `com.unity.collections` 6.4.0
- `com.unity.mathematics` 1.3.3

All three were already cached as transitive deps; the promotion just
records intent — making them load-bearing for `Robogame.Voxel`.

### Asmdef — `Robogame.Voxel.asmdef`

Added the three package references and flipped `allowUnsafeCode: true`
(Burst frequently needs pointer paths internally; safer to allow up
front than chase the friction on the next port).

### [`SurfaceNetsMesher`](../../Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs) — Burst rewrite

The algorithm is unchanged (port of the Phase 1a managed version);
the surface changes:

- Inputs/outputs are `NativeArray<T>` instead of managed arrays.
  `Buffers` struct now wraps `NativeArray<int>`, `NativeArray<float3>`,
  `NativeArray<int>`, plus a length-2 `NativeArray<int> Counts` for
  the vertex/index totals.
- `Buffers : IDisposable` — `using var buffers = SurfaceNetsMesher.Allocate(dim, Allocator.Persistent)`.
- New `cellScale` parameter on `Mesh()` folds world-units scaling
  into the Burst job (one multiply per emitted vertex) so the
  host-side scale loop disappears. Default 1.0 preserves the
  cell-grid output the existing EditMode tests assert on.
- Synchronous `Mesh()` uses `IJobExtensions.Run()` — Burst-compiled,
  zero scheduler overhead, no managed allocation per call.
- New `Schedule()` API for Phase 2's multi-chunk fan-out.
- Job struct is `[BurstCompile] private struct MeshJob : IJob`. Inputs
  marked `[ReadOnly]`. Static `AccumulateCrossing` helper inlined by
  Burst — same shape as the managed version, just with `float3` and
  the implicit Burst SIMD codegen.

### [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs) — zero-alloc remesh

- `_sdf` is now `NativeArray<sbyte>`, allocated `Allocator.Persistent`
  in `EnsureInitialised`, disposed in `OnDestroy`.
- `_meshBuffers` allocated similarly.
- `RemeshNow` passes `_cellSize` as `cellScale` to the mesher — no
  host-side scale loop.
- Mesh upload via the high-level `NativeArray`-overload API:
  `mesh.SetVertices(buffers.Vertices.Reinterpret<Vector3>(), 0, vCount)`
  and `mesh.SetIndices(buffers.Indices.GetSubArray(0, iCount), ...)`.
  Both are zero managed allocation.
- `Reinterpret<Vector3>()` over `NativeArray<float3>` is a zero-copy
  view (both are 3-float structs).
- `RecalculateNormals` is retained — required for URP/Lit shading and
  is O(active triangles), not the full pre-allocated index buffer.
- Bounds set manually (`mesh.bounds = ...`) instead of recalculated
  from vertices; we know the local-space envelope without scanning.

### [`BrushApplicator`](../../Assets/_Project/Scripts/Voxel/BrushApplicator.cs)

Signature update: `Apply(BrushOp, NativeArray<sbyte> sdf, ...)` —
keeps the brush applicator and the mesher reading/writing the same
buffer without copies. The applicator itself stays plain managed C#:
brush ops touch hundreds of cells per call, the cost is negligible
versus the mesher, and the C# version is easier to debug. Phase 3
may revisit if drill-per-tick rates make Burst pay for itself.

### Tests — NativeArray migration

[`SurfaceNetsMesherTests`](../../Assets/_Project/Tests/EditMode/Voxel/SurfaceNetsMesherTests.cs):
all 12 tests rewritten to use `NativeArray<sbyte>` and disposable
`Buffers`. `using var` ensures buffers are disposed even on assertion
failures. Added a 13th test pinning the new `cellScale` parameter.
The original test contracts are preserved — same vertex counts, same
index counts, same closedness invariants. If the Burst port broke
anything, these tests would catch it.

[`DigZoneTests`](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs):
unchanged — `_zone.Sdf` now returns `NativeArray<sbyte>` instead of
`ReadOnlySpan<sbyte>`, but the indexer syntax is the same. The 8
existing assertions still hold.

### New benchmark suite — [`SurfaceNetsBenchmarkTests`](../../Assets/_Project/Tests/PlayMode/Voxel/SurfaceNetsBenchmarkTests.cs)

Two tests that lock in the Phase 1c machine gate from
TERRAFORMING_PLAN.md §12:

- `Mesh_Dim33Sphere_BurstMedianUnderOneMillisecond` — warms up 3
  iterations, measures 50 more via `Stopwatch.ElapsedTicks`, asserts
  the median is < 1 ms. Logs median/min/max to the console.
- `Mesh_RepeatedCalls_ZeroGCAllocations` — warms up 5 iterations,
  measures `GC.GetAllocatedBytesForCurrentThread` delta over 50
  more calls, asserts delta == 0.

If either fails, the failure message points at the most likely
culprits (Schedule vs Run, string interpolation in hot path, etc).

### [`docs/BURST_NOTES.md`](../BURST_NOTES.md)

New file. Per TERRAFORMING_PLAN.md risk T1, the first Burst system
pays an onboarding tax; subsequent systems should pay less. This doc
captures:

- Asmdef setup (refs, `allowUnsafeCode`).
- Job struct pattern + the hard rules (no class refs, no captured
  lambdas, no `Debug.Log`, etc).
- `Run()` vs `Schedule()` — when each is right.
- NativeArray lifetime patterns + `Reinterpret` for zero-copy views.
- Mesh upload (zero managed alloc) using the high-level API.
- Benchmark template with warm-up + median + GC-delta.
- "When to *not* Burst-compile" — `BrushApplicator` is the case-in-point.

## Decisions worth flagging

**Used `Run()` not `Schedule().Complete()`.** Both work; `Run()`
skips the scheduler. The zero-alloc benchmark would fail if we used
Schedule for the single-chunk synchronous path. Schedule is still
exposed for Phase 2 multi-chunk fan-out.

**Folded `cellScale` into the Burst job, removed the host-side scale
loop.** A managed `for (int v = 0; v < vCount; v++) verts[v] *= s;`
costs ~150 µs at dim=33; folded into the Burst job it costs roughly
nothing (one extra multiply per vertex inside the SIMD inner loop).
The 1 ms budget has very little headroom — every host-side
microsecond matters.

**Kept `BrushApplicator` plain managed.** The brush AABB loop touches
maybe ~1500 cells at radius 2 m / cellSize 0.5 m. Even at 100 ns per
iteration that's 150 µs — well within any per-brush budget, and
brush events are sparse (one per click in Phase 1b; ~30 per second
in Phase 3 drilling). Bursting it would force a NativeArray-only
path for what's a debug-friendly piece of code.

**High-level `Mesh.SetVertices`/`SetIndices` not low-level
`SetVertexBufferData`.** The low-level path locks the vertex layout
to whatever we declare in `SetVertexBufferParams`. Position-only
locks out `RecalculateNormals`. Position+Normal would require us to
emit normals in the Burst job (a Phase 4 follow-up). The high-level
path lets `RecalculateNormals` add the normals attribute after the
fact at modest cost.

**`InitializeHalfSpace` stays managed.** Runs once at chunk Awake,
not per frame. Bursting it would trade implementation friction for
zero runtime benefit.

## What I deliberately did NOT do

1. **No `Physics.BakeMesh` for MeshCollider.** Phase 2 — async cook
   coalesced with multi-chunk dirty tracking.
2. **No normals computation in the Burst job.** RecalculateNormals
   stays for Phase 1c; Phase 4 owns the lighting + materials pass
   and can inline normals at that point.
3. **No `IJobParallelFor`.** Single-thread Burst is enough for the
   1 ms budget at dim=33. Phase 4 may parallelize for the 100-chunk
   worst-case scenario.
4. **No `Mat_DigZoneEarth` material.** Phase 4.
5. **No Burst-compiled BrushApplicator.** See "Decisions worth
   flagging" above.

## Files

- **Added:**
  - `Assets/_Project/Tests/PlayMode/Voxel/SurfaceNetsBenchmarkTests.cs`
  - `docs/BURST_NOTES.md`
- **Modified (substantial):**
  - `Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs` — Burst rewrite.
  - `Assets/_Project/Scripts/Voxel/DigZone.cs` — NativeArray buffers, zero-alloc upload.
  - `Assets/_Project/Scripts/Voxel/BrushApplicator.cs` — NativeArray<sbyte> signature.
  - `Assets/_Project/Tests/EditMode/Voxel/SurfaceNetsMesherTests.cs` — NativeArray migration.
- **Modified (asmdef / manifest):**
  - `Packages/manifest.json` — Burst / Collections / Mathematics direct deps.
  - `Assets/_Project/Scripts/Voxel/Robogame.Voxel.asmdef` — refs + `allowUnsafeCode`.
  - `Assets/_Project/Tests/EditMode/Robogame.Tests.EditMode.asmdef` — Collections + Mathematics refs.
  - `Assets/_Project/Tests/PlayMode/Robogame.Tests.PlayMode.asmdef` — same.

## Hard-invariant check

- **No per-frame allocation in steady state.** That's the explicit
  gate, and the benchmark test pins it.
- **No physics changes** beyond the existing MeshCollider lifecycle.
- **No `Tweakable`s touched.**
- **No gameplay-outcome changes** — the meshing is purely visual /
  collision; brush ops are server-authoritative when Phase 6 lands.
- **No new failure modes for arenas without DigZones.** Zero-baseline
  rule satisfied; the new packages add no runtime cost without an
  active dig zone.

## Validation needed before commit

The csproj is stale until Unity regenerates it (the new Burst /
Collections / Mathematics references aren't in the build path yet).
On Unity refresh:

1. `dotnet build Robogame.Voxel.csproj` should succeed clean.
2. Run EditMode tests → `SurfaceNetsMesherTests` (13 tests).
3. Run PlayMode tests → `DigZoneTests` (8 tests) + `SurfaceNetsBenchmarkTests` (2 tests).
4. Open `DigZone_Test.unity`, click `Robogame > Dig Zone > Test Sphere Subtract`. Visual should still match Phase 1b (dimple appears, no regression).

If the benchmark median is over 1 ms, the next things to investigate:
- Burst Inspector window — verify `[BurstCompile]` actually
  compiled the job (look for green checkmarks in `Jobs > Burst >
  Open Inspector`).
- Player settings — Burst can be disabled per-platform / per-build
  config. In-editor it should be on by default.

## What Phase 2 needs from here

Phase 2 extends to multi-chunk dig zones + async `MeshCollider`
bake + `.dig` asset baker.

1. `DigZone` becomes a multi-chunk container; each chunk owns its own
   `NativeArray<sbyte>` SDF and `Buffers`.
2. Apron handling: each chunk's mesher reads its 32³ cells plus a
   one-cell rim from neighbouring chunks (for seam-free meshing).
3. `Schedule()` (already exposed in Phase 1c) feeds the multi-chunk
   fan-out — schedule all dirty chunks, complete in dependency order.
4. `Physics.BakeMesh` on a worker thread, atomic collider swap on the
   main thread.
5. `.dig` baker — editor tool that voxelises an authored mesh into
   the initial SDF state for each chunk.
6. Content-hash on the `.dig` asset for the future Phase 6 netcode
   handshake.

No Phase 1c API changes anticipated; `SurfaceNetsMesher.Schedule()`
is the entry point Phase 2 consumes.
