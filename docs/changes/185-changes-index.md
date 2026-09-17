# 185 — The changes index keeps up with the ledger: land.py writes the Sessions row; rows 132-183 backfilled (LOG-185)

Landed by the factory 2026-09-17T22:24:01Z. Change CHG-017, branch `chg/017-changes-index` @ 0594361f01. Gate: suite PASS, perf N/A, red team N/A — red team N/A under D16b: factory tooling (land.py, a backfill script, a unit test) and a doc index; no shipped runtime code, no NOD action, no invariant. suite: run-tests.sh All on main @ f9b79696 (the Assets tree the branch merges onto, unchanged by it) EditMode 542/542, PlayMode 152/153, 22:23Z; factory unit tests on the branch's files 26/26; backfill idempotent (second run adds 0); perf N/A; console: session connector dead, rig logs used.

Factory shift 6 on the desktop, 2026-09-17, branch `chg/017-changes-index` (built scratch-only by a builder fieldhand, assembled with git plumbing).
Class AUTO (charter I1: doc drift against code, "the changes index" by name; an instrument). Red team N/A (D16b: tooling and a doc; no shipped runtime code, no NOD action, no invariant).

## Intent

CLAUDE.md § Active work sends every session to docs/changes/README.md for "the current
session's intent", and its "Sessions (newest first)" table stopped at 131 while the ledger was
at 183: 52 entries, Grey's sessions 132–173 and the factory's 174–183, were unlisted, because
`land.py` wrote the docs/changes entry, the LOOP-STATE bullet and the NEEDS-GREY line but never
the index row (FINDINGS F-036).

## What shipped

- `land.py` step 6b: right after the docs/changes entry is written, the chain inserts
  `| NNN | [title](NNN-slug.md) |` as the first row under the table's separator, adds the README
  to the named paths it commits, prints the row under `--dry-run`, and REFUSES (before the
  merge) when the README has no such table, so a missing index fails loud.
- `backfill_changes_index.py`: idempotent, forward-only (rows above the table's current top),
  titles from each entry's first heading with the `# NNN — ` prefix and ` (LOG-NNN)` suffix
  stripped; `--dry-run`, `--readme`, `--changes-dir`. Run once: 52 rows, 132–183, newest first;
  a second run adds 0. LF preserved (the script writes with an explicit newline).
- docs/changes/README.md: the 52 rows; nothing else in the file changed (52 insertions, 0
  deletions).
- Two new unit tests in test_land.py: the row lands at the top with its exact text; a README
  without the table refuses before any merge. The suite's fixture gained a minimal table.

## Verification

Factory unit tests on the branch's files (a `git archive` into scratch): 26 tests, OK (24
before). Backfill idempotence: the dry run on the branch's README adds 0 rows. Unity suite on main @ f9b79696
(the Assets tree this branch merges onto; nothing under Assets/ changes here): EditMode 542/542,
PlayMode 152/153 (the documented skip), 0 failures, 2026-09-17T22:23Z. Console: the bridge is
down for this session (dialled before 8080 was up); rig logs used.

## Out of scope, recorded

Sessions 108–122 are also absent from the table, a gap BELOW its top row that predates the
factory; the forward-only backfill leaves it (F-044, a one-flag extension of the script).

## Revert

`git revert`; the table stops at 131 again and the chain stops writing rows.
