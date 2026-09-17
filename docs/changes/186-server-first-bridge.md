# 186 — Server-first bridge: the launcher serves 8080 before any session dials it; mcp_http.py uses the bridge with a dead connector (LOG-186)

Landed by the factory 2026-09-17T22:24:10Z. Change CHG-020, branch `chg/020-server-first-bridge` @ aaa521cbd4. Gate: suite PASS, perf N/A, red team N/A — red team N/A under D16b: factory tooling only (launcher, an HTTP MCP client, briefs), nothing under Assets/, no NOD action, no invariant. suite: run-tests.sh All on main @ f9b79696 EditMode 542/542, PlayMode 152/153, 22:23Z (the branch changes nothing under Assets/); unit 32/32 from the branch's blobs, launcher dry-run and a live server on 8081 + live client checks on 8080 on file (.utmp/factory/gate/CHG-020-*.txt); perf N/A; built by the peer runner session, reviewed by shift 6.

Factory, 2026-09-17, branch `chg/020-server-first-bridge`, built by the previous runner session (game: Robogame factory) at shift 6's request with git plumbing only, no checkout; landed by shift 6.
Class AUTO (charter I1: tooling, harnesses and instruments). Red team N/A (D16b: nothing under Assets/, no NOD action, no invariant). Seven files, all under `.claude/`.

## Intent

A Claude Desktop session dials `.mcp.json`'s UnityMCP once, at session start, and never again:
`reconnect_session_connector` refuses project servers and `/mcp` is not available on the
Desktop. So whether a shift had the bridge was decided in the second the session was opened,
and three of six shifts lost that coin (3, 4 and 6). The launcher started the Editor and
printed the prompt in the same breath; Grey opened the session; 8080 came up minutes later.

Two things made the Editor a bad thing to wait for. The package's HTTP auto-start only fires
once the Editor has been focused (ASSUMPTIONS #12, three observations), and the server the
Editor launches carries a handshake that the rig's batch quits used to kill (F-031, LESSONS 9).
The load-bearing fact the other way is that `HttpAutoStartHandler` reuses a server that is
already reachable instead of launching its own. So the server can come first, started by hand
with no handshake, and the Editor simply connects to it whenever it is ready.

Grey's steer the same day: fire-and-polish, no terminal steps, no Unity juggling.

## What shipped

- **`Start-Factory.ps1`** brings the rig up server-first: start the package's own server
  command by hand when 8080 is silent (`uvx --offline --from mcpforunityserver==<package
  version> mcp-for-unity --transport http --http-url http://127.0.0.1:8080
  --project-scoped-tools`, no pidfile, no token, logged under `.utmp/factory/`), wait for it to
  listen, open this clone's Editor only if none is open, bring its window to the foreground once,
  wait for it to register on the server, and only then print the shift prompt. `-NoEditor` skips
  all of it; `-DryRun` prints the steps in order. An incidental hardening: a branch with no
  upstream no longer aborts the preflight.
- **`mcp_http.py`** speaks Streamable-HTTP MCP with the standard library: `ping`, `instances`,
  `tools`, `resource`, `call`, `code`, `wait-instance`. A shift whose connector is dead uses the
  bridge through it; exit 2 is the only "no bridge".
- **`test_mcp_http.py`**: eight tests on a mocked transport, including the negative one that
  `wait-instance robogame-factory` must not accept Grey's Editor, named `robogame`.
- **`/robogame-factory` step 1c**, the **sweeper**'s console sweep and the **red team**'s
  console check all carry the HTTP fallback; `BRIDGE_DOWN` now means the server is down, not
  the connector.
- The factory scripts README gains the row.

## Verification

- Factory unit tests in a scratch copy of main plus the new files: 32/32
  (`.utmp/factory/gate/CHG-020-unit.txt`).
- Launcher dry run against this clone with the server and Editor already up: both reported
  present, the remaining rig steps listed in order, nothing started
  (`CHG-020-dryrun.txt`).
- The pinned offline server command started a second server on port 8081 in under 25 s,
  answered `initialize` and the instances resource, and was stopped; 8080 untouched
  (`CHG-020-server8081.txt`).
- The client against the live shift-6 server: `ping`, `instances`, `wait-instance` both ways,
  `read_console`, `execute_code`.
- Suite: nothing under Assets/ changes; shift 6's `run-tests.sh All` is the D2 record.
- The proof that matters is the next cold start: `Start-Factory.ps1 -Desktop` ending in
  `bridge live: ...` and the session opened after it showing `UnityMCP: connected`. The first
  wake records it and closes BACKLOG 12.

## Owed

D-007 (a login-time task running the same server start, so 8080 is up before Grey opens
anything) is on the board. The Desktop-shift lock never receives a pid, so every later preflight
calls it stale after three minutes; a finding, not fixed here.
