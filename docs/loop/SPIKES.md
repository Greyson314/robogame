# SPIKES — one line per spike (D4 SPIKE tier). Append-only; never red-teamed; never a gate; never merged.

Line format: `N. [TAG] question — source — what was tried (branch/command, n) — answer ± D6 band — VERDICT — lesson` (≤ 900 characters; the cap is a ceiling, not a target).
Verdicts: KEEP (→ CHANGE-QUEUE with a draft acceptance criterion) / DROP / NOTE (a fact, not a candidate) / BLOCKED (names the missing tool, asset or capability) / PARK (needs a PLAY verdict or a DECIDE from Grey; reopens when it lands).
The cheapest kill is a pillar check, then a count, then a build. A spike branch is deleted or archived after its line is written; it never grows into the feature.

1. [PARK] Can the foreground move to the hive by hosting MCP for Unity's Python server there and pointing a desktop Editor at it (option C)? — LOOP-STATE BACKLOG 8; Grey 2026-09-16 "spike C later" — nothing tried (ASSUMPTIONS #3–#5 unverified) — no answer — PARK until Grey opts in; reopens without ceremony — lesson: none yet.
2. [OPEN] docs/subsystems/spherical-arenas.md describes planet arenas as unbuilt while PlanetArenaController / PlanetBody / PlanetGravityBody ship (F-012): rewrite in place, split, or move to historical? — doc-drift sweep 2026-09-16 — not yet tried; the cheapest step is a one-page current-state read of the three classes against the doc's sections — no answer — OPEN (next shift, rung 7) — lesson: none yet.
