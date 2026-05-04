# Session 16 — Rotor / aerofoil decoupling (work-in-progress, three regressions outstanding)

> Status: **partially shipped, regressions not yet fixed.** Compiles
> clean, but three behaviours broke in the process and are listed at
> the bottom of this file. The change is being kept on `main` rather
> than reverted — the architectural shift is the right one — but the
> next session has to fix the regressions before the feature is back
> at parity with session 15.

## Intent

Session 14/15 shipped the rotor as a single block that, when the
blueprint flag `RotorsGenerateLift` was set, *auto-spawned* four
synthetic aerofoil children under a kinematic hub at scene root.
That worked, but it baked the rotor + blade ring into one indivisible
unit. The user's call:

> "the rotor and foils are separate parts that should be *able* to be
> connected in the garage, and then the foil should spin the foils in
> the game, but they aren't one part. They're 5 (1 rotor, 4 foils)."

So: blades become first-class blocks the player places independently
in the garage, and the rotor *adopts* whichever aerofoils happen to
sit in its spin-plane neighbours at game-start.

## What shipped

### 1. `RotorBlock.cs` — adopt-don't-synthesise

- Deleted the synthetic-blade fields: `_bladeCount`, `_bladeRadius`,
  `_bladeSize`. Kept `_collectivePitchDeg` (still applies to adopted
  foils as a fixed mounting pitch).
- Replaced `BuildBlade(i, chassis)` with `AdoptAdjacentAerofoils(chassis)`:
    - Iterates the 6 axial offsets, skips any whose direction is
      within ~25° of the rotor's spin axis (so a cube above/below the
      hub never becomes a blade).
    - Looks up each surviving neighbour cell via
      `BlockGrid.TryGetBlock`. If it has an `AeroSurfaceBlock`
      component, the foil is *adopted*: reparented under the kinematic
      hub with `worldPositionStays: true`, then re-rotated so its
      lift axis aligns with the spin axis, its chord faces the spin
      tangent, and the collective pitch is baked in around the radial
      axis. Finally `aero.ConfigureRotorMode(_hub, chassis)` switches
      it to sample velocity from the hub and dump force onto the
      chassis.
    - Each adoption is recorded in a `List<AdoptedFoil>` (parent +
      local TR) so `DestroyLiftRig` can put the foil back where it
      came from on a mid-game `Rebuild` (parent change after debris
      detach, etc.).
- Removed the `Tweakables.Changed` subscription. Geometry no longer
  comes from sliders — it comes from each foil's own `_wingSize` —
  so there's nothing to hot-rebuild from a tweakables event.

### 2. `GameplayScaffolder.cs` — Helicopter blueprint declares its own foils

- The helicopter's main rotor at `(0, 1, 0)` is now surrounded by four
  explicit `BlockIds.Aero` entries at `(±1, 1, 0)` and `(0, 1, ±1)`.
  The rotor finds and adopts them at game-start.
- The tail rotor at `(1, 0, -3)` is left bare — cosmetic spinner only
  until the player adds adjacent foils manually.

### 3. `Tweakables.cs` — dropped the synthetic-blade knobs

- Removed `RotorBladeLength` and `RotorBladeChord` (constants +
  `Register` calls). Blade dimensions now come from each
  `AeroSurfaceBlock._wingSize`, since blades are independent grid
  cells.
- `RotorRpm` stays — it still drives the hub's angular velocity.

## Why this is the right shape (even with the regressions)

- `BlockGrid` indexes by grid position, not by transform parent —
  reparenting an Aero block GameObject under the rotor's hub does
  *not* break splash damage, CPU connectivity, or destruction events.
  The grid still sees the foil at its placed cell.
- `AeroSurfaceBlock.ConfigureRotorMode(hub, chassis)` was already the
  intended entry point for "this foil's airspeed comes from a
  kinematic hub, but its lift goes to a different rb." Reusing it for
  adopted foils means the lift math is the *same path* a synthesised
  blade used in session 15.
- A bare rotor with no neighbours is now a meaningful, supported
  configuration: cosmetic spinner, zero lift, no special-case code.
  Previously, a `GeneratesLift=true` rotor *always* spawned four
  blades; the player had no way to opt out per-rotor.

## Outstanding regressions (the user's call-outs)

These are the things that broke and are **not yet fixed** in this
change:

### R1 — Garage doesn't render the foils on the default helicopter

User reports the four `Aero` entries placed around the main rotor
don't appear visually in the garage.

