# 210 — The unused Post Processing Stack v2 package and its scripting define are removed (D-008) (LOG-210)

Landed by the factory 2026-09-19T20:24:13Z. Change CHG-038, branch `chg/038-remove-ppv2` @ b9a210bb74. Gate: suite PASS, perf N/A, red team N/A — suite 575/575 + 165/166 at b9a210bb, the define guard RED at commit 1 by design (.utmp/factory/gate-037-038.log); player build Succeeded 281,916,035 B + first run 0 errors (.utmp/factory/gate-038-build.log); perf N/A: nothing hot; red team N/A: no first-party runtime code, package + project settings only (D16b).

## What changed

The Post Processing Stack v2 package (`com.unity.postprocessing` 3.5.4) is removed from `Packages/manifest.json` and `packages-lock.json` (nothing else depended on it), and `ProjectSettings.asset`'s `scriptingDefineSymbols` block (`UNITY_POST_PROCESSING_STACK_V2` on 16 platforms, injected by the package's own DefineSetter) is replaced with Unity's canonical empty form `scriptingDefineSymbols: {}`. The project is URP: its post stack is the URP Volume system our PostProcessingBuilder and EnvironmentBuilder author (`UnityEngine.Rendering.Universal`), which is a different package and is untouched. Source: F-029.

Grey's answer (I1: a package removal is ASK), verbatim, INBOX 2026-09-19T05:34:30Z: "decide D-008: remove".

## Evidence

- Tests first, by the guard CHG-010 left behind: at commit efe2af62 (package gone, define still there) `ScriptingDefinesTests.StandaloneDefines_AllHaveAConsumer` is RED (EditMode 574/575: the define has lost its only consumer); at commit b9a210bb (define stripped) the suite is green: EditMode 575/575, PlayMode 165/166 (the one documented skip) (`.utmp/factory/gate-037-038.log`).
- Player build at b9a210bb: `[PLAYER-BUILD] result=Succeeded totalSize=281916035 totalTime=49.3 errors=0 scenes=6` (main before: 282,093,848 B; the build is 177,813 B smaller). Unity exit 21 after the report is the known teardown crash (SPIKES L8).
- Headless first run of that build, 30 s, `-factory-mute`: `[FIRST-RUN] errors=0 exceptions=0 warnings=0` (`.utmp/factory/first-run/20260919T202155Z/Player.log`).
- Before the build: no file under Assets/ names `UnityEngine.Rendering.PostProcessing` or the define; the PPv2 `PostProcessVolume` script GUID is in no scene or prefab (F-029's re-check, shift 8).
- Red team N/A (D16b): no first-party runtime code changes; the gate is the suite, a Succeeded build and a clean first run.

## Revert

`git revert` of the merge; the package resolves again on the next Editor focus and its DefineSetter re-injects the define.
