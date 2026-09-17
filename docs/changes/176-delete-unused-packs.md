# 176 — Delete the two unused, unlicensed packs (FattyPolyTurret, TrueShadow) on Grey's nod (LOG-176)

Landed by the factory 2026-09-17T02:30:45Z. Change CHG-005, branch `chg/005-delete-unused-packs` @ 62d8f3c9e3. Gate: suite PASS, perf N/A, red team PASS — suite: foreground run-tests.sh All on 62d8f3c9 (.utmp/factory/gate/CHG-005-suite.txt); red team PASS (.utmp/factory/gate/CHG-005-redteam.md): 252 GUIDs 0 hits, shader names, Resources paths, asmdefs, type collisions, duplicate assets all clean; note -> F-025 (stale LETAI_TRUESHADOW define); perf N/A; console BRIDGE_DOWN (rig logs clean).

Factory shift 3 on the desktop, 2026-09-17, branch `chg/005-delete-unused-packs`.
Class ASK (charter I1: deleting assets needs Grey's nod). Nod recorded verbatim: Grey,
2026-09-17T01:52:02Z via /inbox, "FattyPolyTurretFree + Part2Free and Le Tai's TrueShadow:
Delete both." (NEEDS-GREY § ANSWERED, D-004).

## Intent

Two imported packs sat in Assets/ with no license on disk and no use anywhere in the
project (FINDINGS F-004, F-005; provenance sweep 2026-09-16). Charter I6: a product that
aspires to ship with one unlicensed asset in it is unshippable, and the audit is cheaper
at entry than at release. Grey chose deletion over licensing for both.

## What shipped

- Removed `Assets/FattyPolyTurretFree/` (54 meta files; its Readme.txt was the Part2
  pack's), `Assets/FattyPolyTurretPart2Free/` (18) and `Assets/Le Tai's Asset/TrueShadow/`
  (176; the parent folder held nothing else and went with it).

## Verification

Pre-check (the spec's guard, run before deleting, 2026-09-17T02:05Z): all 252 unique GUIDs (the pre-check counted 248 asset metas; the red team added the three folder metas and one more, all 0 hits)
from the three packs' .meta files grepped across every .unity, .prefab, .mat, .asset and
.controller under Assets/ (outside the packs) and under ProjectSettings/: 0 hits. No script
under Assets/_Project references the packs (sweep 2026-09-16).

Suite, test rig, branch @ 62d8f3c9, 2026-09-17T02:21-02:23Z (`run-tests.sh All`): EditMode 530/530
(0 failed, 0 inconclusive), PlayMode 152/153 (0 failed; the one skip is the documented
`MatchFlowTests.SpawnBot`); 0 compile errors; no log line names the deleted packs. Red team: see the
evidence block above.

Console: bridge down this shift (LOOP-STATE § RIG); the GUID pre-check is the guard against
a missing-reference warning, since nothing referenced them.

## Revert

`git revert` of the merge commit restores all three folders byte-for-byte.