Likely cause (to be investigated next session): the foils *are* placed
into the BlockGrid by `ChassisFactory`, and `RobotAeroBinder` *does*
add `AeroSurfaceBlock` to them — but in the garage the chassis
`Rigidbody` is kinematic-frozen, the `RotorsGenerateLift` flag is
flipped on by the factory, and `BuildLiftRig` runs `AdoptAdjacentAerofoils`
which **reparents the foil GameObject out from under the BlockGrid
root and under the hub at scene root**. If the garage relies on the
foil being under the chassis transform for its render or layout pass,
the visual is now stranded at scene root with the wrong transform
chain. (Or alternatively: the garage build path doesn't run the lift
flag flip and the foils sit at their placed cells, but their
`Vertical=false` orientation looks like a flat tile from above and is
hard to see — either is plausible without instrumenting.)

### R2 — Non-RPM rotor knobs gone from the tweaks menu

This one is a *deliberate* deletion in the change above (Section 3),
not an accidental regression — but the user wants the controls back.
We need to either:
  - add a per-rotor `CollectivePitch` tweakable that drives
    `_collectivePitchDeg` live, or
  - re-introduce blade-size sliders that override each adopted foil's
    `transform.localScale` along the radial axis (cleaner: scale the
    foil's "Wing" child rather than the foil root, so collision and
    grid bounds stay at one cell).

The "right" answer is probably the second — players don't think in
terms of pitch, they think in terms of "longer blades = more lift".

### R3 — Rotors no longer generate lift

Even with foils adjacent, the chassis doesn't rise. Possible causes
(to triage next session, in order of suspicion):

1. **Spin-axis dot-product cull is wrong.** `_spinAxisLocal` is in
   block-local space; we transform it to grid space via
   `transform.localRotation * _spinAxisLocal` and then compare to
   axial offsets. For the default helicopter rotor at `(0,1,0)` with
   identity local rotation and `_spinAxisLocal = +Y`, this should
   correctly *exclude* `(0, ±1, 0)` and *include* the four lateral
   neighbours. But the rotor visual mast points up via the
   `RotorMast`/`RotorSpin` *visual* hierarchy, not via the block's
   own rotation — so this reasoning needs verification on a chassis
   that actually loaded.
2. **Adopted foil's radial vector collapses to zero.** If
   `aero.transform.localPosition` after `SetParent(_hub, true)` ends
   up close to zero (e.g. because the foil was placed *exactly* at
   the rotor's hub world position, not offset by a cell), the radial
   normalisation fails and we `continue` past `ConfigureRotorMode`.
   The hub world position is `transform.TransformPoint((0, SpinHeight,
   0))` = roughly 0.78 m above the rotor cell centre. A foil placed
   at the cell `(1, 1, 0)` sits ~1 m to the side and ~0.78 m below
   the hub centre — the radial vector should be non-zero, but after
   projecting out the spin-axis component (`_spinAxisLocal = +Y`),
   we drop the Y component and what's left has magnitude ~1, which
   should be fine. Worth a `Debug.Log` of the projected radial.
3. **Order-of-ops bug between `RobotAeroBinder.OnEnable` and the
   factory's `RotorsGenerateLift = true` flip.** Session-14 already
   moved the flag flip into the factory's `finally` block after
   `SetActive(wasActive)`, so the binders have run by then. But
   `AeroSurfaceBlock.OnEnable` resolves `_velocityRb` /
   `_forceTargetRb` from its parent chain; we then call
   `ConfigureRotorMode` which overrides those. If
   `ConfigureRotorMode` runs *before* the foil's `OnEnable` (because
   the `SetActive(wasActive)` enables the rotor first and the rotor
   reaches sideways for foils that haven't been enabled yet), the
   foil's later `OnEnable` will *re-resolve* `_velocityRb` from the
   parent — which is now the kinematic hub at scene root — and the
   `_forceTargetRb = ResolveForceTarget(...)` walk will succeed
   (the hub is kinematic, so it walks up; but the hub's parent is
   *scene root*, so the walk falls off the world and returns null).
   That'd silently kill lift on every adopted foil. **This is the
   most likely culprit.** The fix is for `AeroSurfaceBlock.OnEnable`
   to no-op when `_rotorMode` is already true — a guard the
   session-14 work added but may not survive in the current branch.
   Verify and re-add if missing.

## Files touched

- [Assets/_Project/Scripts/Movement/RotorBlock.cs](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
- [Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
- [Assets/_Project/Scripts/Core/Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs)

## Decision: keep, don't revert

The decoupling is the right architectural shape — bare rotors, mixed
rotor sizes, asymmetric blade counts, and rotors-in-the-cargo-bay are
all natural with this design and impossible with the session-15 auto-
spawn. Next session fixes the three regressions on top of the new
shape rather than rolling back to one block / four hidden blades.
