---
description: Generate game-design ideas grounded in current mechanics + vibe + competitor references + player payoff, surface them for check-off, then build the approved ones autonomously.
---

# /ideate — grounded ideation → check-off → autonomous build

You are running the Robogame ideation workflow. The goal is to surface a small slate of
*grounded* new-feature ideas, let the user check off which to build, and then build the
approved ones autonomously. Work the phases in order. Do not skip the check-off gate.

Optional argument `$ARGUMENTS` is a focus hint (e.g. "movement", "progression", "arena
hazards"). If empty, range across the whole game.

---

## Phase 0 — Ground yourself (read before generating anything)

1. Read `docs/research/idea-backlog.md` (create it from the stub schema below if missing).
   Note every idea already marked `approved`, `rejected`, or `shipped` — you must NOT
   re-pitch those. `rejected` especially means "never suggest again."
2. Read the highest-numbered file in `docs/changes/` — that is the current WIP state.
   What shipped most recently is the freshest signal for what the game is becoming.
3. Skim `docs/changes/architecture.md` for the current module/mechanic inventory, so ideas
   build on what *exists* (blocks, chassis types, arenas, tip-blocks) rather than greenfield.

The design-pilot subagent (next phase) reads the pillars and competitor reference itself, so
you don't need to — but you DO need the backlog and WIP state to brief it.

## Phase 1 — Generate a grounded slate (delegate to design-pilot)

Dispatch the `design-pilot` subagent in one Agent call. Brief it with:

- The focus hint (`$ARGUMENTS`) if any.
- The **already-decided ideas to avoid** (titles from the backlog: approved/rejected/shipped).
- The recent-WIP context you gathered in Phase 0.
- This explicit ask: *"Propose up to 4 new feature ideas. Each must be grounded in (a) the
  game's existing mechanics, (b) the established vibe/art-direction, (c) at least one
  competitor reference for how a comparable game handled it, and (d) an estimate of player
  payoff. Rank the 4 by player payoff, highest first. For each: short name, 2-3 sentence
  mechanic, which pillar it serves or strains, the competitor reference, a one-line
  player-payoff rationale, and a rough prototype cost in sessions. Flag any idea that would
  require revisiting a committed pillar or a hard invariant."*

Cap the slate at **4 ideas** (the check-off tool surfaces at most 4 cleanly). If design-pilot
returns more, keep the extras in the backlog as `proposed` and only surface the top 4 by payoff.

If any returned idea would violate a hard invariant in CLAUDE.md, drop it before surfacing and
say so — do not make the user reject something that was never buildable.

## Phase 2 — Surface for check-off (the gate — never skip)

Present the slate to the user as prose first: a numbered list, ranked by player payoff, each
with its one-line payoff rationale and prototype cost so the ranking is legible.

Then use **AskUserQuestion** to capture verdicts:

- **Q1 (multiSelect):** "Which of these should I build now?" — options are the idea names.
  Selected = `approved`.
- **Q2 (multiSelect):** "Of the ones you didn't pick, which should I mark *rejected* so I
  never suggest them again? (Anything left unselected stays in the backlog as `proposed` for
  a future round.)" — options are the *un-approved* ideas. Selected = `rejected`; the rest
  stay `proposed`. Skip Q2 if every idea was approved.

## Phase 3 — Persist verdicts to the backlog

Append/update each idea in `docs/research/idea-backlog.md` with its status, the date
(`2026-05-28` format — use today's actual date), the one-line payoff rationale, and the
competitor reference. Keep entries terse (≈3-5 lines each). This is the dedupe memory for
future runs — its accuracy is load-bearing.

## Phase 4 — Build the approved ideas autonomously

The check-off in Phase 2 IS the sign-off. For each `approved` idea, no further approval gate —
run the full chain:

1. **Planner subagent** drafts a short implementation plan (internal step, not an approval
   gate — do not stop for plan sign-off; the user already chose fully-autonomous build).
2. **Implement** the change in the main checkout
   (`C:\Users\Grey\Desktop\mutedtuple\robogame\`, never a worktree). Honor every hard
   invariant: server authority, single-Rigidbody chassis, build-only-in-garage, no Tweakable
   affecting gameplay, zero-baseline-cost blocks, no per-frame allocations. Ship VFX + audio
   with the feature (invariant #8).
3. **Verify**: dispatch `qa-verifier` and `perf-checker` in parallel (one message, two Agent
   calls). Skip `perf-checker` only if the feature demonstrably adds zero physics objects.
4. **Commit** at the checkpoint on `main` (commit freely; do NOT push unless asked).
5. Update the idea's backlog status to `shipped`.

Then move to the next approved idea. Build in payoff order (highest first).

### The one place you MUST stop mid-build

Fully-autonomous applies to *expected* work. If, while building, an approved idea turns out to
require an **architectural change, a new ADR, an invariant revision, or any irreversible /
externally-visible action**, STOP and surface it before proceeding. The autonomy grant covers
implementing the agreed feature, not silently re-architecting to make it fit. Fail loud.

After all approved ideas are built, give a one-paragraph summary: what shipped, what's still
`proposed` in the backlog for next time.

---

## Backlog stub schema (create `docs/research/idea-backlog.md` if absent)

```markdown
# Idea Backlog

Dedupe memory for the `/ideate` workflow. Statuses: `proposed` (surfaced, not yet decided),
`approved` (building / queued), `rejected` (never re-suggest), `shipped` (built & committed).
Hand-editable — move things between sections or delete freely.

## Shipped

## Approved

## Proposed

## Rejected
```
