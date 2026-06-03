# 108 — Combat pass: knockback, mortar, mines

> Status: **In progress.** A research-first feature pass adding three combat
> mechanics in dependency order: shared weapon-hit **knockback** → **mortar**
> weapon (top-mounted, lobbed) → **mines** module. Knockback is the shared
> foundation the other two lean on. Sequenced with a checkpoint per mechanic.
>
> Targeter/design decisions came from a `design-pilot` research round grounded
> in the pillars + Robocraft/WoT/Crossout/TF2 references; the user picked the
> mortar targeter style (start-of-arc preview, camera-offset launch) and the
> SMG debt-buffer.

## Mechanic 1 — Knockback / knockup (shipped, code)

Every damaging weapon hit now imparts an impulse to the *target* chassis,
mirroring the recoil that already kicks the *firer*. Kinetic hits stagger the
target along the shot direction; explosive hits push away from the blast centre
with an upward pop.

**`KnockbackReceiver`** (new, `Combat`) — a per-chassis impulse sink, lazily
added to a `Robot` the first time it's knocked (zero baseline cost — invariant
#5; a never-hit bot never carries it). Two paths, both applied at the chassis
**centre of mass** so knockback is pure translation — a graze on a wing tip
can't barrel-roll a light bot (rotational impact stays the momentum-damage
system's job). All force goes to the single chassis Rigidbody (invariant #4).

- **Immediate** (cannon / mortar / explosion): the impulse lands this physics
  step. Punchy stagger / pop.
- **Smoothed** (rapid-fire SMG): the impulse accumulates into a debt vector that
  bleeds out exponentially over a 0.7 s time constant. A 12 Hz pellet stream
  becomes one bounded push instead of per-frame jitter — the user's SMG concern.
  Total momentum imparted equals one impulse of the summed debt; it's just
  spread in time.

Every impulse is clamped to a **delta-v ceiling scaled by chassis mass**
(3 m/s immediate, 4 m/s accumulated debt), so no weapon can launch a
skeleton-framed light bot to orbit — combat stays readable regardless of how
little a target weighs.

**Wiring.** `ProjectileSpec` gains `Knockback` (N·s) + `KnockbackSmoothed`
(bool). `ProjectileWorld.Resolve` now receives the projectile travel direction
and applies knockback in each damage path: direct + ring → kinetic (horizontal
stagger, vertical dropped), area-splash → explosive (radial + upward bias,
linear distance falloff). Per-weapon magnitude lives on the definition assets
(`WeaponDefinition` / `CannonDefinition` / `BombDefinition`) with inline
fallbacks on the block components, same resolution pattern as recoil. Starting
values: SMG 3 (smoothed), cannon 18 (immediate), bomb 40 (explosive). All tune
in the inspector.

Knockback only fires when damage actually lands (it sits after the `TakeDamage`
calls, so friendly-fire-suppressed hits impart nothing).

## Mechanic 2 — Mortar (pending)

## Mechanic 3 — Mines (pending)

## Files

New: `Combat/KnockbackReceiver.cs`. Edited: `Combat/ProjectileSpec.cs`,
`Combat/ProjectileWorld.cs`, `Combat/WeaponDefinition.cs`,
`Combat/CannonDefinition.cs`, `Combat/BombDefinition.cs`,
`Combat/ProjectileGun.cs`, `Combat/CannonBlock.cs`, `Combat/BombBayBlock.cs`.

## Verification

Knockback: compile-reviewed by hand; **Unity MCP was not connected this session**
so the automated compile/console check and `qa-verifier`/`perf-checker` passes
could not run. Needs an editor recompile + console glance, and a playtest to tune
the feel values. No new allocations on the hot path (receiver early-outs when
debt is ~0; lazily added once per ever-hit bot).

## Invariant compliance

- **#1** knockback magnitudes are server-authoritative definition data, no
  Tweakable.
- **#4** all impulses applied to the single chassis Rigidbody at its CoM.
- **#5** `KnockbackReceiver` is added only on first hit — zero cost for an
  untouched bot.
- **#6** receiver `FixedUpdate` allocates nothing and early-outs at rest.
