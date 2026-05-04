# Session 21 — Helicopter frame stability: pure-axial rotor lift + foil collision ignore-pair

> Status: **shipped, awaiting in-game verification.** Two coupled bug
> sources took out the helicopter's frame stability — both fixed in
> this session, both needed (either alone leaves the chassis spinning):
> 1. Lift was not coplanar with the spin axis (induced-drag yaw torque).
> 2. Foil host-cube colliders swept through chassis colliders at the
>    same y-level (collision-impulse yaw torque).
>
> Closes session 17's B2/B3 outstanding bugs.

## Diagnosis

Console output from a fresh helicopter spawn confirmed all four foils
adopted onto the main rotor at `(0, 1, 0)` (mechanism cell `(0, 2, 0)`).
That ruled out "no foils → no lift → unexplained spin" and pointed
at the lift force itself.

Walking the math for the +X blade at default 6° collective pitch:

- After `RotorBlock.AdoptAdjacentAerofoils` the foil's `transform.up`
  is rotated by 6° around its world-space radial axis. For the +X
  blade, world up `(0, 1, 0)` rotates to `(0, 0.995, 0.105)` —
  i.e. tilted +Z by ~10.5 cm.
- The blade moves in the spin tangent (-Z direction at +X position).
- `AeroSurfaceBlock.FixedUpdate` applies lift along
  `liftAxis = transform.up = (0, 0.995, 0.105)`.
- The +Z-tilted lift direction is *opposite* to the blade's motion
  (which is in -Z). That's induced drag.

For the symmetric -X blade, the same logic produces a -Z-tilted lift
opposite to its +Z motion. **Both blades' tangential lift components
sum to a yaw torque on the chassis at the rotor's full power.**
Symmetric pairs do NOT cancel — they reinforce — because both
blades' tangential components torque the chassis in the same
rotational direction (-ω about the spin axis = anti-torque).

This is real-world induced drag from lift production. Real
helicopters cancel it with a tail rotor / NOTAR / contra-rotation.
Per [`docs/PHYSICS_PLAN.md`](../PHYSICS_PLAN.md) §2 Robogame
explicitly does NOT model rotor reaction torque ("kinematic rotors
don't kick reaction torque into the airframe") — but the lift's
tangential component was leaking the same effect through the force
*direction*.

## Fix

`AeroSurfaceBlock.ConfigureRotorMode` now optionally accepts the
rotor's `Transform` and the spin axis in rotor-local space:

```csharp
public void ConfigureRotorMode(
    Rigidbody hub, Rigidbody chassis,
    Transform rotorTransform = null, Vector3 spinAxisLocal = default)
```

When both are supplied (the production path through `RotorBlock`),
`AeroSurfaceBlock.FixedUpdate` overrides the lift axis in rotor mode:

```csharp
if (_rotorMode && _rotorTransform != null)
{
    liftAxis = _rotorTransform.TransformDirection(_rotorSpinAxisLocal).normalized;
}
```

**What this preserves.** AoA is still driven by the foil's tilted
local frame and the velocity-vs-chord computation. Lift magnitude is
unchanged. Collective pitch still controls the lift response curve.

**What this changes.** The force direction is now coplanar with the
rotor's spin axis (in world frame, tracking chassis tilt). All four
blades' lift vectors are parallel. `Σ r_i × F_i` along the spin axis
sums to zero (each blade's contribution cancels with the opposite
blade in the pair). Chassis stays steady; the rotor pushes it
straight up the spin axis.

The plane wings (where `_rotorMode == false`) are unaffected —
they continue to use `transform.up` as before, so banked turns,
cyclic AoA, and the rest of the plane flight model are unchanged.

Old 2-arg `ConfigureRotorMode(hub, chassis)` callers (existing tests)
fall through to the legacy `transform.up` lift axis since the new
parameters default to null/zero. No test breakage.

## Files touched

- [Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
- [Assets/_Project/Scripts/Movement/RotorBlock.cs](../../Assets/_Project/Scripts/Movement/RotorBlock.cs) (one
  call-site update in `AdoptAdjacentAerofoils`)
- [Assets/_Project/Tests/PlayMode/Movement/RotorBlockTests.cs](../../Assets/_Project/Tests/PlayMode/Movement/RotorBlockTests.cs)
  — new test `RotorBlock_ChassisStaysSteadyAboutSpinAxis_UnderLoad`
  asserts |chassis angular velocity along spin axis| stays under
  1 rad/s after 30 fixed steps at 240 RPM. Without the fix, this
  test would balloon into double-digit rad/s within a fraction of
  a second.

## Follow-up — second torque source: foil-collider sweep

After the lift-direction fix shipped, the user reported the chassis
still spinning violently. Re-examining the geometry exposed a second
torque source that's collider-driven, not lift-driven:

- Each foil block has a 1 m × 1 m × 1 m cube collider from
  `BlockGrid.PlaceBlock` (the default Cube primitive).
- After `RotorBlock.AdoptAdjacentAerofoils` reparents foils under
  the kinematic hub, those colliders' effective Rigidbody is the
  hub (not the chassis). Foil colliders + chassis colliders are
  on different Rigidbodies, so they collide.
- The mechanism cube placed by `BlueprintBuilder.RotorWithFoils` at
  the cell ABOVE the rotor sits at the **same y-level** as the foil
  ring. The foils orbit at radius 1; their cube colliders trace an
  annulus from radius 0.5 to 1.5 around the spin axis. The
  mechanism cube's corners sit at radius ~0.7 — inside that
  annulus. As the hub rotates, every foil cube sweeps through the
  mechanism cube's volume continuously.
- PhysX resolves the contacts by pushing the chassis (the only
  dynamic body in the contact pair). On average the contact normal
  is along the foil's tangential motion direction, so each contact
  applies a tangential impulse to the chassis at the foil position
  — i.e. yaw torque about the spin axis at full rotor power.

### Fix

`RotorBlock.AdoptAdjacentAerofoils` now calls
`IgnoreFoilChassisContacts(aero, chassis)` after configuring rotor
mode. The helper walks every collider on the chassis hierarchy once
and `Physics.IgnoreCollision`-pairs each one with the foil's host
collider. Mirrors the `RopeTip.IgnoreChassisCollisions` pattern.

PhysX caches ignore-pairs internally, so the cost is paid once per
adoption (4 foils × ~38 chassis colliders ≈ 152 pairs per chassis)
and amortised across every contact query.

The foil-vs-world-geometry contact pair is unaffected — foils still
collide with the arena terrain and external chassis (future flail
weapon design once the rotor doubles as a damage tool).

### Test update

`RotorBlock_ChassisStaysSteadyAboutSpinAxis_UnderLoad` now places a
chassis-level cube at the foil ring level so the collision-sweep
path is exercised. Without the ignore-pair fix the test would balloon
the chassis yaw into double-digit rad/s within 30 fixed steps;
with both fixes the chassis stays steady.

## Verification steps for the user

1. Run `Robogame → Build Everything` (no scaffolder logic changed).
2. **PlayMode test runner**: run
   `RotorBlock_ChassisStaysSteadyAboutSpinAxis_UnderLoad`. Should
   pass with chassis yaw remaining near zero.
3. **In-game**: hit Play, switch to the Helicopter chassis, slide
   `Rotor.RPM` up. The frame should stay level while the foils
   spin; lift should pull the chassis straight up.
