# 200 — One road onto the test rig: run-tests.sh --at <sha>, the rig wait built in; the launcher swaps an Editor-launched MCP server for a hand-started one (LOG-200)

Landed by the factory 2026-09-18T22:32:16Z. Change CHG-028, branch `chg/028-rig-road` @ e19a12f129. Gate: suite PASS, perf N/A, red team N/A — D16b: tooling only (.claude/). Suite through the NEW script: --at e6197795 EditMode 562/562, manifest grep 0 (.utmp/factory/chg028/accept1-editmode.log); default mode EditMode 562/562 (accept1-defaultmode.log); busy-rig refusal exit 3 (wait-test.log); factory unit tests 45/45; launcher -DryRun three cases incl. stale pidfile (foreground review found the unverified Stop-Process pid; fixed in e19a12f1). Nothing under Assets/ changes; main PlayMode 154/155 today. Live swap NOT exercised (next shift start, ASSUMPTIONS 13)..

## What changed

- `.claude/scripts/run-tests.sh` gains `--at <sha>` (reset the rig to a commit instead of syncing the working tree) and `--label <name>` (result and log file names). The package strip (CHG-015) and the rig rule (wait while a `-runTests` Unity.exe is alive; exit 3 after `RIG_WAIT_TIMEOUT_SECS`, default 1800) now live in the script for both modes. It prints the rig manifest's `com.coplaydev.unity-mcp` count and the skipped count.
- `Start-Factory.ps1`: when 8080 is served by a server the Editor launched itself (tell: `Library/MCPForUnity/RunState/mcp_http_8080.pid`, and its pid must own the 8080 listener or be its verified parent/child), the launcher swaps it for a hand-started one and waits up to 2 min for the Editor to re-attach. A stale pidfile is a warning, never a `Stop-Process`. `-DryRun` prints the step.
- `red-team.md` and `sweeper.md` name `run-tests.sh --at <sha>` as the only road onto the rig. The scratch `.utmp/factory/rig_run.sh` is deleted.

## Why

F-057: one direct rig run without the strip killed the shift-8 bridge for the rest of the shift (LESSONS 15). One road onto the rig makes the strip unavoidable.

## Evidence

- `run-tests.sh --at e6197795 --label chg028-accept1 EditMode` → 562/562, manifest grep 0 (`.utmp/factory/chg028/accept1-editmode.log`); default mode 562/562 (`accept1-defaultmode.log`).
- Busy rig: `RIG_WAIT_TIMEOUT_SECS=20` → "rig still busy after 20s", exit 3 (`wait-test.log`).
- Factory unit tests 45/45. Launcher `-DryRun`: no pidfile → nothing to swap; stale pidfile → warning, no swap; true pidfile → swap step printed.
- Review: the first build trusted the pidfile's pid; fixed in e19a12f1 before the gate.

## Not proven

The live swap. ASSUMPTIONS 13 says an Editor whose own server dies may stop its bridge; the swap is guarded and reports a failed re-attach. It runs for the first time at a shift start that finds an Editor-launched server.

## Revert

`git revert`.
