# 194 — Doc drift from the red teams: SurfaceNets dim=34, the ASCII legend derived from the glyph map, index rows 101-104 and 108-122 (LOG-194)

Landed by the factory 2026-09-18T06:25:48Z. Change CHG-025, branch `chg/025-red-team-drift` @ 4e93c44110. Gate: suite PASS, perf N/A, red team N/A — Suite at 4e93c441 on the rig (direct run, MCP stripped): EditMode 557/557 (543 + 14 BlueprintAsciiDumpTests cases, RED first: 7/14 failed on main incl. Hover Tank), PlayMode 152/153 (1 documented skip); Python 45/45 (the --all tests RED first: unrecognized argument). Perf N/A: docs, a test-only dump utility, tooling. Red team N/A under D16b: BlueprintAsciiDump is referenced only under Assets/_Project/Tests (grep in RESULT.md); the rest is docs and factory tooling; no shipped runtime path, no NOD action, no invariant. Foreground arbitration mid-build: the glyph map widened to every id the 14 presets use (six more ids than F-035 named), the legend test kept as the guard (F-035 line)..

## Intent

Three drift findings the shift 5–7 red teams left behind, all of the kind "a doc or a generated file says something the code no longer does": two SurfaceNets stragglers after CHG-012 renamed the benchmark to dim=34 (F-039); the preset ASCII snapshot's legend, hand-typed and out of step with the glyph map, with the Hover Tank preset printing `?` for every hoverblade (F-035); and the session index in docs/changes/README.md missing nineteen rows below its top (F-044), which CHG-017's forward-only backfill could not reach. One intent: the docs say what the code does, and the two generated ones now cannot drift the same way again.

## What shipped

- `Assets/_Project/Scripts/Voxel/SurfaceNetsMesher.cs:60` — the doc comment says `dim=34 (32 cells + 2 apron)`; `docs/subsystems/terraforming.md` § Phase 1c names the synchronous Burst compile in the warm-up (`BurstCompiler.Options.EnableBurstCompileSynchronously`, CHG-012) before the three timed windows.
- `Assets/_Project/Scripts/Block/BlueprintAsciiDump.cs` — the glyph map gains `H=HoverBlade`, `c=Cannon`, `M=Mortar`, `D=Drill`, `s=Spring`, `E=ModuleEmp`: the map had never caught up with six block ids the fourteen shipped presets use, not only the one F-035 named (the builder found the other five when the new test went red on six presets, and the foreground widened the change rather than ship a red test). The legend is now DERIVED from the map (`glyph=Name` per entry in map order, the name read from the `BlockIds` constant by reflection, then `?=unmapped`), so a glyph and its legend entry cannot part again.
- `Assets/_Project/Tests/EditMode/Blueprints/BlueprintAsciiDumpTests.cs` (new, 14 cases, one per shipped preset): every grid glyph in a preset's dump is named in the dump's own legend and none is `?`. Written first: 7 of 14 RED on main (Ground/Tank, Plane, Boat, DrillBot, Hover Tank, SpringBot and one more), Hover Tank exactly as the spec predicted. This is the guard: a new block that ships in a preset without a glyph turns it red.
- `docs/blueprint-snapshots/presets.md` — regenerated on the rig by the existing dump test: 14 legend lines changed, 12 former `?` cells now carry a glyph (Ground/Tank 2, Plane 1, Boat 2, DrillBot 1, Hover Tank 4, SpringBot 2); every other cell byte-identical.
- `.claude/scripts/factory/backfill_changes_index.py` gains `--all` (every `docs/changes/NNN-*.md` with NNN ≥ 100 and no row is inserted in descending order among the existing rows, not only above the top); the forward-only default is unchanged and re-tested; `tests/test_backfill_changes_index.py` (new, 4 tests; 3 RED first: `unrecognized arguments: --all`). Run once: `docs/changes/README.md` gains exactly the nineteen rows 101–104 and 108–122, descending order preserved, idempotent on a second run.

## Verification

- Suite at the branch's frozen sha 4e93c441 on the rig (direct run, MCP package stripped as run-tests.sh does): EditMode 557/557 (543 + 14), PlayMode 152/153 (the one documented skip), 0 failures. Python factory tests 45/45. RED evidence in `.utmp/factory/gate/CHG-025/red.txt`, the gate in `suite.txt`.
- `git diff` of the branch against main touches only the nine paths above; nothing under `.utmp`.
- Red team N/A (D16b): BlueprintAsciiDump is referenced only under `Assets/_Project/Tests` (grep in RESULT.md), the rest is docs and factory tooling; the green suite is the gate.
- Built with git plumbing against a working tree another builder owned (LESSONS 10); the builder caught and fixed one staleness hazard of that route: main gained row 193 (CHG-026) mid-build and the first index work copy would have dropped it, so the branch was rebuilt on the refreshed HEAD.

## Aftermath

- F-056: row 109's title reads the entry's first line, an HTML comment, because `title_from_heading` never skips to the first `#` heading; a one-line fix on the next index-tooling touch, with F-055 (land.py's unenforced 600-char bullet cap).
- The glyph map still names only what shipped presets use; the test is the oracle for the next block, by design (a full vocabulary of thirty-odd glyphs would be busywork nobody reads).
