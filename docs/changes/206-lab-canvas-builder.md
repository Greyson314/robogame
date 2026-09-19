# 206 — LabCanvasBuilder: the Lab's one-shot UGUI scene construction leaves LabController (LOG-206)

Landed by the factory 2026-09-19T05:35:50Z. Change CHG-035, branch `chg/035-lab-canvas-builder` @ 5c19b216f6. Gate: suite PASS, perf N/A, red team PASS — Suite at 5c19b216 via run-tests.sh --at (builder; results copied to .utmp/factory/chg035/): EditMode 562/562, PlayMode 161/162 (1 documented skip); the pinning test's 134-line UGUI dump byte-identical before/after. Foreground before/after screenshots over the bridge match (panel region 0.23 % of pixels differ by > 8/255, max 28: the animation at a different frozen instant; no edge moved). Red team sonnet/medium PASS 83k (.utmp/factory/gate/CHG-035-redteam.md). Perf N/A: Update untouched, construction is one-shot. Branch predates CHG-006/034/036 (disjoint files); merged main rechecked at shift end..

## What changed

The Lab's one-shot decorative and widget UGUI construction (`BuildGround`, `BuildScrews`/`BuildScrew`, `BuildHeader`, `BuildJournal`, `BuildSwitchboard`/`BuildSliderRow`/`BuildNameField`/`BuildActions`, `BuildVial`) moved from `LabController` into a new static `LabCanvasBuilder`, same folder/assembly. `LabController.BuildCanvas` keeps the canvas root and Panel shell and now calls into the builder, assigning the returned widget references to its own fields exactly as before. `RefreshList`/`BuildNewRow`/`BuildRowShell` (rebuild against live editor state, not one-shot) and the shared UGUI primitives/fonts/three per-frame-shared arrays stay on `LabController`, widened `private` → `internal` (visibility only, as CHG-033 did for `RowEntry`/`GroupSection`). LabController.cs: 1209 → 726 lines. Source: F-053 (D8 S1).

No value, colour, size, anchor, sibling order or callback changed. `LabController.Update` is untouched (INV-6): same fields, same per-frame path, only their initial assignment moved.

## Evidence

- Tests first: commit b8840d69 carries only `LabCanvasBuilderTests` (a PlayMode pinning test dumping the full UGUI hierarchy under `_root`), green on the old `LabController` (161/162) — a refactor pinning test, not red-first.
- Commit 5c19b216 is the extraction: same test, green, and its hierarchy dump is byte-identical before/after (`diff` of the two TestRun logs' dump sections, 134 lines, no output).
- Suite at 5c19b216 via `run-tests.sh --at`: EditMode 562/562, PlayMode 161/162 (1 documented skip).
- Screenshots over the bridge (Garage → Laboratory open, time frozen): before on main @ a2dcdda2, after on a scratch merge of the branch; same 830×345 frame; inside the Lab panel 298 of 128,982 pixels differ by more than 8/255 (max 28), i.e. the animated fog, bubbles and flicker at a different frozen instant; no edge moved (`.utmp/factory/chg035/shots/`).
- Red team (sonnet/medium, 83k) PASS: every moved body read against the pre-image, literals, colours, anchors, sibling order and wiring unchanged; the batched widget assignment is inert (nothing read those fields in between); INV-6: no hunk touches `Update`.

## Revert

`git revert`.
