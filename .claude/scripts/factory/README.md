# .claude/scripts/factory — the Robogame Factory's tools

Doctrine and state live in [docs/loop/](../../../docs/loop/README.md). These are the scripts the charter names.

| script | runs on | what |
|---|---|---|
| `Start-Factory.ps1` | desktop | preflight (charter D13), the rig SERVER-FIRST (CHG-020: start MCP for Unity's server by hand when 8080 is silent, wait for it, open and foreground the factory Editor, wait for it to register; `-NoEditor` skips it), then a Windows Terminal tab running `claude` with the /loop prompt, or with `-Desktop` the prompt to paste into a Claude Desktop session. `-DryRun` prints the rig steps and the launch. |
| `mcp_http.py` | desktop | MCP for Unity over plain HTTP (stdlib only): `ping`, `instances`, `tools`, `resource <uri>`, `call <tool> '<json>'`, `code '<C#>'`, `wait-instance <name> [s]`. A session's UnityMCP connector dials once at start and cannot be re-dialled; this script does not care. Exit 0 ok, 1 tool error, 2 no server. |
| `perf_band.py` | desktop (bridge up) | a SETTLED idle-frame band from the live factory Editor: `--scene Arena|Garage --warmups 2 --runs 5` runs the PerfBaselineHarness over `mcp_http.py`, waits for the rig before every run (a `-runTests` Unity.exe, never `-batchmode`: AssetImportWorkers carry it), records the Editor's focus, and prints one BAND line (min–max and spread per metric) to `.utmp/factory/perf-band-<scene>-<date>.txt` for LOOP-STATE § THE BUILD (CHG-026). |
| `Stop-Factory.ps1` | anywhere | appends `STOP` to `docs/loop/INBOX.md`, commits, pushes; the loop ends the shift at its next checkpoint. |
| `ping.py` | desktop (or anywhere with the webhook) | the one road a ping takes to Discord: dash-bullets, the six-bullet cap, the same-day repetition guard, parts ≤ 1,900 chars. `DISCORD_WEBHOOK_FACTORY` from the environment or `.env`; without it, prints and exits 0. |
| `land.py` | desktop | `gate` records suite / perf / red-team verdicts against the branch's frozen sha; `land` merges, writes the docs/changes entry, the LOOP-STATE bullet and the NEEDS-GREY line, pings, ticks, pushes — one command, `--dry-run`. |
| `discord_intake.py` | hive (timer), works anywhere | #blue-mao-pow → INBOX.md, commit, push. `--dry-run` fetches and prints only. |
| `systemd/` | hive | the intake's unit + timer. |
| `tests/` | anywhere with Python 3.10+ | `python -m unittest discover -s tests -t tests` (stdlib only; no network). |

Standard library only, on purpose: the desktop gets Python from `winget` with nothing else installed.
Written 2026-09-16 on the hive; the PowerShell scripts have not yet run on Windows (first shift, HANDOFF (1)).
