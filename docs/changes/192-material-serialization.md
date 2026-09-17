# 192 — Three materials re-serialized by the Editor, committed once; F-040's trigger found (LOG-192)

Landed by the factory 2026-09-17T23:15:03Z. Change CHG-018, branch `chg/018b-material-serialization` @ c67ce6543c. Gate: suite PASS, perf N/A, red team N/A — Generated files (D16b). Suite on the branch's checkout after CHG-023: .utmp/factory/gate/CHG-018-suite.txt (the first attempt on chg/018 hit the CHG-023 flake: 151/153). F-040 reproduced by the harness route this shift; the acceptance run follows the landing..

Factory shift 7, 2026-09-17, branch `chg/018-material-serialization` (plumbing; the files are the Editor's own output).
Class AUTO (I1: the rig; a clean tree for every landing, D13). Red team N/A (D16b: generated files). Three material assets, one number each.

## Intent

Three materials (`Mat_GarageFloor`, `Mat_GarageWall`, `Mat_WaterFloor`) came back modified after
work in the factory Editor, each with one `_Color` row rewritten by a few units in the last
place (FINDINGS F-040). A dirty tree blocks the landing chain and hides real changes, so the
question was what rewrites them and whether committing the Editor's serialization once ends it.

## What shipped

- The Editor's own serialization of the three materials, captured after the perf-band run and
  committed as is. No value a player could see changes (0.24705882 → 0.24705878 is below any
  8-bit colour step).
- The trigger, found this shift: not a scene load and not play mode (three loads and a 12 s
  play left the tree clean) but the in-Editor PlayMode test run over `run_tests`, the route the
  perf harness uses. `Mat_ArenaStair`, named in F-040, did not drift this time.

## Verification

- Suite on the branch's checkout: `.utmp/factory/gate/CHG-018-suite.txt` (a formality: nothing
  the suite reads changed).
- The acceptance that matters: one more harness run after this lands, then `git status`. Clean
  means the rewrite was a one-time re-serialization; dirty again means a script writes the
  shared material on every run and the writer, not the file, is the fix. Recorded in the
  LOOP-STATE bullet and F-040.

## Aftermath

F-040 narrowed to its trigger. The values move DOWN by a few ulps each time they are rewritten,
which smells like a colour-space round trip somewhere on the test-run path; if the acceptance
run dirties the tree again, that is the next finding's evidence pointer.
