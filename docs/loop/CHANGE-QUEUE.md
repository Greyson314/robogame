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
| 1 | CHG-003 | PresetBlueprintTests: drop the stale DefaultBuggy path | AUTO | T1 | test (zero Inconclusive) | S | LANDED 2026-09-17 (docs/changes/174) |
| 5 | CHG-008 | preset test coverage: Grappler + HoverTank, one list | AUTO | T1 | test (14 cases pass; slot-coverage guard) | S | SPEC |
| 6 | CHG-011 | rig audio mute (Grey's steer 2026-09-17) | AUTO | rig (D7) | test (batch session is muted) | S | BUILD |
| 2 | CHG-002 | doc drift from the 2026-09-16 sweep (six edits) | AUTO | D1, D2 | sweep re-run + Traces Validate | S | LANDED 2026-09-17 (docs/changes/175-doc-drift-sweep.md; red team KILL then PASS) |
| 3 | CHG-001 | provenance records: artgen manifest + unity-mcp package row + three Asset Store rows (D-004) | AUTO | L1, L2 | test (manifest covers every FBX) + provenance re-sweep | S | LANDED 2026-09-17 (docs/changes/177-provenance-records.md) |
| 4 | CHG-005 | delete the two unused packs (FattyPolyTurretFree + Part2Free, Le Tai's TrueShadow) | ASK, nod given (D-004) | L1 | grep of their GUIDs in Assets/_Project = 0 + suite green | S | LANDED 2026-09-17 (docs/changes/176-delete-unused-packs.md) |

Specs pending (not yet written): CHG-004 bomb-bay door cue (F-008, ASK: audible + visible; needs a read of the AudioCue / VfxKind enums first) · CHG-006 atomic blueprint and concoction writes (BACKLOG 4; UserBlueprintLibrary.cs:147, ConcoctionLibrary.cs:122, Tweakables.cs:512 write with File.WriteAllText) · CHG-007 enable MatchFlowTests.SpawnBot via a MinimalArena test scene (BACKLOG 2).

### CHG-003 PresetBlueprintTests: drop the stale DefaultBuggy path — status: LANDED (docs/changes/174, 2026-09-17; red team PASS with notes → F-021, F-022)
Class: AUTO (I1: a failing or flaky test fixed; the case is Inconclusive on every run)
Pillar or readiness item: LAUNCH-READINESS T1 (every inconclusive justified) — removes the one unjustified inconclusive from the suite.
Source: F-016; ASSUMPTIONS #10 (falsified).
Change: remove `BlueprintFolder + "/Blueprint_DefaultBuggy.asset"` from `PresetPaths` in Assets/_Project/Tests/EditMode/Blueprints/PresetBlueprintTests.cs:37. GameplayScaffolder.cs:38-39 records that session 61 retired the Buggy preset and none is in the repo, so the entry tests nothing; Assets/_Project/Tests/EditMode/Blueprints/ScriptedChassisBuilderTests.cs:177 names the same path and gets the same treatment. If a Buggy preset is meant to exist, that is a visible content addition and an ASK, not this change.
Acceptance: `PresetBlueprintTests.Preset_PassesValidation` reports 0 Inconclusive on the test rig (EditMode total drops by one; passed = total). Written first: a guard test `PresetPaths_AllExistOnDisk` that fails on main today because the Buggy path is absent, and passes after the edit.
Must not break: the presets already listed stay covered (the remaining twelve paths all exist on disk, 2026-09-17). Red team note: two scaffolder presets were never in the list (F-022 → CHG-008); this change neither adds nor removes coverage for them. No runtime code touched; no invariant.
Revert: `git revert` of the landing commit; leaves T1 as it is today.
Feel change? no

### CHG-002 doc drift from the 2026-09-16 sweep (six edits) — status: LANDED (docs/changes/175-doc-drift-sweep.md, 2026-09-17; red team round 1 KILL: tip-blocks.md:140 still said 250; fixed, round 2 PASS)
Class: AUTO (I1: doc drift against code, tier-2 docs, dangling TRACEs)
Pillar or readiness item: LAUNCH-READINESS D1 (README current) and D2 (architecture.md matches the code); best-practices § 12.5 currency.
Source: F-009, F-010, F-011, F-013, F-014, F-015.
Change: (a) HudPointerGuard.cs:32 trace `DOC:best-practices§statics` → `DOC:CLAUDE.md§Known failure modes`; (b) tip-blocks.md:16,31 leash damper 250 → 0, deliberate, citing RopeBlock.cs:597-624; (c) scalable-parts.md § Status → the shipped phases 0/1/1.5 with pointers to docs/changes/38 and 39; (d) architecture.md:53 `PlanetGravity` → `GravityField`; (e) README.md:254-262,276 date + Changelog pointing at docs/changes/README.md; (f) best-practices.md:573-574,778-780 strike the DevHud straggler note. One intent: drift found by one sweep; no behaviour.
Acceptance: Robogame → Traces → Validate reports zero dangling traces (bridge up), or the doc-drift sweep's file-level TRACE grep finds none (bridge down); a doc-drift re-sweep reports none of F-009–F-015. No unit test: prose and one comment.
Must not break: (a) edits a comment in a C# file, so the suite stays green (EditMode 529/530 → per CHG-003, PlayMode 152/153). Tier-2 docs stay consistent with docs/invariants.md.
Revert: `git revert`; leaves the six drifts as they are today.
Feel change? no

### CHG-001 provenance records: artgen manifest + unity-mcp package row — status: LANDED (docs/changes/177-provenance-records.md, 2026-09-17)
Class: AUTO (I1: provenance records, I6)
Pillar or readiness item: LAUNCH-READINESS L1 (generated assets record their generator) and L2 (third-party package rows).
Source: F-006, F-007.
Change: add `artgen/README.md` with a table mapping every FBX under Assets/_Project/Art/Models/** to its generator script in artgen/ (source of truth per docs/changes/130; FBX is a build artifact) plus the Blender version used; add a row to docs/PACKAGE_MODIFICATIONS.md for `com.coplaydev.unity-mcp` (origin: the GitHub pin in Packages/manifest.json:3; editor-only, non-shipping; its license as the package's own file states it); add three rows to docs/subsystems/art-direction.md § Imported Assets for Stylized Nature Pack (ArenaProps.cs:57), Polytope Studio trees (ArenaProps.cs:276) and Handpainted Grass and Ground Textures (FluffGround.cs:275): source Unity Asset Store, license the Unity Asset Store EULA, per Grey's D-004 line of 2026-09-17 quoted in NEEDS-GREY § ANSWERED.
Acceptance: an EditMode test `ArtgenManifestTests.EveryGeneratedFbxHasAManifestRow` that parses artgen/README.md's table and asserts every .fbx under Assets/_Project/Art/Models has a row and every row's script exists on disk (written first; fails on main today: no manifest); and a provenance re-sweep that reports none of F-001, F-002, F-003, F-006, F-007.
Must not break: nothing at runtime; no scene, asset or import setting touched. INV-8 n/a.
Revert: `git revert`; leaves L1/L2 as they are today.
Feel change? no

### CHG-005 delete the two unused imported packs — status: LANDED (docs/changes/176-delete-unused-packs.md, 2026-09-17)
Class: ASK (I1: deleting assets needs Grey's nod). NOD RECORDED: Grey, 2026-09-17T01:52:02Z via /inbox, "FattyPolyTurretFree + Part2Free and Le Tai's TrueShadow: Delete both." (NEEDS-GREY § ANSWERED D-004).
Pillar or readiness item: LAUNCH-READINESS L1 (I6: a product that ships with an unlicensed asset is unshippable; these two have no license on disk and no use).
Source: F-004, F-005; D-004.
Change: `git rm -r` Assets/FattyPolyTurretFree, Assets/FattyPolyTurretPart2Free and "Assets/Le Tai's Asset/TrueShadow" (and their .meta files; if "Le Tai's Asset" then holds nothing else, the folder too). Nothing under Assets/_Project references them by script (provenance sweep 2026-09-16).
Acceptance: (1) before deleting, collect every GUID from the packs' .meta files and grep Assets/_Project (scenes, prefabs, materials, assets) for them: 0 hits, recorded in the docs/changes entry; (2) the suite stays green (EditMode 530/530, PlayMode 152/153); (3) the packs are gone from the tree.
Must not break: any scene, prefab or material that referenced a pack asset by GUID (the pre-check above is the guard; a hit turns this into a DROP or an ASK). No invariant touched; no runtime code.
Revert: `git revert` of the merge restores both packs byte-for-byte.
Feel change? no (nothing in a shipped scene references them, per the pre-check)

### CHG-008 preset test coverage: Grappler + HoverTank, one list — status: SPEC
Class: AUTO (I1: new test coverage)
Pillar or readiness item: LAUNCH-READINESS T1 — every shipped preset validated by the suite.
Source: F-022 (red team on CHG-003, 2026-09-17).
Change: add `Blueprint_DefaultGrappler.asset` (scaffolder slot 2, the preset that replaced Buggy) and `Blueprint_DefaultHoverTank.asset` (slot 8) to `PresetBlueprintTests.PresetPaths`; make `ScriptedChassisBuilderTests.EveryShippedPreset_PassesLibraryAwareValidation` iterate the same list (an `internal static` on PresetBlueprintTests, same asmdef) so the two lists cannot diverge; reword the CHG-003 guard's docstring to "list → disk" only.
Acceptance: `Preset_PassesValidation` runs 14 cases, all Passed; a new test `PresetPaths_CoverEveryScaffolderSlot` compares `PresetPaths` against the ten slot paths of GameplayScaffolder.cs:975-982 (hard-coded in the test with a comment naming that line, since the scaffolder's constants live in an Editor asmdef) — written first, fails on main today (Grappler, HoverTank missing).
Must not break: nothing at runtime; the guard from CHG-003 keeps passing.
Revert: `git revert`; back to twelve validated presets.
Feel change? no

### CHG-011 rig audio mute: no game audio from batch or factory Editors — status: SPEC
Class: AUTO (I1: tooling, harnesses and instruments; not visible to a player, never runs in a build)
Pillar or readiness item: the rig (D7): Grey's tolerance of the factory is the binding constraint; the batch PlayMode runs played the garage music through the desktop speakers every two minutes (Grey, 2026-09-17T02:33Z, INBOX).
Source: INBOX 2026-09-17T02:33:13Z (Grey's steer).
Change: `Assets/_Project/Scripts/Tools/Editor/RigAudioMute.cs`, an `[InitializeOnLoad]` editor class that sets `EditorUtility.audioMasterMute = true` when `Application.isBatchMode` or the env var `ROBOGAME_RIG_MUTE` is `1`; `Start-Factory.ps1` sets that env var for the Editor it starts. The FMOD integration (RuntimeManager.cs:1513) and MusicConductor (:256) already mirror the Editor mute onto their buses, so one switch covers MPTK music, FMOD SFX and the bank-less music group. A human's Editor (no env var, not batch) is untouched.
Acceptance: EditMode test `RigAudioMuteTests.BatchMode_HasEditorAudioMuted`: in a batch session `EditorUtility.audioMasterMute` must be true (ignored in a human's Editor). Written first; fails on the rig today. Plus Grey's word that the speakers stay quiet during rig runs (FYI asks for it).
Must not break: nothing at runtime (editor-only assembly, Robogame.Tools.Editor); the suite's audio tests must still pass with the mute on (they assert routing, not audible output).
Revert: `git revert`; the music comes back.
Feel change? no (rig only)

## BUILT THIS SHIFT (moved to docs/changes on landing; tally for HEALTH)

- shift 2, 2026-09-16: nothing built; the shift ended on the plan cap before rung 1.
- shift 3, 2026-09-17: CHG-003 landed (docs/changes/174); CHG-002 landed (docs/changes/175-doc-drift-sweep.md); CHG-005 landed (docs/changes/176-delete-unused-packs.md); CHG-001 landed (docs/changes/177-provenance-records.md).
