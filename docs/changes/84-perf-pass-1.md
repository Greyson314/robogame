# 84 — Performance pass 1 (baseline harness + OnGUI GC fixes)

> Status: **code complete; harness green; idle sim numbers captured;
> OnGUI before/after needs the editor Test Runner (headless can't
> tick OnGUI — see below).** User closed the editor mid-session so
> CLI batchmode ran: all 3 `PerfBaselineHarness` tests Passed after
> a bootstrap fix. Numbers + the headless/OnGUI limitation are in
> `docs/PERFORMANCE_BASELINES.md`.
>
> User intent: execute `PERFORMANCE_PASS_PLAN.md`. User explicitly
> relaxed the plan's "measure-before-fix" gate ("make all changes
> first, analyze after") and approved Phase 4 + Phase 5, then
> narrowed scope to **Arena only** (drop Water/Planet).

## What shipped

**Harness (Phase 0/1, editor-playmode variant).**
[`PerfBaselineHarness`](../../Assets/_Project/Tests/PlayMode/Perf/PerfBaselineHarness.cs)
— a `Perf`-category PlayMode test. Loads Arena and Garage, settles
(240 frames + 2 s), samples 600 uncapped frames, logs frame-time
percentiles + `GC.GetAllocatedBytesForCurrentThread()` delta as a
greppable `[PERF-BASELINE]` line, appends to
`docs/perf-captures/harness-log.txt`. Hard-asserts idle GC < 2048
B/frame so a future per-frame-alloc regression fails loudly. Editor
absolute numbers are 5–10× off (PERFORMANCE.md §3.2) but valid for
the *delta*, which is the falsifiable number the pass needs.

**Fix 1 — ObjectiveHud OnGUI GC.** Was allocating 6× `new GUIStyle`
+ 2× `"/ " + int` concat **per OnGUI call**; OnGUI runs 2–6× per
displayed frame and the Arena scene always has a live match, so this
was steady-state garbage. Variants now pre-built in `EnsureStyles`
(the file's own existing pattern); timer colour set in-place on the
cached style (no alloc); `"/ target"` moved to the dirty-string
pattern already used for the timer/frags lines.

**Fix 2 — ScrapCarriedIndicator OnGUI GC + GetComponent.** `OnGUI`
called `GetComponent<Camera>()` every IMGUI event (PERFORMANCE.md
§2.5 violation) and built `$"⛁ {scrap}"` per visible robot per
event. Camera cached in `Awake`; scrap labels interned in a small
`Dictionary<int,string>` that saturates within a second.

**Fix 3 — PerformanceHud resample cadence.** `_resampleInterval`
1 s → 2.5 s (Phase 4.1). The 3× `FindObjectsByType` scan is already
gated to visible-only, so this only matters when F3 is left on —
it stops the HUD's own scan from contaminating a measurement.

## What was deliberately NOT done (and why)

- **DigZone `_pendingBakes` gate (Phase 4.4)** — `PollBakeAndSwap`
  is one bool check ×36 ≈ µs. The plan §5.1 predicted this exact
  shape. Adding the gate would be dead code (Rule 2/3). Documented
  as confirmed-cheap.
- **DigZone `Camera.main` cache** — initially landed, then reverted
  when scope narrowed to Arena: the LOD branch is off in Arena
  (`_enableLod:0`) and no other in-scope scene has a DigZone. Latent
  footgun (C# default `=true`) flagged in BASELINES, not patched.
- **Phase 5 outline bake / water analytic normals** — their triggers
  are a Frame-Debugger SetPass count and a water-path profile the
  idle harness cannot produce, and both are visible changes
  unverifiable without a human. Per the plan's own "no big rock
  without measurement" gate, deferred despite Phase-5 approval.
  Surfaced rather than blind-landed (Rule 7/12).

Full suspect-by-suspect triage table: `docs/PERFORMANCE_BASELINES.md`.

## Net finding

Matches the plan's "honest picture": per-frame hygiene is good, no
single bottleneck. Two real steady-state GC sources in always-on
Arena overlays — fixed. The deliverable is the trend harness + two
targeted fixes, not a 2× win.

## User follow-ups

1. **Capture the numbers.** Run `PerfBaselineHarness` (`Perf`
   category) via Test Runner, or CLI batchmode with the editor
   closed (command + git-stash before/after procedure in
   `docs/PERFORMANCE_BASELINES.md`). Do **not** use `-nographics` —
   it skips the OnGUI path these fixes target.
2. **Active/combat rows** still need a manual build run (human at
   the controls); append to BASELINES per PERFORMANCE.md §9.
3. The harness asserts idle GC < 2048 B/frame — if it fails on the
   "before" stash that *confirms* the regression these fixes remove.
