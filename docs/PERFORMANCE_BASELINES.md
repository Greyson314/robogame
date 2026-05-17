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

### Numbers — PENDING EXECUTION

The harness and all fixes are committed; numbers below are unfilled
because the editor held the project lock during this session and the
agent will not kill a live editor (unsaved-scene risk). Run path A or
B above to populate. **"Before" = `git stash` of this session's
commit; "after" = working tree.**

| Scene  | State | when   | avg ms | median ms | p99 ms | p99.9 ms | fps | GC B/frame |
|--------|-------|--------|--------|-----------|--------|----------|-----|------------|
| Garage | idle  | before | …      | …         | …      | …        | …   | …          |
| Garage | idle  | after  | …      | …         | …      | …        | …   | …          |
| Arena  | idle  | before | …      | …         | …      | …        | …   | …          |
| Arena  | idle  | after  | …      | …         | …      | …        | …   | …          |

Active / combat rows still need a manual build run (a human at the
controls — the harness only reproduces idle). Capture them per
PERFORMANCE.md §9 and append here.

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

**Conclusion matches the plan's "honest picture":** the codebase's
per-frame hygiene is good. There is no single 16 ms bottleneck. The
two real findings are steady-state OnGUI GC in two always-on Arena
overlays — fixed. Everything else is either already-mitigated or a
documented future hotspot not yet firing. The deliverable is the
trend-tracking harness + two targeted GC fixes, exactly as the plan
predicted ("treat the trend line as the deliverable, not a one-shot
2× win").
