# 181 — SurfaceNets benchmark: warm the Burst job synchronously, gate on the best of three medians, log a harness row (LOG-181)

Landed by the factory 2026-09-17T18:42:18Z. Change CHG-012, branch `chg/012-surfacenets-gate` @ 60f3580663. Gate: suite PASS, perf N/A, red team PASS — tests-first: cold-cache repro 3/3 FAIL at 1.81-1.97 ms on main's test, fix 3 cold + 2 warm PASS (.utmp/factory/gate/CHG-012-cold-runs.txt, -warm-runs.txt); suite: red team's own All, EditMode 537/537, PlayMode 152/153, 100 s (.utmp/factory/gate/CHG-012-redteam.md); red team PASS, note 1 = F-038 residual (first post-recompile run best 0.942 ms), notes 4-5 = F-039; perf N/A (batch-rig rows only); console clean over the bridge.

Factory shift 5 on the desktop, 2026-09-17, branch `chg/012-surfacenets-gate`.
Class AUTO (charter I1: a flaky test fixed; an instrument). Test assembly only, plus one tier-2 doc line.

## Intent

`SurfaceNetsBenchmarkTests` asserted a 50-iteration median remesh time under 1 ms as a hard
machine gate and measured 1.005 ms once on the test rig, passing on every rerun (FINDINGS
F-021, red team on CHG-003). Two spikes bounded it: isolated medians 0.487–0.609 ms over six
runs (SPIKES L3); under full-suite load 0.360–0.376 ms over five (L3b), lower than isolated,
so load was not the cause. The one failure followed a full recompile. A proxy without its
band is a coin (D6), and a flaky test is a bug (D2).

## What shipped

- The median test warms the Burst job with 10 untimed calls (was 3), then times three
  consecutive 50-iteration windows and gates on the best median: a real regression is slower
  in all three windows, a transient (a late compile, a CPU power-state dip, a neighbouring
  test's tail) is not. The assertion message prints all three medians.
- Every run appends a `[SURFACENETS-BENCH] dim=34 warmup=10 windows=3x50 medians=a/b/c ms
  best=x ms min=y ms max=z ms` row to docs/perf-captures/harness-log.txt through
  `PerfBaselineHarness.AppendToLog` (made `internal`; same assembly), so drift is a number in
  the log rather than a memory.
- The test is named for what it measures (`Mesh_Dim34Sphere_…`; the constant has been 34,
  32 cells + 2 apron, since Phase 2b) and docs/subsystems/terraforming.md § 12's Phase 1c line
  says what the test now does.
- The 1 ms target is untouched (Grey's number). The GC-zero test is untouched.

## Verification

Reproduction, tests first (`.utmp/factory/gate/CHG-012-cold-runs.txt`, rig @ 6a5456a3, main's
test, `Library/BurstCache` deleted before each run, the fixture alone): medians 1.973, 1.971
and 1.810 ms, minimums 1.58–1.66 ms, three failures out of three against the 1 ms gate. With a
warm cache the same fixture measured 0.49–0.61 ms isolated (SPIKES L3) and 0.36–0.38 ms
under load (L3b). So the cause is a cold Burst cache: the job compiles asynchronously and the
whole timed window runs on the managed fallback, about 3.5× slower. The 1.005 ms on CHG-003's
gate was a window that straddled the compile finishing.

Acceptance on the branch @ 02a01596 (same procedure, same rig, `CHG-012-cold-runs.txt` and
`CHG-012-warm-runs.txt`): three cold-cache runs PASSED with best medians 0.660, 0.542 and
0.651 ms (window medians 0.54–0.69 ms), two warm runs PASSED at 0.364 and 0.362 ms (windows
0.362–0.374 ms, inside the L3/L3b envelope); the five `[SURFACENETS-BENCH]` rows are in
docs/perf-captures/harness-log.txt (batch rig, fixture alone, 2026-09-17 13:28–13:30 local).
A cold-cache run still sits about 0.3 ms above a warm one, so the band is real and the row
is what makes it visible; the gate keeps a 1.5× margin over the worst cold best-median.

Suite (red team's own run, rig @ 60f35806, `run-tests.sh All`): EditMode 537/537, PlayMode
152/153 (the one documented skip), 0 failures, 100 s wall. Red team: see the evidence block
above. Perf: the rows above are batch-rig rows (ENGINEERING RULE 7), a band, not a claim about
the live Editor. Console: clean over the bridge (0 errors, 0 warnings).

## Residual (red team note 1, FINDINGS F-038)

The red team's first full-suite run was also the first run after the test assembly recompiled
for its new `Unity.Burst` reference, and its row read medians 1.039 / 0.942 / 1.412 ms, best
0.942 ms: two windows over 1 ms, 5.8 % from red, passing only through best-of-three. The
synchronous compile fixes the cold-cache case (measured, 3 of 3) but not this post-recompile
case, so F-021 has a second, unmeasured cause (the likeliest: Burst compiling other jobs on
worker threads right after a reload, contending for the cores). The second run in the same
session read 0.430 ms. The gate is therefore narrower than before, not closed: F-038 carries
the residual with the row as evidence. Two doc stragglers noted by the red team are F-039.

## Revert

`git revert` of the merge commit; the coin comes back.
