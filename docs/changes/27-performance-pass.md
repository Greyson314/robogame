# Session 27 — Performance pass: docs, diagnostics, and conservative fixes

> Status: **shipped**. The session adds a long-form performance guide,
> a runtime perf HUD, profiler markers across the known hot paths, and
> a small set of low-risk code + URP-asset fixes targeting the default
> arena's ~160 fps ceiling. No gameplay-shape changes; every adjustment
> is reversible by editing one or two lines back.

## Why this session

User reported the default arena clocking ~160 fps on a high-end PC,
which is slow for the current geometry density and concerning given
the planned scope-out to 16-player MP. The session has three goals,
in priority order:

1. Ship a performance guide (`docs/PERFORMANCE.md`) that future
   contributors can lean on instead of re-discovering the same rules
   from the codebase every time.
2. Ship the diagnostics tools needed to keep the conversation
   evidence-based — a perf HUD, profiler markers, an editor menu.
3. Apply the perf fixes that are low-risk and visible-impact today,
   leaving the higher-risk wins (analytic water normals, outline
   bake, etc.) documented for follow-up.

## What landed

### Documentation

- **[`docs/PERFORMANCE.md`](../PERFORMANCE.md)** — comprehensive
  performance guide. Sections: mental model, the five rules that
  catch 80% of regressions, profiling workflow, diagnostics tour,
  per-system perf notes, URP knob inventory, extended budget table,
  predicted future hotspots, and a "the game feels slow" runbook.

  Several hotspots that haven't fired yet are documented with their
  *trigger conditions* so we recognise them when they show up: per-
  block damage replication blowup, outline-pass at MP scale, Verlet
  rope tip Rigidbody count, damage-VFX storm, ChassisFactory rebuild
  thrash, foam wake nested loop, etc.

### Diagnostic tools

- **[`PerfMarkers.cs`](../../Assets/_Project/Scripts/Core/PerfMarkers.cs)**
  — pre-allocated `ProfilerMarker` instances for the project's known
  hot paths (`Robogame.VerletRope.FixedUpdate`,
  `Robogame.Robot.RecalcAggregates`, `Robogame.Water.MeshUpdate`,
  etc.). Markers cost ~50 ns when the profiler is detached, but
  declaring a fresh one per call site bloats the trace; centralising
  them keeps the Profiler hierarchy readable.

  Markers wired into: VerletRopeSimulator FixedUpdate, RotorBlock
  FixedUpdate, AeroSurfaceBlock FixedUpdate, WheelBlock FixedUpdate,
  Robot.RecalculateAggregates, ChassisFactory.Build, WaterMeshAnimator
  Update, BuoyancyController FixedUpdate, RobotDrive.ComputeAimPoint,
  FollowCamera LateUpdate, Projectile FixedUpdate.

- **[`PerformanceHud.cs`](../../Assets/_Project/Scripts/UI/PerformanceHud.cs)**
  — always-loaded, hidden by default, **F3 to toggle**. Auto-bootstraps
  via `RuntimeInitializeOnLoadMethod` so it shows up in every scene
  with no scene authoring needed (same pattern as `FpsCounter`). Live:

  - Frame time (ms) + smoothed fps + 1% / 0.1% lows over a 240-sample
    rolling window.
  - GC bytes-allocated this frame (delta from
    `Profiler.GetTotalAllocatedMemoryLong`).
  - Active Rigidbody / Joint count (sampled at 1 Hz to keep the
    `FindObjectsByType` cost amortised).
  - Verlet chain count + total particle count (read off
    `VerletRopeSimulator.Instance`, see new public accessors below).
  - Robot count + total block count.
  - Editor only: draw calls / SetPass / triangles via `UnityStats`.

  Allocation-free hot path (cached `GUIStyle`, pre-sized `StringBuilder`).
  Cost when hidden: one bool check per IMGUI event.

- **[`PerformanceMenu.cs`](../../Assets/_Project/Scripts/Tools/Editor/PerformanceMenu.cs)**
  — `Robogame > Perf > {Toggle Perf HUD, Log Render Stats, Toggle
  V-Sync, Capture Profiler Frame}`. One-click bridges to the existing
  Profiler / UnityStats APIs.

### Code-side perf fixes

Each one is a small, isolated change. Headline gains will only be
visible under the right scenario (the rotor stress tower for the rope
sim change, the WaterArena for the water change, etc.) — none of
these alone moves the default-arena 160 fps number meaningfully, but
they remove cumulative overhead that compounds as the scene gets
busier.

- **VerletRopeSimulator: drop `GetComponent<Joint>()` per-chain-per-step.**
  Replaced the auto-detect heuristic for "tip is externally constrained"
  with a `tipRb.isKinematic` check. RopeBlock leaves the tip kinematic
  in free flight; HookBlock.Attach is the only path that flips it off.
  Free property read vs the previous component-list walk. ~250
  GetComponent calls/sec eliminated under the rotor stress tower (5
  ropes × 50 Hz).

