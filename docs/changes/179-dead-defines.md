# 179 — Strip the dead scripting defines left by deleted packs, with a guard (LOG-179)

Landed by the factory 2026-09-17T16:21:53Z. Change CHG-010, branch `chg/010-dead-defines` @ bbc855ba12. Gate: suite PASS, perf N/A, red team PASS — suite: tests-first fail -> impl green, full recompile (.utmp/factory/gate/CHG-010-suite.txt); red team PASS: exhaustive symbol hunt zero hits incl. package cache and binaries, notes -> F-028 (guard substring semantics), F-029 (PPv2 package in URP) (.utmp/factory/gate/CHG-010-redteam.md); perf N/A; console BRIDGE_DOWN.

Factory shift 4 on the desktop, 2026-09-17, branch `chg/010-dead-defines`.
Class AUTO (charter I1: doc drift against code / provenance litter; no behaviour: the symbols had no consumer). ProjectSettings only.

## Intent

The red team on CHG-005 found `LETAI_TRUESHADOW` still on the Standalone scripting-define
line after the pack that auto-added it was deleted (FINDINGS F-025). `GRASSFLOW_SRP` is the
same class: GrassFlow is disconnected (art-direction.md) and nothing under Assets/,
Packages/ or the package cache names the symbol. Dead defines mislead the next reader of
LAUNCH-READINESS L1 and can never be removed by the pack that wrote them.

## What shipped

- `ScriptingDefinesTests.StandaloneDefines_AllHaveAConsumer` (EditMode, new): every define
  on the Standalone target must be named by at least one .cs/.shader/.cginc/.hlsl/.asmdef/
  .compute file under Assets/, Packages/ or Library/PackageCache (asmdef versionDefines count,
  which is how `UNITY_POST_PROCESSING_STACK_V2` is consumed). Written first: on the rig at 16:06Z it failed naming both symbols (EditMode 536/537).
- `ProjectSettings/ProjectSettings.asset`: `LETAI_TRUESHADOW` and `GRASSFLOW_SRP` stripped
  from every platform line that carried them (Standalone carried both; GrassFlow was also on
  Android, Switch, PS4, WebGL, XboxOne and iPhone). `UNITY_POST_PROCESSING_STACK_V2` stays: the installed post-processing package's own DefineSetter.cs writes it on every domain reload (the red team corrected the spec: no asmdef
  declares it), so stripping it would be undone at once; whether a URP project should carry that package is F-029.

## Verification

Suite, test rig, branch @ bbc855ba, 2026-09-17T16:07-16:09Z (`run-tests.sh All`): EditMode 537/537
(0 failed, 0 inconclusive), PlayMode 152/153 (the one documented skip). The define change forced a
full recompile, which logged the project's complete warning census (254 lines, all pre-existing;
recorded as F-027 for CHG-009). Red team: see the evidence block above.

Perf: N/A. Console: this session's bridge is down (LOOP-STATE § RIG). The suite compiling
and passing with the defines gone is the proof that no code depended on them.

## Revert

`git revert` of the merge commit puts the two symbols back; nothing else changes.
