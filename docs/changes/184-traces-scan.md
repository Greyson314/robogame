# 184 — ContinualTraces scans traces anywhere in a comment; the doc-drift sweep's index step has rules (LOG-184)

Landed by the factory 2026-09-17T22:21:54Z. Change CHG-019, branch `chg/019-traces-scan` @ bdc085c1d6. Gate: suite PASS, perf N/A, red team N/A — red team N/A under D16b: Editor-only tooling (ContinualTraces.cs), a new EditMode test and a sweep brief; no shipped runtime code, no NOD action, no invariant. suite: builder's runs on the branch, tests-first EditMode 540/542 (the two intended reds), after the fix run-tests.sh All EditMode 542/542, PlayMode 152/153 (.utmp/factory/gate/CHG-019-suite.txt); index 111→125 traces, 0 dangling, second rebuild byte-identical; perf N/A; console: session connector dead, rig logs used; 8080 survived every batch quit (BACKLOG 20 true positive).

Factory shift 6 on the desktop, 2026-09-17, branch `chg/019-traces-scan` (built by a builder fieldhand on the rig).
Class AUTO (charter I1: tooling and instruments; doc drift). Red team N/A (D16b, narrowed 2026-09-17: an Editor-only tool under Assets/_Project/Scripts/Tools/Editor plus a sweep brief; no shipped runtime code, no NOD-list action, no invariant touched); the green suite is the gate.

## Intent

The trace scanner's regex only matched `TRACE[` immediately after `//`, so of 131 raw `TRACE[`
lines under Assets it indexed 111: eleven traces written mid-comment and three on `///` doc lines
were invisible, among them BuildModeController.cs:92, the only INV-2 anchor, so `## INV-2` never
appeared in docs/TRACES.md and a dangling INV-2 trace would never have been reported (FINDINGS
F-041, red team on CHG-014). The sweep step CHG-014 added had no bridge-down rule and the brief's
closing section still called the sweeper read-only (F-042).

## What shipped

- `ContinualTraces.cs`: the marker is matched anywhere after a comment start, on `//` and `///`
  lines alike, through a testable `TryParseMarker(line, out id, out note)` used by the scan loop;
  the id resolution is unchanged; the self-exclusion (ContinualTraces.cs, whose index-header
  literal would match) now also skips ContinualTracesTests.cs, whose string fixtures produced a
  phantom `## LOG-5` and a nonsense `## id` anchor on the first rebuild (disclosed by the builder;
  the syntax-unaware line scanner cannot tell a fixture from a marker).
- `Assets/_Project/Tests/EditMode/Tools/ContinualTracesTests.cs` (new, 5 cases): plain, mid-comment,
  `///`, trailing-after-code, and a negative. Written first: the mid-comment and `///` cases were
  red on main's regex (EditMode 540/542), green after the fix (542/542).
- docs/TRACES.md regenerated from the batch rig with the widened scanner (`-executeMethod`, log in
  `.utmp/factory/gate/CHG-019-rebuild.log`): 14 more sites, `## INV-2` present with
  BuildModeController.cs:92, 0 dangling; every previously indexed row is still present.
- `.claude/agents/sweeper.md`: the index step returns `BRIDGE_DOWN` when the menu item fails, the
  closing section names the one write-and-restore the sweep performs, and the step checks the live
  Editor's project is this clone before trusting the diff.

## Verification

Suite on the branch (`.utmp/factory/gate/CHG-019-suite.txt`): tests-first EditMode 540/542 with the
two intended failures; after the fix `run-tests.sh All` EditMode 542/542, PlayMode 152/153 (the one
documented skip), 0 failures. The MCP server on 8080 (handshake-tracked, auto-started by the
Editor this shift) survived every batch quit of these runs: BACKLOG 20's owed true-positive proof of
CHG-015. Perf: N/A. Console: this session's connector was dead (dialled before 8080 was up); rig
logs used.

## Revert

`git revert`; the 14 sites drop out of the index and the brief loses its rules.
