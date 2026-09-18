# 197 — The first Windows player build from the CLI: PlayerBuild.cs + build-player.sh, and what it measured (LOG-197)

Landed by the factory 2026-09-18T06:48:39Z. Change CHG-029, branch `chg/029-player-build` @ c48a4effb4. Gate: suite PASS, perf N/A, red team N/A — Suite at c48a4eff on the rig (direct run, MCP stripped): EditMode 562/562 (561 + PlayerBuildTests, RED first: no PlayerBuild), PlayMode 153/154 (1 documented skip). The instrument's first measurement: the player build FAILS reproducibly (two runs, 197 identical CS0246 errors: the PlayMode test assembly compiled into the Standalone player, F-058); the tool logged its row and exited 1 as designed; B1 unknown → open. Perf N/A. Red team N/A under D16b: an Editor tool and a shell script, no shipped runtime code, no NOD action, no invariant..

## Intent

LAUNCH-READINESS B1 ("a Windows player build succeeds from the CLI, reproducibly") had been `unknown` since the checklist was seeded: no build had ever been recorded in docs/changes. Every later readiness row (size and load time, first run, options, quit flows, Steam overlay) needs a build to exist, and a build that has never been tried is the readiness item most likely to hide a surprise. This change is the instrument: a build script the rig can run, one row per build in the harness log, and the first measurement, whatever it says.

## What shipped

- `Assets/_Project/Scripts/Tools/Editor/PlayerBuild.cs` (Robogame.Tools.Editor): `BuildWindows()` for `-executeMethod` and a menu item Robogame → Build → Windows Player; the enabled scenes of EditorBuildSettings in order (six shipped scenes, Bootstrap first), StandaloneWindows64, BuildOptions.None, output `Builds/Windows/Robogame.exe`; one `[PLAYER-BUILD] result=<Succeeded|Failed> totalSize=<bytes> totalTime=<s> errors=<n> warnings=<n> scenes=<n>` row from the BuildReport, logged and appended to docs/perf-captures/harness-log.txt with a timestamp; in batch a failed build exits 1.
- `.claude/scripts/build-player.sh [sha]`: resets the test-rig worktree to the sha, strips the MCP package as run-tests.sh does (a build Editor quitting with the package present would kill the factory's server, F-031/F-057), runs the build headless, prints the row, the exit code and the log path.
- `Assets/_Project/Tests/EditMode/Tools/PlayerBuildTests.cs`: the tool's scene list equals the enabled EditorBuildSettings scenes in order (written first; RED: the tool did not exist).
- Two `[PLAYER-BUILD]` rows in docs/perf-captures/harness-log.txt: the first measurement.

## Verification

- THE MEASUREMENT (B1): the build FAILS on main, reproducibly. Two runs on the branch's sha: `result=Failed totalSize=0 totalTime=42.3 errors=197 warnings=77 scenes=6` and `result=Failed totalSize=0 totalTime=3.4 errors=197 warnings=0 scenes=6`, byte-identical error sets. All 197 are `CS0246` (UnityTest / UnityTearDown attributes not found) in 22 files under Assets/_Project/Tests/PlayMode: the `Robogame.Tests.PlayMode` assembly has no `UNITY_INCLUDE_TESTS` define constraint and no platform restriction, so a Standalone build compiles the PlayMode tests into the player and they cannot resolve the test framework there (F-058; CHG-030 is the one-line fix, gated by this instrument). B1 moves `unknown` → `open` with its cause; B2 (size, time) stays `unknown` until a build succeeds; B3's first-run log was not captured (no executable).
- The instrument itself: the tool ran in batch, wrote its row, exited 1 as designed; the script printed the row and the log path; `.utmp/factory/gate/CHG-029/build-errors.txt` holds every error line.
- Suite at the frozen sha c48a4eff on the rig: EditMode 562/562 (561 + PlayerBuildTests), PlayMode 153/154 (the one documented skip), 0 failures. RED first: the tests-only commit did not compile (no `PlayerBuild`).
- Red team N/A (D16b: an Editor tool and a shell script; the build artefact is not committed). Perf N/A.

## Aftermath

- CHG-030 (`UNITY_INCLUDE_TESTS` on the PlayMode test asmdef) is gated by `build-player.sh`; if the build then succeeds, B1 is done and B2 gets its first numbers from two rows; if a new error set appears, that is the next finding.
- `Builds/` under the rig is wiped by every `clean -fd`; the rows are the record. A release-shaped build (options, IL2CPP, a signed exe) is a later row.
