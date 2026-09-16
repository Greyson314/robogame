# LOOP-STATE (Robogame Factory) — read first, write last (every wake)

Directive version: v1.0. Last touched: 2026-09-16 (pre-launch seed, written on the hive).

## HANDOFF — FIRST LAUNCH (consume on the first shift: fold anything durable into the sections below, then delete this section)

(1) Verify every state file reads and writes; verify `python`, `git`, `claude` and the Unity exe named in `.claude/scripts/run-tests.sh` are all reachable from this shell.
(2) Verify the channel: `python .claude/scripts/factory/ping.py --file <hello.txt> --lead "first shift" --dry-run`, then for real. Nothing else pings today.
(3) Stamp `.utmp/factory/loop-tick.txt`. Record in § SHIFT the model and effort this session actually runs on (`/status`), and read Grey's overage decision from NEEDS-GREY D-001; if unanswered, run this shift with no overage and keep the DECIDE open.
(4) PROVE THE RIG, unattended, from this session: `.claude/scripts/run-tests.sh All` (the first run creates `.claude/worktrees/test-rig` under this clone and warms its Library, ~5 min); record pass/fail counts and duration. Then the perf harness: the PlayMode `[Category("Perf")]` tests (Assets/_Project/Tests/PlayMode/Perf/) append rows to docs/perf-captures/harness-log.txt; run the Arena idle baseline 5 times and record the spread of avg and p99 in § RIG. If an Editor is serving MCP (port 8080), prove `read_console` and `run_tests` against THIS clone's instance (`set_active_instance`), and record the instance id. If the rig cannot be driven from here, that is the first CHANGE; nothing else lands until it can.
(5) Publish § FACTORY FLOOR: the standing sweeps (D4) with their cadence, the fieldhand tiers, the end-of-shift routine.
(6) Fill CHANGE-QUEUE from docs/research/idea-backlog.md § Approved (Rider Effects; Concoction Identity — both ASK class: feature) and from the known unknowns below that are AUTO class; run the first sweep set and fill FINDINGS; put the first APPROVE slate and any DECIDE on the board; write the shift report.
(7) Record the C spike in SPIKES (see § BACKLOG item 8) as PARK until Grey opts in; do not attempt it on the first shift.
(8) Verify that `/loop` armed from the launcher's initial prompt (this session should be scheduling its own wakeups). If it did not, note it in LESSONS.md and tell Grey in the shift report to type the prompt by hand next time.

## NEEDS GREY

The board is NEEDS-GREY.md (D12). This section only carries a pointer and the count: 0 APPROVE / 0 PLAY / 0 BUY / 2 DECIDE / 0 FYI at seed.

## SHIFT (D13; replaces the kernel's LIVENESS block while the factory runs in shifts)

