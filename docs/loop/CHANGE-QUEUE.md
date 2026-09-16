# CHANGE-QUEUE — specs awaiting build (D4 CHANGE tier), ranked by pillar value × cheapness

## ENGINEERING RULES (binding on every change; the product earns these — promote from LESSONS.md § METHOD LESSONS when they recur)

1. The ten invariants in docs/invariants.md, by number, are rules here too; a change that touches one names it in "Must not break".
2. Profile before claiming a perf characteristic (INV-7): a Profiler capture or a harness row, on this rig, with its band.
3. A bug found is a test written; a flaky test is a bug.
4. Every new feature ships with VFX + audio (INV-8); a missing cue is declared, not skipped.
5. Fieldhands edit DISJOINT files in the factory clone; the foreground commits named paths; nothing is ever edited under `.claude/worktrees/` (the Editor watches the clone root; the PreToolUse hook enforces it).
6. The test-rig worktree is never opened in an Editor; `run-tests.sh` clobbers it on every run.
7. The batch rig (`-nographics`) and the live Editor are different rigs; their numbers are not compared.
8. Load-bearing lines whose reason is not obvious carry a `// TRACE[id]: note` (CLAUDE.md); a change that cites an ADR, an invariant or a session log anchors it.
9. One docs/changes entry per landed change, written by `land.py` from the change's evidence; never hand-appended to an older entry.
10. Features come through the idea backlog: /ideate's `rejected` is never re-pitched; a new feature the loop conceives enters the backlog as `proposed` before it enters this queue.
11. The I1 class is written on the spec; an ASK-class change waits on the board and never starts BUILD before its check-off.

## SPEC FORMAT (one page, written BEFORE code)

    ### CHG-NNN title — status: SPEC / APPROVE-WAIT / TESTS / BUILD / GATE / LANDED
    Class: AUTO | ASK (I1)
    Pillar or readiness item: which, and how this serves it
    Source: F-NNN / SPIKES L<n> / backlog item / NEEDS-GREY verdict
    Change: what, in one paragraph
    Acceptance: a test name, a proxy delta with its band, or ONE playtest question — never "is it fun"
    Must not break: invariants and subsystems touched
    Revert: how; what state it leaves
    Feel change? yes → a PLAY entry after landing; no

## QUEUE

| rank | id | title | class | pillar / readiness | acceptance kind | est. cost | status |
|---|---|---|---|---|---|---|---|

(empty — HANDOFF (6) fills it)

## BUILT THIS SHIFT (moved to docs/changes on landing; tally for HEALTH)

(empty)
