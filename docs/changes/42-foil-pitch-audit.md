# Session 42 — Foil pitch: Phase 0 audit

> Status: **audit only, no code changes.** First step of the
> [Foil Rotation Plan](../FOIL_ROTATION_PLAN.md). Catalogues what's
> wired today, where pitch already lives implicitly, and locks in the
> storage shape for Phase 1.

## What's wired today

**Free wings have no pitch.** `AeroSurfaceBlock` has no per-instance
pitch / incidence parameter. AoA in
[`FixedUpdate`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs:285)
comes entirely from `Atan2(-crossVel, forward)` — i.e. the geometric
angle between the foil's chord and the local airflow vector. A flat
free wing at zero AoA produces only the `_zeroLiftBias = 0.12f` share
of full-AoA lift (the camber-bias term in
[line 306](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs:306)).
That's why a naïvely-mounted plane wing "flies, but barely."

**Adopted rotor blades have a hardcoded collective pitch.**
[`RotorBlock._collectivePitchDeg`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs:73)
is a `[SerializeField, Range(0f, 20f)]` with default `6f`.
`AdoptAdjacentAerofoils` at
[line 559](../../Assets/_Project/Scripts/Movement/RotorBlock.cs:559)
bakes it into the foil's `transform.rotation` via a
`Quaternion.AngleAxis(_collectivePitchDeg, radialWorld)` rotation
applied at adopt time:

```
Quaternion pitchRot = Quaternion.AngleAxis(_collectivePitchDeg, radialWorld);
aero.transform.rotation = pitchRot * worldRot;
```

So pitch IS expressed for rotor blades, just at the rotor level (one
collective for every adopted foil) and not at the per-foil level.
Stored on the rotor's component (not the blueprint), which makes it a
per-machine SerializeField rather than a blueprint config — a quiet
PHYSICS_PLAN §5 violation that nobody's noticed because there's no
per-rotor variation in shipped chassis.

**`BlueprintBuilder.RotorWithFoils` bakes no tilt.** It just places
the rotor + mechanism cube + 4 foil cells. All tilt magic is in
RotorBlock's adopt pass.

**`VariantConfigPanel` is a thin slider rig.** Today: foil section
shows span / thickness / chord sliders + 4 preset buttons. Rope
section shows segment count. No expander, no readouts, no preset
cards. Phase 3 would extend it materially — adding pitch slider is
trivial; the full preset-cards-and-readouts UI from Plan §3.4 is a
proper rebuild.

## Storage decision: separate `float Pitch` field on Entry

Plan §3.1 offered Vector4 Dims or configBlob. I'm going with a third
path: **add a separate `float Pitch` field to
`ChassisBlueprint.Entry`**. Smaller diff than Vector4 (no type-change
ripples through every Dims consumer), no new architectural concept.
Easy to fold into a real `configBlob` later without breaking saves.

Pitch is a single scalar, not a vector — Vector4 would be packing two
unrelated things (foil dims + foil pitch) into one struct purely for
"one mechanism" purity. The cost in code churn (~20 sites touch
Dims) outweighs the conceptual win.

For both foils and rotors, `Entry.Pitch` is the per-instance
pitch / collective in degrees. Default 0:
- Foil with Pitch = 0 → flat wing (current behaviour).
- Rotor with Pitch = 0 → fall back to `RotorBlock._collectivePitchDeg`
  SerializeField default (handles existing assets).
- Rotor with Pitch ≠ 0 → use Entry.Pitch as the collective.

## Implementation: additive AoA, not transform tilt

Plan §3.7 says the foil mesh visually rotates to match pitch. Two
ways to wire that to lift:

(A) Tilt the foil's `transform.rotation` by pitch. Existing AoA
math picks it up via `transform.InverseTransformDirection(worldVel)`.
This is what the rotor adopt pass currently does for collective.

(B) Tilt only the wing-mesh's `localRotation`. Add pitch as an
additive offset to AoA in `FixedUpdate`: `aoa = atan2(...) + pitchRad`.

I'm picking **(B)**. Reasons:
- Single source of truth: pitch lives on `_pitchDeg`, no risk of
  drift between transform rotation and stored pitch.
- Decouples visual from math — visual rotation can change
  independently (e.g. pitch animation, build-mode preview).
- `FixedUpdate` lift cost: one extra add (+ field cache). Trivial.
- For rotor mode where lift direction is overridden anyway, the
  baked-tilt approach was paying for nothing — just the AoA
  contribution mattered. Additive captures it directly.

Verified analytically: a free foil with pitch=8° and zero crossVel
produces AoA = pitchRad ≈ 0.14, lift factor ≈ 0.14 + 0.12 (bias) =
0.26. Same outcome whether pitch is baked into transform or added
to AoA, because forward (= localVel.z) is unchanged in both cases.
For rotor blades, identical result via the same math.

## v1 scope: skip the override toggle

Plan §3.3 proposed a per-foil `OverrideRotorCollective` bool —
asymmetric rotor builds for tinkerers. I'm deferring this. Reasons:
- 99% case is "rotor's collective wins for adopted blades." That's
  the symmetric-rotor invariant from Plan §5.5.
- Adds a serialized bool per Entry, more migration churn.
- No demand yet (no shipped chassis needs it).

Re-add later when a tinkerer asks for it. Documented in Phase 2
session log as a follow-up.

## Open answers (committing per Plan §6)

- **Vector4 vs configBlob.** Neither — separate `float Pitch`. See above.
- **Default pitch values from §3.5.** Use as-is. Tune from playtest.
- **Primary slider name.** "Pitch" — technically correct, builders
  will recognise it. Soften later if it confuses non-flight-sim
  players.
- **Negative pitch range.** Symmetric ±18°. Tail elevators need
  negative pitch out of the box.
- **Stall threshold.** 18° soft / 20° hard, per the existing
  `_stallAoA = 0.35 rad` calibration.
- **Sweep / dihedral.** Out of scope, per Plan §7.

## Phase 1 + 2 + 5 plan (next commit)

Bundling the data model, rotor wire-up, and visual feedback into
one commit since they're tightly coupled in the additive-AoA
approach. Stops before Phase 3 (UI rebuild) for a checkpoint.

1. `ChassisBlueprint.Entry`: add `float Pitch` field + constructor overloads.
2. `BlueprintSerializer`: schema v3 with `pitch` per entry; v1/v2 default to 0.
3. `BlockBehaviour`: store `_pitchDeg`, expose `Pitch` getter, `SetPitch(float)` mutator with `PitchChanged` event.
4. `BlockGrid.PlaceBlock`: thread pitch through to `Initialize`.
5. `AeroSurfaceBlock`:
   - Read pitch from BlockBehaviour at OnEnable + PitchChanged.
   - `FixedUpdate`: add pitch to AoA additively.
   - `ApplyOrientationToVisual`: tilt wing-mesh by pitch around chord axis (foil-local +Z).
6. `RotorBlock`:
   - Read collective from blueprint Entry.Pitch (fall back to SerializeField).
   - `AdoptAdjacentAerofoils`: write rotor.collective into adopted foils' pitch via `SetPitch`. Drop the `pitchRot` baking from line 559.
7. `BlueprintValidator`: warn if pitch > 18° or < -18°; error if out of [-20°, 20°].
8. Tests: foil at pitch=8° produces 8°/0.14rad of additive AoA; save/load round-trips pitch; rotor adopt-pass propagates collective to blades.
