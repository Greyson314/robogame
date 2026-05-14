# Tip-block attach mechanics

> How rope-mounted tip blocks (Hook, Mace, Magnet) attach to enemy
> chassis, transmit force back through the rope, and avoid the
> "destroys itself in two seconds" failure mode that plagued every
> hook iteration up to session 60.

## The system, one diagram

```
chassis        rope (Verlet)              tip body     target
  ┌──┐    │1│─│2│─│3│─...─│N-1│         ┌────────┐    ┌──────┐
  │RB│════════════════════════════════│hostRb│ ⤙ │  RB  │
  └──┘ ◄────── ConfigurableJoint ───────►└────────┘    └──────┘
            (limit = totalRopeLength,             ▲
             spring 8000 N, damper 250)           │
            "the leash"                           │  SpringJoint
                                                  │  (rest = 0,
                                                  │   spring 300 N,
                                                  │   no break)
                                                  │  "the bite"
```

There are **three** constraints working at once. Each one has a
distinct job, and the previous design's bug was that two of them
overlapped.

| Constraint | Owner | Job |
|---|---|---|
| Verlet chain (`VerletRopeChain`) | `RopeBlock` | Position-only. Maintains chain length between chassis anchor and tip body. Doesn't transmit *force* — particles are pure position state. |
| Chassis↔tip leash (`ConfigurableJoint`) | `RopeBlock` | The **force coupling**. Linear-limited at total rope length with a soft spring (8000 N, 250 damper). Stops the chassis flying off forever once the rope is taut. Always-on. |
| Tip↔target bite (`SpringJoint`) | `HookBlock` / `MagnetBlock` | The **latch**. Rest distance 0; pulls target toward the tip with `F = spring × distance`. No `breakForce` — bounded force envelope by construction. |

## Why the old design broke

Pre-session-60 the hook used a `ConfigurableJoint` with `Locked`
linear motion and `breakForce = 1200 N`. The fundamental problem:

> A locked-motion joint applies whatever **impulse** the constraint
> solver needs to keep two bodies coincident this physics step. Those
> impulses spike arbitrarily under acceleration — target jinks, plane
> banks, chassis-tip leash tightens — and trip the break threshold
> before the system reaches steady state.

The hook's band-aids told the story:

- **`GrappledChassisSpring = 600`** (softened from 8000) — the
  chassis↔tip leash was applying huge restoring impulses, which the
  locked grapple joint transmitted to the target. Softening reduced
  but didn't eliminate the resonance.
- **`GrappledTipMass = 25 kg`** (fattened from 0.5) — heavier tip
  body absorbed more of the chassis-leash impulse before it reached
  the target chassis. Worked, but at the cost of swinging a virtual
  cannonball.
- **Reattach cooldown** — paper over the symptom of repeated
  attach/break/attach cycles.

A `SpringJoint` with rest distance 0 applies **force** = spring ×
distance. Force is bounded by how far apart the bodies are; impulse
is bounded by force × dt. No spikes, no break threshold, no
band-aids.

## The damage half — chassis impact handler was destroying tip blocks

Separate bug. `MomentumImpactHandler` (line 166 pre-fix) walked the
*other* collider for `IDamageable` and damaged it directly **whenever
the other body had no MomentumImpactHandler of its own**. The
rope-tip body doesn't carry one (it's not a chassis), so the
fallback path fired and chewed the tip block's HP (~150) down at
~5 dmg/0.2 s under sustained contact.

Session 60 added the exemption:

```csharp
if (otherComp.GetComponentInParent<TipBlock>() != null) return;
```

Tip blocks are **chassis weapons**, not standalone damageable
entities. Their damage contract is:

| Damage source | Applied? | Why |
|---|---|---|
| Direct ranged hit (ProjectileWorld) | ✅ Yes | Tip blocks are blocks in the chassis grid; bullets / cannons / bombs hit them like any other block. |
| `TipBlock.HandleCollision` *as the attacker* | Depends on `DamagePerKj` | Hook = 0, Mace = 2.0, Magnet = 0. Mace is the only direct-damage tip. |
| `MomentumImpactHandler` fallback (the bug) | ❌ No (after session 60) | Tip blocks aren't a chassis with an impact handler; the IDamageable fallback used to mistakenly hit them. |
| `MomentumImpactHandler` self-damage on the **enemy chassis** | ✅ Yes (to the enemy, not us) | When the magnet bashes the enemy chassis, the enemy's own handler bills its own grid. Newton's third law on damage. |

