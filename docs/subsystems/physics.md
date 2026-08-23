# Robogame — Physics Plan

> **Audience.** Anyone (human or AI) about to add or modify a
> physics-driven block, weapon, or arena feature. Read § 1 before
> writing code; consult § 2–4 by topic.
>
> **Scope.** What we use today, what we want to migrate to, and the
> *trigger conditions* that flip a "future plan" into "do it now."
> Naming the triggers is the whole point of this file: the failure
> mode we're avoiding is "the budget got blown three months ago and
> nobody noticed."
>
> **Companion docs.** [BEST_PRACTICES § 16](../best-practices.md#16-performance-budgets-targets-not-law)
> for the budget table; [netcode.md](netcode.md) for the
> server-authority contract this document inherits.

---

## 1. Non-negotiables (read this first)

These rules apply to every physics-driven block, full stop:

1. **Single-Rigidbody-per-chassis with compound colliders.** Free-body
   children of a moving Rigidbody fight the solver. If a feature needs
   a free body, parent it under scene root, not under the chassis. See
   BEST_PRACTICES § 3.1.
2. **Default to zero baseline cost.** Every new physics block must
   have a configuration that adds zero Rigidbodies and zero colliders.
   Anything heavier is opt-in (per-chassis blueprint config or a debug
   tweakable). The `RotorBlock` "ropes = 0" path is the established
   pattern.
3. **No per-frame allocations.** No `new` in `Update` / `FixedUpdate`
   / `OnCollision*`. Pre-size lists at build time, reuse them.
4. **Profile before merging.** A new physics block is not done until
   you have a Profiler capture under a *populated* chassis (not an
   empty test object) showing PhysX simulate < 2 ms / step and active
   Rigidbodies under the § 16 alarm.
5. **Gameplay-observable behaviour MUST NOT depend on a Tweakable.**
   Tweakables are per-machine, persisted to local JSON. The moment
   they affect damage, hit detection, or anything visible to other
   players, they desync the second netcode lands. Move that data to
   the chassis blueprint (server-authoritative) instead. See § 5.

---

## 2. Rope tech: custom Verlet particle solver (shipped)

> The rope chain migrated off PhysX joint chains to a custom Verlet / PBD
> solver. This section describes what ships **today**; the joint-chain
> story is kept below only as the rationale for the move.

### Today (Verlet / PBD)

- **What ships:** [`RopeBlock`](../Assets/_Project/Scripts/Movement/RopeBlock.cs)
  dangles a Verlet particle chain simulated by the
  [`VerletRopeSimulator`](../Assets/_Project/Scripts/Movement/VerletRopeSimulator.cs)
  scene-root singleton — one batched tick over every active
  `VerletRopeChain` per `FixedUpdate`. The **middle of the chain has no
  Rigidbodies**; it is a positions array the solver integrates and
  constraint-relaxes.
- **Only two ends are real Rigidbodies.**
  - *Hub-end* = the chassis itself; particle 0 is anchored to the host
    cell's top face in chassis-local space.
  - *Tip-end* = a fresh scene-root Rigidbody owned by the rope, hosting
    the adopted Hook / Mace + the `TipCollisionForwarder`. The solver
    drives it via `MovePosition` so PhysX still synthesises a velocity
    and world collision stays sane.
- **One joint, not a chain.** A single `ConfigurableJoint` couples
  chassis ↔ tip as a hard distance limit (= total rope length). The
  particle sim enforces chain *shape* but doesn't transmit force back to
  the chassis, so without this joint a grappled hook would let the
  chassis fly off forever. That is the only joint a rope uses.
- **Tip-only collision.** Only the tip Rigidbody has a collider; the
  chain middle is collisionless. Per-segment world collision is still
  future work (see below).
- **`RotorBlock` is not a rope.**
  [`RotorBlock`](../Assets/_Project/Scripts/Movement/RotorBlock.cs) spins
  via pure kinematic transform writes — a kinematic hub plus reparented
  foil transforms driven by `MoveRotation`, **no joints and no dynamic
  Rigidbodies**. It can *adopt* a rope (reparent so the rope swings with
  the hub), but that rope is still the Verlet chain above.
- **Cost / networking shape.** A chain replicates as hub-pose +
  tip-pose + spawn-time data and re-simulates client-side — 2 poses per
  rope, not N. Stress-test cost via the rotor stress tower (settings →
  Stress → "Spawn Rotor Tower" + RPM slider) under the Unity Profiler.

### Why we left PhysX joint chains (historical rationale)

The original rope was N free-body Rigidbodies linked by N
`ConfigurableJoint`s. It was abandoned because: (1) the joint-solver tax
ballooned under sustained high RPM; (2) chains "exploded" under
per-segment collision (joints fight contact resolution and snap to a
mangled pose); (3) N segment poses per chain were unshippable over the
wire (16 players × 2 rotors × 4 ropes × 4 segs ≈ 2,048 poses/tick). The
Verlet solver is cheaper, deterministic, network-friendly, and turns
per-segment collision into a capsule cast instead of a contact-solver
tax.

### Remaining rope work (not yet shipped)

1. **Per-segment world collision** along the full chain length — today
   only the tip collides. Needed for a flail-style weapon that scrapes
   walls along its length; the Verlet design makes this a capsule cast
   per segment per step rather than the joint-chain "explode on contact"
   pathology. This is the gate before rope-along-length damage code.
2. **Burst-compiling** the simulator's integrate + constraint loop — the
   batched chain tick is the cache-friendly candidate. See
   [burst-notes.md](burst-notes.md).

### 2.1 Raycast spring-damper (hover blade) — the non-joint propulsion pattern

Session 99 introduced [`HoverBladeBlock`](../../Assets/_Project/Scripts/Movement/HoverBladeBlock.cs)
as Robogame's first propulsion block that uses neither joint chains
nor PhysX vehicle wheels: one `Physics.RaycastNonAlloc` per blade
per `FixedUpdate`, force applied via `chassisRb.AddForceAtPosition`
at the blade's attach point. The ray direction comes from
`GravityField.SampleAt()` so the same code works on flat and
spherical arenas without branching.

The spring-damper formula is clamped ≥ 0 (blade can't pull the
chassis downward or propel it above target altitude) with damping
gated to active spring (no drag-when-above-range surprise). Passive
banking, passive auto-leveling, and per-corner failure all emerge
for free from the attach-point force application — no explicit
torque code.

This is the pattern future propulsion blocks should reach for first
when "raycast + force on chassis" suffices. Joints stay reserved
for cases that genuinely need geometric constraints (rope chains,
spinning rotors with adopted bodies), and even those are migrating
toward Verlet per § 2.

### 2.2 Aero control surfaces — authority from geometry (ADR-0009, session 167)

There is no chassis-level plane controller. Pitch, roll and yaw on a
winged bot come from the foils themselves:
[`RobotDrive`](../../Assets/_Project/Scripts/Movement/RobotDrive.cs)
maps the raw axes through the blueprint's `ControlScheme` into a
six-DOF [`DriveIntent`](../../Assets/_Project/Scripts/Movement/DriveIntent.cs)
once per tick; every free (non-rotor)
[`AeroSurfaceBlock`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
reads it and deflects by
[`AeroControl.Deflection`](../../Assets/_Project/Scripts/Movement/AeroControl.cs)
— the demand dotted with the surface's own moment direction
`r × liftAxis` about the *live* CoM, capped at
`FoilDefaults.ControlThrowDeg` (10°). The deflection adds to the foil's
AoA, so the existing lift formula (speed² × area × slope) sets the
magnitude. Nothing decides "this is the elevator": a surface behind the
CoM sheds lift to pitch up, one ahead adds lift, left/right wings
oppose for roll, a fin behind the CoM yaws the nose the right way, and a
surface on the CoM does nothing. Shoot a wing off and the CoM moves, so
every survivor re-roles on the spot — invariant #11.

Consequences to design around: zero authority at zero airspeed (taxi on
wheels / thrust, spawn planes with forward speed); static stability is
the builder's problem (CoL behind CoM or the plane is twitchy — the
CoM/CoL overlay shows it); thrust-offset moments are no longer masked
by a hidden torque. `GroundDriveSubsystem` / `HoverDriveSubsystem` are
grandfathered chassis-level drives pending the same migration.

---

## 3. Damage model for kinetic / contact weapons

> Status: **shipped for tip blocks (Hook / Mace, session 19 phase 5),
> still deferred for the bare `RopeTip` and rotor-as-flail use cases.**
> [`TipBlock`](../Assets/_Project/Scripts/Movement/TipBlock.cs) (with
> `HookBlock` and `MaceBlock` subclasses) implements the damage model
> below. `RopeTip.DealsDamage` remains a gating bool, hard-wired to
> `false` for the default (no-tip) chain. This section is the
> authoritative spec — when more kinetic damage paths land, they
> follow this shape.

### The four required elements

When kinetic damage ships, every damaging contact must satisfy:

1. **Mass-velocity rule.** Damage is a function of `(reduced_mass *
   v_rel^2) / 2` (kinetic energy of the contact in kJ), then scaled
   by a cosmetic `dmgPerKj` constant. NOT raw velocity, NOT raw mass.
   Two heavy slow things and two light fast things at the same KE
   should hurt the same. The existing
   [`MomentumImpactHandler`](../Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs)
   already does this for chassis-vs-chassis ramming; reuse that math.
2. **Speed threshold.** Below ~ 4 m/s relative, no damage. Stops a
   rope tip resting against a wall from bleeding HP every physics
   step. Mirrors `Tweakables.ImpactMinSpeed` for ramming damage.
3. **Cooldown / debounce.** PhysX can fire `OnCollisionEnter` /
   `OnCollisionStay` multiple times per step under high-velocity
   sustained contact. A hit should debounce per (attacker, target)
   pair for ~ 0.1 s, otherwise a single rope brush deletes a target.
4. **Visual cue.** Every damaging contact spawns a hit spark / particle
   so the player can read where damage came from. Free-body kinetic
   damage with no visual is invisible damage; nobody learns to play
   around it.

### Authority

Server-authoritative once netcode lands. Client predicts the visual
spark; the actual HP write is server-side only. See
[netcode.md](netcode.md). Until netcode, single-machine
authority is fine and damage runs locally in the contact callback.

### Tuning knobs (shipped)

Live in [`Tweakables.cs`](../Assets/_Project/Scripts/Core/Tweakables.cs)
under group "Combat":

- `Combat.RopeDamagePerKj` — default 2.0, range 0..50. Mirrors
  `Impact.DamagePerKj` but tuned conservatively (rope tip momentum
  is small at default mass × default RPM).
- `Combat.RopeMinSpeed` — default 4.0 m/s, range 0..20.
- `Combat.RopeHitCooldown` — default 0.10 s, range 0.02..1.0.

These are **performance / feel** knobs, not gameplay-shape knobs.
Hook vs Mace differentiation comes from `BlockDefinition.Mass`
(0.5 kg vs 2.0 kg) — same dmg/kJ across tip types, mass differential
drives the kinetic-energy contribution. The rope COUNT, RADIUS,
SEGMENT COUNT stay graphics-only (see § 5).

**MP debt:** these are still per-machine Tweakables today, which
violates § 1.5. Server picks canonical values when netcode lands.

---

## 4. Stress-test discipline

The arena ships a built-in stress target: settings → Stress →
"Spawn Rotor Tower" (or DevHud → "Spawn / Refresh Rotor Tower").
That blueprint is intentionally tuned to *break* the § 16 budget:
5 rotors × 4 ropes × 4 segs = 80 dynamic Rigidbodies, just past the
"alarm" threshold of 64.

**When to use it:**

- Before merging any change to `RopeBlock`, `RotorBlock`,
  `ConfigurableJoint` setup, `MomentumImpactHandler`, or anything
  that touches the chassis Rigidbody pipeline. Capture a baseline,
  capture after, look at the delta.
- After bumping the default chassis loadouts in `GameplayScaffolder`.
- When investigating a "physics feels wrong" bug report — the
  stress tower at high RPM exposes solver-stability issues that
  the default plane never triggers.

**What to look at in the Profiler:**

- `Physics.Simulate` per `FixedUpdate` (target: < 2 ms).
- Active Rigidbody count (target: < 64).
- Active contact count (target: < 4,000).
- Allocations / frame (target: 0 B in steady state).
- Frame time (target: 16.6 ms).

If any of these fail under the default tower configuration, it's a
regression — bisect against the previous capture.

---

## 5. Tweakables vs blueprint data

> **Rule.** Anything that affects gameplay-observable behaviour
> belongs on the chassis blueprint. Anything that affects only how
> the local machine renders / simulates the chassis belongs in
> `Tweakables`.

### Why this matters

`Tweakables` are per-machine, persisted to local JSON. They're
fantastic for live-tuning (drag a slider, see the result), and
catastrophic for multiplayer if a player can desync the world
state by editing a slider.

The contract is enforced by review, not by code. Reviewer's
checklist when a PR adds a new `Tweakables` key:

- [ ] Does this value affect damage dealt?
- [ ] Does this value affect hit detection / collision area?
- [ ] Does this value affect what other players see?
- [ ] Does this value affect movement / control authority?

If any answer is "yes," the value goes on the blueprint, not in
`Tweakables`.

### Current state by knob

| Tweakable | Status |
|---|---|
| `Plane.*`, `Thruster.*`, `Rudder.*`, `Ground.*`, `Chassis.*` | Single-player only today. **Will move to per-block / per-chassis config when netcode lands** — currently a known debt. |
| `Water.*` | Arena property. Same arena → same value for all players, server pushes the seed. Stays. |
| `Combat.Smg*`, `Combat.Bomb*` | Same debt as Plane / Thruster. Move to `WeaponDefinition` SOs when a second weapon ships. |
| `Combat.RopeDamagePerKj`, `Combat.RopeMinSpeed`, `Combat.RopeHitCooldown` | **MP debt.** Drive contact damage from `Hook` / `Mace` tip blocks (§ 3). Currently per-machine; server picks canonical values once netcode lands. |
| `Aero.WingSpan`, `Aero.WingChord`, `Aero.WingThickness` | **Cosmetic / visual.** Drive `_wingMesh.localScale` only — `AeroSurfaceBlock.FixedUpdate` does NOT read them. If any future PR couples them to lift / drag / hit area, they MUST move to per-block blueprint config first. |
| `Rope.*` | **Cosmetic / quality.** Rope blocks today don't deal damage at the segment level (only the adopted Hook / Mace tip does). The rope chain itself is just a hanging string. Stays in Tweakables. |
| `Rotor.RPM` | **Retired** — migrated to per-rotor blueprint config (`Entry.BlockConfig`, build-mode RPM slider, default 240 via `RotorDefaults`). RPM also drives the rotor's CPU price quadratically (`CpuBudget.EffectiveCpuCost`). Session 123. |
| `Impact.*` | Same single-player debt. Server picks the canonical values when MP lands. |
| `Stress.*` | Dev-only. Never observed by other players because stress targets are local-only entities. Stays. |

---

## 6. Open items 🔬

- **Stress tower benchmark numbers.** Need a real Profiler capture
  with the tower at 600 RPM logged here so future regressions have
  a baseline. Capture lives in `docs/perf-baselines/` (TBD).
- **Verlet rope prototype.** Prove the migration path on a sandbox
  branch before any of the § 2 triggers fire. A 30-minute sketch
  of `RopeSimulator` would de-risk a lot.
- **Per-block blueprint config.** The blueprint `Entry` struct today
  is `{blockId, position}`. When weapons / rotors need per-instance
  config (RPM, fire mode, cosmetic colour, etc.), extend it to
  `{blockId, position, configBlob}` and version the serializer.
  Touches `ChassisBlueprint`, `RobotBlueprintSerializer`,
  `BlockBehaviour`, every block's `Configure` path.

---

*This file is a living document. When a rule changes, update it
here in the same PR that breaks the rule, and link it in
[CHANGES.md](../CHANGES.md).*
