# Idea Backlog

Dedupe memory for the `/ideate` workflow. Statuses: `proposed` (surfaced, not yet decided),
`approved` (building / queued), `rejected` (never re-suggest), `shipped` (built & committed).
Hand-editable — move things between sections or delete freely.

Entry format (terse, ≈3-5 lines):

```
### {Idea name} — {status} ({YYYY-MM-DD})
Payoff: {one-line player-payoff rationale}
Reference: {competitor game + what they did}
Notes: {optional — why rejected, or what shipped}
```

## Shipped

> Seeded 2026-05-28 from session logs 38–100. These are player-facing mechanics already in
> the game — `/ideate` should build *on* them, not re-pitch them. Infra work (netcode, perf
> passes, test debt) is intentionally omitted; it isn't the kind of thing this workflow proposes.

### Hover-blade propulsion + altitude control — shipped (2026-05-28)
Payoff: raycast-based hover movement; Space climbs / Shift descends, altitude latches. First non-joint propulsion block (HoverTank preset). Refs: TerraTech/Trailmakers hover.

### Helicopter chassis (rotor + foils) — shipped (2026-05-28)
Payoff: spinning rotor + 4 foils with frame staying steady; per-block blueprint config + live variant-panel propagation (sessions 38–96).

### Grapple Magnet weapon — shipped (2026-05-28)
Payoff: single-shot fire-and-retract rope+magnet projectile (~24 m) that latches to enemies via SpringJoint tether. Grappler plane preset. Ref: tip-block family.

### Dig-only terraforming + drill block — shipped (2026-05-28)
Payoff: smooth-voxel destructible terrain you can tunnel through; drill block, bedrock floor, shallow craters. Dig-only by invariant. Ref: From the Depths / voxel diggers.

### VoxelChaserBot enemy AI — shipped (2026-05-28)
Payoff: A*-on-occupancy-grid chaser bot that follows the player across dug terrain. First real PvE opponent. Visual-playtest gate for terraforming.

### Scoreboard + kill feed + nameplates — shipped (2026-05-28)
Payoff: Tab-held scoreboard, persistent kill feed, world-space chassis nameplates. SP layer (NGO replication deferred). Ref: standard arena-shooter QoL.

### Feel/juice pack — shipped (2026-05-28)
Payoff: damage-number clustering, low-HP vignette + audio, scrap-pickup magnet trail, crosshair ammo state, live foil-pitch propagation. Combat/feedback polish.

### Multiple arena types (flat / spherical planet / water) — shipped (2026-05-28)
Payoff: three distinct battlegrounds incl. planet arena with radial gravity. Ref: spherical-arenas subsystem.

## Approved

## Proposed

## Rejected
