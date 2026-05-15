# 65 — Terraforming Phase 1b (DigZone runtime + test scene + brush menu)

> Status: **code written, validation pending Unity refresh + test run.**
> Phase 1b wires Phase 1a's mesher into a live `DigZone` MonoBehaviour,
> a programmatically-scaffolded test scene, and a menu-driven brush
> trigger. Plain managed C# meshing (Phase 1c ports to Burst).

## Why this session

User: *"let's carry on with phase 1."* Followed by: *"i need to step
away, please continue to proceed autonomously and use your best
judgement."*

Then a plan-quality pass: *"modify the plan to be more autonomous,
as I won't be here to constantly oversee."*

So this session does two things:
1. **Plan upgrade** — fix a real spec bug in TERRAFORMING_PLAN § 2
   (sign convention vs. invariant direction were inverted), and add
   per-phase machine gates so future autonomous sessions have a
   clear "done" signal that doesn't require a human.
2. **Phase 1b implementation** — the natural follow-on from Phase 1a.

## What changed — plan edits

### § 2 sign-convention fix

The plan had:
- § 3: `sdf < 0 → interior (solid), sdf >= 0 → exterior (empty)`.
- § 2: invariant `sdf monotonically non-increasing` via `min(...)`.

Under § 3's convention digging pushes cells from interior toward
exterior, i.e. SDF **increases** over time — which is `max(...)` and
non-decreasing, the opposite of what § 2 said. With the buggy
formula, every "subtract" brush would have *added* material instead
of removing it. Fixed § 2 in place: `min → max`, "non-increasing"
→ "non-decreasing", and added the concrete `brush(c) = r - length(c - p)`
formula for `SphereSubtract` so future readers don't have to
re-derive it.

### § 12 autonomy contract

Added a top-of-section paragraph establishing two exit gates per
phase: a **machine gate** (CI-runnable, no human required) and an
optional **visual playtest** (the cursor-and-eyeball check the user
runs on return). The machine gate is the hard requirement; the
visual playtest is iteration material, not a phase rollback.

Two corollaries documented:
- Test scenes are scaffolded from editor menus, not hand-authored,
  so they regenerate deterministically on a fresh checkout.
- Prefer menu-driven brush triggers over Scene-View click handlers
  for the first cut — menu items are invokable from `[Test]` /
  batch mode; Scene-View clicks are not.

Per-phase machine gates added inline:
- Phase 0: `dotnet build` clean + existing tests pass. ✅ session 63.
- Phase 1a: `SurfaceNetsMesherTests` pass. ✅ session 64.
- Phase 1b: PlayMode test instantiates DigZone, applies centred
  SphereSubtract, asserts SDF mutation + mesh swap + monotonicity.
- Phase 1c: benchmark test asserts `< 1 ms` median remesh + zero
  GC allocations.
- Phase 2: bake-and-load round-trip + seam test.
- Phase 3: DrillBlock contact test + bomb crater test.
- Phase 4: 100-chunk worst-case tri-count test + LOD seam test.
- Phase 5: occupancy-grid traversability + A* path test.
- Phase 6: BrushOpBatch encode/decode + two-NetworkManager
  convergence + bandwidth assertion.

Also noted Phase 1's operational split into 1a/1b/1c with pointers
to the session logs.

## What changed — Phase 1b code

### [`BrushApplicator`](../../Assets/_Project/Scripts/Voxel/BrushApplicator.cs) — `Robogame.Voxel`

Static method `Apply(BrushOp, sbyte[] sdf, int dim, float cellSize, Vector3 chunkOriginWorld)`.
Implements the corrected § 2 max-fold: for each cell in the brush's
bounding box, compute `brushValue = r - distance` (positive inside
the brush, negative outside), convert to sbyte units (× 64 per the
§ 3 storage scale), and `sdf[c] = max(sdf[c], brushValue)`.

Cells outside the brush AABB are explicitly not touched — that's
both perf and a correctness guard against the "deep-interior cells
drift toward zero" failure mode. Phase 1b ships `SphereSubtract`
only; `CapsuleSubtract` is Phase 3 when `DrillBlock` lands.

Returns the count of changed cells so the caller can early-out of
the remesh when a brush misses the chunk (Phase 1b uses this; tests
assert on it).

