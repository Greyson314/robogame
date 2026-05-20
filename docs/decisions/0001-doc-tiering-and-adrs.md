# 0001 — Tier the docs, adopt ADRs for new decisions

- **Status.** Accepted
- **Date.** 2026-05-19

## Context

The `docs/` folder grew to ~20 markdown files, most named `*_PLAN.md`,
each written at a different point in the project's life. Some plans
were forward-looking proposals that got partially implemented; some
were fully shipped but never archived; some were "discipline docs"
that were never operationalised. Agents picking up the project read
them all as authoritative directives, with no way to tell which were
load-bearing and which were stale.

The failure mode this creates: an agent reads a partially-superseded
plan, treats it as gospel, and either (a) re-implements work that
already shipped, or (b) follows rules that have been quietly walked
back. The "session N's highest-numbered file is the current state"
convention helped for recent work but didn't catch the older
`*_PLAN.md` drift.

## Decision

Three changes, taken together.

**Tier the docs by directory.** Tier 1 (always-current invariants and
conventions) lives at `docs/` root. Tier 2 (living subsystem docs)
lives in `docs/subsystems/`. Tier 3 (research, reference, historical
design rationale) lives in `docs/research/`, with completed-or-stale
plans under `docs/research/historical/`. Agents are told explicitly:
follow tier 1 + accepted ADRs; tier 2 and 3 are reference, not
directives, and may lag the code.

**Adopt ADRs for new decisions.** `docs/decisions/` holds short,
numbered, immutable decision records. New architectural choices get
an ADR before merging. Changes to tier-1 invariants require an ADR.
See `docs/decisions/README.md` and the template in `0000-template.md`.

**Consolidate the hard rules into one file.** `docs/invariants.md`
is the canonical tier-1 list. Subsystem docs reference it instead of
restating; if they conflict, `invariants.md` wins.

## Alternatives considered

**Status frontmatter on existing docs.** Add `status: accepted |
superseded | draft` to each doc and leave them at `docs/` root.
Lower-friction, no restructure. Rejected because the
`last_validated` / status fields rot faster than the docs themselves
do in practice — a directory-based tier survives drift better.

**Aggressive deletion only.** Delete every superseded plan, no tier
system. Simpler. Rejected because some plan docs (e.g. `physics.md`,
`netcode.md`, `terraforming.md`) have genuine forward-looking content
mixed with current-state, and the right move for them is "promote to
tier-2 subsystem doc," not "delete." Deletion is still part of this
change for the ones that were truly fully superseded.

**Full Nygard ADRs with formal templates and review process.**
Considered, judged too heavy for a solo dev. The lightweight
template in `0000-template.md` is the compromise — enough structure
to make decisions reviewable, not so much that nobody writes one.

## Consequences

**Positive.**

- Agents reading `docs/research/historical/` see the directory name
  and don't take the contents as directive.
- New decisions get a permanent record with explicit status, not
  buried in a session log.
- Tier-1 rules live in one short file; conflicts between subsystem
  docs and the invariants are resolvable by lookup.

**Negative.**

- Cross-references in code, scripts, and existing session logs that
  pointed at old paths (`docs/PHYSICS_PLAN.md`) need to be updated.
  Session logs are historical record and have been left alone; agent
  prompts, CLAUDE.md, and README links have been updated. New links
  must use the new paths.
- The `*_PLAN.md` naming has been retired. New plans live in
  `subsystems/` as living docs, or as ADRs if they encode a decision.

**Invariants this ADR creates.**

- New documents follow the tier convention. Plan docs at `docs/` root
  are not allowed; they go under `subsystems/` or as an ADR.
- Changes to a rule in `invariants.md` require an accepted ADR.

## Notes

- This restructure deleted `NETCODE_PHASE1_HANDOFF.md` (purpose
  served — session 87 confirmed Phase 1 loopback) and
  `SCRAP_LOOP_PLAN.md` (fully shipped in session 58). Git history
  preserves the content.
- `BUILDING_ARCHITECTURE_REVIEW.md`, `GAME_FEEL_PLAN.md`, and
  `FOIL_ROTATION_PLAN.md` moved to `docs/research/historical/`. The
  carry-forward items from foil-rotation are tracked in
  `docs/changes/README.md` under "Known unknowns going forward."
