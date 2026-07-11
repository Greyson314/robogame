# ADR-0005 — Concoction scope widening, colour chemistry, and naming

**Status:** Proposed (implemented session 141, autonomous run — awaiting
user review; revert is contained to the files in the session log)
**Date:** 2026-07-11
**Extends:** ADR-0004 (concoction persistence + governance)

## Context

Session 141 executes the user-requested "full pass" on concoctions:
more levers, a colour identity, colour-derived default names, a real
Laboratory screen, and concoction selection on ammo weapons. ADR-0004's
governance (per-player library, blueprint carries only the id, server
resolves + clamps, CPU surcharge legitimizes the power) is unchanged —
this ADR records the decisions that extend it.

## Decisions

1. **Levers are five:** damage / size / knockback (shipped) + speed +
   spread. Speed scales launch/muzzle velocity everywhere (applied in
   the mortar's speed RESOLVER so the arc preview can't lie); spread
   only bites where a spread stat exists (SMG) and is a documented
   no-op elsewhere, as size is on splash-less weapons.
2. **On-hit effects are NOT a lever.** They stay scoped as the separate
   approved "Rider Effects" backlog item (discrete Burn/Smoke/EMP
   choice), to be rendered as a fixed-colour FLECK over the mixed base
   wash, never averaged into the hue. Folding them in here would smuggle
   a status-effect subsystem (per-target tick state, netcode-visible)
   into a UI/data pass.
3. **Concoctions extend to SMG + Cannon.** `IsConcoctableBlock` covers
   all four ammo weapons; the ammo-multiplier config and the concoction
   surcharge now STACK in `CpuBudget` (concoction surcharge priced on
   the block's BASE cost, not the ammo-scaled price, so the two knobs
   price independently).
4. **Surcharge formula v2:** factor 0.3 across five levers (was 0.5
   across three). Calibration anchors preserved exactly — all-neutral
   +75% of base, all-max +150%. Mid-shaped v1 recipes shift ≤ ±0.2×base
   on load; accepted instead of per-recipe formula versioning (needless
   machinery pre-multiplayer).
5. **Colour chemistry** (`ConcoctionColor`): weighted circular hue
   blending; only above-neutral slider parts pour pigment; dominance
   (resultant length) drives saturation so hue-opposed mixes read as
   sludge; total level drives darkness. Anchors — damage 350° Madder,
   knockback 38° Ochre, speed 215° Prussian, spread 300° Orchid, size
   245° Indigo. The green band 90–165° is RESERVED (repair/regen) and
   vermilion's exact hue is avoided (rationed UI chrome).
6. **Default names are colour-derived:** "{Dark|Pale|Murky} {pigment}
   Concoction" from a 12-band pigment table (Madder, Ochre, Saffron,
   Citron, Verdigris, Teal, Cerulean, Prussian, Indigo, Amethyst,
   Orchid, Rose Madder) + specials (Standard Mixture / Raw Mixture /
   Black Bile). Collision → numeral suffix in the Lab.
7. **Combat readback:** projectile visual tint + impact spark/shockwave
   tint carry the recipe's mixed pigment (`ProjectileSpec.TintImpact`,
   pooled-VFX colour re-stamped every spawn). Damage numbers stay
   untinted. **Carve-out recorded:** Lab-authored pigment is the ONE
   sanctioned continuous off-palette colour space — when the 12-token
   palette lock un-suspends, art-direction.md must inherit this
   exception explicitly or a future pass will clamp it and kill the
   feature's premise.
8. **The Laboratory stays an in-garage full-screen overlay**, not a new
   scene/GameState — "another screen" is delivered visually (two-pane
   journal + bench, own identity) without new scene plumbing. Revisit
   only if the user wants a physically distinct room.

## Consequences

- Old saved recipes load with speed/spread at neutral (serializer v2
  branches on schemaVersion — JsonUtility zero-fill trap).
- SMG/Cannon variant panel runs a combined ammo + concoction layout.
- Kill-feed concoction chip ("Concoction Identity" ticket) is NOT in
  this pass — needs damage-source attribution plumbing.