### [`DigZone`](../../Assets/_Project/Scripts/Voxel/DigZone.cs) — `Robogame.Voxel`

`MonoBehaviour, IDigZone`. Single hard-coded chunk per the Phase 1
scope. `[ExecuteAlways]` so Awake fires in Edit Mode too, which is
what makes the test scene render its surface in the Scene View
without entering Play Mode.

Owns:
- `sbyte[] _sdf` — `dim³` samples, allocated once in Awake.
- `SurfaceNetsMesher.Buffers _meshBuffers` — pre-sized at Awake,
  reused across remeshes.
- `Mesh _mesh` — runtime-created Mesh with `IndexFormat.UInt32`
  for headroom; cleaned up in OnDestroy.

API:
- `InitializeHalfSpace()` — Phase 1b seed. Lower half solid, upper
  half exterior. Mesher extracts a flat top plane on first remesh.
- `ApplyBrush(BrushOp op)` — delegates to `BrushApplicator`, returns
  changed-cell count, triggers `RemeshNow()` if anything changed.
- `RemeshNow()` — calls the mesher, scales vertex positions by
  `_cellSize` for the local mesh, uploads via `Mesh.SetVertices` /
  `SetIndices`, swaps `MeshCollider.sharedMesh` (synchronous cook;
  Phase 1c moves to `Physics.BakeMesh` async).

Registers with `DigField` in OnEnable, unregisters in OnDisable.
`WorldBounds`, `CellSize`, `ChunkSizeCells`, `ContainsPoint`
implement `IDigZone`.

### [`DigZoneSceneScaffolder`](../../Assets/_Project/Scripts/Tools/Editor/DigZoneSceneScaffolder.cs) — `Robogame.Tools.Editor`

Two editor menu entries:
- `Robogame > Dig Zone > Build Test Scene` — creates
  `Assets/_Project/Scenes/DigZone_Test.unity` from scratch: ground
  plane, sun, framed camera, one `DigZone` at world origin. Material
  pulled from `WorldPalette.ArenaGround` so the chunk reads against
  the toon palette instead of magenta-default (Phase 4 swaps in the
  real `Mat_DigZoneEarth`).
- `Robogame > Dig Zone > Test Sphere Subtract` — finds the DigZone
  in the current scene and applies a centred 2 m `SphereSubtract`.
  Repeat clicks dig deeper (idempotent at saturation per the § 2
  monotonicity invariant; this is the assertion the PlayMode test
  pins).

### [`DigZoneTests`](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs) — `Robogame.Tests.PlayMode.Voxel`

7 PlayMode tests covering the Phase 1b machine gate:

- `Awake_HalfSpaceSeed_ProducesNonEmptyMesh` — the seed produces a
  meshed surface, not zero-vertex output.
- `OnEnable_RegistersWithDigField` — `DigField.ZoneAt` resolves the
  zone at points inside its bounds.
- `WorldBounds_MatchesChunkSizeAndOrigin` — `IDigZone.WorldBounds`
  agrees with serialised `chunkSize × cellSize`.
- `ApplyBrush_SphereSubtractAtChunkCentre_MutatesSdfInsideBrush` —
  a cell that was interior pre-brush is exterior post-brush. Direct
  SDF inspection via `DigZone.Sdf`.
- `ApplyBrush_CellsOutsideBrushAabb_UnchangedSdf` — a corner cell
  far from the brush is untouched. Pins the AABB-clipping guard.
- `ApplyBrush_RemeshesSurface_VertexCountChanges` — the mesh
  swapped to a different one (more / fewer vertices).
- `ApplyBrush_MeshColliderSwapped` — atomic swap: collider's
  sharedMesh is non-null both pre- and post-brush, and post-brush
  matches `CurrentMesh`.
- `ApplyBrush_AppliedTwice_SecondApplicationChangesNothing` — the
  monotonicity invariant: re-applying the same brush mutates zero
  cells. If this test fails, max-fold is broken and every brush
  would churn SDF infinitely.

## Decisions worth flagging

**`[ExecuteAlways]` on `DigZone`.** Trade-off: simplifies the editor
visual (open scene, see meshed surface immediately without entering
Play Mode) at the cost of `Awake` firing in edit context. Mitigated
by `EnsureInitialised` being idempotent and the SDF init being
side-effect-free.

