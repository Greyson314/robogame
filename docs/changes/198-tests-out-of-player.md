# 198 — The PlayMode test assembly stays out of the player: UNITY_INCLUDE_TESTS on its asmdef, and the first successful Windows build (LOG-198)

Landed by the factory 2026-09-18T06:58:57Z. Change CHG-030, branch `chg/030-tests-out-of-player` @ c8db74e1eb. Gate: suite PASS, perf N/A, red team N/A — Suite ran at dbebd4c3 (this sha's tree minus the two appended text rows in docs/perf-captures/harness-log.txt): EditMode 562/562, PlayMode 153/154 (1 documented skip), 0 failures (.utmp/factory/gate/CHG-030/gate.txt). The acceptance: build-player.sh twice at dbebd4c3 → result=Succeeded totalSize=282090782 both, totalTime 312.6 s cold / 11.0 s warm, errors=0; Builds/Windows 282,380,832 B both (identical); Unity exit 21 from a -quit teardown crash AFTER the report (F-059, not a build failure). Perf N/A. Red team N/A under D16b: build configuration of a test assembly; no shipped runtime code, no NOD action, no invariant..

## Intent

CHG-029's first-ever CLI player build (docs/changes/197) failed reproducibly with 197 identical errors, every one of them a test attribute the player cannot resolve: the `Robogame.Tests.PlayMode` assembly had no `UNITY_INCLUDE_TESTS` define constraint and no platform restriction, so a Standalone build compiled the PlayMode tests into the game (F-058). The Unity Test Framework's convention is that constraint; the EditMode assembly was already Editor-only. One line, gated by the instrument that found it.

## What shipped

- `Assets/_Project/Tests/PlayMode/Robogame.Tests.PlayMode.asmdef`: `"defineConstraints": ["UNITY_INCLUDE_TESTS"]`. The define exists in the Editor (the batch rig included) and in test-player builds only, so the suite still compiles and runs and a normal player build skips the assembly.
- The two `[PLAYER-BUILD] result=Succeeded` rows in docs/perf-captures/harness-log.txt (the branch carries them; the rig's log is not the clone's).

## Verification

- THE BUILD (the acceptance): `build-player.sh` twice on the branch's sha, Windows64, BuildOptions.None, the six shipped scenes: `result=Succeeded totalSize=282090782 totalTime=312.6 errors=0 warnings=29 scenes=6` (cold) and `result=Succeeded totalSize=282090782 totalTime=11.0 errors=0 warnings=29 scenes=6` (warm, incremental); `Builds/Windows` 282,380,832 bytes on disk both times, identical. LAUNCH-READINESS B1 → done; B2 gets its first numbers (size 282.1 MB, band 0 over two runs; build time two points, cold and warm). The 29 warnings are build-report warnings not yet classified (none is a `warning CS` line; a read of the build log is a small follow-up).
- Named caveat, not hidden: after the report is written the batch Editor crashes in its `-quit` teardown (`GetManagerFromContext: pointer to object of manager 'PhysicsManager' is NULL`, `Fatal Error!`, `Crash!!!`, Unity exit code 21) on both runs. The artefact is complete and identical across runs, so the row is the verdict; the crash is F-059 (SPIKES L8 owed: what touches Physics at shutdown; `EditorApplication.Exit(0)` instead of `-quit`; a run without `-nographics`). `build-player.sh` should judge by the row until then.
- Suite at the tested sha dbebd4c3 on the rig (MCP stripped): EditMode 562/562, PlayMode 153/154 (the one documented skip), 0 failures: the PlayMode tests still compile and run in the Editor with the constraint. The landed sha differs from the tested one only by the two appended text rows in docs/perf-captures/harness-log.txt (the gate notes say so).
- Red team N/A (D16b: build configuration for a test assembly; no shipped runtime code, no NOD action, no invariant). Perf N/A.
- The FIRST-RUN log (B3) was not captured: launching the player on Grey's desktop while he is at it would take the screen and the speakers; the plan is in LAUNCH-READINESS B3.

## Aftermath

- B3 next: a headless 30-second player run (`-batchmode -nographics -logFile`) once the player's audio init is checked for a batch guard; then the Player.log's error count is a finding or a done.
- F-059: the shutdown crash after a build; the build script's exit code lies until it is fixed.
