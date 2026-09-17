# 187 — GroundDriveSubsystem: delete the three dead inline drive fields (LOG-187)

Landed by the factory 2026-09-17T22:41:36Z. Change CHG-021, branch `chg/021-dead-drive-fields` @ 9fd2c9a7a0. Gate: suite PASS, perf N/A, red team PASS — Suite on the branch's checkout 2026-09-17T22:3xZ: EditMode 542/542, PlayMode 152/153 (documented skip), 0 failures (.utmp/factory/gate/CHG-021-suite.txt). Red team sonnet/medium (D16b: runtime file) PASS, 30k tokens: fields unread anywhere incl. prefabs/scenes/assets and SerializedObject paths; INV-1 holds (.utmp/factory/gate/CHG-021-redteam.md). Perf N/A: no runtime path changes..

Factory shift 7, 2026-09-17, branch `chg/021-dead-drive-fields` (built with git plumbing from main; the first change under CHARTER D8 SUGGESTIONS S1, Grey's simplification and deletion steer).
Class AUTO (I1: a console warning fixed; a refactor with zero behaviour change proven by the suite). One file, seven lines deleted, four comment lines added.

## Intent

`GroundDriveSubsystem` carried three serialized tuning fields, `_acceleration`, `_maxSpeed` and
`_turnRate`, under a "Tuning — Drive" header in the inspector. Nothing read them. The drive
values are blueprint-authoritative and resolved once in OnEnable into `_cfg`
(`GroundTuningConfig`), a migration the file's own comment records ("were per-machine Tweakables
read every FixedUpdate. PHYSICS_PLAN §1.5 / §5"); the three fields were the stranded inline
defaults from before that migration. The compiler said so on every full recompile (CS0414,
FINDINGS F-037), and a knob that does nothing misleads whoever next tunes the drive, which is
INV-1's neighbourhood.

## What shipped

- The three fields and their header are deleted; a four-line comment at the same spot says where
  drive tuning actually lives, so the next reader does not put them back.
- Nothing else. `GroundDriveSubsystem` is attached at runtime by `ChassisAssembler` and sits on
  no prefab or scene, so no serialized values were lost; the `GroundDriveTuning` ScriptableObject
  keeps its own `Acceleration`/`MaxSpeed`/`TurnRate`, which `BlueprintBlob` writes.

## Verification

- Suite on the branch: EditMode and PlayMode as recorded in `.utmp/factory/gate/CHG-021-suite.txt`
  (the movement tests, `GroundDriveGroundingTests` among them, unchanged).
- Red team (D16b: shipped runtime code): `.utmp/factory/gate/CHG-021-redteam.md`.
- The three CS0414 lines leave the warning census on the next full recompile; CHG-009 takes the
  rest of the first-party sites.

## Aftermath

F-037 closed. The deletion is the shape S1 asks for: a thing the code no longer needs, removed
with the suite as the proof, no board entry.
