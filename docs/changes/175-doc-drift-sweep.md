# 175 — Doc drift from the 2026-09-16 sweep: six docs say what the code does (LOG-175)

Landed by the factory 2026-09-17T02:21:05Z. Change CHG-002, branch `chg/002-doc-drift-sweep` @ 4600f4448f. Gate: suite PASS, perf N/A, red team PASS — suite: foreground run-tests.sh All twice (80d328e3 and 4600f444), .utmp/factory/gate/CHG-002-suite.txt; red team: round 1 KILL (tip-blocks.md:140 still 250) -> fixes -> round 2 PASS on 4600f444, .utmp/factory/gate/CHG-002-redteam.md; perf N/A; console BRIDGE_DOWN (0 warning CS on the red team's rerun; the 18 CS0618 on a full recompile are pre-existing -> F-024).

Factory shift 3 on the desktop, 2026-09-17, branch `chg/002-doc-drift-sweep`.
Class AUTO (charter I1: doc drift against code, tier-2 docs, dangling TRACEs). One C# comment edit; no behaviour.

## Intent

The 2026-09-16 doc-drift, best-practices and readiness sweeps found six places where
a doc contradicts the code it describes (FINDINGS F-009, F-010, F-011, F-013, F-014,
F-015; LAUNCH-READINESS D1, D2). One change, one intent: the docs say what the code does.

## What shipped

- `HudPointerGuard.cs`: the trace `DOC:best-practices§statics` named a heading that does
  not exist (the validator resolves the file only, so it never reported it: misleading
  prose, not a dangling link); retargeted to `DOC:CLAUDE.md§Known failure modes`, the
  id two other sites already use, so the statics-reset site stays in the trace index (F-009).
- `docs/subsystems/tip-blocks.md`: the leash's damper is 0 by design
  (`Movement/RopeBlock.cs:624`), not 250: the diagram, the constraint table and the
  closing "default is fine throughout" line (the red team caught the third) (F-010).
- `docs/subsystems/scalable-parts.md` § Status: Phase 1 (session 38) and 1.5 (session 39)
  shipped; it said "no code yet" (F-011).
- `docs/changes/architecture.md`: `PlanetGravity` does not exist; the gravity math is
  `GravityField` in Robogame.Core (F-013).
- `README.md`: the Changelog now points at docs/changes/README.md; the date is current (F-014).
- `docs/best-practices.md` § 12.5 and the open-items list: the DevHud `FindObjectOfType`
  straggler is gone from the code; the notes that said otherwise are struck, and the three
  `#if`-gated fallbacks (ObjectiveHud, FpsCounter, PerformanceHud) are named (F-015).
- Red team round 1 was a KILL (tip-blocks.md:140 still said 250); fixed and re-verified.

## Verification

Suite, test rig, branch @ 80d328e3, 2026-09-17T02:06-02:08Z (`run-tests.sh All`): EditMode 530/530
(0 failed, 0 inconclusive), PlayMode 152/153 (0 failed; the one skip is the documented
`MatchFlowTests.SpawnBot`). Red team: see the evidence block above.

File-level TRACE check: no `TRACE[DOC:...]` id in Assets/_Project/Scripts resolves to a
missing docs/ heading after this change (Robogame → Traces → Validate needs the bridge,
which is down this shift; see LOOP-STATE § RIG). Perf: N/A (no runtime code).

## Revert

`git revert` of the merge commit; the six drifts come back exactly as the sweep found them.
