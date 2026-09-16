# LOOP-STATE (Robogame Factory) — read first, write last (every wake)

Directive version: v1.1. Last touched: 2026-09-16 (end of shift 2, on the desktop). The first-launch HANDOFF was consumed this shift; its remainder is in § NEXT ITEM and § BACKLOG 11.

## NEEDS GREY

The board is NEEDS-GREY.md (D12). This section only carries a pointer and the count: 0 APPROVE / 0 PLAY / 0 BUY / 4 DECIDE (D-001 urgent) / 3 FYI as of 2026-09-16 shift 2 end.

## SHIFT (D13; replaces the kernel's LIVENESS block while the factory runs in shifts)

Lock: `.utmp/factory/shift.json` (written by Start-Factory.ps1: start time, mode, maxHours, model, effort; the end-of-shift routine copies it to `shift-last.json` with `ended` and deletes it).
Tick: `.utmp/factory/loop-tick.txt` (one line per real wake: UTC, what this wake did).
Session: a Claude Desktop session titled "Robogame factory" on the clone (launched via `/robogame-factory`, which runs `Start-Factory.ps1 -Desktop` then arms `/loop`), or the Windows Terminal tab the launcher opens (untested route, ASSUMPTIONS #6). Keep it open; detach nothing, close nothing.
Model policy (D11): foreground fable @ xhigh (Grey, 2026-09-16; confirmed by the session's own metadata on 2026-09-16); fieldhands per .claude/agents/ (sonnet, effort set per agent); red team opus @ high. Overage: D-001 OPEN → **no overage**: the wake routine reads the plan's usage (`get_usage`) and a weekly window at 100 % ends the shift (LESSONS § METHOD 1).
Shared account: the Cosmonaut (bootleg_botlet, on the hive) runs on the same Max plan; every cap is shared. On 2026-09-16 the weekly all-models window was already at 100 % when shift 2 began.
Session-bound clocks: none. A nightly shift, when Grey wants one, is a Task Scheduler job that runs Start-Factory.ps1 (README).
Hot-file size budget (D13): ~200 KB; 2026-09-16 preflight measured 56 KB. Start-Factory.ps1 measures it at preflight.
Last commit known to this instance: shift 2 started at fd9be978 (main = origin/main); the shift's own commits follow it ("factory: shift 2 …").

## THE BUILD (D2 — main's current evidence; "unknown" is a legal, honest state until measured)

main @ fd9be978 (2026-09-16) = origin/main at shift start; no product code changed this shift.
Suite (2026-09-16, `run-tests.sh All` on the test rig, warm): **EditMode 529/530** (1 inconclusive: `PresetBlueprintTests.Preset_PassesValidation(Blueprint_DefaultBuggy)`, a stale path, F-016 → CHG-003), **PlayMode 152/153** (1 skip: `MatchFlowTests.SpawnBot_ResultingGameObject_HasResolvedIInputSource`, documented, BACKLOG 2), **0 failures**. Wall time 1m28s (EditMode test time 0.8 s, PlayMode 44.6 s, the rest is two Unity batch startups). Results: `.claude/worktrees/test-rig/TestResults-{EditMode,PlayMode}.xml`.
Perf (docs/perf-captures/harness-log.txt, newest rows 2026-08-17, live Editor): Arena idle avg 5.13 ms, p99 6.12 ms, p99.9 8.50 ms, GC 0 B/frame; Garage idle avg 1.88 ms, p99 2.56 ms; render probe 592 blocks: chassis-in-view 2.19 ms. Budget: 16.6 ms target / 33 ms cliff (best-practices § 16). Run-to-run band: unknown until measured 5× (BACKLOG 11).
Soak: none exists. Last playtest: session 172's two user reports (grapple on trees; pogo tune), both closed in 172.
Console on scene load: unknown (bridge down all shift, F-017). Invariants: INV-1/2/3/4/9 hold by code evidence, INV-5/6 spot-checked, INV-7/10 not evaluated (F-020).
Known weaknesses: F-008 (bomb-bay doors have no INV-8 cue, S1); F-012 (spherical-arenas.md describes a feature that shipped under other names, S1 doc); five imported packs without a license (F-001–F-005, D-004); the rest in FINDINGS.md.

## RIG (D7/D11 — where the truth test runs; one job at a time unless proven parallel-safe)

Machine: Grey's Windows desktop (Tailscale `desktop-p2msnrt`). Unity 6000.4.4f1 at `C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe` (hard-coded in run-tests.sh). Python 3.14.0 (`python` on PATH), git 2.54, `claude` at `~/.local/bin`.
Checkout: the factory's own clone, `C:\Users\Grey\Desktop\mutedtuple\robogame-factory`. Grey's checkout at `C:\Users\Grey\Desktop\mutedtuple\robogame` is never edited by the factory. Clone-local rig config: `.vscode/settings.json` is `skip-worktree` (LESSONS § METHOD 3).
Suite: `.claude/scripts/run-tests.sh [EditMode|PlayMode|All]` from the clone root (Git Bash); syncs the clone's working tree into `.claude/worktrees/test-rig` (exists, Library warm) and runs Unity batch `-nographics`. Measured 2026-09-16: All = 1m28s warm. Ran concurrently with the factory's Editor importing the clone, without incident (one data point).
Perf harness: PlayMode tests `[Category("Perf")]` in Assets/_Project/Tests/PlayMode/Perf/ (`PerfBaselineHarness.Arena_Idle_Baseline`, `Garage_Idle_Baseline`; `PerfRenderProbe.Arena_ChassisRenderCost_Attribution`); rows append to docs/perf-captures/harness-log.txt. In `-nographics` the render probe is meaningless; CPU/physics/GC rows stand. Graphics numbers need the live Editor.
MCP: `.mcp.json` → `http://127.0.0.1:8080/mcp`, served by whichever Editor auto-started MCP for Unity's server. The factory's Editor: `Unity.exe -projectPath <clone>` (Start-Factory.ps1 launches it; 2.1 GB RSS while importing). **The Desktop session dials the server once at session start** (ASSUMPTIONS #8): if the Editor was not serving 8080 by then, the bridge stays down until `reconnect_session_connector("UnityMCP")` runs at a turn end (`/robogame-factory` step 1c; repeat it on the first wake if 8080 was not up). When Grey's Editor is also up, target this clone's instance with `set_active_instance`, never Grey's.
Blender: `.mcp.json`'s blender server; connected on 2026-09-16 (31 tools) but unused.
Discord: `.env` absent → `ping.py` dry-runs (D-003).
Must NOT be open while a batch run executes: nothing (the test-rig worktree has its own Library); the test-rig worktree itself is never opened in an Editor.

## FACTORY FLOOR (D16e — v1, published 2026-09-16; iterate)

WAKE ROUTINE (every wake, in order, cheap): (1) `git fetch` + INBOX log between HEAD and origin; (2) plan usage (`get_usage`): a weekly window at 100 % under D-001's default ends the shift; (3) bridge check (`session_connectors_status`; reconnect if `failed` and 8080 listens); (4) the suite in the background if the tree changed since the last green; (5) stamp the tick.
STANDING SWEEPS (D4; `sweeper` on sonnet/medium, one sweep per call, ≤ 5 sweeps a shift per LESSONS § METHOD 4): console + suite-and-perf every shift (console needs the bridge); doc-drift, best-practices, invariants weekly (run 2026-09-16; next due 2026-09-23); visual on every visible landing and weekly (bridge); provenance on every import and monthly (run 2026-09-16); readiness weekly; /ideate through design-pilot when APPROVE has < 2 open and the queue's feature lane is empty.
FIELDHANDS: per D11 tiers. A landing costs qa-verifier + perf-checker (when hot) + red-team; budget the D16d ceiling as ≤ 5 sweeps + ≤ 3 gates a shift, or fewer sweeps when the queue is deep.
SHIFT SHAPE: wake routine → rung 0 (suite) → ladder (D14) with the queue built through the landing chain (`land.py gate` / `land.py land`) → END-OF-SHIFT ROUTINE: LOOP-STATE (§ SHIFT LOG bullet ≤ 600 chars, § NEXT ITEM), FINDINGS/SPIKES/CHANGE-QUEUE/LAUNCH-READINESS/ASSUMPTIONS/LESSONS current, NEEDS-GREY counts, the report through `ping.py` (dry until D-003), the tick, the lock → `shift-last.json` + delete, commit "factory: shift N end …", push, a one-line push notification, the loop stopped.
MOLT: when the hot set passes 200 KB or the context shows rot (D11), write state and restart the session.

## HEALTH (D16c; shift 2, 2026-09-16)

Changes landed/week with evidence: 0. Board entries answered/week: 0 (D-001 partially, 2026-09-16). Bugs closed with tests: 0. Readiness items moved: 4 (T1 unknown→open, L1 unknown→grey, L2 unknown→open, D2 unknown→open).
Main: green (0 failures). Suite: 683 cases, 1m28s warm. Perf vs budget: last rows 2026-08-17, Arena idle avg 5.13 ms of 16.6 ms; band unmeasured. Board: 4 DECIDE open, oldest D-001 (2026-09-16). Findings surfaced 20 / acted on 1 (F-017, the command fix) / specced 8 (into CHG-001..003). Pipeline occupancy: 0 lanes. Delegation mix: 4 × sweeper sonnet/medium (~349k tokens measured: 71k + 74k + 100k + 104k); foreground fable/xhigh (context 164k at 23:38Z). Harness stubs: 0 seen. Tokens this shift: ~349k fieldhand + the foreground, against the 4M ceiling; all on usage credits (D-001).

## LANES IN FLIGHT (D4 CHANGE tier — each change's stage: SPEC / APPROVE-WAIT / TESTS / BUILD / GATE / LANDED; the named dependency if serialized)

- CHG-003 — SPEC (AUTO, S). CHG-002 — SPEC (AUTO, S). CHG-001 — SPEC (AUTO, S). None started: the shift ended on the plan cap before rung 1.

## BACKLOG (with priors; triage on merit; AUTO items → CHANGE-QUEUE, ASK items → NEEDS-GREY, unknowns → SPIKES)

1. ~~`FindObjectOfType` straggler in DevHud.cs~~ — gone from the code; the stale doc note is F-015 → CHG-002.
2. `MatchFlowTests.SpawnBot` is `[Ignore]`d, blocked on `Tests/Scenes/MinimalArena.unity` (the skip message names the trimmed GameStateController + one-block library it needs) — AUTO (coverage), M → CHG-006 to spec. Prior: high.
3. Tweakables defaults vs persisted JSON: a bumped default does not reach a user with a saved value (architecture.md gotcha) — readiness risk; SPIKE first (count the affected keys), then ASK if behavior changes. Prior: high.
4. Atomic writes (`.tmp` + `File.Replace`, best-practices § 11.3) for UserBlueprintLibrary.cs:147, ConcoctionLibrary.cs:122, Tweakables.cs:512 — readiness T4, AUTO once specced with a test → CHG-005. Prior: high.
5. Editor test asmdef `Robogame.Block.Tests` (best-practices § 14.3) — AUTO (coverage/tooling), M. Prior: medium.
6. A headless soak (arena, bots on `IInputSource`, N minutes, exceptions + GC/frame + p99) — instrument, rung 5; AUTO. Prior: high; the gate is weaker without it.
7. Recorded-input replay through `IInputSource` — instrument; blocked on ASSUMPTIONS #1. Prior: medium.
8. The C spike — SPIKES L1, PARK until Grey opts in.
9. idea-backlog § Approved: Rider Effects; Concoction Identity — ASK (feature) once specced; both queued behind the core Lab per the backlog. Prior: Grey approved them 2026-06-04.
10. Launch-readiness sweep — partly done 2026-09-16 (T1, L1, L2, D1, D2); the rest of the `unknown` rows need the bridge (T2, C1, C2) or a build (B1–B5).
11. HANDOFF remainder: the perf harness Arena idle baseline 5× for the run-to-run band (§ RIG); prove `read_console` + `run_tests` against this clone's Editor instance once the bridge is up; record the instance id.
12. Instrument: `Start-Factory.ps1 -Desktop` waits for 8080 to listen (bounded) before printing the prompt, so the session dials a live server (F-017, LESSONS § METHOD 2). AUTO (tooling), S.
13. F-008 bomb-bay door cue → CHG-004 spec for APPROVE (ASK). S.
14. F-012 spherical-arenas.md → SPIKES L2 (rung 7). L.
15. Console sweep + visual sweep — never run (bridge). First thing after the bridge is proven.

## INSTRUMENTS (built, reusable)

- `.claude/scripts/run-tests.sh` — batch suite on the test-rig worktree (1m28s warm, 2026-09-16).
- Perf harness (Assets/_Project/Tests/PlayMode/Perf/) → docs/perf-captures/harness-log.txt.
- Robogame → Traces → Validate / Rebuild Index (Assets/_Project/Scripts/Tools/Editor/ContinualTraces.cs); file-level fallback: grep `TRACE\[` and resolve ids by hand (the doc-drift sweep does this when the bridge is down).
- `/ideate` (.claude/commands/ideate.md) with design-pilot; docs/research/idea-backlog.md as dedupe memory.
- `.claude/scripts/factory/`: Start-Factory.ps1, Stop-Factory.ps1, ping.py, land.py, discord_intake.py (+ tests: `python -m unittest discover -s .claude/scripts/factory/tests -t .claude/scripts/factory/tests` → OK on 2026-09-16).
- `/robogame-factory` (.claude/commands/robogame-factory.md): preflight → plan-cap check → bridge reconnect → arm /loop.
- Session introspection from a Desktop session: `get_usage` (plan windows, context size), `get_session` (model, effort), `session_connectors_status` / `reconnect_session_connector` (the bridge).
- Agents: planner, design-pilot, test-drafter, qa-verifier, perf-checker, sweeper, red-team.

## BUDGET LEDGER (I2; tokens per shift under the D16d ceiling; purchases by proposal)

| Date | Item | Tokens / cost | Note |
|---|---|---|---|
| 2026-09-16 | charter + seed (on the hive) | ~0 to the factory | — |
| 2026-09-16 | shift 1 (fable/xhigh) | foreground only, state read; ended on STOP | — |
| 2026-09-16 | shift 2 (fable/xhigh + 4 sweeps) | fieldhands 349k measured; foreground context 164k at 23:38Z, output unmeasured | all usage credits: the weekly all-models window was at 100 % at shift start (D-001) |

Purchases: none. Grey's measured usage goes beside every estimate here.

## OPEN JOBS (background — never foreground waits; long jobs list the successor queued behind them)

(none — the shift ended cleanly; the factory's Editor may still be running on the clone and is harmless)

## SHIFT LOG (this shift's ≤ 600-char bullets; `land.py` appends here; archive to docs/loop/shift-log-archive.md when past ~20 KB)

- 2026-09-16T20:48Z–21:45Z — shift 1 opened (fable/xhigh, maxHours 0) and ended on a STOP from Grey before any object-level work. Read CHARTER + LOOP-STATE + NEEDS-GREY + INBOX + pillars; no sweep, no spike, no landing, no gate. Grey's `decide D-001` line recorded verbatim in NEEDS-GREY § ANSWERED; D-001 stays open because the answer does not pick between its two options. No Discord shift report: Grey issued the STOP at the terminal, so the report's audience was present (D12 "nothing repeated that day"). HANDOFF is untouched — the next shift starts at (1).
- 2026-09-16T23:34Z–23:55Z — shift 2 (fable/xhigh, Desktop). Rig proven: suite 1m28s warm, EditMode 529/530 (1 inconclusive, F-016), PlayMode 152/153 (1 documented skip), 0 failed. Four file-level sweeps → F-001–F-020; CHG-001..003 specced (AUTO); D-003, D-004 raised; /robogame-factory gained cap + bridge steps. Bridge down all shift (F-017). Plan cap: weekly all-models 100 % with extra usage on → shift ended under D-001's default; nothing dispatched after the read, the four running sweeps let finish. No landing, no gate. Report dry-run (no .env).

## NEXT ITEM

Wake routine first (usage: if D-001 is still unanswered and a weekly window is at 100 %, end the shift again; the window resets 2026-09-18T05:00Z). Then: rung 0, the suite in the background; rung 1, build CHG-003 (test first), then CHG-002, then CHG-001, each through `land.py gate` → `land.py land` with qa-verifier + red-team (perf N/A: nothing hot); BACKLOG 11 (perf band 5×, MCP proof) and BACKLOG 15 (console + visual sweeps) once the bridge is up; spec CHG-004 for the board.
