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

- **Removed** the cosmetic tail rotor at `(0, 1, -2)` and the single
  rope-tail at `(0, -1, -3)`.
- **Added** eight `Rope` + tip pairs, all at `y=-1` below an existing
  wing or tailplane cell, with the tip block at `y=-2`:

| Rope cell | Tip type | Tip cell |
|---|---|---|
| `( 2, -1,  0)` | Hook | `( 2, -2,  0)` |
| `(-2, -1,  0)` | Hook | `(-2, -2,  0)` |
| `( 3, -1,  0)` | Mace | `( 3, -2,  0)` |
| `(-3, -1,  0)` | Mace | `(-3, -2,  0)` |
| `( 1, -1, -3)` | Hook | `( 1, -2, -3)` |
| `(-1, -1, -3)` | Hook | `(-1, -2, -3)` |
| `( 2, -1, -3)` | Mace | `( 2, -2, -3)` |
| `(-2, -1, -3)` | Mace | `(-2, -2, -3)` |

Mix is intentional — 4 hooks + 4 maces so the user sees both contact
profiles in one play session. Outer wings carry the heavier maces
(longer lever arm = more pendulum momentum); inner wings + tailplane
inboards carry the lighter hooks.

## Connectivity

Every rope cell is face-adjacent to an existing wing or tailplane
`Aero` cell (e.g. rope at `(2, -1, 0)` is adjacent to `(2, 0, 0)` —
the second wing segment). Each tip cell is face-adjacent to its own
rope cell at `y=-1`. Validator passes.

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
