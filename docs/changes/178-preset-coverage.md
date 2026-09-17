# 178 — Preset test coverage: every scaffolder slot validated, one list, HoverTank quarantined loudly (LOG-178)

Landed by the factory 2026-09-17T16:06:30Z. Change CHG-008, branch `chg/008-preset-coverage` @ 77f1a42480. Gate: suite PASS, perf N/A, red team PASS — suite: tests-first fail -> impl found F-026 -> quarantine green (.utmp/factory/gate/CHG-008-suite.txt); red team PASS, quarantine judged honest, diagnosis confirmed, HoverTank drives in play (S2), four notes folded into CHG-013 (.utmp/factory/gate/CHG-008-redteam.md); perf N/A; console BRIDGE_DOWN.

Factory shift 4 on the desktop, 2026-09-17, branch `chg/008-preset-coverage`.
Class AUTO (charter I1: new test coverage). Test code only.

## Intent

The red team on CHG-003 found that two of the ten player-facing presets the scaffolder
wires into `GameStateController._presetBlueprints` (slot 2 `Blueprint_DefaultGrappler`,
the preset that replaced Buggy in session 61, and slot 8 `Blueprint_DefaultHoverTank`,
session 99) were in neither `PresetBlueprintTests.PresetPaths` nor
`ScriptedChassisBuilderTests`' own list, so no test validated them (FINDINGS F-022;
LAUNCH-READINESS T1).

## What shipped

- `PresetBlueprintTests.PresetPaths_CoverEveryScaffolderSlot` (new): the ten slot paths,
  hard-coded with a comment pointing at GameplayScaffolder's `presets.arraySize = 10`
  block, must all be in `PresetPaths`. Written first: on the test rig at 15:53Z it failed naming Grappler and HoverTank (EditMode 533/534).
- `PresetPaths` gains Grappler and HoverTank (14 presets validated) and becomes `internal`;
  `ScriptedChassisBuilderTests.EveryShippedPreset_PassesLibraryAwareValidation` iterates
  that same list instead of its own ten, so the two suites cannot drift apart again.
- The CHG-003 guard's docstring now says what it checks: list → disk only.
- The new coverage found a real defect on its first run: `Blueprint_DefaultHoverTank` fails
  library-aware validation (four corner cubes at y=0 have Up=+Y, so their implied host is
  the hoverblade beneath them, a leaf block). The chassis builds and drives; it is not
  player-buildable-shaped. FINDINGS F-026, fix specced as CHG-013. To keep main green
  without hiding it, `PresetBlueprintTests.KnownInvalid` quarantines that one preset with
  an assertion that it STILL fails, so the entry expires the day the fix lands (the
  opposite of the F-016 permanent-Inconclusive pattern). Both suites honour the same table.

## Verification

Suite, test rig, branch @ 77f1a424, 2026-09-17T15:57-15:58Z (`run-tests.sh All`): EditMode 536/536
(14 `Preset_PassesValidation` cases; 0 failed, 0 inconclusive), PlayMode 152/153 (the one
documented skip). Before the quarantine (78ea48da): EditMode 534/536, the two failures being the
HoverTank preset in both suites, i.e. F-026. Red team: see the evidence block above.

Perf: N/A (test code). Console: this session's bridge is down (LOOP-STATE § RIG).

## Revert

`git revert` of the merge commit; back to twelve validated presets and two lists.
