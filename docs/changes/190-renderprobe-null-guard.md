# 190 — PerfRenderProbe skips renderers destroyed mid-probe (a flaky test fixed) (LOG-190)

Landed by the factory 2026-09-17T23:13:39Z. Change CHG-023, branch `chg/023-renderprobe-null-guard` @ 6e6472435a. Gate: suite PASS, perf N/A, red team N/A — Test file only (D16b). The flake: CHG-018's gate run failed PerfRenderProbe.Arena_ChassisRenderCost_Attribution with MissingReferenceException at :131 (a captured block renderer destroyed before the shadow-caster toggle; the restore loop already null-checked); the same code passed on main minutes before and after (shift7-main-playmode-recheck.txt), so timing, not code. Fix: skip destroyed renderers in the toggle loop and log the live count. Suite on the branch's checkout: .utmp/factory/gate/CHG-023-suite.txt..

Factory shift 7, 2026-09-17, branch `chg/023-renderprobe-null-guard` (plumbing; rung 0, D2: a flaky test is a bug).
Class AUTO (I1: a flaky test fixed). Red team N/A (D16b: a test file). One file, one loop.

## Intent

The CHG-018 gate run failed `PerfRenderProbe.Arena_ChassisRenderCost_Attribution` with a
`MissingReferenceException` at the shadow-caster toggle: the probe captures the chassis' block
renderers at the start, then runs two settle-and-measure windows, and by the time it toggles
`shadowCastingMode` on the captured list some of those renderers have been destroyed. The
restore loop a few lines below already skips destroyed entries; the toggle loop did not. The
same suite had passed on the same code minutes earlier, so this is timing, not the materials
CHG-018 touched.

## What shipped

- The toggle loop skips destroyed renderers, mirroring the restore loop, and logs how many of
  the captured renderers were still alive when toggled.
- Nothing else. The measurement windows and the harness row are unchanged.

## Verification

- Suite on the branch's checkout: `.utmp/factory/gate/CHG-023-suite.txt`.
- The failure record: `.utmp/factory/gate/CHG-018-suite.txt` and the rig's PlayMode XML from
  that run (stack at `PerfRenderProbe.cs:131`).

## Aftermath

F-048 asks the real question the flake exposed: if the chassis' instanced renderer consolidates
the block renderers during the probe, the "shadow casters off" window measures fewer casters
than the probe thinks, and the attribution row is optimistic by that share. The new log line
puts the number in the batch log so the next perf pass can read it instead of guessing.