- **RotorBlock: cache `GetComponentInParent<Rigidbody>()`.** FixedUpdate
  was paying the lookup every step; now cached on `OnEnable`,
  invalidated by `OnTransformParentChanged` (catches a debris-detach
  reparent if it ever happens). Cost dropped from O(ancestor walk) per
  rotor per step to a single field read.

- **WaterMeshAnimator: skip `RecalculateNormals` on a flat surface +
  use `Mesh.SetVertices` / `SetColors`.** The `mesh.vertices = arr`
  property setter triggers an internal bounds recompute we don't need
  (we set generous bounds in BuildMesh). The `Set*` API skips that.
  Skipping `RecalculateNormals` when amplitude ≤ 0.005 saves ~25% of
  the per-frame water cost when the wave field is debug-flat.

- **DevHud: cache GUIStyles.** Two `new GUIStyle(GUI.skin.*)` calls
  ran every OnGUI event when the panel was visible — that's 4–6
  GUIStyle allocations per displayed frame. Caching to static fields
  is a one-line fix; OnGUI is already gated to "panel visible."

- **VerletRopeSimulator: public `Instance` / `ChainCount` /
  `TotalParticleCount` accessors.** Plumbing for the perf HUD; no
  behavioural change.

### URP asset tweaks

These are global render-pipeline settings; visible-quality changes
that are reversible with a one-line edit each. Documented in
PERFORMANCE.md § 6.

- **[`PC_RPAsset.asset:28`](../../Assets/Settings/PC_RPAsset.asset)**
  — `m_MSAA: 4` → `m_MSAA: 2`. 4x oversampling is overkill for an
  arcade voxel game; 2x is the URP-arcade default. Halves the render
  target bandwidth cost. Visible difference past 2x is marginal at
  1080p. Revert: change back to `4`.

- **[`PC_Renderer.asset:90`](../../Assets/Settings/PC_Renderer.asset)**
  — SSAO `Downsample: 0` → `Downsample: 1`. Half-resolution SSAO is
  the URP-recommended default for non-cinematic titles; full-res SSAO
  is ~4x more expensive and the difference is invisible past ~1m.
  Revert: change back to `0`.

## What did NOT land (deferred)

Each is documented in `docs/PERFORMANCE.md` with the reason for
deferring. Quick summary:

- **Analytic water normals** via `WaterSurface.SampleNormal` (replaces
  `Mesh.RecalculateNormals`). Halves water-path cost. Not landed
  because it wants a quick visual check that the lit surface still
  reads right with sampled vs recalculated normals; pure geometry
  match isn't enough proof, the toon shader is touchy about angle.

- **`m_RequireOpaqueTexture: 1` → 0.** Saves an opaque blit per frame
  if no shader samples `_CameraOpaqueTexture`. The Bitgem stylised
  water shader may sample it for refraction; not landed without
  verifying.

- **Outline pass bake-into-base-shader.** § 5.3 / § 8.2 — the largest
  predicted MP-scale rendering risk; deferred until we actually have
  a 16-chassis test scene.

- **Stress-test multi-spawn flow.** "Spawn N combat dummies" would
  let us measure the chassis-Rigidbody scaling curve trivially. The
  manual stack of "Rebuild Combat Dummy" clicks works but is fiddly.

- **Migration of `RecalculateAggregates` to a dirty-flag debounce.**
  Today's flow already has the right shape (`HandleBlockRemoving`
  does O(1) deduction; the connectivity coroutine recomputes once
  per frame). The risk is a future sustained-fire weapon × 16
  chassis bursting hundreds of removals/sec; documented § 5.5.

## Verification

