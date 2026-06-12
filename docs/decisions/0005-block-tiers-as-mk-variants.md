# 0005 — Block tiers as authored Mk-variant definitions

- **Status.** Proposed
- **Date.** 2026-06-12

## Context

The ideas file asks for weapon tiers ("Catapult → X → mortar?") and the
launch-readiness push needs a growth axis for builds beyond "more of the
same block." Robocraft's answer was T1–T5 tiers bound to player
progression and matchmaking brackets (see
`docs/research/robocraft-reference.md`); we have neither progression nor
matchmaking, and the singleplayer loop's only economy is per-match scrap.

What we do have is a CPU budget (1000 per CPU block,
`Block/CpuBudget.cs`) that already prices per-instance upgrades: rotor
RPM scales cost quadratically, ammo multipliers scale it linearly,
concoctions add surcharges. The budget is the established balancing
axis; any tier design that bypasses it would need a second progression
system built from scratch and would invalidate the existing balance
math.

## Decision

A tier is an **authored sibling BlockDefinition** in the same family —
`Cannon Mk2`, `SMG Mk2` — with higher damage/HP/mass and a CPU cost
scaled so **power-per-CPU stays roughly flat across tiers**. Tiers buy
*concentration* (fewer blocks to armour, bigger single points of
failure), not efficiency. No unlock gate at launch: every Mk is
buildable in the garage from day one, constrained only by the budget.

Mechanically this is zero new systems: a new `BlockDef_*` asset + a
`BlockIds` const per Mk, hotbar grouping by family, and the existing
budget/trim/validator paths price it with no code changes. The
`BlockDefinitionWizard` scaffolds each variant.

Suggested first wave (numbers are starting points for playtest, all
≈flat DPS/CPU against the session-127 cannon rebalance):

| block      | CPU | damage | clip | notes |
|------------|-----|--------|------|-------|
| SMG Mk2    | 45  | 55     | 24   | slower fire (8/s), heavier hit |
| Cannon Mk2 | 70  | 220    | 4    | slug one-shots 2-cube clusters |

## Alternatives considered

- **Per-instance tier slider (ConfigValue), like ammo/RPM.** Rejected
  for weapons: continuous damage scaling makes per-shot feel mushy and
  unreadable ("how strong is that gun?" has no answer), the kill feed /
  nameplates can't say "MK3", and explosives' ConfigValue slot is
  already spoken for by concoctions (ADR-0004).
- **Robocraft-style progression-gated tiers.** Rejected for launch:
  needs XP, unlock UI, and matchmaking brackets to make sense; in
  singleplayer it's just artificial delay. The ADR doesn't preclude
  layering unlocks on later — the Mk assets are the same either way.
- **Tier = upgrade applied at the depot mid-match (scrap-as-spend
  idea).** Deferred, not rejected: it's a fun in-match economy sink but
  it violates "blueprints frozen at match start" (invariant #2) unless
  scoped as a buff, not a block swap. Needs its own ADR if pursued.

## Consequences

- Balance work scales with the Mk roster — every new Mk is a row in the
  balance table (see session 127's DPS/CPU survey methodology).
- Hotbar UX needs family grouping before the roster grows past ~12
  block types (today it's flat).
- The "flat power-per-CPU" rule becomes the family pricing invariant;
  deviations are deliberate balance levers and should be commented in
  the definition asset.

## Notes

Seeded from the Obsidian ideas file ("weapon tiers?"). Session-127
churn pass drafted this; implementation deliberately NOT started —
awaiting user direction on the first-wave roster.
