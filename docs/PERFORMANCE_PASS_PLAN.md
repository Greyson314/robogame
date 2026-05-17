# Robogame — Performance Pass Plan

> **Audience.** A Claude Code agent (or human collaborator) picking
> this up cold. Read [`PERFORMANCE.md`](PERFORMANCE.md) first — the
> rules, knobs, and "predicted future hotspots" list. This document
> is the *workflow* for the current pass: measure, triage, fix,
> verify, stop.
>
> **Origin.** User reported that perf feels like it's dwindling with
> every new feature, but the game isn't fundamentally perf-intensive.
> That intuition is plausibly correct. The fix is methodology, not a
> sweeping refactor.
>
> **Last applied.** Session 84
> ([`docs/changes/84-perf-pass-1.md`](changes/84-perf-pass-1.md)) —
> built the idle baseline harness, triaged every Phase-2 suspect
> statically, fixed two real OnGUI GC sources (ObjectiveHud,
> ScrapCarriedIndicator). Numbers pending a test run (editor lock
> blocked autonomous capture); Phase-5 big rocks deferred as
> measurement-gated. See [`PERFORMANCE_BASELINES.md`](PERFORMANCE_BASELINES.md).

---

## Table of contents

1. [The honest picture](#1-the-honest-picture)
2. [Why this happens (feature accretion, not one bottleneck)](#2-why-this-happens)
3. [Phase 0 — Establish baselines (do this before touching anything)](#3-phase-0--establish-baselines)
4. [Phase 1 — Track the trend over time](#4-phase-1--track-the-trend-over-time)
5. [Phase 2 — Likely suspects given recent work](#5-phase-2--likely-suspects-given-recent-work)
6. [Phase 3 — Triage matrix](#6-phase-3--triage-matrix)
7. [Phase 4 — Quick wins worth attempting after measurement](#7-phase-4--quick-wins)
8. [Phase 5 — Big rocks: don't touch without numbers](#8-phase-5--big-rocks-dont-touch-without-numbers)
9. [Success criteria and exit gate](#9-success-criteria-and-exit-gate)
10. [Agent handoff checklist](#10-agent-handoff-checklist)

---

## 1. The honest picture

The project's perf framework is already in good shape. [`PERFORMANCE.md`](PERFORMANCE.md)
documents the rules, the runtime perf HUD (F3) reports frame time /
GC / Rb count / draw calls live, [`PerfMarkers.cs`](../Assets/_Project/Scripts/Core/PerfMarkers.cs)
tags the known hot paths, and `Robogame > Perf` in the editor menu
bridges to the Profiler. Session 27 (the original perf pass) did the
once-over; session 83 added a follow-up pass on the dig system that
recovered idle FPS by killing 36 shadow casters and culling undug
chunk renderers.

What's missing is **a recurring baseline-and-compare habit**. Every
session adds features; no session captures a perf number the next
session can diff against. So the user's perception that "it feels
slower" is unfalsifiable today — there's no record of what "before"
was. That's the first thing to fix.

The realistic scenario is this: the game isn't a single 16ms
bottleneck. It's 30–40 systems each costing 50–200 µs that have
slowly compounded into a 4–6 ms baseline. None of them are wrong;
collectively they look like decay. The pass should treat the **trend
line** as the deliverable, not a one-shot 2× win.

---

## 2. Why this happens

A non-exhaustive list of systems that tick *every frame* in the
default Arena scene, all of which arrived during the past 30
sessions (not present in the session-27 baseline):

- **`DigZone.Update`** + 36 child `DigChunk` poll-and-bake checks
  (session 83).
- **`MatchController`** ticked from `ArenaController.Update`
  (session 28).
- **`ProjectileWorld`** custom-stepped integrator, runs every frame
  even with zero live projectiles (session 32).
- **`VfxSpawner`** pooled procedural particle dispatcher (session 29).
- **`AudioRouter`** Tweakables-driven mix routing (session 30).
- **`ScrapCarriedIndicator`**, `KillAnnouncer`, `HitMarkerOverlay`,
  `FloatingDamageOverlay`, `ObjectiveHud`, `StartMatchHud`,
  `VehicleStatsHud` — each one is small, all are always-on (sessions
  28–58).
- **`VoxelChaserBot`** in the dig zone, `A*` and `OccupancyGrid`
  consumers (session 79).
- **`ScrapDepot`** AOE check, `ScrapPickup` magnetic pull (sessions
  35, 58).
- **`ChassisWindAudio`** per-chassis (session 30 follow-up).

`grep "void Update\("` returns 51 distinct script files. Many of
those are MonoBehaviours that Unity will dispatch even if the body
early-returns — Unity pays a per-MonoBehaviour scripting overhead
just to call into managed code. Cumulative cost is real even if no
single one shows up in a flat hierarchy view.

Add to this the GPU-side accretion: the Fluff grass mesh, the voxel
dig surface (now triplanar URP dirt shader, 36 renderers), the toon
outline pass on every chassis block, optional water, optional
spherical-arena gravity, ropes-as-PhysX-joint-chains today (Verlet
migration is tech debt). Three separate "ground" systems — the
heightmap-baked Fluff host mesh, the voxel chunks, and the grass
shader itself — coexist over the same square footprint.

None of these are wrong. The point is that a 200 fps → 120 fps
delta over six months is what you'd predict from this trajectory
even with perfect per-feature hygiene. **The cost is the surface
area, not a bug.**

---

## 3. Phase 0 — Establish baselines

This is the most important phase. Skip it and every later
recommendation becomes a guess.

### 3.1 Build, not editor

Do **all** measurement in a **Development Build**, not in the
editor. [`PERFORMANCE.md` § 3.2](PERFORMANCE.md#32-capture-in-a-build)
notes that editor numbers are 5–10× off some metrics because the
editor itself ticks. Editor profiling is useful for *delta*
measurements ("did this change help?"), not absolute claims.

Build settings: Player Settings → enable Development Build,
Autoconnect Profiler, Deep Profiling Support **off** (it adds
overhead that distorts the trace).

### 3.2 The four scenes to baseline

| Scene | Why baseline | Notes |
|---|---|---|
| **Garage** | Quietest possible scene — chassis + dropdown + camera, no combat, no terrain. | Floor for everything else. If Garage is slow, the rest can't be fast. |
| **Arena** | The full default loadout: hills + Fluff + dig zone + match controller + bots. | This is the scene the user actually plays in. |
| **WaterArena** | Adds `WaterMeshAnimator` + buoyancy. Isolates water cost vs base Arena. | If WaterArena ≪ Arena, water is fine. |
| **PlanetArena** | Adds spherical gravity. Less-played scene; baseline once. | Don't over-invest until the user actually flies it. |

### 3.3 What to capture, per scene

Capture three steady-state snapshots per scene: **idle** (camera
stationary, nothing happening), **active** (player flying around,
no combat), **combat** (one bot engagement, weapons firing).

From the F3 perf HUD:

- Smoothed frame time (ms) + fps.
- Avg / 1% / 0.1% low.
- GC alloc / frame. Anything > 0 in steady state is a regression to
  chase.
- Active Rigidbody count.
- Active Joint count.
- Verlet chain count + particle count.
- Robot count + total block count.
- Draw calls / SetPass / Triangles (editor only — but a build-time
  approximation is fine for trend-tracking).

From a Profiler attach (CPU panel, Hierarchy mode, 60-frame
average): top 10 inclusive ms entries. Save the
`Robogame.*`-prefixed marker times specifically — those are the
ones whose costs the codebase is supposed to understand.

### 3.4 Hardware envelope

Note the machine. CPU model, GPU, RAM, resolution, V-Sync state.
Without this the numbers can't be compared across sessions. A
template line: `2026-05-17 / GTX 1660 / Ryzen 5 3600 / 32 GB / 1080p / V-Sync off`.

### 3.5 Estimated time for Phase 0

Two to three hours, all hands. Resist the urge to fix anything yet.

---

## 4. Phase 1 — Track the trend over time

Create [`docs/PERFORMANCE_BASELINES.md`](PERFORMANCE_BASELINES.md)
(new file) with one row per measurement event. Format:

```markdown
## 2026-05-17 — session 84 baseline

Machine: GTX 1660 / Ryzen 5 3600 / 32 GB / 1080p / V-Sync off

| Scene       | State  | fps | 1% low ms | 0.1% low ms | GC/f | Active Rb | Draw | SetPass | Tris  |
|-------------|--------|-----|-----------|-------------|------|-----------|------|---------|-------|
| Garage      | idle   | …   | …         | …           | 0    | …         | …    | …       | …     |
| Arena       | idle   | …   | …         | …           | …    | …         | …    | …       | …     |
| Arena       | active | …   | …         | …           | …    | …         | …    | …       | …     |
| Arena       | combat | …   | …         | …           | …    | …         | …    | …       | …     |
| WaterArena  | active | …   | …         | …           | …    | …         | …    | …       | …     |
| PlanetArena | active | …   | …         | …           | …    | …         | …    | …       | …     |

Top 5 CPU markers (Profiler Hierarchy, Arena/active, 60-frame avg):
1. …
2. …
…
```

Subsequent sessions append a new section. The diff between the most
recent section and a baseline 5–10 sessions back is the answer to
"is perf actually dwindling?". Without this you can't tell. With
it, the user can point at a row and say "that's where it got
worse" — which gives the next pass a concrete starting point
instead of a vibe.

[`PERFORMANCE.md` § 9 — runbook](PERFORMANCE.md#9-runbook-the-game-feels-slow)
already documents how to do a single capture; this doc formalises
*recording the result*. The new file is a log, not a plan. Keep it
narrow.

---

## 5. Phase 2 — Likely suspects given recent work

Read this section as "where I'd look first", not as "fix these in
order". Every item below should be confirmed by a Profiler capture
before any code edit. The order is rough Bayesian likelihood given
the changes since session 27.

### 5.1 Terraforming arc baseline cost (sessions 63–83)

Twenty sessions added a full voxel terrain system. The follow-up
pass in session 83 was substantial (deferred dirty flush, analytic
normals, dig-mask upload throttle, shadow-casting off, undug-cull),
but the *structural baseline* is still 36 MeshFilter + MeshRenderer
+ Collider + DigChunk MonoBehaviour combinations sitting in the
scene. Even with renderers culled, every chunk still pays a per-
frame Update tick (`DigZone.Update` polls them all at
[DigZone.cs:311](../Assets/_Project/Scripts/Voxel/DigZone.cs#L311)).

Specific things to verify:

- **`DigChunk.PollBakeAndSwap` at idle.** If it's a single bool
  check 36× per frame it's ~µs and irrelevant. If it allocates or
  touches Unity API, it's a real cost. Profile under `Robogame.DigChunk.Remesh`
  marker.
- **`DigZone.Update` LOD branch.** `_enableLod` is `false` per
  session 83's fix, but the branch is still there. If it accidentally
  flips back on (Inspector default, build override, etc.), idle cost
  cliffs immediately. Worth a sanity assertion at runtime.
- **Chunk MeshCollider memory.** 36 colliders × per-chunk mesh data
  is real GPU memory pressure even when chunks are render-disabled.
  Confirm `convex: false` is OK on a terrain-sized concave mesh
  (yes, but worth verifying no bake hitch on chunk swap).
- **Heightmap host mesh AND voxel surface coexist.** Per session 83
  the voxel chunks are the sole *collider* but the grass mesh
  remains as a visual layer. Double-check no shadow casters, no
  duplicate physics, no overlap-then-clip work.

### 5.2 PerformanceHud cost when visible

When F3 is on, `Resample` runs every second with **three
`FindObjectsByType` scans** ([PerformanceHud.cs:216-222](../Assets/_Project/Scripts/UI/PerformanceHud.cs#L216-L222)):
Rigidbody, Joint, Robot. Each scan walks every GameObject in the
scene. In a 36-chunk Arena the scene has hundreds of GameObjects,
and the scan allocates the result array (`Rigidbody[]`, etc.).

The HUD's own jitter could be falsely attributed to the game.
Verify by toggling F3 off and re-measuring. If fps moves > 5%, the
HUD is too expensive when on; the fix is to drop the resample
cadence to 2–3 s or to use a per-Rigidbody registry instead of a
scene-wide scan.

### 5.3 Always-on HUD overlays

`ScrapCarriedIndicator`, `KillAnnouncer`, `HitMarkerOverlay`,
`FloatingDamageOverlay`, `ObjectiveHud`, `StartMatchHud`,
`VehicleStatsHud`, `DeathOverlay`, `MatchEndOverlay`. Each is a
MonoBehaviour with `Update` and/or `OnGUI`. [`PERFORMANCE.md` § 5.8](PERFORMANCE.md#58-imgui-overhead)
calls out the IMGUI rule: OnGUI runs 2–6× per frame, anything
allocated there is multiplied.

Confirm each one is allocation-free by toggling F3 on and watching
the GC line during a steady-state frame. Any positive number is a
regression. The biggest suspects are `KillAnnouncer` (text
formatting), `FloatingDamageOverlay` (multiple labels), and
`ObjectiveHud` (status text rebuild).

### 5.4 MK Toon outline pass

[`PERFORMANCE.md` § 5.4](PERFORMANCE.md#54-rendering--toon-outlines--srp-batcher)
flagged the per-object outline pass as the single most expensive
*future* feature at MP scale. It's not future at single-chassis
scale either — every block-renderer with the outline material
produces a silhouette draw. ~150 blocks per chassis × 1 chassis × 2
draws each = ~300 extra draws even in singleplayer.

Easiest measurement: open Frame Debugger in a build (or editor
with caveat), look at the SetPass total, then disable
`MKToonPerObjectOutlines` in [`PC_Renderer.asset`](../Assets/Settings/PC_Renderer.asset)
and recount. If the delta is > 100 SetPass calls, the outline pass
is a meaningful chunk of GPU time and the documented mitigations
(§ 5.4) move up the priority list.

### 5.5 Per-block damage VFX storm

[`PERFORMANCE.md` § 8.4](PERFORMANCE.md#84-damage-vfx-storm) flagged
`CombatVfxLibrary.Load` spawning per-hit. With the SMG firing on a
single bot during combat baseline (3.3), look at the Memory
profiler for instantiation spikes. If hit sparks are being
`Instantiate`d rather than pooled, that's the cause — fix is
`UnityEngine.Pool.ObjectPool<T>`. Confirm by running the combat
baseline with and without firing.

### 5.6 Audio cue spam

Session 83 found `DrillContact` + `DebrisDust` firing every 30 Hz
on top of the `DrillActive` motor loop and added an 8 Hz throttle.
Worth a sweep for other systems that emit cues on every physics
event without rate-limiting — `MomentumImpactHandler` is a candidate
(every chassis-collision impact), `WeaponBlock` fire (already gated
by fire rate, fine), `BombDefinition` impacts.

### 5.7 ScrapCarriedIndicator + FindObjects throttling

[ScrapCarriedIndicator.cs:70](../Assets/_Project/Scripts/Gameplay/ScrapCarriedIndicator.cs#L70)
does a `FindObjectsByType<Robot>` on a 0.5 s cadence. That's fine
in singleplayer, expensive at 16-chassis MP. Document as
pre-existing tech debt; don't fix yet.

### 5.8 Verlet rope sim

Per [`PERFORMANCE.md` § 5.1](PERFORMANCE.md#51-verlet-rope-simulator)
the sim itself is ~µs at default. The cost shows up only under the
rotor stress tower. If the user has been adding helicopter-heavy
playtests, verify the stress tower still hits its budget. Otherwise
de-prioritise.

### 5.9 GameObject.Find at runtime

`ArenaController` uses `GameObject.Find` in 7 places (spawn paths).
These are not per-frame — they run on spawn/respawn. Surface as
"won't cause a frame bug but will cause a respawn hitch with 16
chassis" — same shape as [`PERFORMANCE.md` § 8.5](PERFORMANCE.md#85-chassisfactorybuild-re-entry-on-rebuild-storm).
Confirm with the combat baseline (which includes a bot kill /
respawn cycle).

---

## 6. Phase 3 — Triage matrix

Once the Profiler captures are in hand, slot each cost into one of
three buckets. The bucket dictates the response.

| Bucket | Definition | Response |
|---|---|---|
| **Known-deferred** | Already documented in `PERFORMANCE.md` § 8 ("predicted future hotspots") or session changelogs. | Confirm it's still in the predicted shape. If yes, no action this pass — note in baselines doc. If it's firing earlier than predicted, escalate to "quick win" if cheap, "big rock" if not. |
| **Regression** | Per-frame cost that was lower in a prior baseline. | Highest priority. The session that introduced it has the context. Bisect against `docs/changes/` if needed. |
| **Unknown** | Cost the codebase doesn't account for at all. | Investigate. May surface a hidden bug (e.g., `Camera.main` in a hot path snuck back in). |

Resist the urge to fix items from the known-deferred bucket "while
we're here" unless they're actually firing. Most of the predicted
hotspots in `PERFORMANCE.md` § 8 are pre-emptive notes — they
become real when MP lands, not in singleplayer.

---

## 7. Phase 4 — Quick wins

Each of these is < 50 lines of code, low risk, and has a high
probability of contributing if the measurement says so. Do
**none** of them blind. Each entry lists the measurement that
should trigger it.

### 7.1 PerformanceHud resample cadence drop

**Trigger:** Phase 0 measurement shows F3-on costs > 5% fps vs F3-off.
**Fix:** raise [`PerformanceHud._resampleInterval`](../Assets/_Project/Scripts/UI/PerformanceHud.cs#L73)
from 1 s to 2.5 s. Or replace the `FindObjectsByType` scans with
a `RigidbodyRegistry` (statics-keyed) that systems add to in
`OnEnable` / `OnDisable`. Latter is a real change; the former is
two characters.

### 7.2 Outline pass disable on non-chassis renderers

**Trigger:** Frame Debugger shows > 100 SetPass from outlines.
**Fix:** layer-mask the `MKToonPerObjectOutlines` renderer feature
to the `Chassis` layer (or to `Chassis + Enemy` if outlines on
foes are gameplay-relevant). The infrastructure for layer-masked
feature passes is built into URP. Verify in `PC_Renderer.asset`.

### 7.3 GC alloc audit per always-on HUD

**Trigger:** Phase 0 shows GC/frame > 0 in steady state.
**Fix:** identify the OnGUI / Update that's allocating using the
Memory profiler's allocation calltree. Fix per the patterns in
[`PERFORMANCE.md` § 2.1](PERFORMANCE.md#21-zero-allocations-per-steady-state-frame).
Almost always a `string.Format` or a cached-style miss.

### 7.4 DigZone idle CPU verification

**Trigger:** Profiler shows `Robogame.DigZone.*` markers > 0.2 ms
at idle.
**Fix:** skip the per-chunk poll loop when no chunk has a pending
bake. Add an int counter `_pendingBakes` incremented on bake-issue,
decremented on `PollBakeAndSwap` success, and gate
[`DigZone.Update`](../Assets/_Project/Scripts/Voxel/DigZone.cs#L311)'s
poll loop on `_pendingBakes > 0`. Same shape as the
`_hasPendingDirty` / `_maskDirty` gates already in place.

### 7.5 Hit-spark pool

**Trigger:** Memory profiler shows hit-spark GC during combat.
**Fix:** as [`PERFORMANCE.md` § 8.4](PERFORMANCE.md#84-damage-vfx-storm)
documents — `UnityEngine.Pool.ObjectPool<T>` for the hit-spark
prefab, hard cap 32 concurrent.

### 7.6 OnGUI gated to visible

**Trigger:** Any overlay class shows up in Profiler CPU > 0.05 ms
when hidden.
**Fix:** early-return in `OnGUI` if not visible. Pattern is already
in [`PerformanceHud`](../Assets/_Project/Scripts/UI/PerformanceHud.cs#L268)
and [`DevHud`](../Assets/_Project/Scripts/UI/DevHud.cs).

### 7.7 Audio cue rate-limit sweep

**Trigger:** Memory profiler shows AudioRouter allocations during
sustained combat.
**Fix:** mirror session 83's drill throttle pattern on
`MomentumImpactHandler` and any other per-collision cue source.

---

## 8. Phase 5 — Big rocks

These are documented as deferred in `PERFORMANCE.md`. The reason
they're deferred is that they want a real reason to land. If Phase
0–4 close the gap, leave them deferred. If not, the order below is
the priority queue.

1. **Outline-bake-into-shader.** [`PERFORMANCE.md` § 5.4](PERFORMANCE.md#54-rendering--toon-outlines--srp-batcher).
   The single biggest pre-MP rendering risk. Real work; ship-quality
   shader change.
2. **WaterSurface analytic normals.** [`PERFORMANCE.md` § 5.2](PERFORMANCE.md#52-water-surface).
   Halves water-path cost. Needs a visual diff check — the toon
   shader is touchy.
3. **Verlet migration for ropes + rotors.** PHYSICS_PLAN § 2 calls
   out the existing PhysX joint chains as tech debt. The migration
   target is the same Verlet sim that already exists for free-rope
   chains. Big lift; only do it if joint chains show up in the
   profile, which today they don't.
4. **Per-block damage replication batching.** [`PERFORMANCE.md` § 8.1](PERFORMANCE.md#81-per-block-damage-replication-blowup).
   Land with the netcode PR, not before.
5. **Foam wake spatial hash.** [`PERFORMANCE.md` § 5.2 / § 8.7](PERFORMANCE.md#87-foamwake-foam-loop-nested-overts--contacts).
   WaterArena-only. Doesn't fire in default Arena.

---

## 9. Success criteria and exit gate

The pass is **done** when:

1. `docs/PERFORMANCE_BASELINES.md` exists with at least one full
   measurement row for all four scenes × three states.
2. Either: the user's "feels slow" intuition is reproduced in
   numbers (a row that's measurably worse than a prior baseline,
   if any prior baseline exists), or it is *not* reproduced and
   that's explicitly noted in the row's commentary.
3. Any Phase 4 quick win whose trigger condition fires has been
   landed and re-measured. The before/after lives in the same
   baseline file.
4. The Profiler capture from the Arena/combat scenario is saved
   (export as .data) under `docs/perf-captures/` so the next
   session can diff against it.

The pass is **not** done if:

- A "fix" was landed without a before/after number. Per
  [`PERFORMANCE.md` § 9](PERFORMANCE.md#9-runbook-the-game-feels-slow):
  *"'I made it faster' with no numbers is unfalsifiable."*
- A big rock from Phase 5 was landed without measurement saying it
  was the bottleneck. Pre-emptive optimisation is how a perf pass
  introduces new regressions.

The pass should be **abandoned and re-scoped** if:

- Phase 0 shows the game is comfortably within budget (e.g.,
  Arena/active > 144 fps with 0 B GC) — the user's perception was
  off and the right response is to log the baseline and stop.
- Phase 2 surfaces a true architectural problem (e.g., the dig
  zone is fundamentally too expensive at this scale) — the right
  response is its own design doc, not a perf pass.

---

## 10. Agent handoff checklist

If you are the agent picking this up, before writing any code:

- [ ] Read [`PERFORMANCE.md`](PERFORMANCE.md) in full. Especially
      § 1 (mental model), § 2 (the five rules), § 8 (predicted
      future hotspots).
- [ ] Read [`BEST_PRACTICES.md` § 16](BEST_PRACTICES.md#16-performance-budgets-targets-not-law)
      for the budget table.
- [ ] Read the most recent session log
      [`docs/changes/`](changes/) — the highest-numbered file.
      Session 83 specifically details the most recent dig perf
      pass.
- [ ] Confirm you can build a Development Build and attach the
      Profiler. If you can't, escalate — Phase 0 can't proceed
      from editor numbers alone.

Then, in order:

1. Phase 0 (measure). Output: a fully populated row in
   `PERFORMANCE_BASELINES.md` (create the file).
2. Show the row to the user. Ask: "do these numbers match how it
   feels?" — this is where the user's intuition gets compared
   against reality, before any time is spent on fixes.
3. Phase 2 / 3 (read the profile, triage). Output: a list of 1–5
   suspects with marker timings.
4. Phase 4 (quick wins, only triggered ones). Output: code edits
   + re-measured row.
5. Phase 5 only if the user approves a specific big rock with
   a measurement that justifies it.
6. Write a new session entry under `docs/changes/NN-perf-pass-N.md`
   summarising what was measured, what was changed, and the
   before/after numbers.

**Do not** spawn additional perf-related tickets, refactor adjacent
code, or "improve" anything that wasn't measured. Per Rule 3
(Surgical Changes) and Rule 4 (Goal-Driven Execution) in
[`CLAUDE.md`](../CLAUDE.md): the goal is the verified baseline + one
or two targeted fixes, not a tour of the codebase.

---

*This document is the workflow for one perf pass. When the pass is
done, link to the session entry at the top of this file ("Last
applied: session NN") and leave the workflow in place for the next
one.*