Lock: `.utmp/factory/shift.json` (written by Start-Factory.ps1: start time, mode, maxHours, model, effort; deleted by the end-of-shift routine).
Tick: `.utmp/factory/loop-tick.txt` (one line per real wake: UTC, what this wake did).
Session: name `robogame-factory`, host = Grey's desktop, terminal = the Windows Terminal tab the launcher opened (keep it open; detach nothing, close nothing).
Model policy (D11): foreground fable @ xhigh (Grey, 2026-09-16; launch with `-Model fable -Effort xhigh`); fieldhands per .claude/agents/ (sonnet, effort set per agent); red team opus @ high. Overage: {{Grey's word — NEEDS-GREY D-001; "no overage" until then}}.
Shared account: the Cosmonaut (bootleg_botlet, on the hive) runs on the same Max plan; every cap is shared.
Session-bound clocks: none. A nightly shift, when Grey wants one, is a Task Scheduler job that runs Start-Factory.ps1 (README).
Hot-file size budget (D13): ~200 KB; at seed ≈ 56 KB (CHARTER 33 + LOOP-STATE 11 + NEEDS-GREY 2 + INBOX 1 + pillars 9). Start-Factory.ps1 measures it at preflight.
Last commit known to this instance: {{set on first shift}}.

## THE BUILD (D2 — main's current evidence; "unknown" is a legal, honest state until measured)

main @ 9d642c80 on origin (2026-08-30, session 172). Whether the desktop checkout is ahead of origin is unknown from the hive; the first preflight will show it.
Suite: 85 test files; 517 `[Test]` + 57 `[TestCase]` + 101 `[UnityTest]` by grep (2026-09-16). Last recorded run: EditMode 252/253, PlayMode 92/93, 0 failures, 1 inconclusive + 1 `[Ignore]` (session 95). Duration: unknown until proven (README says 30–90 s warm).
Perf (docs/perf-captures/harness-log.txt, 2026-08-17): Arena idle avg 5.13 ms, p99 6.12 ms, p99.9 8.50 ms, GC 0 B/frame; Garage idle avg 1.88 ms, p99 2.56 ms. Budget: 16.6 ms target / 33 ms cliff (best-practices § 16). Run-to-run band: unknown until measured 5×.
Soak: none exists. Last playtest: session 172's two user reports (grapple on trees; pogo tune) — both closed in 172.
Known weaknesses: see § BACKLOG items 1–7.

## RIG (D7/D11 — where the truth test runs; one job at a time unless proven parallel-safe)

Machine: Grey's Windows desktop (Tailscale `desktop-p2msnrt`). Unity 6000.4.4f1 at `C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe` (hard-coded in run-tests.sh).
Checkout: the factory's own clone, `{{path Grey chose — default C:\Users\Grey\Desktop\mutedtuple\robogame-factory}}`. Grey's checkout at `C:\Users\Grey\Desktop\mutedtuple\robogame` is never edited by the factory.
Suite: `.claude/scripts/run-tests.sh [EditMode|PlayMode|All]` from the clone root (Git Bash); syncs the clone's working tree into `.claude/worktrees/test-rig` and runs Unity batch `-nographics`. Results: `.claude/worktrees/test-rig/TestResults-<Platform>.xml`, log `TestRun-<Platform>.log`.
Perf harness: PlayMode tests `[Category("Perf")]` in Assets/_Project/Tests/PlayMode/Perf/ (PerfBaselineHarness, PerfRenderProbe); rows append to docs/perf-captures/harness-log.txt. In `-nographics` the render probe is meaningless; CPU/physics/GC rows stand. Graphics numbers need the live Editor or a batch run without `-nographics`.
MCP: `.mcp.json` → `http://127.0.0.1:8080/mcp`, served by whichever Editor auto-started MCP for Unity's server. The factory's Editor: `Unity.exe -projectPath <clone>` (Start-Factory.ps1 launches it when no Editor is serving 8080 and `-NoEditor` was not given). When Grey's Editor is also up, both register on one server; target this clone's instance with `set_active_instance` and never Grey's.
Blender: `.mcp.json`'s blender server, only when Blender is open with its MCP add-on started; otherwise the tools fail to connect and content work degrades to file-level edits.
Durations and spread: unknown until HANDOFF (4).
Must NOT be open while a batch run executes: nothing (the test-rig worktree has its own Library); the test-rig worktree itself is never opened in an Editor.

## FACTORY FLOOR (D16e — your structure; design it, publish it, iterate)

(unpublished — HANDOFF (5). Seed: the standing sweeps in D4 with a cadence each — console + suite + perf every shift; doc drift, best-practices, invariants weekly; visual audit on every visible landing and weekly; provenance on every import; readiness weekly; /ideate when the APPROVE lane has < 2 entries.)

## HEALTH (D16c, once a shift: changes landed/week with evidence, board entries answered/week, bugs closed with tests, readiness items moved, main status, suite size + duration, perf vs budget, board depth + oldest age, findings surfaced vs acted on, pipeline occupancy, delegation mix, output tokens/turn + cache-read tokens/turn, harness stubs, tokens this shift vs the D16d ceiling)

(reported each shift once running)

## LANES IN FLIGHT (D4 CHANGE tier — each change's stage: SPEC / APPROVE-WAIT / TESTS / BUILD / GATE / LANDED; the named dependency if serialized)

(none yet)

## BACKLOG (with priors; triage on merit; AUTO items → CHANGE-QUEUE, ASK items → NEEDS-GREY, unknowns → SPIKES)

1. `FindObjectOfType` straggler in Assets/_Project/Scripts/UI/DevHud.cs (best-practices § 12.5 open item) — AUTO (best-practice), S. Prior: certain.
2. `MatchFlowTests.SpawnBot` is `[Ignore]`d, blocked on `Tests/Scenes/MinimalArena.unity` (docs/changes/README.md) — AUTO (coverage), M. Prior: high.
3. Tweakables defaults vs persisted JSON: a bumped default does not reach a user with a saved value (architecture.md gotcha) — readiness risk for players; SPIKE first (count the affected keys), then ASK if behavior changes. Prior: high.
4. Atomic blueprint writes (`.tmp` + `File.Replace`, best-practices § 11.3) — readiness (save safety), AUTO once specced with a test. Prior: high.
5. Editor test asmdef `Robogame.Block.Tests` (best-practices § 14.3) — AUTO (coverage/tooling), M. Prior: medium.
6. A headless soak (arena, bots on `IInputSource`, N minutes, exceptions + GC/frame + p99) — instrument, rung 5; AUTO. Prior: high; the gate is weaker without it.
7. Recorded-input replay through `IInputSource` — instrument; blocked on ASSUMPTIONS #1 (PhysX divergence measured first). Prior: medium.
8. The C spike (Grey's option C, PARKED until Grey opts in): host MCP for Unity's Python server on the hive (`Server/` of the v9.7.3 package, `--http-host <hive tailscale ip> --http-port 8080`), point a desktop Editor at it (Window → MCP for Unity → Settings, remote URL + the insecure-remote opt-in), verify `read_console` and `run_tests` from a hive session against this clone's instance; verdict KEEP means the foreground can move to the hive. Prior: medium; three unverified assumptions (ASSUMPTIONS #3–#5).
9. idea-backlog § Approved: Rider Effects; Concoction Identity — ASK (feature) once specced. Prior: Grey approved them 2026-06-04.
10. Launch-readiness sweep: fill LAUNCH-READINESS.md statuses; the unknown-owner items become DECIDE entries. Prior: certain.

## INSTRUMENTS (built, reusable)

- `.claude/scripts/run-tests.sh` — batch suite on the test-rig worktree.
- Perf harness (Assets/_Project/Tests/PlayMode/Perf/) → docs/perf-captures/harness-log.txt.
- Robogame → Traces → Validate / Rebuild Index (Assets/_Project/Scripts/Tools/Editor/ContinualTraces.cs).
- `/ideate` (.claude/commands/ideate.md) with design-pilot; docs/research/idea-backlog.md as dedupe memory.
- `.claude/scripts/factory/`: Start-Factory.ps1, Stop-Factory.ps1, ping.py, land.py, discord_intake.py (+ tests).
- Agents: planner, design-pilot, test-drafter, qa-verifier, perf-checker, sweeper, red-team.

## BUDGET LEDGER (I2; tokens per shift under the D16d ceiling; purchases by proposal)

| Date | Item | Tokens / cost | Note |
|---|---|---|---|
| 2026-09-16 | charter + seed (on the hive) | ~0 to the factory | — |

Purchases: none. Grey's measured usage goes beside every estimate here.

## OPEN JOBS (background — never foreground waits; long jobs list the successor queued behind them)

(none yet)

## SHIFT LOG (this shift's ≤ 600-char bullets; `land.py` appends here; archive to docs/loop/shift-log-archive.md when past ~20 KB)

(empty)

## NEXT ITEM

HANDOFF (1).