**Material handling deferred.** Used `WorldPalette.ArenaGround` as a
placeholder — it's grass-coloured rather than dirt, but it's better
than magenta. Phase 4 introduces `Mat_DigZoneEarth` per
TERRAFORMING_PLAN § 7.

**`DestroyImmediate` in PlayMode test TearDown.** Existing PlayMode
tests use `Object.Destroy`, which is deferred — fine for `[UnityTest]`
with `yield return null` but problematic for plain `[Test]` where no
frame advances between tests, causing static-registry pollution
(`DigField` would carry stale zones from prior tests). `DestroyImmediate`
keeps test isolation clean.

**`SurfaceNetsMesher` allocation in `RemeshNow`.** Each remesh
allocates a fresh `Vector3[]` for vertices and `int[]` for triangles
to convert from `Buffers` (over-allocated) to Mesh-sized arrays.
Acceptable at Phase 1b for the test scene (one chunk, sparse remeshes).
Phase 1c eliminates these via `Mesh.SetVertexBufferData` against
pre-sized `NativeArray` buffers.

## What I deliberately did NOT do

1. **Did not run the PlayMode tests.** Unity hasn't regenerated
   csproj/.meta files yet — the user needs to focus Unity, then run
   the new tests via Test Runner → PlayMode → `DigZoneTests`.
2. **Did not implement `CapsuleSubtract`.** Phase 3 lands it with
   the DrillBlock.
3. **Did not move to async `Physics.BakeMesh`.** Phase 1c.
4. **Did not add VFX / audio hooks.** Phase 3 is the first phase
   that produces gameplay-visible effects worth wiring cues to.
5. **Did not commit a `Mat_DigZoneEarth` asset.** Phase 4.

## Files

- **Added:**
  - `Assets/_Project/Scripts/Voxel/BrushApplicator.cs`
  - `Assets/_Project/Scripts/Voxel/DigZone.cs`
  - `Assets/_Project/Scripts/Tools/Editor/DigZoneSceneScaffolder.cs`
  - `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`
- **Modified:**
  - `Assets/_Project/Scripts/Tools/Editor/Robogame.Tools.Editor.asmdef`
    — added `Robogame.Voxel` reference.
  - `Assets/_Project/Tests/PlayMode/Robogame.Tests.PlayMode.asmdef`
    — added `Robogame.Voxel` reference.
  - `docs/TERRAFORMING_PLAN.md` — § 2 sign-convention fix, § 12
    autonomy contract + per-phase machine gates, Phase 1 sub-phase
    note.

## Hard-invariant check

- **No physics, no networking** beyond what was already in place
  (DigField static registry is process-local; no RPC code).
- **No `Tweakable`s added** — gameplay-outcome rule satisfied.
- **DigZone is a single-chunk prototype**, not networked. Phase 6
  is when terrain replicates.
- **No per-frame allocations in steady state** once Phase 1c lands.
  Phase 1b's per-remesh allocations are confined to brush events,
  not the main loop.

## What Phase 1c needs from here

When Phase 1c starts:

1. Add `com.unity.burst` + `com.unity.collections` +
   `com.unity.mathematics` packages.
2. Set `allowUnsafeCode: true` in `Robogame.Voxel.asmdef`.
3. Port `SurfaceNetsMesher.Mesh` to `[BurstCompile] IJobParallelFor`.
   Inputs/outputs become `NativeArray`. `Buffers` struct switches
   to `NativeArray` members.
4. Wire `DigZone.RemeshNow` to schedule the job + complete on the
   same frame (or async-with-fence for Phase 2 multi-chunk).
5. Eliminate per-remesh allocations: `Mesh.SetVertexBufferData` /
   `SetIndexBufferData` against pre-sized `NativeArray` buffers.
6. Add benchmark test asserting `< 1 ms` median remesh + zero GC
   allocations across 50 iterations at `dim=33`.

Per TERRAFORMING_PLAN § 13 risk T1, budget ~1 week for "fighting
Burst" the first time. Document the patterns in
`docs/BURST_NOTES.md` so Phase 4+ Burst systems pay less tax.
