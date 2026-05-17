# Robogame — Performance Baselines

> A log, not a plan. One section per measurement event. The diff
> between a recent section and an older one is the answer to "is perf
> actually dwindling?". See [`PERFORMANCE_PASS_PLAN.md`](PERFORMANCE_PASS_PLAN.md)
> Phase 1 for the format rationale and
> [`PERFORMANCE.md`](PERFORMANCE.md) for the rules.

## How to capture a row

Two paths. Both produce the same `[PERF-BASELINE]` log line and append
to `docs/perf-captures/harness-log.txt`.

**A — Editor Test Runner (editor already open).**
`Window > General > Test Runner > PlayMode`, run the `Perf` category
(`PerfBaselineHarness`). Idle rows only; graphics are on so the OnGUI
GC delta is real.

**B — CLI batchmode (editor must be closed — it holds the project
lock).**

```
& "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" `
  -runTests -batchmode -projectPath "C:\Users\Grey\Desktop\mutedtuple\robogame" `
  -testPlatform PlayMode -testCategory Perf `
  -testResults "C:\Users\Grey\Desktop\mutedtuple\robogame\docs\perf-captures\arena-results.xml"
```

Do **not** pass `-nographics` — the HUD allocations this pass targets
live in `OnGUI`, which only ticks with a GUI repaint. A `-nographics`
run under-reports the delta.

**Clean before/after delta** (the falsifiable number the plan's exit
gate wants), run path B twice:

```
git stash            # revert the session-84 fixes → "before"
<run path B>         # row tagged "before"
git stash pop        # restore the fixes → "after"
<run path B>         # row tagged "after"
```

Editor numbers are 5–10× off absolute (PERFORMANCE.md §3.2) but valid
for the *delta* — same machine, same harness, same scene.

---

## 2026-05-17 — session 84 baseline (idle, editor-playmode)

Machine: _fill from the run host_ — e.g. `GTX 1660 / Ryzen 5 3600 / 32 GB / 1080p / V-Sync off`
Harness: `PerfBaselineHarness`, 240 warmup frames + 2 s, 600 sample frames, uncapped.
Scope: **Arena** (the scene actually played) + **Garage** (quiet floor).
WaterArena / PlanetArena deliberately out of scope this pass (user call —
neither contains a DigZone; water path is already amplitude-gated).

### Numbers — captured (CLI batchmode, headless, after-state)

Machine: **AMD Ryzen 7 9800X3D / RX 9070 XT / 31 GB / headless batchmode / V-Sync off**
Captured 2026-05-17 14:20 via `Unity.exe -runTests -batchmode` (no
`-nographics`). All three harness tests **Passed**.

| Scene  | State | path            | avg ms | median ms | p99 ms | p99.9 ms | ~fps | GC B/frame |
|--------|-------|-----------------|--------|-----------|--------|----------|------|------------|
| Garage | idle  | sim (no OnGUI)  | 0.203  | 0.187     | 0.344  | 0.513    | 4933 | 0          |
| Arena  | idle  | sim (no OnGUI)  | 0.338  | 0.321     | 0.581  | 0.686    | 2963 | 0          |

**Read these correctly.** This is the **non-OnGUI idle path only**
(Update / FixedUpdate / physics / render-setup). Headless batchmode
has no IMGUI repaint loop, so `OnGUI` never ran — proven by the 0 B
GC on both scenes (the pre-fix code *would* allocate here if OnGUI
ticked). These numbers therefore neither confirm nor deny the
session-84 OnGUI fixes; they establish that **the idle simulation
path is sub-millisecond and zero-GC on this machine — categorically
not the source of any perceived slowdown.** The slowdown surface is
GPU (Fluff grass / toon outlines, PERFORMANCE.md §5.3–5.4) and the
always-on IMGUI overlays — neither observable headless.

#### Before/after for the OnGUI fixes — NOT obtainable headless

The two fixes live in `OnGUI`. A headless "before" run produces the
identical 0 B / same sim numbers (theatre, not evidence — Rule 12).
The real numeric before/after needs OnGUI to tick: run
`PerfBaselineHarness` from the **editor Test Runner** (Game-view
repaints OnGUI) or a **windowed Development Build**, with the
`git`-stash before/after procedure above. Until then the OnGUI fixes
rest on **code-level certainty**: moving `new GUIStyle` out of a
per-call method behind a build-once `_stylesBuilt` guard
deterministically eliminates the per-call allocation — this is a
property of the C# control flow, not a measurement-dependent claim.

Active / combat rows still need a manual windowed build run (a human
at the controls). Capture per PERFORMANCE.md §9 and append here.

### Static triage — the analytical deliverable

Phase 0 measurement is build-and-pilot work the agent can't do
autonomously. What it *can* do — and did — is read every Phase 2
suspect and classify it from source. GC allocations and per-frame
scans are facts in the code, not vibes. Verdicts:

| Suspect | Verdict | Evidence |
|---|---|---|
| PerformanceHud `Resample` 3× `FindObjectsByType` | **already safe** | gated `if (!_visible) return;` (PerformanceHud.cs:174). Zero cost in normal play. Cadence still raised 1→2.5 s to de-contaminate F3-on measurement (§7.1). |
| DigZone 36-chunk `PollBakeAndSwap` loop | **already safe** | `PollBakeAndSwap` is `if (!_hasPendingBake) return false;` (DigChunk.cs:292) — one bool check ×36 = ~µs. Plan §5.1 predicted exactly this. **No `_pendingBakes` gate added** — it would be dead code (Rule 2/3). |
| DigZone `_enableLod` → `Camera.main` every Update | **latent, not firing** | C# default `_enableLod=true` (DigZone.cs:60) is a footgun, but Arena overrides `_enableLod:0` (Arena.unity:735) and WaterArena/PlanetArena/Garage contain no DigZone. Not fixed this pass (de-scoped; not firing in Arena). Flagged for the next scene that adds a DigZone without the override. |
| ObjectiveHud `OnGUI` | **fired — FIXED** | 6× `new GUIStyle` + 2× `"/ "+int` concat per OnGUI; OnGUI runs 2–6×/displayed frame; Arena always has a live match → steady-state GC. Now cached in `EnsureStyles` + dirty-string for target. |
| ScrapCarriedIndicator `OnGUI` | **fired — FIXED** | `GetComponent<Camera>()` per OnGUI event (PERFORMANCE.md §2.5 violation) + `$"⛁ {scrap}"` interp per visible robot per event. Camera cached in Awake; scrap strings interned. (Sub-agent's "new GUIStyle/frame" claim was wrong — `EnsureStyle` is guarded; verified.) |
| KillAnnouncer / HitMarkerOverlay / StartMatchHud / VehicleStatsHud / DeathOverlay / MatchEndOverlay | **already safe** | all early-return when hidden + cached styles + pre-stamped strings. No action. |
| FloatingDamageOverlay | **latent, low** | bounded pooled accumulators; `new GUIContent` per visible number capped <32. Acceptable; no action. |
| ProjectileWorld zero-projectile tick | **already safe** | O(_count) loop; body never executes at count 0. No setup alloc. No action. |
| MomentumImpactHandler / impact audio | **already safe** | 0.2 s pair-cooldown gate (5 Hz cap) already present. Matches session-83 throttle discipline. No action. |
| WaterMeshAnimator `RecalculateNormals` | **already safe (Arena)** | amplitude-gated `if (_recalculateNormals && amplitude > 0.005f)`. WaterArena-only and de-scoped. Analytic-normals (Phase 5 #2) **not landed** — trigger needs a water-path profile this pass can't produce. |
| MK Toon outline pass | **measurement-gated — NOT landed** | `m_LayerMask: 4294967295` (all layers) in PC_Renderer.asset. Phase 5 #1. Trigger is a Frame-Debugger SetPass count the idle harness cannot produce, and layer-masking is a visible change unverifiable without a human. Per the plan's own "no big rock without measurement" gate, deliberately deferred — not blind-landed despite Phase-5 approval. |

### 2026-05-17 — in-game bisect findings (user-measured, ~330–385 fps, 9800X3D/9070XT)

Empirical deltas from the new `Settings → Perf Bisect` switches:

| Lever | Δ fps | Verdict |
|---|---|---|
| Disable 2 AI bots (of 4; dummy + arch bot remain) | ~+15 | AI/physics cost modest |
| Camera away from ALL bots+dummies | ~+40 | **chassis block *rendering*** is the cost, not AI |
| Disable Fluff grass | ~+40 | matches §5.3 (#1 GPU). `_ShellCount` 7→6 landed; `HillsSettings.resolution` 121→81 is the big remaining lever (needs a rebake) |
| Disable dig chunk renderers | ~+5–10 | ruled out — session 83 culling holds |

**Chassis-render root cause + fix (session 84, commit 44cf5a1).**
`BlockBehaviour.UpdateDamageVisual` ran in `Awake`, setting a
per-renderer `MaterialPropertyBlock` on every block → every block
SRP-Batcher-excluded for life, even undamaged (~150 individual
draws/chassis). This is the §8.2 predicted hotspot, live. Fixed:
MPB skipped at full health (`SetPropertyBlock(null)` → rejoin
batch; visually identical — full-health tint was `baseColor×1`);
`CpuBlockMarker` beacon moved from per-renderer MPB to one shared
static `Material` (emission is static; only the light pulses) so
it's SRP-batchable and not wiped by the full-health clear.
`BlockGrid.ApplyTint` left as-is (no-ops on the untinted chassis in
play today; MP team-colour conversion is documented future work,
not landed — Rule 2). Confirmed Δ to be filled by the next in-game
F3 read.

### 2026-05-17 — render-cost probe + GPU-blind finding (key methodology note)

`PerfRenderProbe` (CLI, headless, Arena, camera framing 227 chassis
block renderers vs empty sky):

```
away=0.317ms  baseline=0.323ms  noChassisShadowCast=0.314ms  noSunShadows=0.315ms
chassis-in-view = 0.006ms | chassis-shadowcast = 0.009ms | all-shadow = 0.009ms
```

Headless ran ~3000 fps vs the user's 300–400 in play. **Conclusion:
CLI batchmode does not do representative GPU shading.** It measures
CPU render *submission* (227 blocks = 6 µs, genuinely free) and is
**blind to the GPU costs that actually matter here** — grass shell
expansion, chassis fragment/overdraw, shadow shading. Same class of
limit as OnGUI. This also explains why the SRP/MPB chassis fix
(commit 44cf5a1) did not move the user's fps: CPU submission was
already free; that fix is correct hygiene + MP-relevant and was
kept, but it was never this machine's lever.

**What the harness IS good for going forward:** a CPU-time / GC /
render-submission regression guard (idle + chassis-in-view), run
headless in CI reach. It is *not* a GPU-cost tool — GPU deltas need
the editor Game-view or a windowed Development Build (the user's
play sessions are the GPU measurement loop).

**Landed this pass (documented best-practice GPU reductions, commit 8bc532a):**
shadow cascades 4→2 (§6); hills resolution 121→81 + rebake (grass
input tris 28800→12800, ×~22 in the geom shader — §5.3's biggest
grass lever); Fluff `_ShellCount` 7→6; `_FinsEnabled` 1→0 (§5.3 #5,
the one visible knob — revert by setting it back to 1). All
reversible asset values. GPU impact unverifiable autonomously;
user verifies in play.

**Biggest remaining lever — needs design sign-off, NOT blind-landed.**
227 individual block renderers means 227 draws × MK Toon fragment +
overdraw + (now 2-cascade) shadow shading on the real GPU. The
canonical fix is per-chassis **mesh-combining** (or GPU instancing
of the shared cube mesh) — collapsing ~150 draws/chassis to a
handful. This is a real architecture change: combined meshes break
per-block damage/removal unless rebuilt on the connectivity event,
so it touches destructible-block gameplay (§8.2). Recommended as
the next major work item with the architect, not an unattended
edit. Two user-decision items also deferred: the player-only
outline layer-mask (§5.4 — user wants outlines on their own
chassis long-term) and disabling the CPU-beacon point lights
(readability tradeoff).

**Conclusion matches the plan's "honest picture":** the codebase's
per-frame hygiene is good. There is no single 16 ms bottleneck. The
two real findings are steady-state OnGUI GC in two always-on Arena
overlays — fixed. Everything else is either already-mitigated or a
documented future hotspot not yet firing. The deliverable is the
trend-tracking harness + two targeted GC fixes, exactly as the plan
predicted ("treat the trend line as the deliverable, not a one-shot
2× win").
