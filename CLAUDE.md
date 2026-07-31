# Robogame — Claude Code Project Context

> Entry point for any Claude Code session on this project. Intentionally short — the load-bearing context lives in the linked docs. Read this once, then load the docs you need for the task at hand.

## What this project is

Robogame is a personal recreation of [Robocraft](https://store.steampowered.com/app/301520/Robocraft/) — a voxel-style robot building and battle game — built in **Unity 6** with **C#**. Solo dev, AI-assisted ("vibe-coded"), with strict architectural discipline as a counterweight to the iteration speed.

Eventual goal: ship to Steam. Current state: singleplayer with garage + arenas (flat / spherical / water) + build mode + multiple chassis types. Multiplayer is planned, not yet started.

## Docs are tiered — read accordingly

The docs tree is organised into three tiers. Treat them differently.

**Tier 1 — invariants and conventions. Always current. Follow these.**

- **[docs/invariants.md](docs/invariants.md)** — the hard rules. Violating one is a regression. Changes require an ADR.
- **[docs/best-practices.md](docs/best-practices.md)** — coding conventions, perf budgets (§ 16).
- **[docs/decisions/](docs/decisions/)** — Architecture Decision Records. Follow every `Accepted` ADR. Ignore `Superseded` and `Rejected` ones.

**Tier 2 — living subsystem docs. Current state of each system. Read by topic.**

Located in [`docs/subsystems/`](docs/subsystems/). These are kept up to date but they describe one subsystem's situation, not project-wide rules. If a subsystem doc conflicts with `invariants.md`, the invariants win. If you find a tier-2 doc that contradicts the code you just read, fix the doc before working in that subsystem.

- [physics.md](docs/subsystems/physics.md) — rope tech today vs. Verlet target, migration triggers.
- [netcode.md](docs/subsystems/netcode.md) — multiplayer-readiness contract.
- [terraforming.md](docs/subsystems/terraforming.md) — smooth-voxel dig-only terrain.
- [tip-blocks.md](docs/subsystems/tip-blocks.md) — Hook/Mace/Magnet attach mechanics. Read before touching tip-block behaviour.
- [audio.md](docs/subsystems/audio.md) — audio plumbing rules.
- [art-direction.md](docs/subsystems/art-direction.md) — palette, art rules, imported assets.
- [spherical-arenas.md](docs/subsystems/spherical-arenas.md) — planet-arena physics and gravity.
- [performance.md](docs/subsystems/performance.md) — perf rules, diagnostics, predicted hotspots.
- [performance-pass.md](docs/subsystems/performance-pass.md) — measurement workflow.
- [performance-baselines.md](docs/subsystems/performance-baselines.md) — capture log.
- [burst-notes.md](docs/subsystems/burst-notes.md) — Burst onboarding patterns.
- [scalable-parts.md](docs/subsystems/scalable-parts.md) — per-instance dimensions (Phase 2+ pending).

**Tier 3 — reference and historical. Not directives.**

- [docs/research/](docs/research/) — domain knowledge: [robocraft-reference.md](docs/research/robocraft-reference.md), [game-design-pillars.md](docs/research/game-design-pillars.md).
- [docs/research/historical/](docs/research/historical/) — completed or superseded design docs kept for rationale. **Do not take direction from these.** They describe how thinking evolved.

**Utility / context.**

- [README.md](README.md) — top-level overview, multiplayer roadmap.
- [docs/changes/README.md](docs/changes/README.md) — session log index. The highest-numbered file is the current state of WIP.
- [docs/changes/architecture.md](docs/changes/architecture.md) — current modules, runtime flow, gotchas.
- [docs/PACKAGE_MODIFICATIONS.md](docs/PACKAGE_MODIFICATIONS.md) — third-party package source edits.

## How documentation evolves

- New plan or proposal → write an ADR in `docs/decisions/`. Get user approval before merging.
- Decision changes a hard rule → ADR is mandatory. Update `invariants.md` once accepted.
- New living subsystem doc → put it in `docs/subsystems/`. Lowercase-kebab filename.
- Doc fully superseded by code → delete it (git preserves history) and note the delete in the next session log entry. Do NOT leave stale plans in `docs/` root.
- Doc partially superseded → either split (move still-true bits to a subsystem doc), or move the whole thing to `docs/research/historical/` and capture remaining work in `docs/changes/README.md` "Known unknowns."

This convention is itself encoded in [docs/decisions/0001-doc-tiering-and-adrs.md](docs/decisions/0001-doc-tiering-and-adrs.md).

## Continual Traces — code↔decision breadcrumbs

When a line of code exists *because* of a decision, rule, finding, or
prior session, anchor it with an inline trace so the rationale stays
discoverable and the link rots loudly:

```csharp
// TRACE[ADR-0002]: mirror is the sanctioned 2nd Rigidbody
// TRACE[INV-4]: single Rigidbody per chassis — carve-out applies here
// TRACE[AUDIT-1]: replay must never global-Simulate the whole scene
```

Syntax: `// TRACE[id]: note` (inline `//`, not `///`). Id kinds:
`ADR-NNNN` → `docs/decisions/`, `INV-N` → `docs/invariants.md` § N,
`AUDIT-N` → finding N in the 109 audit, `LOG-NN` → session log
`docs/changes/NN-*.md`, `DOC:name[§sec]` → a file under `docs/`.

Tooling (matches the scaffolders): **Robogame → Traces → Validate** reports
dangling traces in the console; **Robogame → Traces → Rebuild Index**
regenerates [docs/TRACES.md](docs/TRACES.md) (anchor → code sites). Both
manual, non-blocking. Implemented in
`Assets/_Project/Scripts/Tools/Editor/ContinualTraces.cs`. Don't blanket-
retrofit existing prose refs ("ADR-0002", "invariant #4") — add a trace
when you touch load-bearing code whose reason isn't obvious from the line.

## Hard invariants (do not violate without explicit user approval)

The canonical list with full rationale lives in [docs/invariants.md](docs/invariants.md). The short version, for reference:

1. No `Tweakable` affects gameplay outcomes.
2. Building happens only in the garage; blueprints frozen at match start.
3. Server is authoritative for all gameplay state.
4. Single Rigidbody per chassis.
5. Default to zero baseline cost for new physics blocks.
6. No per-frame allocations.
7. Profile before claiming a perf characteristic.
8. Every new feature ships with VFX + audio.
9. Terraforming is dig-only.
10. Triangle and chunk budgets for voxel terrain are hard ceilings.

Read [docs/invariants.md](docs/invariants.md) before doing real work.

## Known failure modes (these have bitten before)

- **Statics survive domain reload, GameObjects don't.** Any static cache of Unity objects must `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset.
- **`AddComponent<T>` runs `OnEnable` synchronously.** Reflection-based serialised-field assignment must happen with the root deactivated. See `ChassisFactory.Build`.
- **`AssetDatabase.Refresh` invalidates C# refs.** Re-load by path right before `SerializedObject.FindProperty(...).objectReferenceValue = ...`.
- **Input System UI doesn't gate over UI for free.** Use `EventSystem.current.IsPointerOverGameObject()` to suppress fire / camera-capture / etc. when the cursor's on the HUD.
- **Pattern-matching to "Unity rope = ConfigurableJoint chain" is the wrong reflex.** PhysX joint chains are unstable under sustained spin and expensive to network. The custom Verlet/PBD rope solver shipped (`VerletRopeSimulator`); `RotorBlock` is joint-free, and `RopeBlock` keeps exactly one sanctioned chassis↔tip leash `ConfigurableJoint`. Do not add new joint chains. See [docs/subsystems/physics.md § 2](docs/subsystems/physics.md).

## User preferences

- Cite sources when confidence is anything other than high.
- Avoid common AI writing tropes ("Not just X, but Y", em-dash and semicolon spam).
- Take a beat before responding to ensure no hallucination.
- Prefer prose over bullet lists for explanations.
- Be honest about confidence levels, especially on perf numbers.

## Workflow

This is AI-assisted vibe-coded development. Claude Code is the primary coding tool. The user is the architect and reviewer.

For non-trivial implementation work:

1. **Use the Planner subagent first** (`.claude/agents/planner.md`). It reads relevant docs and produces a plan for user review *before* execution. Catches design-implementation drift cheaply rather than after a 10-minute build.
2. **Run the Test Drafter in parallel** when adding gameplay systems (`.claude/agents/test-drafter.md`). Tests land alongside code rather than as later cleanup.
3. **After implementation lands, dispatch `qa-verifier` and `perf-checker` in parallel** (a single message with two Agent tool calls) before declaring the feature done. `qa-verifier` runs build + tests + Unity console check; `perf-checker` captures the profiler and compares against budgets. Their verdicts are the gate for "done." Skip both for pure doc / comment / cosmetic changes; skip `perf-checker` alone for features that demonstrably add zero physics objects.
4. **Use `design-pilot` for game-design questions.** When the question is "what would be fun here?" / "how did Robocraft handle X?" / "should mechanic Y feel like Z?", route to the design subagent — it reads `docs/research/game-design-pillars.md` and `docs/research/robocraft-reference.md` so design ideation stays grounded.

Skip subagents for trivial work: one-line fixes, doc edits, pure cosmetic tweaks.

**Background-mode test runs.** `.claude/scripts/run-tests.sh` takes 30–90s. When the main agent has other work to make progress on while tests run (e.g., starting docs or queueing the next sub-task), invoke it via Bash with `run_in_background: true` and continue. The harness notifies on completion — do not poll.

### Project hooks

`.claude/settings.json` wires four project-scoped hooks (PowerShell scripts under `.claude/hooks/`): a `PreToolUse` worktree-edit guard, a `PostToolUse` C# edit marker, a `Stop` reminder to check the Unity console after C# edits, and a `SessionStart` notice surfacing the current session log. See [docs/changes/88-claude-code-hooks.md](docs/changes/88-claude-code-hooks.md) for details and rationale.

### Subagent roster

`.claude/agents/` holds five subagents. Two read-only research (`planner`, `design-pilot`), one drafts tests (`test-drafter`), two verify after implementation (`qa-verifier`, `perf-checker`). See [docs/changes/89-subagent-squadron.md](docs/changes/89-subagent-squadron.md) for the dispatch protocol and the rationale for each.

## Active work

Check the highest-numbered file in [docs/changes/](docs/changes/) for the current session's intent and any outstanding regressions. New session entries go in `docs/changes/NN-slug.md`, never appended to existing files.


# CLAUDE.md — 12-rule template

These rules apply to every task in this project unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.

## Rule 1 — Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.

## Rule 2 — Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

## Rule 3 — Surgical Changes
Touch only what you must.
Don't "improve" adjacent code, comments, or formatting.
Match existing style.

## Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

## Rule 5 — Use the model only for judgment calls
Use me for: classification, drafting, summarization, extraction.
Do NOT use me for: routing, retries, deterministic transforms.
If code can answer, code answers.

## Rule 6 — Token budgets are not advisory
If approaching extreme token usage for task, summarize and start fresh.
Surface the breach. Do not silently overrun.

## Rule 7 — Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

## Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

## Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

## Rule 10 — Checkpoint after every significant step
Summarize internally or externally what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

## Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside the codebase.
If you genuinely think a convention is harmful, surface it. Don't fork silently.

## Rule 12 — Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.