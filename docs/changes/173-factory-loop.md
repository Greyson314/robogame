# 173 — The Robogame Factory: an autonomous progress loop, instantiated (LOG-173)

Session on the hive, 2026-09-16, on branch `bot/robogame-factory-loop-eb0ffe`.
No C# touched; no Unity verification applies.

## User intent

Grey runs an autonomous research loop for a trading bot (the Cosmonaut,
`~/bootleg_botlet`) from templates in `~/dev/templates`. He wanted the
same shape for this game as a "progress factory": a loop whose "primary
goal is to get this game polished, refined, and developed towards the
goal of readiness for launch", that finds "issues, needed polish, visual
changes, performance tweaks, features, inconsistencies, best practices,
requested purchases" and surfaces them to him, and that can triage his
feature requests without repeated wakes. The rub: Unity, Blender and
the Unity MCP live on the Windows desktop, while the templates and the
Cosmonaut live on the hive. Asked for: a copy of the product-factory
template, an analysis of what in it was overfit to a backtesting
trading bot, a proposed approach, and — with permission — the template
modified to match.

## What shipped

- **docs/loop/** — the factory's home: `CHARTER.md` (the constitution,
  filled), `LOOP-STATE.md` (with a first-launch HANDOFF), `NEEDS-GREY.md`
  (the board: APPROVE / PLAY / BUY / DECIDE / FYI), `INBOX.md`,
  `FINDINGS.md`, `SPIKES.md`, `CHANGE-QUEUE.md`, `LAUNCH-READINESS.md`,
  `ASSUMPTIONS.md`, `LESSONS.md`, `LAUNCH-PROMPT.md`, `README.md` (the
  decisions, setup, and a provenance list of every cut from the template).
- **.claude/scripts/factory/** — `Start-Factory.ps1` (preflight + a
  Windows Terminal tab running `claude` with the /loop prompt),
  `Stop-Factory.ps1`, `ping.py` (six-bullet cap, same-day repetition
  guard), `land.py` (`gate` + `land`: the landing chain as one command
  with `--dry-run`), `discord_intake.py` (#blue-mao-pow → INBOX, commit,
  push), the hive's systemd unit + timer, and 21 unit tests (stdlib,
  no network) that pass on the hive.
- **.claude/agents/** — `red-team` (opus/high, the gate's independent
  pass) and `sweeper` (sonnet/medium, the finder); `effort:` set on the
  five existing agents (they inherited the foreground's effort before).
- **.claude/commands/inbox.md** — `/inbox <text>` from any session.
- `.gitignore`: `.env`. `CLAUDE.md`: a pointer section.

## Decisions (Grey, 2026-09-16, via the check-off in session)

1. Host: the desktop, in the factory's own clone; the hive-hosted MCP hub
   parked as the first instrument spike.
2. Cadence: shifts first; nightly Task Scheduler shift optional later.
3. Consent: provable classes land automatically, everything visible,
   felt, new, removed, bought or invariant-adjacent asks (CHARTER I1).
4. Inbound: both — the hive's Discord intake on #blue-mao-pow, and git.

## What we learned

- **The template was overfit in two layers.** Textually: the
  Cosmonaut's anecdotes and research vocabulary inside the constitution
  (fifteen cuts, listed in docs/loop/README.md). Structurally: the 24/7
  liveness kernel, a P&L-shaped daily digest, no "find" tier, consent
  placed at release, and a scribe pattern for a human who does not read
  the files. Grey reads these files; the scribe is `/inbox`.
- **"The hive can't drive the Unity MCP" is true as configured, false
  as architecture.** MCP for Unity v9.7.3 (the pinned version) is an
  Editor plugin that connects OUTBOUND over WebSocket to a separate
  Python server; the server "can run on a different machine". Today the
  Editor auto-starts it on 127.0.0.1:8080 and `.mcp.json` points there,
  so a hive session gets connection-refused. A hub on the hive would
  work with no port opened on Windows; it is unverified and parked
  (LOOP-STATE § BACKLOG 8, ASSUMPTIONS #4–5).
- **The desktop has no Python** (run-tests.sh's own comment); the kernel
  tools need it. One `winget install` (README § Desktop setup).
- **origin/main is 17 days behind the desktop's work** (last push
  2026-08-30). The first thing before any shift is a push.
- The channel `blue-mao-pow` (id from the webhook object) is readable by
  the existing intake bot (verified 2026-09-16 with two HTTP 200s).

## Open items

- Grey: merge this branch; push the desktop; clone the factory copy;
  install Python; a fresh webhook into `.env` (the one pasted in chat is
  in a transcript); answer NEEDS-GREY D-001 (overage) and D-002 (v1
  scope); then `Start-Factory.ps1`.
- Hive: enable `robogame-inbox-intake.timer` once docs/loop is on main.
- Untested on Windows: both PowerShell scripts and whether `/loop` arms
  from the launcher's initial prompt (HANDOFF (8), ASSUMPTIONS #6).
- I2 was reshaped for a subscription plan (no % ladder); invariants are
  Grey's — confirm or revert.