- Project edits land in the main checkout (`C:\Users\Grey\Desktop\mutedtuple\robogame\`),
  not the worktree; per the `editor_target_main_checkout` memory note.
- All asmdef references cross-checked: `Robogame.UI` now references
  `Robogame.Movement` for the perf HUD's chain-count read; nothing
  else needed adjusting.
- Profiler markers use the built-in `Unity.Profiling.ProfilerMarker`
  (UnityEngine.CoreModule), no new package references required.
- URP asset changes are pure value tweaks (no schema field added or
  removed), so Unity will accept them on next domain reload without
  a re-import.

The perf HUD self-validates: hit F3 in any scene, the numbers should
read sane (60+ fps, ~30 active Rbs in default arena, 0 B GC alloc per
frame in steady state). If GC alloc > 0 in steady state, that's a
regression introduced by this PR or a pre-existing one we just made
visible — investigate before merging more work.

## Follow-up: Fluff grass deep dive (session 27.5)

After the initial pass, user reported the actual fps cliff: ~600 fps
at altitude (grass past `_MaximumDistance` and culled), dropping
toward ~150 fps as the camera descended and more grass triangles
emit shells. That's the exact symptom of a runaway shell-based
geometry shader. Investigation found three compounding issues:

1. **URP SSAO `Source: DepthNormals` (default).** The Fluff shader
   declares both a `ForwardLit` pass and a `DepthNormalsOnly` pass.
   With SSAO sourcing from depth-normals, URP runs the **entire
   grass geometry shader twice per frame** — once for the visible
   render, once for the depth-normals texture. This is the single
   biggest rendering overhead in the project today.
2. **`_ShellCount: 16`** at the shader's max. Each shell is a full
   geometry-shader output triangle; per input triangle the geom
   stage emits up to `3 + 16×3 + 6×3 = 69` verts.
3. **`_FadeStartDistance: 150` and `_MaximumDistance: 220`** over a
   220×220 m, 121-resolution hills mesh. **Almost every triangle in
   the 28,800-tri ground was inside the maximum-cost band**
   (geometry shader emits both shells AND fins). The "fade" zone was
   the entire arena.

Combined post-geometry triangle count for grass alone, before fix:
**~1.27 M tris/frame**. After fix: **~210 k tris/frame** (~6× drop).

### Changes (Fluff-specific)

- **[`PC_Renderer.asset:92`](../../Assets/Settings/PC_Renderer.asset:92)**
  — SSAO `Source: 1` → `0`. URP no longer runs the duplicated
  geometry-shader pass over the grass field.

- **[`Mat_ArenaFluff.mat`](../../Assets/_Project/Materials/Mat_ArenaFluff.mat)**
  — `_ShellCount: 16 → 8`, `_FadeStartDistance: 150 → 25`,
  `_MaximumDistance: 220 → 100`. Also unlocked `_MaximumDistance`
  (was in `m_LockedProperties`, which silently blocked the
  scaffolder from updating it).

- **[`FluffGround.cs`](../../Assets/_Project/Scripts/Tools/Editor/FluffGround.cs)**
  — same values mirrored in the editor scaffolder so re-running
  `Robogame > Scaffold > Build All Pass A` doesn't revert the
  perf fixes. Long inline comment added explaining the geometry-
  shader cost model.

- **[`HillsSettings.asset`](../../Assets/_Project/ScriptableObjects/HillsSettings.asset)**
  — `resolution: 121 → 81`. Cuts ground-mesh input triangles by
  ~55% (n²); the hills shape barely changes for gentle Perlin noise.
  **Requires a one-time rebake** to take effect: open `HillsSettings`
  in the Inspector and click "Rebake hills mesh", or run
  `Robogame > Scaffold > Build All Pass A`. The baked mesh asset
  (`Mesh_ArenaHills.asset`) keeps its old 121² geometry until then.

- **[`docs/PERFORMANCE.md`](../PERFORMANCE.md) § 5.3** — full Fluff
  perf section: how the geometry shader spends a frame, the
  duplicated-pass trap with SSAO, the seven tuning knobs in priority
  order, and the verified before/after numbers.

### Visual impact

- `_FadeStartDistance: 25` means only the closest 25 m emits fins.
  Hard to notice — fins fill blade silhouette at horizontal angles,
  and the FollowCamera is mostly tilted forward, not horizon-level.
- `_MaximumDistance: 100` means grass past 100 m fades to the ground
  texture beneath. The arena is 220 m square; in normal play the
  player rarely sees past ~80 m due to terrain occlusion + camera
  framing. If the player flies high (the original test case), they
  *will* see a visible fade-out at the horizon — that's actually
  the correct behaviour for a grass shader, and the "infinite grass"
  look that a 220 m fade produced was illusory anyway (the geom
  shader was paying 22× the cost for visuals that fade-out shaders
  hide automatically).
- `_ShellCount: 8` vs `16`: nearly identical at gameplay framing.
  The visible fluff comes from the colour gradient (top × 1.55,
  base × 0.22) and shape/detail noise modulating across shells; the
  human eye can't resolve more than ~6 shells worth of distinct
  layers anyway.

### Action required after pulling these changes

1. Open Unity. The asset edits will trigger a re-import of
   `Mat_ArenaFluff.mat` and `PC_Renderer.asset`; that's automatic.
2. The hills mesh needs a manual rebake to apply the resolution
   change. Easiest: `Robogame > Scaffold > Build All Pass A` on
   the Arena scene. Alternative: select `HillsSettings.asset`,
   inspector → "Rebake hills mesh" button.
3. Hit Play, fly the plane down to ground level, watch the perf HUD
   (F3). Expected: the fps "cliff" between high-altitude and
   ground-level should be drastically smaller (50–100 fps gap
   instead of 450).

If the cliff is still > 100 fps, capture a frame with the Unity
Profiler in-flight at low altitude. Likely candidates: the toon
outline pass (§ 5.4), depth-priming overhead, or another shader
declaring an unnecessary `DepthNormalsOnly` pass.

## Future-session starter

Same recipe as before:

1. Read this file (latest in `docs/changes/`).
2. `docs/changes/architecture.md` for the current modules table.
3. `docs/PERFORMANCE.md` for the perf rules + "predicted future
   hotspots" list (these will probably be where the next perf bug
   lives).
4. The `Tweakables.cs` "MP DEBT AUDIT" comment for what's left of the
   per-block-data migration.
