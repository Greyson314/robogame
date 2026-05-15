# Robogame — Claude Code Project Context

> Entry point for any Claude Code session on this project. Intentionally short — the load-bearing context lives in the linked docs. Read this once, then load the docs you need for the task at hand.

## What this project is

Robogame is a personal recreation of [Robocraft](https://store.steampowered.com/app/301520/Robocraft/) — a voxel-style robot building and battle game — built in **Unity 6** with **C#**. Solo dev, AI-assisted ("vibe-coded"), with strict architectural discipline as a counterweight to the iteration speed.

Eventual goal: ship to Steam. Current state: singleplayer with garage + arenas (flat / spherical / water) + build mode + multiple chassis types. Multiplayer is planned, not yet started.

## Read these before doing real work

- **[README.md](README.md)** — top-level overview, architecture principles, multiplayer roadmap.
- **[docs/changes/README.md](docs/changes/README.md)** — session log index. The highest-numbered file is the current state of WIP.
- **[docs/changes/architecture.md](docs/changes/architecture.md)** — current modules, runtime flow, gotchas.
- **[docs/PHYSICS_PLAN.md](docs/PHYSICS_PLAN.md)** — § 1 is non-negotiable. Read in full before any physics work.
- **[docs/TIP_BLOCK_ATTACH.md](docs/TIP_BLOCK_ATTACH.md)** — how rope-mounted Hook/Mace/Magnet latch onto targets, the three concurrent constraints (Verlet chain + chassis↔tip leash + tip↔target SpringJoint), and why the pre-session-60 Locked ConfigurableJoint kept self-destructing. Read before touching any tip-block behaviour.
- **[docs/BEST_PRACTICES.md](docs/BEST_PRACTICES.md)** — coding conventions, perf budgets (§ 16).
- **[docs/PERFORMANCE.md](docs/PERFORMANCE.md)** — perf rules, diagnostics, predicted future hotspots, "the game feels slow" runbook.
- **[docs/PACKAGE_MODIFICATIONS.md](docs/PACKAGE_MODIFICATIONS.md)** — third-party package source edits and how to re-apply them after an upgrade.
- **[docs/NETCODE_PLAN.md](docs/NETCODE_PLAN.md)** — multiplayer-readiness contract.
- **[docs/AUDIO_PLAN.md](docs/AUDIO_PLAN.md)** — audio plumbing rules; read before authoring sound clips or touching `AudioRouter`.
- **[docs/ART_DIRECTION.md](docs/ART_DIRECTION.md)** — palette, art rules, imported assets.
- **[docs/SPHERICAL_ARENAS.md](docs/SPHERICAL_ARENAS.md)** — planet-arena physics and gravity model.
- **[docs/TERRAFORMING_PLAN.md](docs/TERRAFORMING_PLAN.md)** — smooth-voxel dig-only terrain. Read before any work on destructible terrain, drill blocks, bomb-crater behaviour, or underground POIs. § 2 (dig-only invariant) and § 7 (triangle budget) are the load-bearing constraints.
- **[docs/ROBOCRAFT_REFERENCE.md](docs/ROBOCRAFT_REFERENCE.md)** — design research baseline.
- **[docs/GAME_DESIGN_PILLARS.md](docs/GAME_DESIGN_PILLARS.md)** — committed design directions and open questions.

## Hard invariants (do not violate without explicit user approval)

1. **No Tweakable affects gameplay outcomes.** Tweakables are per-machine, persisted to local JSON. Using one to drive damage, lift, hit detection, or anything visible to other players desyncs the second netcode lands. Move that data to the chassis blueprint (server-authoritative) instead. See PHYSICS_PLAN § 1.5.
2. **Building happens only in the garage.** Blueprints are frozen at match start. The block-index ordering (sorted by `Vector3Int`) is part of the netcode contract — it must be stable across spawn.
3. **Server is authoritative for all gameplay state.** Even in singleplayer, structure code as if the server is a separate process.
4. **Single Rigidbody per chassis.** Free-body children of a moving Rigidbody fight the solver. Parent free bodies under scene root, not under the chassis.
5. **Default to zero baseline cost.** Every new physics block must have a config that adds zero Rigidbodies and zero colliders. Anything heavier is opt-in.
6. **No per-frame allocations.** No `new` in `Update` / `FixedUpdate` / `OnCollision*`. Pre-size lists at build time, reuse them.
7. **Profile before claiming a perf characteristic.** "Well under budget" is not acceptable without a Profiler capture or a static count from a real measurement.
8. **Every new feature ships with VFX + audio.** As of session 30 (audio v1) the project has both pipelines wired (`VfxSpawner` + procedural particle kinds; `AudioRouter` + `AudioCue` + `AudioCueLibrary`). New gameplay systems — weapons, blocks, movement modes, match-state events, UI — must include a good-faith pass at both. If a clip / cue doesn't exist yet, declare the cue, leave the library entry blank, and call `AudioRouter.PlayOneShot` at the gameplay site anyway: the missing-cue logger surfaces it and the audio pass picks it up. Same for VFX: pick the closest `VfxKind`, hook the call site, tune scale at the editor table. **Do not ship a feature with both audio AND VFX deferred to "later".** See [docs/AUDIO_PLAN.md](docs/AUDIO_PLAN.md) for the audio-side contract and [docs/changes/29-vfx-and-audio-bones.md](docs/changes/29-vfx-and-audio-bones.md) for the VFX kinds.

## Known failure modes (these have bitten before)

- **Statics survive domain reload, GameObjects don't.** Any static cache of Unity objects must `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset.
- **`AddComponent<T>` runs `OnEnable` synchronously.** Reflection-based serialised-field assignment must happen with the root deactivated. See `ChassisFactory.Build`.
- **`AssetDatabase.Refresh` invalidates C# refs.** Re-load by path right before `SerializedObject.FindProperty(...).objectReferenceValue = ...`.
- **Input System UI doesn't gate over UI for free.** Use `EventSystem.current.IsPointerOverGameObject()` to suppress fire / camera-capture / etc. when the cursor's on the HUD.
- **Pattern-matching to "Unity rope = ConfigurableJoint chain" is the wrong reflex.** PhysX joint chains are unstable under sustained spin and expensive to network. Custom Verlet solver is the migration target. Existing `RopeBlock` and `RotorBlock` use joints today; that's tech debt to migrate, not a pattern to extend. See PHYSICS_PLAN § 2.

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

Skip subagents for trivial work: one-line fixes, doc edits, pure cosmetic tweaks.

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