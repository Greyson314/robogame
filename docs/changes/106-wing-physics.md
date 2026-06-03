# 106 — Wing physics: bigger wings weigh more and roll slower

> Status: **Shipped (code).** Pure physics/tuning change; no asset bake needed
> (the new `PlaneTuningConfig` reference-inertia fields default-initialise on
> load, so existing blueprint `.asset`s are behaviour-correct without rebaking).

## Why

Playtest finding: a plane's roll rate was identical no matter how big its wings
were. Two root causes (see the session-105→106 analysis):

1. **Roll was a fixed angular acceleration** — `PlaneControlSubsystem` applied
   `AddTorque(..., ForceMode.Acceleration)`, which ignores the inertia tensor
   by construction. Roll rate was purely `RollPower`/`RollDamping`, mass- and
   inertia-independent.
2. **Wing size never changed inertia anyway** — `Robot.RecalculateAggregates`
   used `Definition.Mass` (fixed per type) and treated every block as a
   `cellSize` cube in `ComputeDiagonalInertiaTensor`. Variant span/chord fed
   lift and the visual mesh, never mass or inertia.

The user's intuition (wider wing → more rotational inertia → slower roll) is
physically right; the sim modelled neither half. This change implements both.

## What landed

**B — aero mass + box inertia scale with dimensions (`Robot.cs`).**
- `EffectiveMass`: an aero block's mass scales with foil volume
  `span·thickness·chord` relative to the default foil, clamped [0.25, 6].
  Anchored so default dims resolve to `Definition.Mass` exactly — existing
  chassis are unchanged.
- `ComputeDiagonalInertiaTensor` now sums a per-block **solid-box** inertia from
  each block's chassis-frame half-extents (via `BlockOccupancy.ComputeSweptBoundsLocal`)
  instead of a fixed cube. A non-aero block is a `cellSize` cube, which reduces
  exactly to the historical `(1/6)·m·s²` self-term — **non-aero chassis are
  byte-identical**. Aero blocks contribute their real `span²/12`-style term plus
  the outboard parallel-axis offset, so `Izz` (roll) grows sharply with span.
  Scoped to aero only; hover/rope/rotor keep the cube.

**A — control authority scales with inertia (`PlaneControlSubsystem.cs`).**
Pitch/roll/yaw authority is multiplied by `clamp(refInertia / actualInertia,
0.3, 2.5)`, staying in `ForceMode.Acceleration`. At the reference inertia the
scale is 1 (authored feel preserved); a higher-inertia (bigger-winged) plane
gets a smaller share and rolls slower. Damping stays inertia-independent so
settling stays crisp. New `PlaneTuningConfig.{Pitch,Yaw,Roll}RefInertia`
(server-authoritative config, Bucket B; the dev-tuning sliders can override
live). Defaults are the measured `Blueprint_DefaultPlane` tensor under the new
model: pitch 105, yaw 128, roll 71 kg·m² — so the default plane is the neutral
baseline and is unchanged.

## Measured result (default plane, new model)

| Wings | Izz (roll) | Roll authority scale |
|---|---|---|
| default | 71 | ×0.99 (unchanged) |
| ×1.5 span, ×1.2 chord | 179 | ×0.40 |
| ×2 span, ×1.5 chord | 328 | ×0.30 (floor) |

So a double-span wing now rolls at ~30% of the default rate. Other shipped
planes shift relative to the baseline (e.g. the bomber's heavier wings roll
slower, the prop plane's narrower wings slightly faster) — the intended
consequence of inertia finally mattering.

## Files

Edited: `Robot/Robot.cs` (EffectiveMass + box inertia + bounds-centre COM),
`Block/BlueprintMovementConfig.cs` (3 reference-inertia fields),
`Movement/PlaneControlSubsystem.cs` (inertia-scaled authority + `AuthorityScale`).
New: `Tests/PlayMode/Movement/WingInertiaTests.cs`.

## Tests

PlayMode `WingInertiaTests`: a 3×-span wing raises both mass and inertia >1.5×;
default-dim wings keep authored mass (anchor holds); a two-cube chassis matches
the analytic `7/3` roll inertia (non-aero path unchanged).

## Invariant compliance

- **#1** authority + reference inertias are code/config constants (Bucket B),
  no gameplay Tweakable. **#6** `RecalculateAggregates` is build/damage-time, not
  per-frame; the box-inertia loop is allocation-free. **#7** the reference
  inertias were measured in-engine, not asserted.

## Follow-ups

- Hover blades have the same "size doesn't change mass/inertia" gap; left as-is
  (out of scope). Extending `EffectiveMass` + box inertia to hover is a clean
  future pass.
- Reference inertias are anchored on `DefaultPlane`; if that preset is redesigned
  the neutral baseline drifts. Re-measure if its wings change a lot.
