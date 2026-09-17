# LESSONS — what this factory has paid for

(empty at seed, 2026-09-16.) The template's own lessons, the product flavor's transfer ledger against the research flavor and the kernel's scar tissue, stay in `~/dev/templates/product-factory/LESSONS.md` and `~/dev/templates/loop-kernel/LESSONS.md` on the hive and were not copied: they are about where the rules came from, not about this game. This file records only what THIS loop learns; fold an entry back into the templates when it generalizes, and tell the loop where it now lives.

## METHOD LESSONS (the closed registry for ways of working, D9; promote to CHANGE-QUEUE § ENGINEERING RULES when one recurs)

1. **Read the plan's usage at the top of every wake** (`get_usage` from the Desktop session; `/status` in a tab). 2026-09-16: the weekly all-models window was at 100 % with extra usage enabled before the first wake, so everything shift 2 spent (~350k sonnet tokens of sweeps plus the foreground) was usage credits. D-001's default ("no overage") is enforceable by the loop itself; the "silent stall" LAUNCH-PROMPT warns about is visible from inside the session. Paid for 2026-09-16. AMENDED 2026-09-17: Grey answered D-001 ("it can" use usage credits), so the check stays and its consequence changes: record the windows in the tick and the budget ledger, do not end the shift; the D16d per-shift ceiling is the bound until Grey names a dollar figure.
2. **The Editor must serve MCP on 8080 before the Desktop session connects, or the first wake must reconnect it.** The session's MCP client dials UnityMCP once at session start; the preflight launched the Editor in the same second, the dial failed, and the whole shift ran bridge-down (no console or visual sweep, no MCP proof). Fix landed in `/robogame-factory` (steps 1b, 1c); a launcher-side wait for 8080 is the better instrument (BACKLOG 12). Paid for 2026-09-16.
3. **`.vscode/settings.json` in the clone is not work.** The dotnet extension rewrites `dotnet.defaultSolution` to the clone's folder name on open, so it shows as an orphan at every preflight. Reverted and `git update-index --skip-worktree` set in the clone (rig config, not a commit). Paid for 2026-09-16.
4. **A file-level sweep on sonnet/medium costs 70–105k tokens** (2026-09-16: provenance 71k, invariants 74k, best-practices 100k, doc-drift 104k). Budget four or five sweeps a shift under the D16d ceiling, not eight.

## PREDICTIONS TO SETTLE (from the template; mark paid-for or delete)

- Human bandwidth is the binding constraint and the board's caps will bind within a week.
- The first shift will be mostly instruments (the rig proven, the soak), and that is the right shift. — Half paid 2026-09-16: the rig was proven (suite 1m28s warm, green) and nothing else was built; the shift ended on the plan cap (METHOD 1), not on the ladder.
- "Can't tell" playtest verdicts will be more common than "worse" and will mean the question was wrong more often than the change was small.
