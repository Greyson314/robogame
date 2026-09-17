# 180 — The test rig must not kill the factory's MCP server: run-tests.sh strips the package from the rig (LOG-180)

Landed by the factory 2026-09-17T18:24:50Z. Change CHG-015, branch `chg/015-rig-strips-mcp` @ 3277ac4883. Gate: suite PASS, perf N/A, red team PASS — suite: 3 acceptance EditMode runs 537/537 with 8080 alive after each + a control run isolating the handshake path (.utmp/factory/gate/CHG-015-rig.txt); red team PASS, All 82 s, 8080 listening after its runs, notes 1-5 folded into the entry (.utmp/factory/gate/CHG-015-redteam.md); perf N/A (a test-runner script); console: bridge UP, 0 errors.

Factory shift 5 on the desktop, 2026-09-17, branch `chg/015-rig-strips-mcp`.
Class AUTO (charter I1: tooling, harnesses and instruments). One script, `.claude/scripts/run-tests.sh`; nothing a player sees.

## Intent

Shift 5 was the first with the MCP bridge up from the first wake, and it lost the bridge
in its first hour: the factory Editor's MCP server on 127.0.0.1:8080 died at 12:54:26Z with
no error in its own log, seven seconds before the package's handshake state under
`Library/MCPForUnity/RunState` was emptied, while a fieldhand's batch test run was quitting
(FINDINGS F-031). The cause is in the package: MCP for Unity records the "server I launched"
handshake (pidfile path + instance token) in EditorPrefs, which are per user, not per project
(`IPidFileManager.cs:45`), and its quit cleanup (`McpEditorShutdownCleanup.cs:50`) stops
whatever that handshake names. The test rig is a second Unity instance of the same package
under the same user, so every `run-tests.sh` batch run ends by killing the factory Editor's
server. The Editor's client then retries a dead socket, and the Desktop session has no
console, screenshot or profiler for the rest of the shift.

## What shipped

- `run-tests.sh` step 1b: after the sync into the rig worktree and before the batch run, the
  `com.coplaydev.unity-mcp` entry is removed from the rig's `Packages/manifest.json` and
  `Packages/packages-lock.json` (Python; a `sed` fallback drops the manifest line). Both files
  are tracked, so the sync resets them on every run and the edit never reaches the clone or
  main. No test assembly references the package (grep of Assets/_Project/Tests for
  `MCPForUnity`: 0 hits), so the suite's content is unchanged; the rig also loses the
  package's `[TestRunnerNoThrottle]` hook, which only toggled the Editor's interaction mode.
- The rule and the hand-restart command are in `docs/loop/LESSONS.md` § METHOD 9 and
  LOOP-STATE § RIG.

## Verification

Reproduction: F-031's timeline (the Editor-launched, handshake-tracked server died at a batch
quit with no other actor). Control (18:14Z, `.utmp/factory/gate/CHG-015-rig.txt`): one
EditMode run through main's UNPATCHED script, package present in the rig, with a server
started by hand and therefore carrying no EditorPrefs handshake: 8080 stayed up (pid 32320
before and after), so the kill is the handshake path and nothing else. Acceptance (18:11Z,
same file): three `run-tests.sh EditMode` runs on the branch, 537/537 each, 8080 listening
after each, the rig's manifest and lock without the package during the run, the rig log
without a `MCP-FOR-UNITY` line; EditMode wall time 16–23 s per run. Red team: see the
evidence block above. Perf: N/A (a test-runner script). Console: the bridge was up for this
gate, over the hand-started server.

## Owed and out of scope (red team notes)

- The "8080 survived" clause of the acceptance cannot fail today: the hand-started server has
  no EditorPrefs handshake (`MCPForUnity.LocalHttpServer.LastPidFilePath` is absent,
  `Library/MCPForUnity/RunState` is empty), so the kill path cannot see it with or without
  the strip. The true positive test is owed the next time the factory Editor launches its own
  server (auto-start writes the handshake): one `run-tests.sh EditMode`, 8080 listening
  after. LOOP-STATE § NEXT ITEM carries it.
- The defect is cross-project by construction: Grey's own `robogame` Editor auto-starts a
  server too (the AutoStartOnLoad pref is per user) and its quit cleanup would stop whatever
  the shared handshake names. The strip covers the rig only; the loop restarts the server by
  hand when 8080 is silent (LESSONS § METHOD 9), and the upstream fix belongs to the package.
- The rig now tests a package set the project does not ship (one editor-only package fewer);
  bounded by the zero-reference grep, and a future first-party reference to the package
  would fail only in the rig, loudly.
- `run-tests.sh`'s older comment "this machine has no Python" is stale next to step 1b's
  `python` (a real 3.14.0 here; the Store alias exits rc=49 into the `sed` fallback).

## Revert

`git revert`; the rig kills the bridge again on its next batch run.
