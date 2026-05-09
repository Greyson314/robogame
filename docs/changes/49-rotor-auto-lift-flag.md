# 49 — Auto-derive `RotorsGenerateLift` from grid contents

> Follow-up to session 48. The user reported that after placing a
> rotor + blades from scratch, the rotor spins in the arena but the
> blades stay static — they aren't being adopted into the kinematic
> hub.

## What changed

[`BuildSession.SyncBlueprint`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs)
now auto-sets `Blueprint.RotorsGenerateLift = true` whenever the
synced grid contains any `BlockIds.Rotor` cell.
[`BlockEditor.SyncBlueprintFromGrid`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)'s
legacy fallback path mirrors the rule for sessions that aren't
session-bound (no behavioural divergence between the two code
paths).

## Why this fixes it

The rotor's blade adoption (`RotorBlock.BuildLiftRig` →
`AdoptAdjacentAerofoils`) only runs when the rotor's `GeneratesLift`
property is `true`. That property is set by the chassis assembler
*from the blueprint's `RotorsGenerateLift` flag*. Pre-fix, that flag
was per-blueprint and defaulted to `false` everywhere except the
shipped helicopter preset. A from-scratch chassis ended up with
`flag=false` → cosmetic rotors that spin their disc + bars but
don't reparent the blades, so the blades sit static while the rotor
moves around them.

Per-rotor opt-in is the *correct* eventual fix (per the existing
`ChassisBlueprint.RotorsGenerateLift` doc comment, "Per-rotor opt-in
lands when the blueprint format supports per-cell config"). Until
that lands, "any rotor on the chassis" is the right blueprint-level
granularity — there's no use case today for a chassis with a mix of
lift and cosmetic rotors.

## When the user sees the fix

Build-mode placement updates `state.CurrentBlueprint.RotorsGenerateLift`
on every grid sync. The rotor's `GeneratesLift` only re-reads at
chassis spawn time, so the user has to either:
- Exit build mode (triggers `GarageController.Respawn` → new chassis
  with the flag → rotor adopts at `OnEnable`). The garage chassis is
  kinematic so the rotor stays frozen, but adoption happens.
- Launch to an arena (fresh chassis, non-kinematic, rotor + adopted
  blades spin together).

Live re-adoption in build mode would require subscribing the rotor
to `BlockGrid.BlockPlaced` and re-running the adoption pass on
relevant placements. Skipped for now because build-mode chassis is
frozen anyway — the player wouldn't see the spin difference.

## Files

`BuildSession.cs`, `BlockEditor.cs`. No new tests (the auto-derive
rule is one branch in a hot path; covered transitively by play-test
on the helicopter scenario).

## Verification

1. Garage → fresh chassis (or remove the helicopter preset's blades).
2. Place CPU + cube + rotor (auto-companion gives you the mechanism
   cube). Place blades on the four cube faces.
3. Click Launch.
4. In the arena: the rotor's mast/disc/bars spin AND the four blades
   rotate with the hub.
