# 168 — Lift acts at the foil's geometric centre

Follow-up to [167](167-foil-control-surfaces.md). User question "is the
span-to-roll ratio hard-coded?" surfaced the one non-physical shortcut
left in the foil model: every aerodynamic force was applied at the
foil's **mount cell**, so a long wing gained area and inertia but no
roll leverage or tip damping from its own length. User call: apply lift
at the geometric centre.

## What shipped

- `AeroSurfaceBlock` caches `_aeroCenterShift` (the `ComputeWingShift`
  outward magnitude, `(span−1)/2` along foil-local +Y, recomputed with
  the area scale on OnEnable + DimsChanged). `FixedUpdate` samples
  airflow at and applies lift / drag / sideslip at that point for free
  foils. Rotor blades keep the hub-adjacent mount point — their ω×r
  velocity sampling and disc symmetry are tuned to it, and shifting it
  would retune every helicopter for no player-visible gain.
- The control-deflection `r` (AeroControl) now also measures from the
  centre, so a wide wing's moment axis leans further toward roll.
- `FoilDefaults.ControlThrowDeg` 4° → 8°. Roll **damping** at the
  centre scales with arm² while roll **authority** scales with arm, so
  4° left the stock plane at a third of the 1.5 / 2 rad/s pitch/roll
  target. Throw scales commanded deflection only — hands-off trim
  probes are byte-identical across the bump.
- No preset/layout changes; no serialization changes.

## Measured (baked presets, W held, spawn 250 m / 14 m/s, pv11–pv12)

| probe | hands-off | full Space | full A/D |
|---|---|---|---|
| 167 shipped (mount-cell, 4°) | nose-up drift −0.14 | −0.4 → −0.8 | −2.2 … −2.6 |
| centre, 4° (pv11) | −0.15 (unchanged) | −0.31 → −0.66 | −0.6 … −1.0 |
| **centre, 8° (pv12 = shipped)** | **−0.15 (unchanged)** | **−0.4 → −1.2 (loops in ~5 s held)** | **−0.5 → −2.0** |
| Prop Plane, centre 8° | −20° descent (unchanged) | −0.3 (unchanged) | −0.6 → −2.4 |

Span-to-roll now emerges end-to-end: lift ∝ span (area), lever ∝
mount + (span−1)/2, damping ∝ arm², inertia ∝ span-mass × span². No
constant encodes the ratio; the shared 8° throw is the only tunable.

## Verification

New PlayMode test `WiderWing_GainsRollLever_NotJustArea`: one physics
step from rest, 4× span wing must out-torque 1× by ~area × lever
(asserts 6–14×; mount-cell regression would read ~4×). Headless rig
green at 4° (EditMode 526/527, PlayMode 141/142, 0 failed — the +1 is
the new test) and re-run at the shipped 8°. Console clean. No new
physics objects, no per-frame allocs (one TransformPoint per free foil
per tick replaces a position read).

## Files

`Movement/AeroSurfaceBlock.cs` (force point), `Block/FoilDefaults.cs`
(throw 8°), `Tests/PlayMode/Movement/FoilControlTests.cs` (ratio test +
`BuildBareWingBot`), `docs/subsystems/physics.md` §2.2 (also fixed a
stale 10° claim).

## Open

Same list as 167 (ground/hover migration, rotor cyclic, scheme
dropdown, per-foil throw slider). New: rotor blades still sample ω×r at
the mount cell — long blades under-report tip speed; revisit if rotor
lift ever needs span fidelity.
