# 188 — The KnownInvalid quarantine is proven both ways by a synthetic bad plan (LOG-188)

Landed by the factory 2026-09-17T22:43:41Z. Change CHG-016, branch `chg/016-quarantine-self-test` @ 441dcc83c2. Gate: suite PASS, perf N/A, red team N/A — Test file only (D16b: coverage; no red team). Tests first, three rig runs by the builder: green 543/543 with the matcher intact, RED 542/543 with the negative half's comparison replaced by a literal true (the new test the one failure), green again restored (.utmp/factory/gate/CHG-016-build.md). Full suite on the branch's checkout: EditMode 543/543, PlayMode 152/153 (documented skip), 0 failures (.utmp/factory/gate/CHG-016-suite.txt)..

Factory shift 7, 2026-09-17, branch `chg/016-quarantine-self-test` (a builder on the checkout, tests first).
Class AUTO (I1: new test coverage). Red team N/A (D16b: a test file). One file, one test method, 55 lines.

## Intent

CHG-008 introduced the `KnownInvalid` quarantine in `PresetBlueprintTests`: a table of preset paths
with the error text each is expected to fail with, so a known-bad preset stays visible in the suite
and the entry expires the moment the preset is fixed. CHG-013 emptied the table, which left the
matching logic exercised by nothing: a wrong expected string would have failed no test (FINDINGS
F-034, CLAUDE.md Rule 9).

## What shipped

One EditMode test, `KnownInvalidQuarantineCheck_MatchesRightTextRejectsWrongText`, builds a
synthetic blueprint plan (a cube stacked on a hoverblade, the same defect class the Hover Tank
had; no shipped preset touched), runs it through the same library-aware `BlueprintValidator`
call the quarantine uses, and asserts the quarantine's matching passes with the right expected
substring and throws with a wrong one.

## Verification

Tests first, three rig runs recorded by the builder (`.utmp/factory/gate/CHG-016-build.md`):
green with the matcher intact (543/543), red with the negative half's comparison replaced by a
literal `true` (542/543, the new test the one failure, "Expected AssertionException, none
thrown"), green again restored. Full suite on the branch's checkout:
`.utmp/factory/gate/CHG-016-suite.txt`.

## Aftermath

F-034 closed. LAUNCH-READINESS T1's coverage note: the quarantine pattern is now proven both
ways, so a future entry cannot pass for the wrong reason.
