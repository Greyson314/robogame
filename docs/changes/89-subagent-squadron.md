# 89 — Subagent squadron: QA verifier, perf checker, design pilot + parallel-dispatch protocol

> Status: **Tooling pass.** No gameplay changes. Adds three new subagents under `.claude/agents/` (taking the roster from 2 → 5), encodes a parallel-dispatch protocol in CLAUDE.md, and documents background-mode test runs as the default for `run-tests.sh` when the main agent has other work.

## Why this session

User read external write-ups about Claude Code "subagent squadrons" claiming significant autonomy gains. Pushed back on the prior session's caution around subagent token cost. The pushback was warranted — that caution generalized incorrectly. The 30–100k spin-up cost (per memory `feedback_subagent_economy.md`) is real but it's a *per-invocation* cost, not a *per-session* one, and it amortizes cleanly when the subagent processes naturally-voluminous outputs the main context shouldn't carry.

The right shape for subagents in this project: **context isolation** for noisy verification work, **parallel execution** when independent checks can run simultaneously, and **focused doc loading** for ideation that doesn't need the full Unity tech-debt corpus.

## What shipped

**Three new subagents.**

[`qa-verifier`](../../.claude/agents/qa-verifier.md). Fires after implementation lands. Runs `dotnet build`, runs `.claude/scripts/run-tests.sh`, queries `Unity_ReadConsole`, optionally captures a `SceneView` for visual features. Returns a structured PASS/FAIL/PARTIAL verdict with only the load-bearing evidence. Model: sonnet. Tools: Bash + Read/Glob/Grep + a tight set of Unity MCP tools (no Edit/Write, no profiler).

[`perf-checker`](../../.claude/agents/perf-checker.md). Captures profiler data in editor play mode, compares against budgets in BEST_PRACTICES § 16 and PERFORMANCE.md. Returns WITHIN_BUDGET / DRIFT / VIOLATION / UNVERIFIABLE. Encodes the lessons from session 88's smoke test (frame-time data may be missing even when GC data exists; 2000-frame ring buffer rolls forward; EditorLoop overhead pollutes numbers). Model: sonnet. Tools: profiler + scene/editor management, no Bash, no Edit/Write.

[`design-pilot`](../../.claude/agents/design-pilot.md). Game-design ideation partner. Reads `GAME_DESIGN_PILLARS.md` and `ROBOCRAFT_REFERENCE.md` first, researches comparable games (WebSearch / WebFetch), proposes 2–4 directions with pillar alignment and prototype cost. Explicit "don't propose mechanics that violate hard invariants" rule so the fun side doesn't accidentally invalidate netcode commitments. Model: sonnet. Tools: Read/Glob/Grep + Web only.

**Workflow changes in CLAUDE.md.**

Two additions to the Workflow section:
- After implementation lands, dispatch `qa-verifier` + `perf-checker` in parallel (single message, two Agent calls). Their verdicts gate "done." Skip both for pure doc / comment / cosmetic changes; skip `perf-checker` alone when zero physics objects added.
- `run-tests.sh` is 30–90s. Default to `run_in_background: true` when the main agent has follow-up work. Harness notifies on completion — no polling.

`design-pilot` is invoked on-demand for design questions, not auto-dispatched.

## Token-cost rationale (revised vs prior session)

| Subagent          | When it runs        | Tokens consumed by it (rough)    | Tokens it saves main context |
|-------------------|---------------------|----------------------------------|------------------------------|
| `qa-verifier`     | After each feature  | 30–50k (build log + test parse)  | 10–30k (raw build / test output) |
| `perf-checker`    | After physics work  | 40–80k (profiler captures roll)  | 20–60k (multi-frame profiler dumps) |
| `design-pilot`    | On design question  | 30–60k (pillar docs + web)       | Indirect — keeps design grounded without main agent re-reading everything |

For the verification pair specifically, amortization is favorable because the *alternative* is the main agent doing the same work and carrying the noise. For `design-pilot`, the math is closer; the win is conceptual (it forces the pillar-read before ideation) more than tokens-on-paper.

## Hard invariants the subagents respect

All three subagents are read-only with respect to source code. Only `qa-verifier` runs Bash, and only `dotnet build` / `.claude/scripts/run-tests.sh`. None can call `Unity_RunCommand` (arbitrary code execution risk). None bypass the hard invariants in CLAUDE.md — `design-pilot` has an explicit rule against proposing mechanics that violate them, and the verification agents only verify, never edit.

## Parallel dispatch — concrete example

After landing a new drill block:

```
(single message containing two Agent tool calls)
  Agent(subagent_type=qa-verifier, prompt="Verify the new DrillBlock landed. Touched Assets/_Project/Scripts/Blocks/Drill/...")
  Agent(subagent_type=perf-checker, prompt="Check perf impact of the new DrillBlock in Arena scene. Adds a Rigidbody and a per-frame collision forwarder per drill.")
```

Both run concurrently, two verdicts come back, main agent decides whether to ship.

## Out of scope / followups

- **No auto-commit-on-green-test hook yet.** I raised it as a possibility; deferred pending an agreed predicate. Risk: trigger-happy commits the user didn't intend.
- **No `qa-verifier` for editor-build-script changes.** Those don't run through `run-tests.sh` cleanly. Verification path would need to be `dotnet build` + a manual Unity batch-import. Not wired.
- **`design-pilot` cannot reach external assets behind paywalls.** Robocraft post-mortems and dev blogs are mostly accessible; some Crossout / TerraTech analysis lives behind Patreon or video walls.
- **Token-cost numbers in the table above are rough.** Calibrated against this and the prior sessions' usage, not measured precisely.
