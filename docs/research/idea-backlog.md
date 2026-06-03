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

### CoM/CoL/CoT Garage Overlays — shipped (2026-06-02)
Payoff: three sized colored spheres (mass/lift/thrust) in build mode make a lopsided rig obvious at a glance; the spatial mismatch IS the info, now that wing physics is honest.
Reference: Robocraft garage CoM orb; Trailmakers CoM/CoL/CoT overlays.
Notes: Shipped /ideate movement round. `CenterOverlay` (toggle G), read-only gizmo spheres via RuntimeMaterials; wired by GarageController. See docs/changes/107-movement-consistency.md.

### Hover Blade Mass/Inertia Scaling — shipped (2026-06-02)
Payoff: a hover blade's mass now scales with its N×N footprint (size-4 = 4× a size-2), so hover-vs-wheel is a real mass/CPU tradeoff and one big pad rocks more than four small ones.
Reference: session-106 aero EffectiveMass + box-inertia pattern, extended to hover.
Notes: Shipped /ideate movement round. Extended Robot.EffectiveMass + BlockInertiaBounds; anchored at default size 2 (HoverTank unchanged). See docs/changes/107.

### Thrust-Offset Torque — shipped/already-implemented (2026-06-02)
Payoff: asymmetric thruster placement pitches/yaws the bot under power.
Reference: From the Depths / Besiege emergent thrust torque.
Notes: Approved /ideate movement round then found ALREADY IMPLEMENTED — ThrusterBlock (+ aero, hover, rudder, wheel) already AddForceAtPosition at the block's world position, so off-CoM thrust already induces torque. Net-new this round: the CoT overlay (idea 1) now visualises that offset. The only remaining lever is making it more *felt* (less auto-damping) — a risky feel change, surfaced to the user, not done autonomously.

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

