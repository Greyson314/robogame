# 182 — docs/TRACES.md rebuilt from the Editor; the doc-drift sweep checks it (LOG-182)

Landed by the factory 2026-09-17T18:55:27Z. Change CHG-014, branch `chg/014-traces-index` @ cfcfcfbd87. Gate: suite PASS, perf N/A, red team PASS — acceptance: a second Rebuild Index on today's main identical to the branch file (foreground 18:46Z) and the red team's independent Python re-derivation of all 111 rows, zero differing lines; suite: red team's run on main (identical code) EditMode 537/537, PlayMode 152/153, 2m04s (.utmp/factory/gate/CHG-014-redteam.md); red team PASS, notes 1-4 → F-041, F-042; perf N/A; console clean over the bridge.

Factory shift 5 on the desktop, 2026-09-17, branch `chg/014-traces-index` (built with git plumbing, no checkout).
Class AUTO (charter I1: doc drift against code, dangling TRACEs; the file is generated). Two files: a generated index and a sweep brief.

## Intent

docs/TRACES.md, the code→decision breadcrumb index, is regenerated only by Robogame → Traces →
Rebuild Index in the Editor, and the Editor had not been reachable from a shift until this
one. The red team on CHG-002 counted 17 of its 70 sites stale (FINDINGS F-023); by now two
whole ADR sections (ADR-0008, ADR-0009) had never been indexed.

## What shipped

- docs/TRACES.md rebuilt over the live bridge on the factory Editor at main @ 8751b11e
  (console: "[Traces] Wrote docs/TRACES.md — 111 traces, 0 dangling."): 79 insertions, 20
  deletions; the ADR-0008 and ADR-0009 sections, line numbers moved by later edits, and
  HudPointerGuard's retargeted trace from CHG-002.
- The sweeper's doc-drift sweep (.claude/agents/sweeper.md) now runs Rebuild Index after
  Validate and reports a non-empty `git diff --stat docs/TRACES.md` as one inconsistency
  finding, then restores the file, so the index cannot silently drift again between shifts.

## Verification

Deterministic: a second Rebuild Index on the same code (the clone on chg/015, identical C#)
produced a file identical to the committed one (CRLF-insensitive diff empty, 2026-09-17T18:1xZ).
Validate: 0 dangling. The red team re-derived the whole index independently (a Python port of
`ContinualTraces.Scan`/`RebuildIndex` run at main @ 9fff5c65) and diffed all 111 rows against
the branch's file: zero differing lines. Suite (red team's run on main, identical code):
EditMode 537/537, PlayMode 152/153 (the documented skip), 0 failures, 2m04s wall. Perf: N/A.
Console: bridge up, 0 errors.

## Owed (red team notes → findings)

- The scanner's regex only matches `TRACE[` immediately after `//`: 131 raw `TRACE[` lines in
  Assets become 111 indexed; the gap is 6 self-excluded in ContinualTraces.cs, 3 on `///` lines
  and 11 mid-comment sites, among them BuildModeController.cs:92, the ONLY INV-2 anchor, so
  `## INV-2` is absent from the index (F-041, a tooling fix with a test).
- The sweep sentence has no bridge-down rule (a failed Rebuild Index leaves an empty diff and
  no finding: a silent false negative), and sweeper.md's closing "What you DON'T do" still says
  read-only while the step writes and restores docs/TRACES.md; it also assumes the live Editor's
  project root is the sweeper's git root (F-042, a brief fix).

## Revert

`git revert`; the 17 stale sites and the two missing sections come back.
