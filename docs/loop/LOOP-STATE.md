# LOOP-STATE (Robogame Factory) — read first, write last (every wake)

Directive version: v1.1. Last touched: 2026-09-16 (end of shift 2, on the desktop). The first-launch HANDOFF was consumed this shift; its remainder is in § NEXT ITEM and § BACKLOG 11.

## NEEDS GREY

The board is NEEDS-GREY.md (D12). This section only carries a pointer and the count: 0 APPROVE / 0 PLAY / 0 BUY / 1 DECIDE (D-005, the Editor) / 3 FYI as of 2026-09-17T02:00Z (D-001..D-004 answered and closed).

## SHIFT (D13; replaces the kernel's LIVENESS block while the factory runs in shifts)

Lock: `.utmp/factory/shift.json` (written by Start-Factory.ps1: start time, mode, maxHours, model, effort; the end-of-shift routine copies it to `shift-last.json` with `ended` and deletes it).
Tick: `.utmp/factory/loop-tick.txt` (one line per real wake: UTC, what this wake did).
Session: a Claude Desktop session titled "Robogame factory" on the clone (launched via `/robogame-factory`, which runs `Start-Factory.ps1 -Desktop` then arms `/loop`), or the Windows Terminal tab the launcher opens (untested route, ASSUMPTIONS #6). Keep it open; detach nothing, close nothing.
Model policy (D11): foreground fable @ xhigh (Grey, 2026-09-16; confirmed by the session's own metadata on 2026-09-16); fieldhands per .claude/agents/ (sonnet, effort set per agent); red team opus @ high. Overage: D-001 ANSWERED 2026-09-17 ("it can" use usage credits): the wake routine still reads the plan's usage (`get_usage`) and records the windows in the tick and the ledger; no daily dollar ceiling was stated, so the D16d ceiling (4M tokens a shift) is the bound (LESSONS § METHOD 1).
Shared account: the Cosmonaut (bootleg_botlet, on the hive) runs on the same Max plan; every cap is shared. On 2026-09-16 the weekly all-models window was already at 100 % when shift 2 began.
Session-bound clocks: none. A nightly shift, when Grey wants one, is a Task Scheduler job that runs Start-Factory.ps1 (README).
Hot-file size budget (D13): ~200 KB; 2026-09-16 preflight measured 56 KB. Start-Factory.ps1 measures it at preflight.
Last commit known to this instance: shift 2 started at fd9be978 (main = origin/main); the shift's own commits follow it ("factory: shift 2 …").

## THE BUILD (D2 — main's current evidence; "unknown" is a legal, honest state until measured)

main @ fd9be978 (2026-09-16) = origin/main at shift start; no product code changed this shift.
Suite (2026-09-17 after CHG-003, `run-tests.sh All` on the test rig, warm): **EditMode 530/530** (0 inconclusive), **PlayMode 152/153** (1 skip: `MatchFlowTests.SpawnBot_ResultingGameObject_HasResolvedIInputSource`, documented, BACKLOG 2), **0 failures** — but `SurfaceNetsBenchmarkTests` Dim33 failed once at 1.005 ms against a hard < 1.0 ms gate during the red team's reruns (F-021, SPIKES L3): the suite is green with a coin in it. Wall time 1m28s (EditMode test time 0.8 s, PlayMode 44.6 s, the rest is two Unity batch startups). Results: `.claude/worktrees/test-rig/TestResults-{EditMode,PlayMode}.xml`.
Perf (docs/perf-captures/harness-log.txt, newest rows 2026-08-17, live Editor): Arena idle avg 5.13 ms, p99 6.12 ms, p99.9 8.50 ms, GC 0 B/frame; Garage idle avg 1.88 ms, p99 2.56 ms; render probe 592 blocks: chassis-in-view 2.19 ms. Budget: 16.6 ms target / 33 ms cliff (best-practices § 16). Run-to-run band: unknown until measured 5× (BACKLOG 11).
Soak: none exists. Last playtest: session 172's two user reports (grapple on trees; pogo tune), both closed in 172.
Console on scene load: unknown (bridge down all shift, F-017). Invariants: INV-1/2/3/4/9 hold by code evidence, INV-5/6 spot-checked, INV-7/10 not evaluated (F-020).
Known weaknesses: F-008 (bomb-bay doors have no INV-8 cue, S1); F-012 (spherical-arenas.md describes a feature that shipped under other names, S1 doc); five imported packs without a license (F-001–F-005, D-004); the rest in FINDINGS.md.

## RIG (D7/D11 — where the truth test runs; one job at a time unless proven parallel-safe)

Machine: Grey's Windows desktop (Tailscale `desktop-p2msnrt`). Unity 6000.4.4f1 at `C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe` (hard-coded in run-tests.sh). Python 3.14.0 (`python` on PATH), git 2.54, `claude` at `~/.local/bin`.
Checkout: the factory's own clone, `C:\Users\Grey\Desktop\mutedtuple\robogame-factory`. Grey's checkout at `C:\Users\Grey\Desktop\mutedtuple\robogame` is never edited by the factory. Clone-local rig config: `.vscode/settings.json` is `skip-worktree` (LESSONS § METHOD 3).
Suite: `.claude/scripts/run-tests.sh [EditMode|PlayMode|All]` from the clone root (Git Bash); syncs the clone's working tree into `.claude/worktrees/test-rig` (exists, Library warm) and runs Unity batch `-nographics`. Measured 2026-09-16: All = 1m28s warm. Ran concurrently with the factory's Editor importing the clone, without incident (one data point).
Perf harness: PlayMode tests `[Category("Perf")]` in Assets/_Project/Tests/PlayMode/Perf/ (`PerfBaselineHarness.Arena_Idle_Baseline`, `Garage_Idle_Baseline`; `PerfRenderProbe.Arena_ChassisRenderCost_Attribution`); rows append to docs/perf-captures/harness-log.txt. In `-nographics` the render probe is meaningless; CPU/physics/GC rows stand. Graphics numbers need the live Editor.
MCP: `.mcp.json` → `http://127.0.0.1:8080/mcp`, served by whichever Editor auto-started MCP for Unity's server. The factory's Editor: `Unity.exe -projectPath <clone>` (Start-Factory.ps1 launches it; 2.1 GB RSS while importing). **The Desktop session dials the server once at session start** (ASSUMPTIONS #8): if the Editor was not serving 8080 by then, the bridge stays down for that session's whole life: `reconnect_session_connector` refuses project servers (2026-09-17) and `/mcp` is not available in a Desktop session. Sequencing is the only fix (BACKLOG 12). When Grey's Editor is also up, target this clone's instance with `set_active_instance`, never Grey's.
Blender: `.mcp.json`'s blender server; connected on 2026-09-16 (31 tools) but unused.
Discord: `.env` present since 2026-09-17 (D-003 "yes"; DISCORD_WEBHOOK_FACTORY verified by name and length, never by value) → `ping.py` posts for real.
Must NOT be open while a batch run executes: nothing (the test-rig worktree has its own Library); the test-rig worktree itself is never opened in an Editor.
Editor faults on this clone: shift 1's Editor crashed in InitializeAssetDatabaseV2 with 'Assertion failed: mv_size == sizeof(T)' and 'dataValue.mv_size >= sizeof(ArtifactMetaInfo)' (Editor-prev.log 2026-09-16T20:48Z; crash report %LOCALAPPDATA%/Temp/Unity/Editor/Crashes/Crash_2026-09-16_204843616), i.e. a corrupt Library/ArtifactDB or SourceAssetDB; shift 2's Editor hung after 'Input System module state changed: Initialized' with no window for 2 h and was killed 2026-09-17T01:53Z. Fix applied 2026-09-17: Library/ArtifactDB, Library/SourceAssetDB and Library/Artifacts deleted (derived caches; a full reimport runs on the next launch, so the bridge comes up late in shift 3). The launch after the wipe (2026-09-17T01:47Z, pid 29144) hung the same way: log frozen after licensing, no UPM server started (upm.log shows only the batch run's), no window, killed at 01:53Z. So the Library was not the hang's cause. Leading guess: a modal dialog at startup that a GUI launch shows and a batch launch does not. NEEDS-GREY D-005 asks Grey to look at the screen once; until then every shift is batch-only.

## FACTORY FLOOR (D16e — v1, published 2026-09-16; iterate)

WAKE ROUTINE (every wake, in order, cheap): (1) `git fetch` + INBOX log between HEAD and origin; (2) plan usage (`get_usage`): record the windows in the tick; usage credits are allowed (D-001, 2026-09-17) and the D16d per-shift ceiling binds; (3) bridge check (`session_connectors_status`; reconnect if `failed` and 8080 listens); (4) the suite in the background if the tree changed since the last green; (5) stamp the tick.
STANDING SWEEPS (D4; `sweeper` on sonnet/medium, one sweep per call, ≤ 5 sweeps a shift per LESSONS § METHOD 4): console + suite-and-perf every shift (console needs the bridge); doc-drift, best-practices, invariants weekly (run 2026-09-16; next due 2026-09-23); visual on every visible landing and weekly (bridge); provenance on every import and monthly (run 2026-09-16); readiness weekly; /ideate through design-pilot when APPROVE has < 2 open and the queue's feature lane is empty.
FIELDHANDS: per D11 tiers. A landing costs qa-verifier + perf-checker (when hot) + red-team; budget the D16d ceiling as ≤ 5 sweeps + ≤ 3 gates a shift, or fewer sweeps when the queue is deep.
SHIFT SHAPE: wake routine → rung 0 (suite) → ladder (D14) with the queue built through the landing chain (`land.py gate` / `land.py land`) → END-OF-SHIFT ROUTINE: LOOP-STATE (§ SHIFT LOG bullet ≤ 600 chars, § NEXT ITEM), FINDINGS/SPIKES/CHANGE-QUEUE/LAUNCH-READINESS/ASSUMPTIONS/LESSONS current, NEEDS-GREY counts, the report through `ping.py` (dry until D-003), the tick, the lock → `shift-last.json` + delete, commit "factory: shift N end …", push, a one-line push notification, the loop stopped.
MOLT: when the hot set passes 200 KB or the context shows rot (D11), write state and restart the session.

## HEALTH (D16c; shift 2, 2026-09-16)

Changes landed/week with evidence: 6 (CHG-003, CHG-002, CHG-005, CHG-001, CHG-008, CHG-010; 2026-09-17). Board entries answered/week: 0 (D-001 partially, 2026-09-16). Bugs closed with tests: 1 (F-016, CHG-003). Readiness items moved: 7 (S1 done via D-002; T1 unknown→open; L1 unknown→done; L2 unknown→done; D1 open→done; D2 unknown→done; M1–M4 n/a for v1).
Main: green (0 failures). Suite: 683 cases, 1m28s warm. Perf vs budget: last rows 2026-08-17, Arena idle avg 5.13 ms of 16.6 ms; band unmeasured. Board: 4 DECIDE open, oldest D-001 (2026-09-16). Findings surfaced 20 / acted on 1 (F-017, the command fix) / specced 8 (into CHG-001..003). Pipeline occupancy: 1 lane at a time (the test rig syncs the single checkout, so gates serialize; named per D16). Shift 4 tokens: red team ×2 (84k + CHG-010's) + the fable foreground (context ~70 % at shift end); usage card delta about $29 (6386.83 → 6416.28) (shared account, upper bound). Delegation mix: 4 × sweeper sonnet/medium (~349k tokens measured: 71k + 74k + 100k + 104k); foreground fable/xhigh (context 164k at 23:38Z). Harness stubs: 0 seen. Tokens shift 2: ~349k fieldhand + the foreground (usage credits). Shift 3: red team ×4 passes (64k + 65k+84k + 62k + 86k+round 2) ≈ 360k opus + the fable foreground (context ~55 % of 1M at shift end); the usage card's extra-usage figure moved about $58 (6252.60 → 6310.75) during the shift (shared with the Cosmonaut: an upper bound).

## LANES IN FLIGHT (D4 CHANGE tier — each change's stage: SPEC / APPROVE-WAIT / TESTS / BUILD / GATE / LANDED; the named dependency if serialized)

- CHG-003 — LANDED 2026-09-17T02:10Z (docs/changes/174). CHG-002 — LANDED 2026-09-17 (docs/changes/175-doc-drift-sweep.md; red team KILL → fixes → PASS). CHG-005 — LANDED 2026-09-17 (docs/changes/176-delete-unused-packs.md). CHG-001 — LANDED 2026-09-17 (docs/changes/177-provenance-records.md). Shift 4: CHG-008 — LANDED (docs/changes/178-preset-coverage.md). CHG-010 — LANDED (docs/changes/179-dead-defines.md). CHG-012 — measuring the under-load band first (SPIKES L3b). CHG-011 — PARK (PT-001). CHG-001 — SPEC (AUTO, S; grew by three D-004 rows). CHG-005 — SPEC (ASK with the D-004 nod). Serialized on the single working tree: the test rig syncs the clone's checkout, so one branch is checked out per gate.

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
12. Instrument: `Start-Factory.ps1 -Desktop` waits for 8080 to listen (bounded) before printing the prompt, and `/robogame-factory` accepts a lock the launcher wrote minutes earlier as its own instead of refusing it, so the Desktop session is opened AFTER the Editor serves MCP (F-017, LESSONS § METHOD 2, ASSUMPTIONS #8). Blocked on D-005 (the Editor must open at all). AUTO (tooling), S.
13. F-008 bomb-bay door cue → CHG-004 spec for APPROVE (ASK). S.
14. F-012 spherical-arenas.md → SPIKES L2 (rung 7). L.
15. Console sweep + visual sweep — never run (bridge). First thing after the bridge is proven.
16. Rig: the factory Editor hangs at startup (D-005 on the board; see RIG). Instrument once it opens: the launcher tails Editor.log for 'Crash!!!' or a stall and says so in the preflight.
18. CHG-008 (F-022): Grappler + HoverTank preset coverage, one list — AUTO, S. Then SPIKES L3 (F-021) before T1 can close.
19. docs/TRACES.md is stale in 17 sites (F-023): Robogame → Traces → Rebuild Index once the Editor opens (D-005); add TRACES.md to the doc-drift sweep. AUTO, S.
17. Instrument: `/inbox` commits to main with plumbing (`git commit-tree` + `update-ref refs/heads/main`, then push) instead of committing on the checked-out branch (LESSONS § METHOD 5). AUTO (tooling), S.

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
| 2026-09-17 | shift 3 (fable/xhigh + 4 red-team gates) | fieldhands ≈ 360k measured (opus); foreground context ≈ 550k at shift end | usage credits (D-001 answered: allowed); usage card delta about $58 (6252.60 → 6310.75) (upper bound, shared account) |
| 2026-09-17 | shift 4 (fable/xhigh + 2 red-team gates, same session) | fieldhands ≈ 170k (opus); foreground context ≈ 700k at shift end | usage credits (D-001); usage card delta about $29 (6386.83 → 6416.28) (upper bound, shared account) |

Purchases: none. Grey's measured usage goes beside every estimate here.

## OPEN JOBS (background — never foreground waits; long jobs list the successor queued behind them)

(none — the shift ended cleanly; the factory's Editor may still be running on the clone and is harmless)

## SHIFT LOG (this shift's ≤ 600-char bullets; `land.py` appends here; archive to docs/loop/shift-log-archive.md when past ~20 KB)

- 2026-09-16T20:48Z–21:45Z — shift 1 opened (fable/xhigh, maxHours 0) and ended on a STOP from Grey before any object-level work. Read CHARTER + LOOP-STATE + NEEDS-GREY + INBOX + pillars; no sweep, no spike, no landing, no gate. Grey's `decide D-001` line recorded verbatim in NEEDS-GREY § ANSWERED; D-001 stays open because the answer does not pick between its two options. No Discord shift report: Grey issued the STOP at the terminal, so the report's audience was present (D12 "nothing repeated that day"). HANDOFF is untouched — the next shift starts at (1).
- 2026-09-16T23:34Z–23:55Z — shift 2 (fable/xhigh, Desktop). Rig proven: suite 1m28s warm, EditMode 529/530 (1 inconclusive, F-016), PlayMode 152/153 (1 documented skip), 0 failed. Four file-level sweeps → F-001–F-020; CHG-001..003 specced (AUTO); D-003, D-004 raised; /robogame-factory gained cap + bridge steps. Bridge down all shift (F-017). Plan cap: weekly all-models 100 % with extra usage on → shift ended under D-001's default; nothing dispatched after the read, the four running sweeps let finish. No landing, no gate. Report dry-run (no .env).

- 2026-09-17T01:50Z — light wake ([INBOX poke] from the scribe): Grey's D-001 line recorded verbatim, D-001 closed (usage credits allowed; no ceiling named, D16d binds). The "end on a 100 % window" rule withdrawn from § SHIFT, the wake routine, LESSONS § METHOD 1 and /robogame-factory step 1b. Shift 3 re-armed from this session via /robogame-factory (its preflight and tick line say whether the Editor was reused or the shift is batch-only).
- 2026-09-17T02:05:22Z — CHG-003 PresetBlueprintTests: guard the preset list, drop the retired Buggy path — docs/changes/174-preset-list-guard.md — 2026-09-17T02:10Z — CHG-003 landed (docs/changes/174): guard test PresetPaths_AllExistOnDisk + the retired Buggy path dropped from two test lists; EditMode 530/530 (0 inconclusive), PlayMode 152/153; red team PASS, notes → F-021 (SurfaceNets benchmark median 1.005 ms vs a hard 1.0 ms gate: flaky), F-022 (Grappler + HoverTank presets validated by no test). T1: the unjustified inconclusive is gone; a flaky perf gate remains.
- 2026-09-17T02:21:05Z — CHG-002 Doc drift from the 2026-09-16 sweep: six docs say what the code does — docs/changes/175-doc-drift-sweep.md — 2026-09-17T02:25Z — CHG-002 landed (docs/changes/175): six doc-drift fixes (a misleading DOC trace retargeted, leash damper 0 not 250 in three places, scalable-parts status, PlanetGravity→GravityField, README changelog+date, DevHud straggler note struck); suite green; red team KILL in round 1 (a third 250 at tip-blocks.md:140) → fixed → PASS. Readiness D1, D2 done. New: F-023 (TRACES.md stale ×17), F-024 (9 obsolete-API warning sites).
- 2026-09-17T02:30:45Z — CHG-005 Delete the two unused, unlicensed packs (FattyPolyTurret, TrueShadow) on Grey's nod — docs/changes/176-delete-unused-packs.md — 2026-09-17T02:35Z — CHG-005 landed (docs/changes/176): FattyPolyTurretFree + Part2Free and Le Tai's TrueShadow deleted (458 files) on Grey's D-004 nod; 252 GUIDs, 0 references (pre-check + the red team's wider sweep); suite green; red team PASS, note → F-025 (stale LETAI_TRUESHADOW define in ProjectSettings.asset:838). L1: the two unlicensed packs are gone.
- 2026-09-17T02:50:53Z — CHG-001 Provenance records: artgen manifest with guards, MCP package origin, three Asset Store rows — docs/changes/177-provenance-records.md — 2026-09-17T02:55Z — CHG-001 landed (docs/changes/177): artgen/README.md manifest (33 FBX → generator + study, Blender 5.1.2 from the FBX headers) with three EditMode guards, unity-mcp origin row (MIT), three Asset Store rows per D-004; suite EditMode 533/533, PlayMode 152/153; red team KILL in round 1 (32 of 33 rows unchecked by the token parser) → fixed + negative-proved → PASS. L1, L2 done.

- 2026-09-17T01:47Z–03:01Z — shift 3 (fable/xhigh, Desktop, batch-only: the factory Editor hung twice, D-005). Landed 4 through the chain, each red-teamed: CHG-003 (174), CHG-002 (175; KILL→fix→PASS), CHG-005 (176, Grey's nod), CHG-001 (177; KILL→fix→PASS). D-001..D-004 answered and closed; D-005 raised. Readiness: S1, L1, L2, D1, D2 done; T1 open (F-021 flaky gate, F-022). SPIKES L3: isolated medians 0.487–0.609 ms over 6 runs (mean 0.565); the 1.005 ms failure was under full-suite load → KEEP → CHG-012 (banded gate). New findings F-021–F-025; CHG-008/009/010 specced or queued. Report pinged (Discord live). Ended by the loop for context hygiene (D11) with the ladder still holding buildable items.
- 2026-09-17T16:06:30Z — CHG-008 Preset test coverage: every scaffolder slot validated, one list, HoverTank quarantined loudly — docs/changes/178-preset-coverage.md — 2026-09-17T16:12Z — CHG-008 landed (docs/changes/178): PresetPaths covers all ten scaffolder slots (14 presets validated), one list for both suites, a slot-coverage guard; the coverage found F-026 on its first run (the HoverTank preset fails library-aware validation: corner cubes hosted on hoverblades), quarantined in KnownInvalid with an assertion that expires with the fix (CHG-013); EditMode 536/536, PlayMode 152/153; red team PASS, four notes folded into CHG-013.
- 2026-09-17T16:21:53Z — CHG-010 Strip the dead scripting defines left by deleted packs, with a guard — docs/changes/179-dead-defines.md — 2026-09-17T16:25Z — CHG-010 landed (docs/changes/179): LETAI_TRUESHADOW and GRASSFLOW_SRP stripped from seven platform define lines (no consumer anywhere, zero hits including the package cache), guarded by ScriptingDefinesTests; EditMode 537/537, PlayMode 152/153; red team PASS, notes → F-028 (the guard matches substrings, a define-setter counts as a consumer) and F-029 (PPv2 package in a URP project). The recompile logged the 254-warning census (F-027 → CHG-009).

- 2026-09-17T15:50Z–16:28Z — shift 4 (fable/xhigh, same Desktop session as shift 3, batch-only: this session dialled the bridge before the Editor served it). Landed CHG-008 (docs/changes/178) and CHG-010 (179), each red-teamed. CHG-008's coverage found F-026 (the HoverTank preset is not player-buildable-shaped; quarantined loudly, fix specced as CHG-013). SPIKES L3b: the SurfaceNets median under full-suite load 0.36–0.38 ms over 5 runs, LOWER than isolated (0.49–0.61): load is not the cause; the 1.005 ms followed a full recompile (cold Burst likely) → CHG-012 spec. The factory's Editor opened the clone this time (D-005 narrowed: the Library wipe worked; MCP on 8080 is for the next session). Report pinged. Ended by the loop for context hygiene (D11).

## NEXT ITEM

Open the next Desktop session only after the factory Editor serves MCP on 8080 (check `netstat -ano | findstr :8080`), so the bridge is up for the whole shift; then the wake routine. Rung 1: CHG-013 (HoverTank preset fix, test first: the quarantine's assertion flips), CHG-012 (the SurfaceNets gate with the measured band, spec from SPIKES L3/L3b), CHG-009 (nine CS0618 sites; grab the full warning texts from a Core recompile). With the bridge: BACKLOG 11 (perf band 5×, MCP proof, record the instance id), 15 (console + visual sweeps), 19 (TRACES.md rebuild); then the CHG-004 bomb-bay cue spec for APPROVE. PT-001 and D-005 stay on the board until Grey answers.