## Behaviour you can rely on

A plane with `chassis → rope → magnet` can now reliably:

1. **Approach** — fly past an enemy chassis. The magnet's
   `OverlapSphere` pulls them into the magnet's mouth at up to
   600 N (gentle yank, not a catapult).
2. **Latch** — contact creates a `SpringJoint` at rest distance 0.
   `breakForce = ∞`. The target rides at the magnet's mouth.
3. **Drag** — the plane flies wherever. The chassis↔tip leash
   keeps the tip within `totalRopeLength` of the chassis (8000 N
   spring at the limit), the spring tether keeps the target at the
   tip, the Verlet chain renders the natural drape. Target trails
   behind the plane like a fish on a line.
4. **Release** — `MagnetBlock.ReleaseTether()` (or `HookBlock.Release()`)
   destroys the spring joint, returns the tip body to kinematic,
   resumes simulator-driven flight.
5. **Auto-release on target death** — the `FixedUpdate` poll spots
   `connectedBody == null` (Unity's fake-null after the target
   chassis was destroyed) and tears down cleanly.

## Tuning surface

Inspector fields per-tip-block (per PHYSICS_PLAN § 1.5: *no
Tweakable affects gameplay outcomes*):

- `MagnetBlock._pullRadius` — sphere of influence for the approach
  field. 6 m default.
- `MagnetBlock._pullForce` — peak force at the centre, scaled down
  by `_falloffExponent`. 600 N default.
- `MagnetBlock._tetherSpring` / `_tetherDamper` — spring constants
  for the latch. 320 / 110 default.
- `HookBlock._tetherSpring` / `_tetherDamper` — same idea, slightly
  lower defaults (300 / 80) because the hook's J-shape geometry does
  the catching, not a pull field.
- `_relatchCooldown` / `_reattachCooldown` — debounce after release.

If a tether feels too soft (target slips off the magnet on hard
banks), raise `_tetherSpring`. If it oscillates (target bobs in and
out of the magnet's mouth), raise `_tetherDamper`. If targets get
catapulted on first contact (still seeing the old behaviour), drop
`_pullForce` — the pull field is doing too much, the magnet should
mostly be a guide field.

## What we explicitly do *not* do

- **We don't break the joint under load.** `breakForce = ∞`. The
  chassis↔tip leash is the only escape ceiling. If a target is way
  heavier than the chassis, the chassis stalls or gets pulled; the
  tether stays attached.
- **We don't soften the chassis↔tip leash during attach.** The
  bounded spring forces of the SpringJoint don't resonate with the
  leash, so the 8000 N / 250 damper default is fine throughout.
- **We don't fatten the tip mass during attach.** Same reason — no
  catapult impulse to absorb.
- **We don't apply outbound KE damage from the magnet on contact.**
  Magnet `DamagePerKj = 0`. Hook is also 0. Damage emerges from the
  chassis-drag dynamics (banging the target into walls, dragging
  into grinders) — the magnet's job is *control*, not *kill*.

## Pointers

- [HookBlock.cs](../Assets/_Project/Scripts/Movement/HookBlock.cs)
  — reference implementation of the SpringJoint pattern.
- [MagnetBlock.cs](../Assets/_Project/Scripts/Movement/MagnetBlock.cs)
  — same pattern + pull field.
- [TipBlock.cs](../Assets/_Project/Scripts/Movement/TipBlock.cs)
  — base class: contact cooldown, KE damage formula, chassis-collider
  ignore-pairs.
- [RopeBlock.cs](../Assets/_Project/Scripts/Movement/RopeBlock.cs)
  — chassis↔tip leash construction (`_chassisTipJoint` setup), tip
  body lifecycle.
- [VerletRopeSimulator.cs](../Assets/_Project/Scripts/Movement/VerletRopeSimulator.cs)
  — `IsTipExternallyConstrained` flips the chain into pinned-tip mode
  when the tip goes non-kinematic; this is what lets the chain
  conform around an attached tip.
- [MomentumImpactHandler.cs](../Assets/_Project/Scripts/Combat/MomentumImpactHandler.cs)
  — the tip-block exemption (search "session 60").
