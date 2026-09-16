# Robogame Factory — the loop's home

An autonomous "progress factory" for this game: it finds what is wrong or
wanting (bugs, polish, visual drift, perf, inconsistencies, best-practice
violations, missing launch items, things worth buying), surfaces them to
Grey on one board, fixes what Grey has pre-approved by class, and asks
about the rest. Instantiated 2026-09-16 from `~/dev/templates/product-
factory/` (template v1.2, templates repo @ a507788) on the hive; runs on
Grey's desktop.

## Decisions (Grey, 2026-09-16)

1. **Host: the desktop, in the factory's own clone** ("A: Desktop, own
   clone, spike C later"). Grey's checkout is never touched. The
   hive-hosted MCP hub (option C) is parked as the factory's first
   instrument spike (LOOP-STATE § BACKLOG 8).
2. **Cadence: shifts first.** `Start-Factory.ps1` / `Stop-Factory.ps1`;
   an optional nightly Task Scheduler shift later. No 24/7 liveness
   machinery until a shift week lands clean.
3. **Consent: provable classes auto, rest asks.** The matrix is
   CHARTER.md I1.
4. **Inbound: both** — the hive's Discord intake on #blue-mao-pow into
   INBOX.md, and git (`/inbox` from any session, or an edit + push).

## Layout

| file | role | hot? |
|---|---|---|
| `CHARTER.md` | the constitution | hot |
| `LOOP-STATE.md` | build evidence, the shift block, rig, factory floor, health, backlog, shift log, NEXT ITEM; § HANDOFF for the first launch | hot |
| `NEEDS-GREY.md` | the board: APPROVE / PLAY / BUY / DECIDE / FYI / ANSWERED | hot |
| `INBOX.md` | Grey's drops; the intake bot appends here | hot |
| `FINDINGS.md` | one line per sweep finding | cold |
| `SPIKES.md` | one line per spike | cold |
| `CHANGE-QUEUE.md` | specs awaiting build + engineering rules | cold |
| `LAUNCH-READINESS.md` | the goal's checklist with statuses | cold |
| `ASSUMPTIONS.md` | the register | cold |
| `LESSONS.md` | what this factory has paid for; § METHOD LESSONS | cold |
| `LAUNCH-PROMPT.md` | the /loop text and how shifts start and end | — |
| `../../.claude/scripts/factory/` | `Start-Factory.ps1`, `Stop-Factory.ps1`, `ping.py`, `land.py`, `discord_intake.py`, tests, the hive's systemd units | — |
| `../../.claude/agents/` | fieldhand tiers incl. the new `sweeper` and `red-team` | — |
| `../../.claude/commands/inbox.md` | `/inbox` for any session | — |

Hot files are read every wake and share a ~200 KB budget that the
launcher measures. The pillars doc (`docs/research/game-design-pillars.md`)
is hot too and is never edited by the loop.

## How Grey works with it

- Open `NEEDS-GREY.md`. Answer entries there, or with one line in
  `INBOX.md`, or `/inbox <text>` from any Claude session, or a message in
  #blue-mao-pow. Verdicts are quoted verbatim.
- Start a shift: run `Start-Factory.ps1` in the factory clone. Stop one:
  `Stop-Factory.ps1`. Details in `LAUNCH-PROMPT.md`.
- The shift report arrives in #blue-mao-pow once per shift; pings
  otherwise only for a decision, a purchase or a fault.

## Desktop setup (once)

1. Push whatever the main checkout has (origin was 17 days behind it on
   2026-09-16), merge this branch to main, then clone the factory copy:

       git clone https://github.com/Greyson314/robogame.git C:\Users\Grey\Desktop\mutedtuple\robogame-factory

   Any path works; the launcher refuses only Grey's own checkout path.
2. Python (the kernel tools and `land.py`/`ping.py` need it; the desktop
   has none, and the App Execution Alias shim shadows `python3`):

       winget install Python.Python.3.12

   then close and reopen the terminal. `run-tests.sh` keeps working
   without it.
3. `.env` in the factory clone root (gitignored), one line:

       DISCORD_WEBHOOK_FACTORY=<the #blue-mao-pow webhook>

   The webhook Grey pasted in chat on 2026-09-16 is in a transcript; make
   a fresh one in Discord (channel → Integrations → Webhooks) and paste
   that instead. Never commit it, never paste it into a chat again.
4. First shift: `Start-Factory.ps1`. The first wake works LOOP-STATE
   § HANDOFF: proves the rig (`run-tests.sh` warms a test-rig worktree
   under the clone, ~5 min the first time), the channel, the tick, and
   publishes its factory floor.
