# Session 39 — Scalable parts Phase 1.5: lift scales with planform area

> Status: **shipped, untested in-engine.** Closes the "biggest wing
> for free" loophole the Phase 0 audit surfaced. Aerofoil lift now
> scales with `span × chord` so a 2× wing produces 2× lift; a 4× area
> wing produces 4× lift. Default chassis numbers are exactly preserved
> by normalising against `DefaultSpan × DefaultChord`. Sets the
> foundation for honest Phase 3 cost balancing — without this,
> larger-and-cheaper would have been a strict dominance trap.

## Why this session

Phase 0 audit ([37](37-scalable-parts-audit.md)) found that the lift
formula in `AeroSurfaceBlock.FixedUpdate` ignored per-instance dims:

```
liftMag = speedSqr × _liftCoef × liftFactor × sign(forward)
```

Visual mesh tracked dims; force did not. So players could dial a 5×
wing in the variant panel and get a 5× wing visually with default
lift. Phase 3's CPU-cost-by-volume / area math would have made this
worse — bigger wings would cost more without paying off.

## What changed

### Lift formula now multiplies by area scale

[`Movement/AeroSurfaceBlock.cs`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
gains a cached `_liftAreaScale` field:

```
_liftAreaScale = (span × chord) / (DefaultSpan × DefaultChord)
```

`RecomputeAreaScale` runs in `OnEnable` and on
`BlockBehaviour.DimsChanged`, so the cache is always in sync with the
foil's current dims. `FixedUpdate` multiplies it into the lift
magnitude. One extra multiply per foil per fixed step — no
allocation, no per-frame `ResolveDims` call.

Default dims (`Vector3.zero` → block defaults) give scale = 1 by
construction. Every shipped chassis preserves its historical lift
exactly. Confirmed by the new
`LiftForce_DefaultDims_MatchesExplicitDefaults` test.

### PHYSICS_PLAN §5 contract

§5's Aero row already anticipated this: "If any future PR couples
[Tweakables] to lift / drag / hit area, they MUST move to per-block
blueprint config first." `BlockBehaviour.Dims` *is* the per-block
blueprint config the contract calls for — it's blueprint data,
serialised, server-replicable, not a per-machine knob. Phase 1.5
satisfies the §5 contract by reading lift from Dims, not from the
`Aero.*` Tweakables. Those Tweakables remain cosmetic-only; the
visual mesh continues to read them through `ApplyOrientationToVisual`.

No PHYSICS_PLAN edit needed — the rule was right; this session
implements it.

## Tests

[`Tests/PlayMode/Movement/AeroSurfaceBlockTests.cs`](../../Assets/_Project/Tests/PlayMode/Movement/AeroSurfaceBlockTests.cs)
— the old `LiftForce_DoesNotChange_WhenBlockDimsChange` test
asserted the OPPOSITE of the new contract. Renamed and re-purposed:

- **`LiftForce_ScalesWithPlanformArea_WhenSpanChanges`** — measures
  the chassis velocity delta over a `WaitForFixedUpdate`, doubles
  the foil span via `SetDims`, samples again. Asserts the second
  delta is ~2× the first along the lift axis (5% tolerance).
- **`LiftForce_DefaultDims_MatchesExplicitDefaults`** — sanity
  check that `Dims = Vector3.zero` (implicit defaults) and
  explicit `(DefaultSpan, DefaultThickness, DefaultChord)` produce
  identical lift. Defends the baseline against drift in the
  default-resolution path.

## Numerical implications

- **Span sweep, default thickness/chord:**
  - `MinSpan = 0.30` → scale = 0.30. Min wing produces 30% baseline lift.
  - `MaxSpan = 3.00` → scale = 3.00. Max wing produces 3× baseline.
- **Span × chord both maxed (3.0 × 2.5):** scale = `(3 × 2.5) / (1 × 0.9)` ≈ **8.33×** baseline lift. Real but extreme; flagged as a Phase 3 cost-curve concern.
- **Helicopter rotor blades** are also AeroSurfaceBlocks. Default dims (= zero) on every shipped helicopter preset, so scale = 1 for every blade. Custom blade dims via the variant panel WILL scale rotor lift after this change — physically correct and what the player wants from "thicker blades = more lift".

## Notes for the next session

- **Drag and sideslip are not yet area-scaled.** The drag term in
  `FixedUpdate` still reads only `worldVel.sqrMagnitude × _dragCoef`,
  no chord/span factor. A bigger wing should drag more too. Easy
  fix (multiply by the same `_liftAreaScale`, or a separate
  `_dragAreaScale` if we want to break the chord/span asymmetry —
  drag scales more with frontal area than planform area). Not done
  here because it's tuning-affecting and Phase 3 cost balancing
  will want a coordinated pass on lift / drag / cost together.

- **Stall behaviour is dim-independent** — `_stallAoA`,
  `_postStallLift`, `_zeroLiftBias` are still serialized constants
  per AeroSurfaceBlock instance. A long-thin glider wing and a
  short-fat fighter wing should stall differently in real life;
  we don't model that. Acceptable for arcade.

- **Phase 2 is next per the plan** — wheels and thrusters get the
  same `Dims` treatment, with their own swept-bounds entries in
  `BlockOccupancy`. Wheels: radius / width. Thrusters: cross-section
  scales thrust.

- **Phase 1.b reminder** — select-and-modify UX for re-scaling an
  already-placed block is still deferred. Worth thinking about
  before Phase 2 lands so wheel-radius edits don't ship the same
  "next-placement only" gap.
