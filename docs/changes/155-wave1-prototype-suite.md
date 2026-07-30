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
  builds horizontal speed. ConfigValue = per-pogo bounce speed. Verified
  live: sustained ~1.2 m bounce cycle (vy −4.0 → +2.25). Pairs with Gyro
  for wobble damping. Placeholder audio reuses `SpringLaunch`. Quirk: N
  pogos touching down together stack N impulses.

## Next

Wave-2 candidates from the triage: arc emitter, marker dart, balloon /
parachute, caster ball, overdrive, decoy beacon. Heavies still asterisked:
walker legs, grapple zipline, oil/ice sprayers, terrain/economy trio.
