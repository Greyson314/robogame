# 155 — Wave-1 prototype suite (EA triage)

First implementation wave off the EA block triage
([docs/research/ea-block-triage.md](../research/ea-block-triage.md), mirror of
Wayfinder issue #15). Eight low-risk prototypes; zero new Rigidbodies,
Colliders, or Joints anywhere.

## Shipped

- **Splash plumbing** — `BlockGrid.ApplySplashDamage` gained an optional
  `ring0Multiplier` (scales only the direct-hit cell) and a Fuse
  propagation stop. All six existing call sites untouched (default 1f).
- **Fuse** (`block.structure.fuse`) — live fuse takes its ring's damage but
  the splash BFS never crosses it. Logic lives entirely in `BlockGrid`.
- **Counterweight / Feather** — data-only structure blocks (mass 8 / 0.15).
  Feather's "huge" visual is deferred to scalable-parts Phase 4; for now
  it's a normal cell that weighs nothing and pops easily.
- **Gyro** (`GyroBlock`, `IDriveSubsystem` Order 200) — yaw torque from
  steer input (works at standstill) + roll/pitch **rate** damping. No
  gravity reference, so spherical arenas behave identically. Authority via
  `ConfigValue`, default 25 N·m.
- **Pogo** (`PogoBlock`) — passive raycast spring-damper along the mount
  axis, under-damped for bounce, force-capped. Deliberately NOT the Spring
  module (that's a one-shot ability launch); this is the repeated
  automatic bouncer, kin to HoverBlade's non-joint pattern.
- **Spike armor** — ramming a live enemy spike boosts the victim's ring-0
  damage (`ArmorConfig.SpikeDamageMultiplier`, default 2×). Attacker-bonus
  reading only; wielder-side defensive discount is a possible follow-up.
- **Wedge armor** — glancing hits deal reduced damage
  (`ArmorDeflection.ComputeMultiplier`: full damage below 30° incidence,
  lerp to 0.25× at graze). Wired at `ProjectileWorld.ApplyDirect`,
  `ApplyRingSplashOnHit`, and the ram self-damage path.
- **SMG spin-up/overheat** — `WeaponHeatModel` (pure, deterministic,
  tick-driven): 5→12 shots/s over 1.2 s held, 4 s unbroken sustain trips a
  2.5 s lockout, feathering cools at the accumulation rate.
  `ProjectileGun` ticks it, gates fire, tints the weapon toward hot red
  via MPB, and plays `AudioCue.WeaponOverheat` on trip.

New config surface: `ArmorConfig` SO (`Resources/ArmorConfig.asset`,
ImpactConfig pattern — server-canonical, fallback defaults, domain-reload
cache reset). `WeaponDefinition` gained the five spin-up fields;
`_spinUpSeconds = 0` disables the feature per-weapon.

## Design calls made this session (user gave asterisk latitude)

1. Pogo = new passive block, not an auto-trigger mode on Spring.
2. Spike = attacker-bonus only.
3. Wedge = option A: standard box collider, deflection from the reported
   hit normal. A shot into the sloped *visual* face still reads as a
   perpendicular cube-face hit — known mismatch, accepted for the
   prototype. Option B (tilted child BoxCollider) is the follow-up.

## Verification

- Live editor: all 16 assemblies compiled, 0 errors, 0 exceptions.
- Headless rig: EditMode 462/463 (1 pre-existing inconclusive: retired
  Buggy preset validation), PlayMode 122/123 (1 pre-existing skip), 0
  failures. All 9 new tests passed (4 splash/fuse, 5 heat model).
- Wizard run in live editor: 7 new BlockDef assets created, library
  repopulated, `Weapon_Smg.asset` diff is additive-only.
- qa-verifier / perf-checker subagents were NOT dispatched: the identical
  checks ran inline (compile + full suite + console), and the perf
  footprint has direct precedent (gyro = rudder-class torque, pogo =
  wheel-class raycast). PerformanceHud glance recommended at next live
  session.

## Known gaps / debt

- **Invariant #8 partial**: gyro/pogo have procedural visual rigs but no
  audio; armor blocks have neither bespoke VFX nor audio;
  `AudioCue.WeaponOverheat` has no clip wired in `AudioCueLibrary.asset`
  yet (PlayOneShot no-ops until authored). FX/audio pass owed before any
  of these leave "prototype".
- SMG heat state runs on local held-duration — same clock-authority debt
  as fire-rate gating (performance.md §8.10), slightly enlarged.
- Fuse builds on the graph-BFS splash falloff that game-design-pillars
  still marks as an open question; switching splash models later now also
  touches Fuse.
- Wedge visual/mechanic mismatch (option A above).
- Pogo needs a live bounce-feel pass (§15.7 runaway watch) — headless
  tests can't judge feel.

## Post-playtest revisions (same session)

User's first testing pass produced two fixes:

- **SMG spin-up reverted, overheat kept.** Rate-of-fire buildup felt bad;
  fire rate is constant 12/s again, the 4 s sustain → 2.5 s lockout
  stays. `WeaponDefinition` dropped the three spin fields (gate is now
  `HasOverheat`, `_overheatSeconds` 0 = off). `WeaponHeatModel` keeps its
  spin-up support untouched (tests still pass) — `ProjectileGun` passes
  min == max so the ramp is inert; a future minigun block can use it.
- **SMG plain-white bug fixed.** The heat glow's MPB wrote
  `lerp(white, hot, heat)` — at heat 0 that overrode the authored MK Toon
  colour with white. Glow now caches each renderer's authored material
  colour and lerps from that.
- **Pogo v3: perpetual bouncer with air-tilt steering.** v1's stiff
  passive spring launched once then settled (passive spring-dampers always
  dissipate); v2 gated hops on the jump input, which ground-chassis input
  never delivered — "nothing works" in playtest. v3 identity per user:
  "upside without control". Bounces at the instant of foot contact,
  always (0.35 s cooldown); the bounce vector is the STICK's axis, so
  leaning aims the next bounce; WASD applies air-only TILT torque
  (pitch/roll, `Acceleration`, light rate damping) — attitude control,
  never lateral force. Take-off speed is SET along the stick axis (raw
  additive `VelocityChange` mostly cancelled the incoming fall — the bot
  hovered; caught by live play-mode probe over the MCP bridge).
  Perpendicular velocity carries across bounces, so sustained leaning
  builds horizontal speed. ConfigValue = per-pogo bounce speed. Pairs
  with Gyro for wobble damping. Placeholder audio reuses `SpringLaunch`.
  Quirk: N pogos touching down together stack N impulses.
  Playtest pass 2 ("kinda fun"): foot reach cut 1.15 → 0.95 with the ray
  being the foot itself (the old separate 0.9 m trigger fired 0.25 m
  after the foot visual had buried — read as bouncing off the host cube),
  foot sphere drawn one radius short of the contact face, and bounce
  speed 5 → 14 m/s (≈8× height, ~10 m apex). Verified live: sustained
  y 2.7 ↔ 11.1 cycle, vy ±13.
  Playtest pass 3 ("really fun"): momentum banking — impact speed above
  the base takeoff carries into the next bounce at 0.7×, so cliff drops
  launch higher and decay geometrically back to base (flat ground is
  stable by construction; bonus < 1 = no runaway). Live-verified: 25 m
  drop → apexes 14.1 → 13.2 → 12.9 → 11.7 → ~10.5 converging on base.
  Plus a variant-panel "Power ×" slider (PogoDefaults, bounce-HEIGHT
  multiplier via ConfigValue; PogoBlock takes √power on speed) — new
  PogoDefaults.cs, BlockVariants entry, VariantConfigPanel pogo section
  cloned from the weapon-ammo scalar pattern. Pass 4: max power raised
  1.8× → 4× (≈40 m solo hop at full crank) and the leg stretched to a
  3-cell-tall assembly (foot reach 0.95 → 2.5 m — bot rides high on its
  stilt, bounces the moment the distant foot touches; sized-to-mechanic
  convention, mount stays cell-sized). Leg has no occupancy guard —
  blocks placed in the two cells beneath clip through it visually
  (scalable-parts gap, same as Feather).

## Multi-pogo rocket closed: per-chassis bounce arbitration

Playtest: 10 pogos on one bot = functional rocket (500+ m). Each pogo
read the PRE-bounce velocity and queued its full velocity-set, so the
corrections stacked N×. Fix: `PogoBounceArbiter` on the chassis root
(added lazily, no statics) — one bounce claim wins per bounce window;
denied pogos keep their own cooldowns, so extra pogos buy landing
coverage and redundancy, not Δv. Second finding from the same probe:
the winning bounce must apply at the COM — a corner-foot VelocityChange
off-COM became violent spin (the quad test rig tumbled through the
floor). First-claim-wins is contact-order
arbitrary on mixed-power chassis — upgrade to max-request if that
becomes a real build pattern.

Refined same session into **diminishing returns** (user: >1×, <10×):
new `StackingCurves.PowerLaw(count, exponent)` in Robogame.Block is the
shared DR primitive for any "N of the same block" system; each system
owns its exponent as a schema-side constant. Pogo:
`PogoDefaults.StackHeightExponent = 0.5` — N loaded feet → N^0.5 bounce
HEIGHT (4 ≈ 2×, 10 ≈ 3.2×). The arbiter keeps a registered-feet list
(no bounce-time allocation) and `CountLoadedFeet()` re-probes each
foot's ray so the count is same-step accurate at the landing instant.
Live A/B: single rig unchanged (~10 m over contact), quad rig ~20 m —
exactly 4^0.5. Future DR candidates: gyro torque stacks linearly today;
flag it if multi-gyro turrets get silly.

## Pre-existing bug flushed out: tune edits never reached the blueprint

`BuildSession.SyncBlueprint()` ran only on place/remove. Tune-mode edits
(`BlockEditor.PropagateVariantToLiveBlocks`) wrote the live garage block
but not the Entry — and launch/save read the Entry, so a pure
tune-then-launch or tune-then-save session silently reverted EVERY
instance edit (module power, rotor RPM, foil pitch/dims, pogo power).
It looked shipped because placing/removing any block afterwards
re-captures the whole grid, rescuing earlier tune edits by accident.
Fix: propagation now calls `SyncBlueprint()` after applying the edit.
Surfaced by the pogo power slider (playtest pass 5).

## Next

Wave-2 candidates from the triage: arc emitter, marker dart, balloon /
parachute, caster ball, overdrive, decoy beacon. Heavies still asterisked:
walker legs, grapple zipline, oil/ice sprayers, terrain/economy trio.
