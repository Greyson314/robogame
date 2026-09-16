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
| 1 | CHG-003 | PresetBlueprintTests: drop the stale DefaultBuggy path | AUTO | T1 | test (zero Inconclusive) | S | SPEC |
| 2 | CHG-002 | doc drift from the 2026-09-16 sweep (six edits) | AUTO | D1, D2 | sweep re-run + Traces Validate | S | SPEC |
| 3 | CHG-001 | provenance records: artgen manifest + unity-mcp package row | AUTO | L1, L2 | test (manifest covers every FBX) | S | SPEC |

Specs pending (not yet written): CHG-004 bomb-bay door cue (F-008, ASK: audible + visible; needs a read of the AudioCue / VfxKind enums first) · CHG-005 atomic blueprint and concoction writes (BACKLOG 4; UserBlueprintLibrary.cs:147, ConcoctionLibrary.cs:122, Tweakables.cs:512 write with File.WriteAllText) · CHG-006 enable MatchFlowTests.SpawnBot via a MinimalArena test scene (BACKLOG 2).

### CHG-003 PresetBlueprintTests: drop the stale DefaultBuggy path — status: SPEC
Class: AUTO (I1: a failing or flaky test fixed; the case is Inconclusive on every run)
Pillar or readiness item: LAUNCH-READINESS T1 (every inconclusive justified) — removes the one unjustified inconclusive from the suite.
Source: F-016; ASSUMPTIONS #10 (falsified).
Change: remove `BlueprintFolder + "/Blueprint_DefaultBuggy.asset"` from `PresetPaths` in Assets/_Project/Tests/EditMode/Blueprints/PresetBlueprintTests.cs:37. GameplayScaffolder.cs:38-39 records that session 61 retired the Buggy preset and none is in the repo, so the entry tests nothing; Assets/_Project/Tests/EditMode/Blueprints/ScriptedChassisBuilderTests.cs:177 names the same path and gets the same treatment. If a Buggy preset is meant to exist, that is a visible content addition and an ASK, not this change.
Acceptance: `PresetBlueprintTests.Preset_PassesValidation` reports 0 Inconclusive on the test rig (EditMode total drops by one; passed = total). Written first: a guard test `PresetPaths_AllExistOnDisk` that fails on main today because the Buggy path is absent, and passes after the edit.
Must not break: every preset the scaffolder produces stays covered (the remaining ten paths all exist on disk, 2026-09-16). No runtime code touched; no invariant.
Revert: `git revert` of the landing commit; leaves T1 as it is today.
Feel change? no

### CHG-002 doc drift from the 2026-09-16 sweep (six edits) — status: SPEC
Class: AUTO (I1: doc drift against code, tier-2 docs, dangling TRACEs)
Pillar or readiness item: LAUNCH-READINESS D1 (README current) and D2 (architecture.md matches the code); best-practices § 12.5 currency.
Source: F-009, F-010, F-011, F-013, F-014, F-015.
Change: (a) HudPointerGuard.cs:32 trace `DOC:best-practices§statics` → `DOC:CLAUDE.md§Known failure modes`; (b) tip-blocks.md:16,31 leash damper 250 → 0, deliberate, citing RopeBlock.cs:597-624; (c) scalable-parts.md § Status → the shipped phases 0/1/1.5 with pointers to docs/changes/38 and 39; (d) architecture.md:53 `PlanetGravity` → `GravityField`; (e) README.md:254-262,276 date + Changelog pointing at docs/changes/README.md; (f) best-practices.md:573-574,778-780 strike the DevHud straggler note. One intent: drift found by one sweep; no behaviour.
Acceptance: Robogame → Traces → Validate reports zero dangling traces (bridge up), or the doc-drift sweep's file-level TRACE grep finds none (bridge down); a doc-drift re-sweep reports none of F-009–F-015. No unit test: prose and one comment.
Must not break: (a) edits a comment in a C# file, so the suite stays green (EditMode 529/530 → per CHG-003, PlayMode 152/153). Tier-2 docs stay consistent with docs/invariants.md.
Revert: `git revert`; leaves the six drifts as they are today.
Feel change? no

### CHG-001 provenance records: artgen manifest + unity-mcp package row — status: SPEC
Class: AUTO (I1: provenance records, I6)
Pillar or readiness item: LAUNCH-READINESS L1 (generated assets record their generator) and L2 (third-party package rows).
Source: F-006, F-007.
Change: add `artgen/README.md` with a table mapping every FBX under Assets/_Project/Art/Models/** to its generator script in artgen/ (source of truth per docs/changes/130; FBX is a build artifact) plus the Blender version used; add a row to docs/PACKAGE_MODIFICATIONS.md for `com.coplaydev.unity-mcp` (origin: the GitHub pin in Packages/manifest.json:3; editor-only, non-shipping; its license as the package's own file states it).
Acceptance: an EditMode test `ArtgenManifestTests.EveryGeneratedFbxHasAManifestRow` that parses artgen/README.md's table and asserts every .fbx under Assets/_Project/Art/Models has a row and every row's script exists on disk. Written first; fails on main today (no manifest).
Must not break: nothing at runtime; no scene, asset or import setting touched. INV-8 n/a.
Revert: `git revert`; leaves L1/L2 as they are today.
Feel change? no

## BUILT THIS SHIFT (moved to docs/changes on landing; tally for HEALTH)

(shift 2, 2026-09-16: nothing built; the shift ended on the plan cap before rung 1)
