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

### Block-graph damage propagation — approved (2026-05-28)
Payoff: chip damage along block graph + detach subgraph as debris + functional disable; retroactively makes every weapon-placement decision a defensive puzzle. The "mechanical core" per robocraft-reference.
Reference: Robocraft block graph (struct snaps, turret clusters fall off); Crossout functional disable without propagation.
Notes: **PERF-GATED by user** — build only if it stays within budget (invariant #6 no per-frame alloc, server-authoritative). If not performant, skip and surface. ~3-session arc.

### Active Module Slot — approved (2026-05-28)
Payoff: one garage-chosen keybind ability (EMP / Blink / shield), fixed at match start, server cooldown, destructible block disables it; turns "drive and shoot" into "wait for your moment."
Reference: Robocraft modules (Blink/EMP/Disc Shield); Crossout active abilities. ~2 sessions.

### CPU Budget + Garage HUD — approved (2026-05-28)
Payoff: per-block CPU cost + live garage spend-vs-cap bar + strip-at-spawn over budget; garage becomes a resource-allocation puzzle and the balance lever for future blocks.
Reference: Robocraft 2000-CPU cap; Crossout tonnage/energy. Resolves OPEN pillar question (cap shape). ~1 session.

## Proposed

### Dynamic Hazard Objects — proposed (2026-05-28)
Payoff: 2-4 non-AI arena physics hazards (swinging wrecking ball, crater-carving rolling boulder), reset on respawn; arena becomes a "place," third party every fight navigates.
Reference: Besiege boulders/pendulums; TerraTech Worlds roaming hazards. ~1-2 sessions; needs per-contact dig cooldown for tri budget.

## Rejected