5. Optional, later: a nightly shift.

       schtasks /create /tn "robogame-factory-nightly" /sc daily /st 01:00 /tr "powershell -NoProfile -ExecutionPolicy Bypass -File C:\...\robogame-factory\.claude\scripts\factory\Start-Factory.ps1 -MaxHours 5"

   Tick "Wake the computer to run this task" in Task Scheduler's
   Conditions tab if the desktop sleeps. Untested; the launcher's
   `-MaxHours` is honored by the loop, not by the OS.

## Hive setup (once, after this branch is on main)

The intake bot is already invited to #blue-mao-pow and can read it
(verified 2026-09-16 with the botlet's token). Install the timer:

    cp .claude/scripts/factory/systemd/robogame-inbox-intake.* ~/.config/systemd/user/
    systemctl --user daemon-reload
    systemctl --user enable --now robogame-inbox-intake.timer

It polls every five minutes, appends to `docs/loop/INBOX.md` in the hive
clone, commits and pushes to main. The desktop's loop fetches at every
checkpoint. Channel ids are not secrets; the bot token is read from the
botlet's `.env` by variable name only.

## Provenance — what was cut from the template copy (2026-09-16)

The template was derived from a trading-research loop (the Cosmonaut,
bootleg_botlet) by inversion, and its constitution carried that loop's
anecdotes and vocabulary. Cut from the charter, each an exact-match edit
before the fill; the rule behind every anecdote was kept:

1. Header: the template genealogy (which [K] mirror carried which
   Cosmonaut charter version) → a provenance note.
2. END GOAL: "Unlike a research ledger …" and "not false discovery" —
   the product was defined by contrast with a research loop.
3. D1: "(the Cosmonaut wrote each one five times)".
4. D2: "There is no champion build to defend" — the inversion of the
   research flavor's "a ledger, not a champion"; meaningless for a game.
5. D4 SPIKE: the 1,800-character "gate pass or red team" cap (spikes are
   never red-teamed in this flavor) and the twelve-of-sixteen anecdote.
6. D4 CHANGE (2): "(the bias-proof-by-construction analog)".
7. D4 GATE (4) and D16(b): "the gate's reconstruction" (research
   vocabulary for reconstructing a registered result) → the red team is
   the gate's one independent pass; the 0.86–1.84M-token anecdote with it.
8. D6: "a BAND, not a probe" (a cost probe is a trading measurement) →
   "carries its BAND".
9. D12: "one number a bullet" — the P&L-report form of the digest.
10. D13: the 5.3×-budget state-file anecdote.
11. D16(d): "The delegation share is a ratio of runs made, never a reason
    to make more" — a mangled transfer of the Cosmonaut's "generative
    slice" metric.
12. HOT vs COLD: the 1.3 MB screen-log anecdote.
13. CHANGELOG: the kernel-from-the-Cosmonaut lineage.
14. The template's `LESSONS.md` not copied (its transfer ledger against
    the research flavor); replaced by this factory's own.
15. `domain-notes/` not copied: engine lessons live in `docs/subsystems/`.

Structural changes made in the fill, with Grey's four decisions:

- The 24/7 liveness kernel (tick watchdog, fork rule, zombie predecessor,
  session-bound clocks, NEVER IDLE) → the shift model: a launcher
  preflight, a shift lock, an end-of-shift routine, a legal shift end
  when the board is full (D13, D14).
- The daily P&L-shaped digest → one shift report plus a typed board.
- A SWEEP tier (the finder) before SPIKE and CHANGE, with FINDINGS.md;
  LAUNCH-READINESS.md as the goal's instrument.
- Consent: the template's "land everything, gate release" → I1's matrix.
- `CLOSED.md` → idea-backlog § Rejected + LESSONS § METHOD LESSONS;
  `PLAYTEST-QUEUE.md` → NEEDS-GREY § PLAY; `EXAMPLE-robogame.md` consumed
  by the fill and deleted.
- I2's 25/50/75/90 % budget ladder → a subscription-plan shape (Grey to
  confirm; invariants are Grey's).
- The scribe → any interactive session, via `/inbox`.

Kernel docs it still leans on, on the hive: `~/dev/templates/loop-kernel/
RUNBOOK.md` (usage limits, secrets, the death audit) and `tools/`
(`usage_tally.py`, `loop_session_check.py`; both need Python and read
`~/.claude/projects/` on the machine the session ran on).