### Block-graph damage propagation — shipped (2026-05-28)
Payoff: chip damage along block graph + detach subgraph as debris + functional disable; the "mechanical core" per robocraft-reference.
Reference: Robocraft block graph; Crossout functional disable.
Notes: Approved by /ideate run then found ALREADY IMPLEMENTED — splash via SMG SplashRings + BlockGrid.ApplySplashDamage (graph-BFS); detach via Robot.RunConnectivityNextFrame + DetachAsDebris; functional disable via isActiveAndEnabled + RotorBlock.OnDestroy. The only net work done this run: made ApplySplashDamage + FindDisconnectedFrom allocation-free (invariant #6, was allocating per SMG hit).

### Spring Block (+ modular SpringSolver) — shipped (2026-05-29)
Payoff: an underside-mount jump block firing on Space (cooldown-gated impulse along -transform.up), plus a reusable SpringSolver that HoverBlade now shares. User-authored during the session-104 /ideate run.
Reference: Trailmakers/Besiege spring/piston jump blocks.
Notes: Shipped session 104. SpringSolver (HookeDamped + ResolveImpulse), SpringBlock + RobotSpringBinder, SpringTuningConfig + dev overrides, VfxKind.SpringBurst + AudioCue.SpringLaunch, BlockDef + SpringBot preset. HoverBlade migrated to SpringSolver. EditMode 263/264, PlayMode 104/105 green. See docs/changes/104-spring-block.md.

### Active Module Slot — shipped (2026-05-29)
Payoff: one garage-chosen keybind ability (EMP / Blink / Disc Shield), fixed at match start, server-authoritative cooldown, destructible carrier block disables it; turns "drive and shoot" into "wait for your moment."
Reference: Robocraft modules (Blink/EMP/Disc Shield); Crossout active abilities.
Notes: Shipped session 101. Module-category block + ActiveModuleSystem (server-gated cooldown), per-kind ModuleDefinition tuning, ModulePressed on Q, VFX+audio, arena cooldown HUD + garage select panel. Default Tank carries one. See docs/changes/101-active-module-slot.md.

### CPU Budget + Garage HUD — shipped (2026-05-29)
Payoff: per-block CPU cost + live garage spend-vs-cap bar + strip-at-spawn over budget; garage becomes a resource-allocation puzzle and the balance lever for future blocks.
Reference: Robocraft 2000-CPU cap; Crossout tonnage/energy. Cap shape resolved: 250 budget per CPU block.
Notes: Shipped session 102. Found ~70% already built (CpuCost, cap shape, hot readout). Net-new: Block/CpuBudget connectivity-preserving TrimToFit, server-gated strip at ArenaController spawn, garage fill bar. See docs/changes/102-cpu-budget-enforcement.md.

## Approved

## Proposed

### Per-Movement-Type CPU Sub-Budget Display — proposed (2026-06-02)
Payoff: split the CPU bar into colored bands (movement/weapon/structure/module) as a soft readout (no new caps) so build character is glanceable now that modules take a CPU slice.
Reference: Crossout component categories; groundwork if the open CPU-sub-budget question resolves to "yes". ~½ session. Surfaced /ideate movement round; user kept as proposed.

### Phantom Shell (deception module) — proposed (2026-05-29)
Payoff: drop an identical ghost-snapshot decoy + Blink away; phantom is shootable/shatters, bot targeting chases it. Novel in-genre misdirection, SP-demoable via Transform-target redirect.
Reference: Robocraft Blink+Ghost (but interactive/destructible). ~2 sessions. Flag: new non-chassis Rigidbody (legal). Surfaced /ideate round 3, user pivoted to Spring.

### Lodestone Block (gravity-well pulse) — proposed (2026-05-29)
Payoff: toggled short-range gravity-well — eats incoming bullets / yanks bots; rope-mounted = gravity-lasso; soft counter to Grapple Magnet. Makes gravity a build-composable primitive.
Reference: TerraTech attract magnet (never on a rope). ~1 session. Surfaced /ideate round 3, user pivoted to Spring.

### Shard Launcher (fake-signature deception) — proposed (2026-05-29)
Payoff: cluster of fake-chassis-signature shards make bots "squirrel!"-retarget; PvP visual read-break. First mechanic that plays against bot targeting.
Reference: From the Depths flares. ~2-3 sessions. Flag: ProjectileWorld-native + server-auth target hijack. Surfaced /ideate round 3, user pivoted to Spring.

### Crumple Plate (kinetic reactive armor) — proposed (2026-05-29)
Payoff: squash-stretch on hit, then pops off as a high-velocity chunk dealing Mace-style momentum damage to point-blank attackers. Punishes shotgun-rushers.
Reference: reactive-armor lineage, physics-kinetic twist (distinct from round-2 Reactive Armor's splash). ~1 session. Reuses Mace contact-damage formula. Surfaced /ideate round 3, user pivoted to Spring.

### Defensive deception family (smoke / obfuscation / stealth / clone) — proposed (2026-05-29)
Payoff: user-requested theme to explore. A class of DEFENSIVE counterplay built on hiding/faking your bot: smoke-screen emitter (breaks lock + LOS), nameplate/silhouette obfuscation, short active-camo stealth, decoy clone that draws bot/turret aim. Counters the all-offense meta; rewards evasive builds.
Reference: Crossout smoke screen + radar/stealth modules; Robocraft Ghost module (cloak). Likely Active-Module-Slot variants and/or new blocks. Multiple sub-ideas — pick the cleanest 1-2 when surfaced.
Notes: Flagged by user 2026-05-29 during /ideate run. Surface a concrete slate from this theme in a future round.

### Dynamic Hazard Objects — proposed (2026-05-28)
Payoff: 2-4 non-AI arena physics hazards (swinging wrecking ball, crater-carving rolling boulder), reset on respawn; arena becomes a "place," third party every fight navigates.
Reference: Besiege boulders/pendulums; TerraTech Worlds roaming hazards. ~1-2 sessions; needs per-contact dig cooldown for tri budget.

### Capture Nodes w/ block-HP cost — proposed (2026-05-29)
Payoff: contested zones + charge-to-win meter, zone pulses splash damage at occupants; armored-camper vs glass-cannon build choice. Adds a real win condition.
Reference: Robocraft Battle Arena; Crossout Encounter. ~2 sessions. Surfaced /ideate, user re-rolled (too safe).

### Build-within-budget challenge rooms — proposed (2026-05-29)
Payoff: rooms with sub-garage CPU caps (400/600/800) + fixed scenarios; "best bot for 400 CPU" puzzle. Reuses strip-at-spawn + VoxelChaserBot waves.
Reference: TerraTech missions; Trailmakers challenge rooms. ~2-3 sessions. Surfaced /ideate, user re-rolled (too safe).

### Payload escort (moving capture point) — proposed (2026-05-29)
Payoff: slow cargo sled w/ block-HP on a nav-rail, bots target it; Grapple/Hook can tow it faster. Validates tether builds, no new blocks.
Reference: Crossout 2018 Raid escort overhaul. ~2 sessions. Surfaced /ideate, user re-rolled (too safe).

### Per-match modifier draw — proposed (2026-05-29)
Payoff: random rule card per match (No Thrusters / Double Gravity / Frenzy / Dead Weight); server-side param scaling at spawn, blueprint frozen. Cheap variety.
Reference: Robocraft Brawl; TFT augments. ~1 session. Surfaced /ideate, user re-rolled (too safe).

### Morph Hinge (in-build fold point) — proposed (2026-05-29)
Payoff: two garage-authored poses, one button kinematically folds a sub-tree mid-match (fold wings to fit tunnel, deploy blade-arm). Blueprint frozen; transforming bots.
Reference: From the Depths Spin/Turn pop-up systems. ~2-3 sessions. Surfaced /ideate (bold round), user re-rolled.

### Pinch Block (kinematic clamp jaw) — proposed (2026-05-29)
Payoff: clamp arm latches a temporary joint to an enemy chassis ~2s, shared drag, break by damaging block. Grab-and-hold counterpart to Grapple Magnet.
Reference: Trailmakers detachables (inverted). ~2 sessions. Flag: temp joint between two chassis Rbs (legal), needs max-duration + escape. Surfaced /ideate (bold round), user re-rolled.

### Reactive Armor Plate — proposed (2026-05-29)
Payoff: inert until a damage impulse over threshold, then fires a directional counter-blast (existing impulse path reversed) — shoves projectiles/knocks hook loose/punches back mace. Per-plate.
Reference: Crossout explosive barrels as booby-traps. ~1 session. Surfaced /ideate (bold round), user re-rolled.

### Splinter Block (deploy a sub-bot) — proposed (2026-05-29)
Payoff: severs a sub-tree into an independent controllable sub-bot ~15s (else drops as bomb); both halves authored as one frozen blueprint.
Reference: Trailmakers Detachable Block. ~3-4 sessions. Flag: runtime Rigidbody creation from subtree + heaviest netcode; SP-only AI-sub-bot first pass recommended. Surfaced /ideate (bold round), user re-rolled.

## Rejected
