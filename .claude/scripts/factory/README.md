# .claude/scripts/factory — the Robogame Factory's tools

Doctrine and state live in [docs/loop/](../../../docs/loop/README.md). These are the scripts the charter names.

| script | runs on | what |
|---|---|---|
| `Start-Factory.ps1` | desktop | preflight (charter D13) + a Windows Terminal tab running `claude` with the /loop prompt. `-DryRun` prints the launch. |
| `Stop-Factory.ps1` | anywhere | appends `STOP` to `docs/loop/INBOX.md`, commits, pushes; the loop ends the shift at its next checkpoint. |
| `ping.py` | desktop (or anywhere with the webhook) | the one road a ping takes to Discord: dash-bullets, the six-bullet cap, the same-day repetition guard, parts ≤ 1,900 chars. `DISCORD_WEBHOOK_FACTORY` from the environment or `.env`; without it, prints and exits 0. |
| `land.py` | desktop | `gate` records suite / perf / red-team verdicts against the branch's frozen sha; `land` merges, writes the docs/changes entry, the LOOP-STATE bullet and the NEEDS-GREY line, pings, ticks, pushes — one command, `--dry-run`. |
| `discord_intake.py` | hive (timer), works anywhere | #blue-mao-pow → INBOX.md, commit, push. `--dry-run` fetches and prints only. |
| `systemd/` | hive | the intake's unit + timer. |
| `tests/` | anywhere with Python 3.10+ | `python -m unittest discover -s tests -t tests` (stdlib only; no network). |

Standard library only, on purpose: the desktop gets Python from `winget` with nothing else installed.
Written 2026-09-16 on the hive; the PowerShell scripts have not yet run on Windows (first shift, HANDOFF (1)).
