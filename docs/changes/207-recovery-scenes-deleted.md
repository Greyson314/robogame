# 207 — The five tracked crash-recovery scenes under Assets/_Recovery are deleted (D-009) (LOG-207)

Landed by the factory 2026-09-19T20:18:36Z. Change CHG-037, branch `chg/037-recovery-scenes` @ 732c3baf58. Gate: suite PASS, perf N/A, red team PASS — suite 575/575 + 165/166 at 732c3baf (.utmp/factory/gate-037-038.log); perf N/A: no code; red team sonnet/medium PASS 36k (NOD-list action, D16b).

## What changed

The five tracked crash-recovery scenes under `Assets/_Recovery/` (`0.unity`, `0 (1)`–`0 (4).unity`), their metas and the folder's own `Assets/_Recovery.meta` are deleted from the repository (11 paths, 4,800 lines). Unity writes such a scene whenever an Editor dies with an unsaved scene; five had been committed by "sync editor state" commits. `.gitignore` gains `/Assets/_Recovery.meta` beside the existing `/Assets/_Recovery/` rule, because the folder survives on disk with ignored recoveries in it and the Editor re-creates its meta. Untracked recoveries on disk were left alone. Source: F-060 (b).

Grey's nod (I1 NOD list: deleting scenes), verbatim, INBOX 2026-09-19T05:34:30Z: "decide D-009: delete".

## Evidence

- Before the build: none of the six metas' GUIDs is referenced anywhere outside the folder (`git grep`, 0 of 6); `ProjectSettings/EditorBuildSettings.asset` names none of them; the only other mention of `_Recovery` in the tree is the `.gitignore` rule.
- Suite at 732c3baf via `run-tests.sh --at`: EditMode 575/575, PlayMode 165/166 (the one documented skip): main's counts, unchanged (`.utmp/factory/gate-037-038.log`).
- Red team (sonnet/medium, 36k) PASS: the diff is exactly the 11 deletions plus the one `.gitignore` line; GUID and path greps repeated, zero hits; suite re-run at the sha, same counts; console clean over the bridge (`.utmp/factory/gate/CHG-037-redteam.md`).
- Found on the way: this shift's Editor launch sat for 15 minutes on Unity's modal "Recovering Scene Backups" (the same mechanism that made these files). The launcher now answers it with Yes (backups kept, gitignored).

## Revert

`git revert` of the merge; the scenes return.
