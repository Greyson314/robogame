# Session 21 — Helicopter frame stability: pure-axial rotor lift

> Status: **shipped, awaiting in-game verification.** The helicopter
> chassis was spinning about its own rotor axis at near-rotor speed
> (the "catapulting itself within seconds" report) because the lift
> from the four blades wasn't coplanar with the spin axis. Fix below.
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

## Verification steps for the user

1. Run `Robogame → Build Everything` (no scaffolder logic changed,
   but the EditMode tests will pick up the new playmode test in the
   PlayMode runner).
2. **PlayMode test runner**: run
   `RotorBlock_ChassisStaysSteadyAboutSpinAxis_UnderLoad`. Should
   pass with chassis yaw remaining near zero.
3. **In-game**: hit Play, switch to the Helicopter chassis, slide
   `Rotor.RPM` up. The frame should stay level while the foils
   spin; lift should pull the chassis straight up.

If the chassis still spins after the fix, the next suspect is
PhysX's per-step torque integration interacting with the foil
parent chain. Diagnostic step would be a per-FixedUpdate log of
the chassis angular velocity, which the existing diagnostic
infrastructure (the `[RotorBlock] adopted` logs) can be extended
with quickly.
