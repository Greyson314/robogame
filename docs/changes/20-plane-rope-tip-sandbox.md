# Session 20 — Plane reconfigured as rope-tip test sandbox

> Status: **shipped.** Small targeted change. The user is testing the
> new Hook/Mace contact-damage path while session 21's helicopter
> frame-stability bug is investigated in parallel.

## Intent

The helicopter is currently unflyable: the chassis frame spins at
something close to rotor RPM (possibly opposite to the foils, possibly
in lieu of them) and the rig catapults itself within seconds. That's
the next session's bug. To unblock testing of the Phase 5 Hook/Mace
work in parallel, the user asked to strip rotors off the default
plane and load it up with eight ropes-with-tips.

## What shipped

[`GameplayScaffolder.BuildPlaneEntries`](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs):

- **Removed** the cosmetic tail rotor at `(0, 1, -2)`.
- **Replaced** the single bare rope-tail at `(0, -1, -3)` with a
  rope-with-hook pair: rope at `(0, -1, -3)`, hook at `(0, -2, -3)`.
  Hook hangs straight off the thruster cell at the back of the
  fuselage.

[`Tweakables.RopeSegmentCount`](../../Assets/_Project/Scripts/Core/Tweakables.cs):

- Default bumped from 5 → 8 so the plane's tail rope reads as
  "length 8" out of the box (4 m at 0.5 m / segment). Min/max range
  unchanged at 2..32 — the slider still covers everything.
  *Caveat:* if the user already has a value saved to disk in
  `tweakables.json`, that persisted value will reload over the new
  default. Use the slider or delete the file to pick up the new
  default.

## Connectivity

Rope at `(0, -1, -3)` is face-adjacent to the thruster cell at
`(0, 0, -3)`. Hook at `(0, -2, -3)` is adjacent to the rope cell.
Validator passes.

## To exercise

1. Run **Robogame → Build Everything** to refresh
   `Blueprint_DefaultPlane.asset` and the EditMode tests'
   `presets.md` snapshot.
2. Hit Play, switch to the Plane chassis from the garage.
3. Forward flight + dive at the combat dummy or the new barbell
   dummy; the eight tips swing on contact.

## Next up

Helicopter frame-stability bug. The user's report ("frame is
spinning either opposite to the aerofoils or in their stead") points
at one of:

1. Foil adoption is silently producing zero adopted foils on the new
   mechanism-cell scan. With zero foils, the kinematic hub still
   spins (cosmetic visual), but no lift is applied to the chassis.
   The chassis spinning *with* the rotor would then be unexplained
   — but worth verifying via the existing
   `[RotorBlock] '<name>': adopted '<foil>'` Debug.Logs.
2. The mechanism-cell-shift in Phase 3 broke `spinAxisGridInt`
   rounding for some non-default rotation, so the scan looks at the
   wrong cell.
3. PhysX is leaking the kinematic hub's `MoveRotation` writes back
   into the chassis Rigidbody via some interaction we haven't
   accounted for (foil parent→hub→chassis chain or a stale
   transform-parent reference).
