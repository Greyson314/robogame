# ADR-0004 — Player concoctions: first player-content persistence layer

- Status: **Accepted** (2026-06-04)
- Supersedes: none
- Related: [INV-1](../invariants.md) (no Tweakable affects gameplay), [INV-2](../invariants.md) (blueprints frozen at match start), [INV-3](../invariants.md) (server authority), [INV-5](../invariants.md) (zero baseline cost), [ADR-0003](0003-weapon-fork-refactor.md) (shared `WeaponStatsDefinition`).

## Context

The "Laboratory" feature lets a player author **concoctions** — for now, custom
explosive payloads defined by three 0–1 sliders (damage / size / knockback). A
concoction is chosen per explosive-weapon block (Bomb, Mortar) and changes that
weapon's real stats, balanced by a CPU surcharge.

This is the project's **first player-authored content that persists outside a
blueprint**. Two questions need a recorded decision: where concoctions live, and
why a player-tunable thing that affects damage does not violate invariant #1.

## Decision

**1. Persistence model.** Concoctions are plain `[Serializable]` data objects
(`{ id, displayName, damagePct, sizePct, knockbackPct }`) saved as JSON under
`Application.persistentDataPath/concoctions/`, one `*.concoction.json` per
concoction. The on-disk format is owned by a pure `ConcoctionSerializer`
(schema-versioned) with a disk façade `ConcoctionLibrary` — structurally
identical to `BlueprintSerializer` / `UserBlueprintLibrary`. No `AssetDatabase`,
so it works in player builds. This mirrors the existing blueprint persistence
rather than inventing a new mechanism.

**2. The blueprint carries a reference, not the payload.** A chosen concoction
is stored on the block entry as a string id (`Entry.ConcoctionId`), serialized
in blueprint **schema v7**. The blueprint stays the single thing that crosses
the scene/match boundary; the concoction *library* is separate player data the
blueprint points into.

**3. Server authority (INV-3).** At match start a runtime `ConcoctionRegistry`
is populated from the player's library on the server (offline `IsServer==true`,
so SP is byte-identical). Resolved concoction stats are **clamped to range at
use** (`Concoction.Validate()` in `ConcoctionRegistry.TryGet`), so a client can
never inject out-of-range values. In MP the `ConcoctionId` rides in the frozen
blueprint blob; the schema bump to v7 means a v6 client is correctly rejected by
the existing `ContentHashGuard` (intended behavior).

**4. CPU surcharge is the balance lever (INV-5).** An explosive block with no
concoction (`ConcoctionId == ""`) costs its baseline CPU and behaves exactly as
today — zero baseline cost for the feature when unused. A concoction's surcharge
is monotonic in the slider values (raise → costlier, lower → cheaper) and flows
into both the garage spend-vs-cap bar and the server strip-at-spawn `TrimToFit`,
so maxed payloads are bounded by the existing budget rather than by rarity/grind.

## Why this is NOT a banned gameplay Tweakable (INV-1)

Invariant #1 forbids the dev `Tweakables` system from affecting gameplay
outcomes. A concoction is not a `Tweakable`: it is **blueprint-baked,
server-loaded, server-clamped, CPU-budget-governed build customization** — the
exact governance model already used by foil pitch/incidence and
`Entry.BlockConfig` (thruster thrust, rudder authority, rotor RPM), all of which
legitimately affect gameplay. The differentiators that keep it legitimate:
frozen at match start (INV-2), authoritative on the server (INV-3), and paid for
in CPU (INV-5). If any of those three were dropped, this decision would not hold.

## Consequences

- New runtime types in `Robogame.Block`: `Concoction`, `ConcoctionSerializer`,
  `ConcoctionLibrary`, `ConcoctionRegistry`. `Entry` gains a `ConcoctionId`
  string; `BlueprintSerializer` goes to v7 (v1–v6 load with `""` → no change).
- Sets the precedent for future player-content libraries (rider effects, named
  concoctions, future craftables): same JSON-in-`persistentDataPath` +
  registry-loaded-server-side + clamp-at-use shape.
- The static `ConcoctionRegistry` must reset via
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` (statics survive
  domain reload — known failure mode).
- Concoctions are intentionally **not** part of a blueprint file, sharing, or
  import/export in this iteration; the blueprint only carries the id.
