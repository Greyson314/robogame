# Robogame — Performance Guide

> **Audience.** Anyone (human or AI) about to add or touch a system that
> runs every frame, every fixed step, on a collision callback, or during
> a pooled spawn. Read § 1 before any "I'll just sprinkle this in" change.
>
> **Scope.** Concrete, opinionated rules tailored to *this* game — a
> Unity 6 / URP voxel-block vehicular combat title with a 16-player
> client/host MP endgame. Generic Unity perf advice that doesn't apply
> here is left out on purpose.
>
> **Companion docs.**
> [`BEST_PRACTICES.md` § 16](BEST_PRACTICES.md#16-performance-budgets-targets-not-law)
> for the budget table this file extends;
> [`PHYSICS_PLAN.md`](PHYSICS_PLAN.md) for the physics-side budget;
> [`NETCODE_PLAN.md`](NETCODE_PLAN.md) for the bandwidth-side budget that
> a poorly-tuned physics frame will eat alive once netcode lands.

---

## Table of contents

1. [Mental model](#1-mental-model)
2. [The five rules that catch 80% of regressions](#2-the-five-rules-that-catch-80-of-regressions)
3. [Profiling workflow](#3-profiling-workflow)
4. [Diagnostic tools shipped with the project](#4-diagnostic-tools-shipped-with-the-project)
5. [System-by-system perf notes](#5-system-by-system-perf-notes)
6. [Rendering / URP knobs](#6-rendering--urp-knobs)
7. [Performance budgets (extended)](#7-performance-budgets-extended)
8. [Predicted future hotspots](#8-predicted-future-hotspots)
9. [Runbook: "the game feels slow"](#9-runbook-the-game-feels-slow)

---

## 1. Mental model

A frame is **16.6 ms at 60 fps**, **6.94 ms at 144 fps**. We are not
trying to hit "as many fps as possible" — we are trying to hit a stable
high-Hz target with **headroom for 16 chassis × ~150 blocks each + 32
ropes + 8 rotors + projectiles + VFX + wreckage**, on a server that
also has to simulate every client's physics. Any frame budget that
looks comfortable today shrinks roughly **8–16×** under that load.

Three coupled budgets, in priority order:

1. **CPU main thread** — gameplay, physics, animation, render setup,
   GC. The most contested. Below ~8 ms in steady state on a target
   machine, or netcode tickrate suffers.
2. **Render thread + GPU** — draw setup, shadow maps, post, present.
   Less contested than the main thread today; will become contested
   the moment we add real VFX or a competitive number of cubes.
3. **Memory traffic** — GC pressure (and, separately, cache thrash).
   The headline metric is **bytes allocated per steady-state frame**.
   Target: **0**.

Profile in a **build**, not the editor. The editor profiler over-
reports CPU cost (the editor itself ticks). 5–10× difference is normal
on some metrics. A "bad" editor frame can be a fine build frame.

---

## 2. The five rules that catch 80% of regressions

### 2.1 Zero allocations per steady-state frame

The single biggest cause of *stutter* in a Unity game is GC. A 1 KB/frame
allocation isn't slow per byte — but at 60 fps it produces ~60 KB/s of
short-lived garbage, and when the collector decides to run, you eat a
2–10 ms hitch. **The hitch is the cost, not the bytes.**

The four allocation patterns that show up over and over:

```csharp
// 1. LINQ in hot paths.
foreach (var b in blocks.Where(b => b.IsCpu))            // ❌ iterator + closure
for (int i = 0; i < blocks.Count; i++) if (blocks[i].IsCpu) // ✅

// 2. String formatting in Update / FixedUpdate / OnGUI.
GUI.Label(rect, $"FPS: {fps}");                         // ❌ allocates per call
                                                          // ✅ rebuild label only when value changes
                                                          //    (see FpsCounter.cs)

// 3. new List<T> / new T[] inside callbacks.
void OnHit() { var hits = new List<RaycastHit>(); ... } // ❌
                                                          // ✅ static buffer or UnityEngine.Pool.ListPool<T>

// 4. Lambdas that capture locals.
button.onClick.AddListener(() => Save(name));            // ❌ closure
button.onClick.AddListener(HandleSaveClick);             // ✅ method group
```

Less obvious: **`new GUIStyle(GUI.skin.label)` allocates** (and OnGUI
runs **multiple times per frame** — once per IMGUI event). Cache styles
in fields, not local variables. **`new Rect(...)` is a struct** and is
not a GC alloc, but `new Color(...)` inside a tight loop still bloats
the per-vertex working set.

`foreach` on `List<T>` is fine in Unity 6 — IL2CPP optimises it to a
struct enumerator. **`foreach` on `Dictionary<K,V>` still allocates an
enumerator object** — keep a parallel `List<K>` for hot iteration if
you need both lookup and traversal.

### 2.2 Profile before you optimise — and never after

The optimisations that "obviously" work often regress something else.
The optimisations that "obviously won't matter" sometimes save 2 ms.
The single highest-leverage habit is "before I make this change, what
does the profiler say the cost is right now?" — both as a baseline
and as a sanity check that you're optimising the right thing.

A `ProfilerMarker.Auto` block costs ~50 ns when the profiler isn't
attached. Add them to anything that ticks every frame or every fixed
step. The project ships [`PerfMarkers.cs`](../Assets/_Project/Scripts/Core/PerfMarkers.cs)
with pre-allocated markers for the known hot paths; use them rather
than rolling your own per file.

### 2.3 The chassis Rigidbody count is sacred

The single architectural rule that protects the whole-game frame
budget is **one Rigidbody per chassis, compound colliders, free-body
debris parented under scene root** (BEST_PRACTICES § 3.1, PHYSICS_PLAN
§ 1). Active Rigidbodies scale **superlinearly** in the contact solver.
At 16 robots × 1 chassis Rigidbody = 16. At 16 robots × ~150 cubes ×
1 Rigidbody-per-cube = 2,400 — a different game.

Anything that adds a Rigidbody — rotor hubs, rope tip bodies, debris,
new physics blocks — must:

1. Have a **zero-baseline** path (BEST_PRACTICES § 16, PHYSICS_PLAN § 1).
   `RotorBlock` with 0 ropes adopted = 0 extra Rigidbodies.
2. Be **counted** at typical loadouts. The active-Rigidbody alarm in
   the perf HUD trips at 64; cliff at 256.

### 2.4 No `Camera.main` in `Update` / `LateUpdate` / `FixedUpdate`

`Camera.main` does a tag search across the scene every call. Cache it
once in `Awake`/`OnEnable`, refresh on scene-load if you need to.
**`FindObjectOfType` and family are even worse** — they walk the whole
hierarchy. They belong in `Awake`, never in a per-frame call.

### 2.5 No `GetComponent` in hot paths — except cached ones

`GetComponent<T>` is a linear walk over the GameObject's component
list. Cheap once, expensive at 60 Hz. The pattern is:

```csharp
// ❌
private void FixedUpdate() { var rb = GetComponent<Rigidbody>(); rb.AddForce(...); }

// ✅
private Rigidbody _rb;
private void Awake() { _rb = GetComponent<Rigidbody>(); }
private void FixedUpdate() { _rb.AddForce(...); }
```

`GetComponentInParent<T>` walks ancestors too — same rule. The exception
is when the parent can change at runtime (e.g. a foil reparented under
a kinematic rotor hub); in that case cache lazily and invalidate on
the reparent event.

---

## 3. Profiling workflow

### 3.1 The four profiler panels you actually use

| Panel | Use it for | First thing to look at |
|---|---|---|
| **CPU Usage** | "where did the frame go?" | Right-click the spike → "Show in Hierarchy" |
| **Memory** (the package) | "where is GC coming from?" | Allocation calltree at the steady-state row |
| **Frame Debugger** | "why so many draw calls?" | Group by SRP-batch / by shader |
| **Physics** | "why so many contacts?" | Active Rigidbodies + Solver iteration count |

### 3.2 Capture in a build

```
Build → Development Build ✓, Autoconnect Profiler ✓ → run → attach.
```

The editor profiler is fine for *delta* measurements ("did this change
make it faster?") but absolute numbers will be misleading.

### 3.3 The minimum measurement

For any change to a per-frame system, capture before and after:

- `Time.unscaledDeltaTime` ms
- `Profiler.GetTotalAllocatedMemoryLong()` delta over 60 frames
- `Physics.activeRigidbodyCount` (steady state)
- `UnityStats.drawCalls` (Editor only) or the Frame Debugger's count

The perf HUD (§ 4.1) shows these live for free.

### 3.4 Stress scenarios to use, not just "the default arena"

The default arena is comfortable. Real performance work happens at
the edge of the budget. The project ships a few:

- **Rotor stress tower** — settings → Stress → "Spawn Rotor Tower".
  5 rotors × 4 ropes × 4 segs at 600 RPM. PHYSICS_PLAN § 4.
- **Spawn N combat dummies** — DevHud → Rebuild Combat Dummy several
  times to stack chassis count. (Currently a manual stack — the perf
  pass left the multi-spawn stress flow as a TODO.)
- **WaterArena vs Arena** — WaterArena adds the per-vertex water
  surface CPU path; baseline against Arena to isolate water cost.
- **Build-mode rapid placement** — paint blocks fast, watch for
  RecalculateAggregates churn (Robot.cs).

A perf change is not done until it's measured under at least one
stress scenario, not just the default arena.

---

## 4. Diagnostic tools shipped with the project

### 4.1 Performance HUD (F3)

[`PerformanceHud.cs`](../Assets/_Project/Scripts/UI/PerformanceHud.cs)
— always-running, hidden by default, toggle with **F3**. Shows live:

- **Frame time (ms)** plus 1% / 0.1% lows over a rolling 240-sample
  window. The lows are the number that actually matters for feel —
  the "headline FPS" hides hitches.
- **Active Rigidbodies / Joints / Verlet chains / Verlet particles**
  — sampled once per second.
- **GC alloc bytes/frame** delta — the headline allocations number.
  Target: 0 in steady state.
- **Robot count + total block count** — the input to the chassis
  Rigidbody-per-chassis budget.
- **Draw call / SetPass / Triangle counts** in the editor (the build
  player doesn't expose these in `UnityStats`).

The HUD's own cost is profiled at < 0.05 ms / frame; safe to leave on
during development. It auto-bootstraps with `RuntimeInitializeOnLoad`
(same pattern as `FpsCounter`) so it's available in every scene with
zero scene authoring.

### 4.2 Profiler markers

[`PerfMarkers.cs`](../Assets/_Project/Scripts/Core/PerfMarkers.cs)
exposes pre-allocated `ProfilerMarker` instances for the known hot
paths. Use the existing markers when adding work in a tagged area
rather than allocating fresh ones:

```csharp
using (PerfMarkers.RobotRecalcAggregates.Auto())
{
    // your code
}
```

Markers are no-ops with the profiler detached, so cost is ~50 ns per
`Auto()` scope. Adding new markers is cheap; adding *too many* markers
fragments the trace and makes it harder to read — favour module-level
markers (one per significant subsystem entry point) over fine-grained
sub-section markers.

### 4.3 Editor menu

`Robogame > Perf` (in [`PerformanceMenu.cs`](../Assets/_Project/Scripts/Tools/Editor/PerformanceMenu.cs)):

- **Toggle Perf HUD** — toggles the in-scene HUD without needing to
  click the game window first.
- **Log Render Stats** — dumps draw calls, set-pass, triangle, and
  shadow casters via `UnityStats` to the console.
- **Toggle V-Sync** — flips `QualitySettings.vSyncCount` between 0
  and 1 so you can see uncapped fps without changing project settings.
- **Capture Frame** — opens the Profiler with a single-frame deep
  capture (slow, but the most accurate single-frame breakdown
  available short of native tooling).

---

## 5. System-by-system perf notes

### 5.1 Verlet rope simulator

[`VerletRopeSimulator.cs`](../Assets/_Project/Scripts/Movement/VerletRopeSimulator.cs)
is the one piece of project-authored physics. Cost is roughly:

```
chains × subSteps × (N integrate + iterations × (N-1) distance + (N-2) bending) constraint ops
```

At default config (8 segments, 4 sub-steps, 8 iterations, 1 chain) =
~32 × 14 = ~450 vector ops per chain per FixedUpdate. The stress tower
(20 chains × 8 segs) lands around 9k ops/step — single-digit µs.

Rules:

- **No `GetComponent` in the per-step loop.** The simulator caches a
  flag on `VerletRopeChain.PinTip`; whoever attaches a joint to the
  tip Rigidbody (`HookBlock.Attach` / `HookBlock.Release`) is
  responsible for pushing the flag, not the simulator.
- **Pre-size the particle array** at chain construction. Don't
  reallocate on length change; rebuild the chain.
- **No `OnPostSolve` allocations.** The owning `RopeBlock` writes
  cylinder transforms in-place from a pre-sized buffer.

### 5.2 Water surface

[`WaterMeshAnimator.cs`](../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)
is the most expensive per-frame piece in the WaterArena. At 64×64
tessellation = 4,225 verts × 3 sin/cos per vert + optional
`RecalculateNormals`. The recompute is roughly **25%** of the per-
frame cost; the wake-foam dual loop is bounded by `bouys.Count` ×
`SurfaceContacts.Count` × verts and is currently the second-largest
cost.

Future moves (**do not preemptively land these** — measure first):

- **Analytic normals** via `WaterSurface.SampleNormal`. Replaces
  `Mesh.RecalculateNormals` with a single derivative sample per vert.
  Halves the per-frame cost; matches the buoyancy code so the visual
  and the physics agree at sub-pixel level. **The single biggest
  single-line win available on the water path.**
- **Spatial-hash the buoyancy contacts** so the wake loop is `O(verts × ~3)`
  instead of `O(verts × contacts)`. Today's contact count is small
  enough that this isn't a win; will be when MP arrives.
- **Skip `RecalculateNormals`** when amplitude is effectively zero
  (already implemented as of session 27).
- **Skip the entire vertex loop** when no chassis is within `_size/2 +
  margin` of the surface. We always pay 4k verts of trig today even
  in an empty arena.

### 5.3 Fluff grass — the dominant GPU cost (and how to diagnose it)

The arena's stylised grass is OccaSoftware's **Fluff** package
([`Packages/com.occasoftware.fluff/`](../Packages/com.occasoftware.fluff/)),
a shell-based geometry-shader grass system. The look is great; the
defaults are *not* tuned for our framing, and the package's perf
profile fights URP's depth/normals pipeline in a non-obvious way.

This is the single biggest GPU cost in the game today. A symptom you
should learn to recognise: **fps climbs sharply when the camera is
high enough that the grass mesh culls or hits its `_MaximumDistance`
fade**, then drops by hundreds of fps as the camera descends and more
grass triangles emit shells. That's the geometry shader output ramping.

**How the shader spends a frame:**

```
For each triangle of the input ground mesh:
  Vertex stage         — runs once per vert.
  Geometry stage       — emits up to:
                            3 base verts (always)
                          + 3 × _ShellCount  verts (if d < _MaximumDistance)
                          + 3 × FIN_COUNT(6) verts (if d < _FadeStartDistance)
                         = up to 69 output verts per input tri.
  Fragment stage       — once per shaded pixel of every emitted tri.
```

`d` is `saturate(remap(_FadeStartDistance, _MaximumDistance, 0, 1, distToCam))`.
So:

| Distance to camera | What the geom shader emits |
|---|---|
| `< _FadeStartDistance` | base + all shells + all fins (max cost) |
| `_FadeStartDistance` ↔ `_MaximumDistance` | base + all shells (no fins) |
| `> _MaximumDistance` | base mesh only (cheap) |

**The duplicated-pass trap:** the Fluff shader declares both a
`ForwardLit` pass and a `DepthNormalsOnly` pass. If URP's
`ScreenSpaceAmbientOcclusion` renderer feature is configured with
`Source: DepthNormals` (the URP default), URP will run a *second*
geometry-shader pass over the entire grass field every frame — for an
SSAO that probably only contributes 2–3% of the perceived lighting.
Switching to `Source: Depth` (with depth-only normal reconstruction)
eliminates the second pass entirely.

**Verified hot-path cost** for our default arena (220×220 m hills,
res 121 → ~28,800 input tris, `_ShellCount: 16`, `_FadeStartDistance:
150`, `_MaximumDistance: 220`, SSAO Source: DepthNormals):

```
≈ 28,800 input tris × 22 output tris × 2 passes ≈ 1.27 M tris/frame
```

That's grass *alone*, before you've drawn a single block.

**Tuning knobs, in priority order (highest perf gain first):**

1. **URP SSAO `Source: Depth` (not DepthNormals).** Biggest win. Halves
   total grass shader work. Set in [`PC_Renderer.asset`](../Assets/Settings/PC_Renderer.asset)
   under the `ScreenSpaceAmbientOcclusion` component, `m_Settings.Source`.
   `0` = Depth, `1` = DepthNormals. **0 is what you want here.**

2. **`_ShellCount` on `Mat_ArenaFluff` (range 4–16).** Linear in
   geometry-shader output; 8 shells looks nearly identical to 16 at
   our framing because shape/detail noise + colour gradient do most
   of the visible "fluffy" work. Below 6 the canopy starts thinning.

3. **`_FadeStartDistance` and `_MaximumDistance`.** Don't think of
   these as "how far I can see grass." Think of them as "the radius
   in which I pay 22× geometry shader output." `_FadeStartDistance =
   25` means only the closest 25 m emit fins; `_MaximumDistance =
   100` means anything past 100 m is base mesh only. The player
   camera is ~5–10 m off the ground; you do not want a 220 m fade
   radius unless you're targeting a flight sim.

4. **`HillsSettings.resolution` ([HillsSettings.asset](../Assets/_Project/ScriptableObjects/HillsSettings.asset)).**
   Linear in input triangle count, which the geometry shader
   multiplies by 22. Going `121 → 81` cuts input tris by ~55% (n²)
   and barely affects the silhouette of the gentle Perlin hills.
   **Requires a rebake** (`HillsSettings` inspector → "Rebake hills
   mesh", or re-run `Robogame > Scaffold > Build All Pass A`).

5. **`_FinsEnabled = 0`** disables fin emission entirely. Saves 6
   layers per close triangle. Slight quality cost (silhouette reads
   thinner from horizontal angles); we keep it on for now and rely
   on a tight `_FadeStartDistance` to bound the cost. Flip to 0 if
   profiling still shows grass is the bottleneck.

6. **`mr.shadowCastingMode = ShadowCastingMode.Off` on the ground**
   (already set in [`FluffGround.cs:94`](../Assets/_Project/Scripts/Tools/Editor/FluffGround.cs:94)).
   Without this, the grass would render through the `ShadowCaster`
   pass *for every cascade* (×4 with current PC_RPAsset cascade
   count). Don't ever remove this.

7. **Light count.** `_LIGHT_LAYERS` and `_ADDITIONAL_LIGHTS` shader
   variants compile into the grass shader; every additional realtime
   light samples per shell-fragment. Keep additional realtime lights
   on the grass at zero. The directional sun is plenty.

The numbers shipped today (session 27.5): `_ShellCount: 8`,
`_FadeStartDistance: 25`, `_MaximumDistance: 100`, hills resolution
81, SSAO Source: Depth. Combined headline: ground-cost roughly **1.27 M
post-geom tris → ~210 k**, a ~6× reduction. If your build still
isn't hitting target framerate after these, profile before changing
more — there's no single big lever left on grass.

### 5.4 Rendering — toon outlines + SRP batcher

The MK Toon outline pass (`PC_Renderer.asset`) adds a silhouette pass
over every renderer marked with the outline material. With ~20 chassis
blocks × 16 chassis at MP scale = ~320 silhouette draws on top of
~320 base draws. **Outlines are a known 16-player-arena risk.**

Mitigations available (not landed):

- **Bake outlines into the base shader** (cheap distance-fade outline
  via vertex normal extrusion in the same draw call) — eliminates the
  per-object outline pass.
- **Layer-mask the outline pass** to only the player's own chassis or
  the targeted enemy.
- **Switch outline materials only on locally-relevant blocks** at MP
  load time (server pushes a "this is the enemy" hint).

### 5.5 Buoyancy

[`BuoyancyController.cs`](../Assets/_Project/Scripts/Gameplay/BuoyancyController.cs)
is `O(blocks per chassis)` per FixedUpdate. ~30 blocks × 1 chassis ×
50 Hz = 1,500 height samples / sec — comfortable. At 16 chassis × 30
blocks = 24,000 / sec — still comfortable, but the multiplicative
factor with the rest of the physics frame is what bites.

Predicted MP-scale move: **sample at chassis-COM-plus-3-points instead
of every block** for distant chassis (LOD on physics, not just
graphics). The water displacement only has to look right *for chassis
near the local camera*; servers can run cheap volume-overlap math for
the rest.

### 5.6 Block grid mutations

[`Robot.RecalculateAggregates`](../Assets/_Project/Scripts/Robot/Robot.cs)
runs an `O(blocks)` pass to recompute mass, COM, and the diagonal
inertia tensor. Today's flow:

- `BlockPlaced` event → recompute. Build-mode rapid placement bursts
  this once per click (fine — humans place ~3 blocks/sec at peak).
- `BlockRemoving` event → manual O(1) deduction (no full recompute).
  The connectivity flood-fill at end-of-frame triggers exactly one
  recompute regardless of how many blocks fell off.

Future failure mode: a sustained-fire weapon destroying ~10 blocks/sec
× 16 chassis = 160 events/sec. Even at O(150 blocks) each, that's
24,000 vector multiplies/sec — not a frame killer, but a CPU-cache
churn risk. The fix when it lands is **debounce inside the connectivity
coroutine** — one recompute per frame regardless of removal count.
That's the existing pattern in `RunConnectivityNextFrame`; widen the
window to "all damage events from the last N ms" rather than "last
frame".

### 5.7 Aim / camera raycasts

Five distinct raycasts run per local-player frame:

- `AimReticle` — RaycastNonAlloc for "is enemy under crosshair" (every
  Update).
- `RobotDrive.ComputeAimPoint` — RaycastNonAlloc for camera-cursor aim
  (every FixedUpdate via `ApplyMovement`).
- `WeaponMount.ComputeFallbackAim` — RaycastNonAlloc fallback when no
  drive present (every LateUpdate).
- `WheelBlock.FixedUpdate` — RaycastNonAlloc per wheel for ground
  contact (per wheel per FixedUpdate).
- `FollowCamera.ResolveCameraPosition` — SphereCast for obstacle
  avoidance (every LateUpdate).

Total at 144 Hz with 4 wheels: 144 × 3 + 50 × (1 + 4) = ~700 raycasts/sec.
Cheap individually, but a known **MP-scale 16× multiplier** that's
worth thinking about. The single highest-value consolidation: have a
`ChassisAim` component own the camera cursor cast once and publish
the result, rather than three independent consumers each casting.

### 5.8 IMGUI overhead

OnGUI runs **multiple times per frame** (once per IMGUI event:
Repaint, Layout, MouseMove, etc.). Anything you allocate in OnGUI is
allocated 2–6× per displayed frame. Three components currently use
OnGUI:

- [`FpsCounter.cs`](../Assets/_Project/Scripts/UI/FpsCounter.cs) —
  cached label string + cached GUIStyle. ✅ allocation-free hot path.
- [`PerformanceHud.cs`](../Assets/_Project/Scripts/UI/PerformanceHud.cs) —
  cached styles + cached label strings. ✅
- [`AimReticle.cs`](../Assets/_Project/Scripts/Player/AimReticle.cs) —
  4× DrawBar + 1× dot. Allocation-free now (Rect is a struct, GUI.color
  set/restore is free). ✅
- [`DevHud.cs`](../Assets/_Project/Scripts/UI/DevHud.cs) — runs only
  when toggled visible (F1). When hidden, costs one bool check. When
  visible, GUIStyle is cached. ✅

**New IMGUI components are forbidden without a documented
allocation-free hot path.** Prefer UGUI for any UI that's always-on.

---

## 6. Rendering / URP knobs

The PC URP pipeline lives in [`PC_RPAsset.asset`](../Assets/Settings/PC_RPAsset.asset)
and [`PC_Renderer.asset`](../Assets/Settings/PC_Renderer.asset). The
ones with the highest cost-to-quality ratio:

| Knob | Current | Cost | Note |
|---|---|---|---|
| `m_MSAA` | **2x** *(was 4x; reduced session 27)* | 4x oversampling = ~2× pixel cost vs 2x | 2x is the URP-arcade default. Visible difference past 2x is marginal at 1080p. |
| SSAO `Downsample` | **half-res** *(was full-res; reduced session 27)* | full-res SSAO ≈ 4× cost of half | URP recommends half-res for arcade titles. Difference is invisible past ~1m. |
| SSAO `Source` | **Depth (0)** *(was DepthNormals (1); changed session 27.5)* | DepthNormals forces a full extra geometry-shader pass over every renderer | The largest single rendering win the project has had. Switching to Depth-source eliminates the duplicated grass-shader pass entirely (see § 5.3). Keep at 0 unless SSAO normals are visibly wrong on flat-shaded toon surfaces, which they aren't here. |
| `m_RequireOpaqueTexture` | 1 | One opaque blit per frame | Needed only by shaders sampling `_CameraOpaqueTexture` (the Bitgem water shader may; verify before flipping off). |
| `m_RequireDepthTexture` | 1 | One depth blit | Required by SSAO. Don't disable while SSAO is on. |
| `m_MainLightShadowmapResolution` | 2048 | Linear in atlas pixels | Dropping to 1024 halves shadow memory; soft-shadows visibly degrade. Leave at 2048 for now. |
| `m_ShadowCascadeCount` | 4 | One culling pass per cascade | 2 cascades is the URP-arcade default; 4 helps 1km+ view distances which we don't have. |
| `m_ShadowDistance` | 50 | Cascades scale to fit | 50m is fine. Do not push past 100 without a 16-chassis profile capture. |

**Per-object outlines** (`MKToonPerObjectOutlines` renderer feature) is
the single most visible "cool-looking" feature and the single most
expensive feature at MP scale. See § 5.4.

---

## 7. Performance budgets (extended)

This extends [`BEST_PRACTICES.md` § 16](BEST_PRACTICES.md#16-performance-budgets-targets-not-law)
with renderer- and netcode-side numbers.

### Single-player, 1080p, target machine (GTX 1660, 6-core CPU)

| Metric | Target | Cliff | Where to look |
|---|---|---|---|
| Frame time | 6.94 ms (144) | 16.6 ms (60) | PerfHud / Profiler CPU |
| CPU main thread | < 4 ms | 8 ms | Profiler CPU > Hierarchy |
| Render thread | < 2 ms | 5 ms | Profiler GPU |
| GC alloc / frame | 0 B | any in `Update` | PerfHud |
| Active Rigidbodies | < 32 | 64 (warning), 256 (cliff) | PerfHud |
| Active Joints | < 16 | 64 | PerfHud |
| Verlet particles | < 64 | 256 | PerfHud |
| Draw calls | < 800 | 2,000 | Frame Debugger |
| SetPass calls | < 100 | 250 | Frame Debugger |
| Triangles | < 1.5 M | 3.0 M | Stats overlay |

### 16-player MP arena (later target)

| Metric | Target | Cliff | Note |
|---|---|---|---|
| Server tick budget | 16 ms (60 Hz) | 33 ms (30 Hz) | Headless build only |
| Client frame time | 16.6 ms (60) | 33 ms (30) | Lower bar than SP — the network masks some hitches |
| Bandwidth / client | < 64 kbps | 256 kbps | NETCODE_PLAN |
| Active Rigidbodies (server) | < 64 | 256 | One per chassis + debris |
| Verlet particles (server) | < 1,024 | 4,096 | 16 × 4 ropes × 8 segs |

The general rule: **single-player budgets multiplied by 16 ≠ MP
budgets**. The chassis Rigidbody count multiplies linearly; the
contact count multiplies *roughly quadratically*; the rendering count
multiplies linearly with culling but blows up SetPass calls if every
chassis has a different material set. Stress-test at **2 chassis**
before MP lands; the curve from 1 → 2 is more informative than the
curve from 0 → 1.

---

## 8. Predicted future hotspots

These haven't fired yet, but the codebase has the shape that says
"this is where I'd look first." Every prediction has a trigger so we
notice when the prediction comes true.

### 8.1 Per-block damage replication blowup

**Trigger:** sustained-fire weapon × 16 chassis × ~150 blocks each.
Naive replication = **up to 200 messages/sec/chassis × 16 = 3200
messages/sec**. Cliff at ~1500 messages / sec at 64 kbps.

**Mitigation already documented** in BEST_PRACTICES § 15.10 (batch
into per-tick deltas, quantise HP to a byte). Nothing to do today;
flag in the netcode PR.

### 8.2 Outline pass at MP scale

**Trigger:** 16 chassis × ~20 outline-rendered blocks × stencil
silhouette pass = ~320 extra draws *with stencil writes*. SetPass-
bound is real on URP. Cliff at ~250 SetPass / frame.

**Pre-emptive option**: bake outlines into the base shader with a
vertex-normal-extrusion pass in the same material. Keep the toon
material count down to 1–2; never per-block. Documented § 5.4.

### 8.3 Verlet rope tip Rigidbody count

**Trigger:** 16 chassis × max-allowed ropes per chassis × 1 tip
Rigidbody each. At 4 ropes per chassis = 64 tip Rigidbodies. The
chains are not Rigidbodies, but each tip is. Cliff: alarm fires at
the active-Rb HUD threshold (64).

**Mitigation:** rope count per chassis is already a blueprint-level
opt-in (RotorBlock with 0 ropes adopted = 0 tip Rbs). Current
trajectory is fine; just don't let "default = 4" sneak into the
default chassis blueprints.

### 8.4 Damage-VFX storm

**Trigger:** continuous-fire SMG × 16 chassis × ~20 hit sparks/sec
each = 320 instantiated VFX/sec. Currently `CombatVfxLibrary.Load`
spawns per-hit; uncapped this is a known stutter source.

**Mitigation:** pool the hit-spark prefab via `UnityEngine.Pool.ObjectPool<T>`.
Hard-cap concurrent active sparks at 32. Documented BEST_PRACTICES § 8.

### 8.5 ChassisFactory.Build re-entry on rebuild storm

**Trigger:** "the player CPU is dead, respawn in 2 s" on 16 chassis
simultaneously after a single bomb. ChassisFactory does a 50–200 ms
build per chassis on the main thread. 16 × 100 ms = 1.6 s of frame
hitches.

**Mitigation:** stagger respawn (already a Robocraft idiom — players
respawn one at a time). At minimum, distribute the work over a few
frames using Awaitable. **Don't try to multithread `AddComponent`;
Unity's API is main-thread only.**

### 8.6 Block-grid mutation under sustained fire (RecalculateAggregates)

**Trigger:** see § 5.6. Currently a non-issue; explicitly called out
to make it a non-issue forever.

### 8.7 Foam/wake foam loop nested O(verts × contacts)

**Trigger:** > 8 chassis in WaterArena with > 50 surface contacts each.
4,225 verts × 50 contacts × 8 chassis = 1.7M operations / Update.

**Mitigation when it fires:** spatial-hash the contacts on a 4m grid;
test only the cell + 8 neighbours per vert. Already noted § 5.2.

### 8.8 SmoothDamp / FollowCamera cache invalidation on respawn

**Trigger:** every player death triggers `EnsureChassisRendererCache`
to walk the new chassis. ~150 blocks × 1–2 renderers = ~250 component
lookups per respawn. Not a frame killer; cumulative if respawns are
frequent.

**Mitigation:** the rebuild path already invalidates on target
change. Don't make it more aggressive than that.

### 8.9 Tweakables.Get in tight inner loops

`Tweakables.Get(string)` is a `Dictionary.TryGetValue` — O(1) but
with a string-hash on every call. **Calling it inside a per-vertex or
per-block loop wastes cycles.** Always read once per frame at the
loop entry; cache to a local. WaterMeshAnimator is the existing
correct pattern (line 124 in `Update`).

### 8.10 ProjectileGun fire-rate gating uses Time.time

`Time.time` is a local clock. In MP, projectile spawning needs a
server-canonical tick. Today's gate works; under netcode, two clients
firing the same gun from the same input get different spawn cadences
unless the server assigns the timestamp. **Documented in
PHYSICS_PLAN; not a perf bug today, will become a desync bug under MP.**

---

## 9. Runbook: "the game feels slow"

Step-by-step. Resist the urge to skip ahead.

1. **Open the perf HUD (F3).** What's the headline number?
   - 1% low > 16ms? → frame-time issue, probably CPU.
   - 1% low fine, 0.1% low spiky? → GC hitch, probably allocations.
   - Headline FPS fine but feels stuttery? → physics step exceeding
     fixed-step budget; `Physics.Simulate` in profiler.

2. **GC alloc/frame in HUD > 0?** Open the Memory profiler, find the
   biggest per-frame allocator. The four patterns from § 2.1 catch
   nearly all of them.

3. **Active Rigidbodies > 64?** Find the source — usually a regression
   in PHYSICS_PLAN § 1 ("default to zero baseline cost"). Profile
   shows which chassis spawned them.

4. **Draw calls > 1500?** Open the Frame Debugger. Group by SRP-batch.
   The fix is almost always *fewer materials*, not fewer objects.

5. **Profiler CPU > 8 ms in `PlayerLoop`?** Drill into Hierarchy mode,
   find the worst function. Compare against `PerfMarkers` to see if
   it's a tagged subsystem or a new stranger.

6. **Nothing obvious?** Capture a 60-frame Memory profile and look
   for sawtooth allocations — that's the classic "GC every 0.5s"
   signature. The amount per frame may be small but the *ratio* of
   cleanup-to-work is what produces the hitch.

7. **Still nothing?** Run a `Robogame > Perf > Capture Frame` deep
   capture. Slow but exhaustive.

If you change something to fix it: document the before/after numbers
in the session changelog, including the stress scenario you measured
under. "I made it faster" with no numbers is unfalsifiable.

---

*This file is a living document. When a rule changes, update it
here in the same PR that breaks the rule. Numbers go stale; the
architectural shape doesn't.*
