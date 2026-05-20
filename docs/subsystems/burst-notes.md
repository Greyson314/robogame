# Burst notes

> Practical patterns for Burst-compiling a system in this project. Written
> after the Phase 1c port of `SurfaceNetsMesher` (session 66). Skim before
> adding the next Burst system so you skip the same onboarding tax.

## Asmdef setup

For any asmdef that contains `[BurstCompile]` types:

```jsonc
"references": [
    "Robogame.Core",
    "Unity.Burst",
    "Unity.Collections",     // NativeArray<T>, Allocator
    "Unity.Mathematics"      // float3, math.*
],
"allowUnsafeCode": true,     // Burst frequently needs pointer paths
```

The Mathematics reference is optional in strict terms, but in practice
every Burst job ends up using `float3` / `math.sqrt` / `math.cross` —
`Mathf` and `Vector3` work in editor builds but are slower paths.

## Job structure

```csharp
[BurstCompile]
private struct MyJob : IJob   // or IJobParallelFor
{
    [ReadOnly] public NativeArray<sbyte> Input;
    public int Param;
    public NativeArray<float3> Output;
    public NativeArray<int> Counts;  // small back-channel for "how many filled"

    public void Execute()
    {
        // No `class` refs, no captured lambdas, no `Debug.Log`,
        // no managed string interpolation. `math.*` and `float3` only.
    }
}
```

**The hard rules** (Burst will refuse to compile otherwise):
- No `class` instance references inside the job (statics OK if they're blittable).
- No captured lambdas / closures.
- No `try/catch`, no `throw` (Burst 1.8+ has limited `throw` support behind a flag; default is off).
- No managed exceptions, no boxing.
- `Debug.Log` is forbidden in `[BurstCompile]` methods; use `UnityEngine.Debug.Log` outside the job or use the Burst log facilities (`Burst.CompilerServices.Constant`-gated debug only).

**Allowed via static methods on the job struct itself** — small helpers
like our `AccumulateCrossing` factor cleanly.

## `Run()` vs `Schedule()`

| Path | When |
|---|---|
| `myJob.Run()` | Synchronous Burst execution on the calling thread. **Zero scheduler overhead, zero managed alloc.** Use for single-job synchronous paths (e.g. `SurfaceNetsMesher.Mesh`). |
| `myJob.Schedule(dependency)` | Async scheduling on the worker pool. Use for fan-out (multi-chunk, etc.); the caller must `Complete()` the handle before reading outputs. |

The "zero managed alloc" benchmark target was the deciding factor for
`SurfaceNetsMesher.Mesh` using `Run()`. `Schedule().Complete()` works
but allocates a tiny scheduler entry per call.

## NativeArray lifetime

- Allocate with `Allocator.Persistent` for chunk-lifetime buffers; dispose in `OnDestroy`.
- Allocate with `Allocator.TempJob` for short-lived buffers (one frame to one job). The container leak detector warns if you forget to dispose.
- `Allocator.Temp` is for single-frame allocations *not* passed to jobs.
- `Buffers` struct with `IDisposable` pattern keeps callers honest: `using var buffers = SurfaceNetsMesher.Allocate(...)`.

**Reinterpret for zero-copy views:** `NativeArray<float3>.Reinterpret<Vector3>()`
returns a view over the same memory (both are 3-float structs). Required
for the Mesh upload path since `Mesh.SetVertices` overloads take
`NativeArray<Vector3>`, not `NativeArray<float3>`.

## Mesh upload (zero managed alloc)

```csharp
var verts = buffers.Vertices.Reinterpret<Vector3>();
mesh.Clear(keepVertexLayout: false);
mesh.SetVertices(verts, 0, vCount);                              // NativeArray overload, zero alloc
mesh.SetIndices(buffers.Indices.GetSubArray(0, iCount),          // sub-array view, zero alloc
    MeshTopology.Triangles, submesh: 0, calculateBounds: false);
mesh.bounds = new Bounds(...);                                   // set manually instead of recalculating
mesh.RecalculateNormals();                                       // adds normals attribute as needed
```

The low-level path (`SetVertexBufferParams` + `SetVertexBufferData`) is
more direct but locks the vertex layout — `RecalculateNormals` then has
nowhere to put the normals. Stick to high-level unless you're computing
normals in the job and pre-declaring a Position+Normal layout.

## Benchmarking the perf gate

```csharp
// Warm up first — Burst JIT-compiles on first call in-editor.
for (int i = 0; i < 5; i++) job.Run();

long before = System.GC.GetAllocatedBytesForCurrentThread();
var sw = new Stopwatch();
for (int i = 0; i < 50; i++) {
    sw.Restart();
    job.Run();
    sw.Stop();
    ticks[i] = sw.ElapsedTicks;
}
long after = System.GC.GetAllocatedBytesForCurrentThread();
```

Sort ticks; assert median is under budget. Assert `after - before == 0`
for zero-alloc. See `SurfaceNetsBenchmarkTests` for the working pattern.

## Common gotchas

1. **`[ReadOnly]` on input NativeArrays** — without it, Burst's safety
   pass thinks you might write to it and refuses parallel scheduling.
2. **`Math.Round` → `(int)math.round`** — `Mathf.RoundToInt` is fine in
   non-Burst code but not in Burst.
3. **Implicit `Vector3` ↔ `float3` conversion is allowed** in
   Unity.Mathematics — you don't need explicit casts in test code that
   reads job outputs.
4. **First-call timing is misleading** — the JIT compile happens on the
   first call in-editor. Always warm up before measuring.
5. **`Stopwatch.GetTimestamp() / Frequency`** is a finer-grained timer
   than `Time.realtimeSinceStartup` and doesn't allocate.

## When to *not* Burst-compile

Burst's compile time has a real friction cost (full domain reloads get
slower as you add jobs). Not every hot loop is worth Bursting. Rules of
thumb:

- **Yes:** dense numeric loops over `NativeArray<T>`, > 10K iterations,
  called per-frame or per-physics-tick.
- **Maybe:** a few hundred iterations per call but the call is on a
  critical path.
- **No:** anything touching managed types, GameObject lookups, scene
  state, or called a handful of times at startup. `BrushApplicator`
  is the case-in-point — hundreds of cells per brush, runs once per
  brush event, plain managed C# is plenty fast.
