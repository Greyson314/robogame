# 174 — PresetBlueprintTests: guard the preset list, drop the retired Buggy path (LOG-174)

Landed by the factory 2026-09-17T02:05:22Z. Change CHG-003, branch `chg/003-preset-buggy-path` @ 9929740ebe. Gate: suite PASS, perf N/A, red team PASS — suite: foreground run-tests.sh All on the test rig (.utmp/factory/gate/CHG-003-suite.txt; EditMode 530/530, PlayMode 152/153); red team: .utmp/factory/gate/CHG-003-redteam.md, PASS, notes -> F-021 (SurfaceNets benchmark flake), F-022 (Grappler/HoverTank uncovered); perf N/A: test code only; console BRIDGE_DOWN (compile log: 0 warning CS).

Factory shift 3 on the desktop, 2026-09-17, branch `chg/003-preset-buggy-path`.
Class AUTO (charter I1: a flaky or permanently-inconclusive test fixed). No runtime code touched.

## Intent

`PresetBlueprintTests.Preset_PassesValidation` had reported one Inconclusive case on
every run since session 61 retired the Buggy preset: the test's `PresetPaths` list
still carried `Blueprint_DefaultBuggy.asset`, the asset does not exist, and the
test's "asset not found" branch is `Assert.Inconclusive`, so nobody saw it
(FINDINGS F-016; LAUNCH-READINESS T1 "every inconclusive justified").

## What shipped

- `PresetBlueprintTests.PresetPaths_AllExistOnDisk` (new, EditMode): every path in
  `PresetPaths` must load through `AssetDatabase`; a missing one FAILS with the
  list of missing paths and the two possible causes (the scaffolder stopped
  producing it → remove the entry; it was never committed → scaffold and commit).
  Written before the fix; it failed on the Buggy path (test-rig run
  2026-09-17T01:50Z: EditMode 529/531, 1 failed = this guard, 1 inconclusive).
- The stale `Blueprint_DefaultBuggy.asset` entry removed from `PresetPaths` and
  from `ScriptedChassisBuilderTests.EveryShippedPreset_PassesLibraryAwareValidation`'s
  path list (which silently `continue`d over it). `GameplayScaffolder.cs:38-39`
  records the session-61 retirement; the twelve remaining paths all exist.

## Verification

Suite, test rig, branch @ 9929740e, 2026-09-17T01:51-01:52Z (`run-tests.sh All`): EditMode 530/530
(0 failed, 0 inconclusive; the guard passes), PlayMode 152/153 (0 failed; the one skip is the
documented `MatchFlowTests.SpawnBot`). Before the fix, with the guard only: EditMode 529/531,
1 failed (the guard, naming the Buggy path), 1 inconclusive. Red team: see the evidence block above.

Perf: N/A (test code only; nothing hot). Console: bridge down this shift (see
LOOP-STATE § RIG); no scene change, no console impact possible.

## Revert

`git revert` of the merge commit restores the stale entry and the permanent
Inconclusive; nothing else depends on this.
