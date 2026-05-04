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
> **Companion docs.** [BEST_PRACTICES § 16](BEST_PRACTICES.md#16-performance-budgets-targets-not-law)
> for the budget table; [NETCODE_PLAN.md](NETCODE_PLAN.md) for the
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

## 2. Rope tech: where we are and where we're going

### Today (PhysX joint chains)

- **What ships:** [`RopeBlock`](../Assets/_Project/Scripts/Movement/RopeBlock.cs)
  (passive chain) and [`RotorBlock`](../Assets/_Project/Scripts/Movement/RotorBlock.cs)
  (kinematic-hub-driven spinning chain) both build a chain of free-body
  Rigidbodies linked by `ConfigurableJoint`s, parented to a scene-root
  container.
- **Tip collision:** Only the LAST segment of each chain has a
  collider — see [`RopeTip`](../Assets/_Project/Scripts/Movement/RopeTip.cs).
  Per-segment colliders are deliberately avoided (cost + the joint /
  contact-solver fight that destabilises spinning chains under load).
  The tip ignores its own chassis colliders via `Physics.IgnoreCollision`
  at build time.
- **Cost:** ~ N segments × M ropes dynamic Rigidbodies + 1 sphere
  collider per rope. For the default rotor (4 ropes × 4 segs) that's
  16 dynamic rbs + 4 colliders per rotor. The kinematic-hub trick
  avoids any `AddForce` calls — PhysX synthesises tangential velocity
  from the hub's `MovePosition` / `MoveRotation`.
- **Known failure modes** (write these down so we recognise them
  when they show up):
  1. **Solver iteration cost balloons under sustained high RPM.** The
     joint solver does extra work to keep the chain coherent at angular
     velocities the engine wasn't tuned for. Use the rotor stress
     tower (settings → Stress → "Spawn Rotor Tower") to stress-test.
  2. **Chains "explode" under per-segment collision.** Joints try to
     pull a contact-stuck segment back, contact resolution fights the
     pull, angular limits get violated, the chain visibly snaps to a
     mangled pose for one frame and then settles. This is exactly why
     we do tip-only collision today.
  3. **Networking pain.** N segments per chain means N rigid-body poses
     to replicate per chain per tick. At 16 players × 2 rotors × 4
     ropes × 4 segs that's 2,048 poses — beyond any sane bandwidth
     budget. We will NEVER ship that. Ropes will be replicated as
     hub-pose + tip-pose only and re-simulated client-side once we
     migrate to Verlet.

### Future (custom Verlet / PBD solver)

- **Why:** Order-of-magnitude cheaper, deterministic, easy to network
  (replicate hub + tip pose, clients simulate the chain locally), and
  per-segment world collision becomes a single capsule cast per step
  instead of a contact-solver tax. This is the unambiguously correct
  long-term tool.
- **API target:** Same external shape as today — a rope owns a
  hub-end Rigidbody and a tip-end Rigidbody, the body of the chain
  is a positions array updated each `FixedUpdate` by the solver,
  and the tip drives a real collider that does damage / contact
  effects. The middle of the chain has no Rigidbodies at all.
- **Owner:** A single `RopeSimulator` MonoBehaviour scene-root
  singleton that ticks every active chain in one batch (cache-friendly
  Burst-able loop). Rope blocks register / unregister with it on
  enable / disable.

### Migration triggers

Pick whichever lands first:

1. **Profile shows the rotor stress tower (5 rotors × 4 ropes × 4
   segs at 600 RPM) costs more than 1.5 ms of PhysX simulate per
   step, *or* the joint solver iteration count spikes above the
   default budget under that load.** Set up: enter the arena, settings
   → Stress → "Spawn Rotor Tower" + slide RPM to 600, capture in the
   Unity Profiler. Re-run after every rotor / rope-pipeline change
   that could plausibly affect cost.
2. **A flail-style weapon needs rope-vs-arena collision along the
   full chain length** (not just the tip). At that point per-segment
   collision is the feature, and PhysX joints can't deliver it
   without the "explode on contact" pathology described above.
3. **Networking lands.** The PhysX-joint replication cost is too
   high regardless of profile numbers. Ropes go Verlet before any
   over-the-wire chassis state ships.

When *any* of these triggers, file an issue, name this section,
and don't start writing damage code until the migration lands. The
Verlet replacement is somewhere between a long afternoon and a
short weekend depending on how nice we want the API to be.

---

## 3. Damage model for kinetic / contact weapons

> Status: **deferred**. Today no rope, rotor tip, or other kinetic
> chassis component deals damage. `RopeTip.DealsDamage` exists as a
> gating bool and is hard-wired to `false`. This section captures the
> shape of the eventual damage formula so we don't reinvent it under
> pressure.

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
[NETCODE_PLAN.md](NETCODE_PLAN.md). Until netcode, single-machine
authority is fine and damage runs locally in the contact callback.

### Tuning knobs

When ropes start dealing damage:

- `Combat.RopeDamagePerKj` — mirrors `Impact.DamagePerKj`. Likely
  much lower (rope tip momentum is small).
- `Combat.RopeMinSpeed` — below this, no damage. Default ~ 4 m/s.
- `Combat.RopeHitCooldown` — per-pair debounce window in seconds.

These are **performance / feel** knobs, not gameplay-shape knobs.
The rope COUNT, RADIUS, SEGMENT COUNT stay graphics-only (see § 5).

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
| `Rope.*` | **Cosmetic / quality.** Rope blocks today don't deal damage and aren't visible to other players in any consequential way (the chain is just a hanging string). Stays in Tweakables. |
| `Rotor.RPM` | **Cosmetic / quality** today; must move when rotors drive damage. The visual-only rotor (ropes = 0) is fine forever. |
| `Rotor.RopeCount`, `Rotor.RopeRadius`, `Rotor.RopeSegments` | **Graphics-only by contract.** Hard requirement: rotor damage / hit area cannot depend on these. See § 3. |
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
