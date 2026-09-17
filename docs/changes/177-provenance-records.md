# 177 — Provenance records: artgen manifest with guards, MCP package origin, three Asset Store rows (LOG-177)

Landed by the factory 2026-09-17T02:50:53Z. Change CHG-001, branch `chg/001-provenance-records` @ 739c777dcc. Gate: suite PASS, perf N/A, red team PASS — suite: tests-first fail, impl pass, fix pass + negative proof (.utmp/factory/gate/CHG-001-suite.txt); red team round 1 KILL (token parser checked 1 of 33 rows) -> fixes at 739c777d -> round 2 PASS with five mutations (.utmp/factory/gate/CHG-001-redteam.md); perf N/A; console BRIDGE_DOWN (0 warning CS).

Factory shift 3 on the desktop, 2026-09-17, branch `chg/001-provenance-records`.
Class AUTO (charter I1: provenance records, I6; new test coverage). No runtime code touched.

## Intent

Charter I6: nothing enters the repo without a known license, and generated assets record
their generator. The 2026-09-16 provenance sweep found three wired Asset Store packs with
no license record (F-001–F-003), 33 generated FBX files with no manifest (F-006) and one
package with no origin row (F-007). Grey named the three packs' source and license on
2026-09-17 (NEEDS-GREY § ANSWERED, D-004). LAUNCH-READINESS L1 and L2.

## What shipped

- `artgen/README.md`: the generated-asset manifest. One row per FBX under
  `Assets/_Project/Art/Models/` (33), naming the exporter script and the study script it
  builds from, and the Blender version of the last export (5.1.2, docs/changes/130). The
  mapping was read from the scripts themselves (`inv_export.py`'s STATICS table and named
  exporters; the three paper-punk scripts; `rock_01.py`), not inferred from filenames.
- `ArtgenManifestTests` (EditMode, new folder `Tests/EditMode/Provenance/`):
  `EveryGeneratedFbxHasAManifestRow`, `EveryManifestRowNamesAnExistingScript` and
  `EveryManifestRowNamesAnExistingFbx`. Red team round 1 was a KILL: the script check stripped
  only a cell's outer backticks, so 32 of 33 rows were never checked; the backtick is now a
  token delimiter, the orphan-row check was added, and the negative was proved on the rig
  (with `artgen/inv_cube.py` renamed, EditMode 532/533 and the one failure is the script test; restored). Written first: both failed on the branch before the manifest existed (test rig, 2026-09-17T02:30-02:31Z: EditMode: 530/532 passed, 2 failed, 0 inconclusive.; both cases Failed, no artgen/README.md).
- `docs/PACKAGE_MODIFICATIONS.md`: a "package origins" table with `com.coplaydev.unity-mcp`
  (GitHub pin v9.7.3, MIT per the repository's LICENSE read at `main` on 2026-09-17;
  editor-only, nothing of it ships).
- `docs/subsystems/art-direction.md` § Imported Assets: rows for Stylized Nature Pack
  (ArenaProps.cs:57), Polytope Studio trees (ArenaProps.cs:276) and Handpainted Grass and
  Ground Textures (FluffGround.cs:275): source Unity Asset Store, license the Asset Store
  EULA, as Grey stated.

## Verification

Suite, test rig, branch @ 739c777d, 2026-09-17T02:45-02:46Z (`run-tests.sh All`): EditMode 533/533
(0 failed, 0 inconclusive; all three new tests pass), PlayMode 152/153 (0 failed; the one skip is the
documented `MatchFlowTests.SpawnBot`). Red team round 2: PASS (five mutations of the manifest all fail the right test). Latent, not live: a bold `**x.py**` cell would escape the token check; no row uses that shape; a per-row token count closes it on the next touch.

Perf: N/A (docs and an EditMode test). Console: bridge down this shift (LOOP-STATE § RIG).

## Revert

`git revert` of the merge commit removes the manifest, the test and the four rows; L1 and
L2 return to "in progress".
