# Foil Rotation (Pitch) — Plan & Handoff

> **Audience.** A future Claude Code session (or human) picking up the
> foil-pitch feature cold.
>
> **TL;DR.** Add per-foil pitch (angle of attack incidence) as a
> first-class parameter so plane wings, tail surfaces, and helicopter
> blades can each have appropriate built-in incidence without players
> needing to manually rotate mounted foils. Rotor-adopted foils inherit
> a "Collective Pitch" from their parent rotor by default; each foil
> can opt out via an override toggle for asymmetric builds. UI layered
> as preset cards + primary slider + Advanced expander + live
> consequence readout.
>
> **Status.** Phases 0–5 landed across sessions
> [42](changes/42-foil-pitch-audit.md) /
> [43](changes/43-foil-pitch-phase3-ui.md) /
> [44](changes/44-foil-pitch-phase4-and-fixes.md). Implementation is
> functional but **still needs work** — see § 10 Carry-forward. Per-blade
> override toggle, ghost-tilt preview, live mid-edit collective
> propagation, and per-rotor RPM/direction are all deferred. Companion
> to [`SCALABLE_PARTS_PLAN.md`](SCALABLE_PARTS_PLAN.md) — pitch is
> structurally another per-instance scalar on the foil, same shape as
> span / chord / thickness.

---

## 1. Why this exists

Today's `AeroSurfaceBlock` has no per-instance pitch parameter. The
foil's "angle of attack at zero airspeed" is implicit in its mounting
transform — a blade that's been adopted by a rotor takes whatever
local rotation the adopt pass baked in. There's no way for a player
to express "I want this wing tilted 2° up" or "this tail elevator
should have −1° downforce incidence" without hand-editing the
blueprint.

This is a real expression gap for the tinkerer audience and a real
build-correctness gap for new players. A symmetric foil at zero AoA
on a rotor produces only the camber-bias share of full lift (12% in
current code). New players who build a helicopter with naïvely-mounted
flat blades will find that their heli "flies, but barely" with no
in-game guidance about why.

The fix is to make pitch a real per-instance parameter, default it to
sensible values per foil role, and surface its consequence in the
build-mode UI.

## 2. Physics context

### 2.1 What the current code actually does

`AeroSurfaceBlock.FixedUpdate` computes lift as roughly:

```
liftMag = speed² × liftCoef × areaScale × (aoa + zeroLiftBias) × stallFalloff × sign(forward)
```

with these constants:

- `_liftCoef = 0.95f` (lift slope per radian of AoA × speed²)
- `_zeroLiftBias = 0.12f` (camber bias — lift at zero AoA as a
  fraction of the AoA term at 1 rad)
- `_stallAoA = 0.35f rad ≈ 20°` (hard stall angle)
- `_postStallLift = 0.55f` (lift retained past stall)

`aoa` is computed as `Atan2(-crossVel, forward)` in foil-local space —
i.e. the geometric angle between the foil's chord and the local
airflow. Today, that AoA depends entirely on the foil's mounting
transform; there's no per-instance offset.

### 2.2 Why a heli with zero foil angle still flies (badly)

Because of `_zeroLiftBias = 0.12f`. A flat-pitched symmetric foil at
zero AoA produces 12% of the lift it would at one radian of AoA.
Enough to limp into the air, far from enough to fly well. This is the
arcade-friendliness baked into the model — the wrong configuration is
*bad*, not *broken*.

When pitch ships, this bias should stay (it's a foil-shape property,
not a player-tunable). Pitch is an *additional* AoA offset that adds
to whatever the airflow geometry produces.

### 2.3 The four kinds of "rotation"

For clarity, "foil rotation" can mean any of:

1. **Mounting orientation** — which face of the host the foil sticks
   out from. Already shipped via `Entry.Up` (Vector3Int).
2. **Pitch / incidence** — angle around the spanwise axis. Drives AoA
   at any given airspeed. **This plan adds pitch.**
3. **Sweep / yaw** — angle around the lift axis. Affects lateral
   stability for fixed-wing. Out of scope for v1.
4. **Dihedral** — angle around the longitudinal axis. Roll stability
   for fixed-wing. Out of scope for v1.

Pitch is the load-bearing one. The other three are nice-to-haves.

## 3. Decisions committed

These are settled. Future sessions should not re-litigate without
explicit user approval.

### 3.1 Pitch is a per-foil parameter

Pitch lives on the foil's per-instance config, alongside span /
thickness / chord. Implementation: extend `ChassisBlueprint.Entry.Dims`
from `Vector3` to `Vector4` and reserve the fourth component for
pitch in degrees. Or — if the `configBlob` generalisation from
`PHYSICS_PLAN.md` §6 lands first — pitch joins the typed foil-config
payload there. Either is acceptable; pick whichever is closer to
landing.

Pitch is a per-foil property because non-rotor foils (plane wings,
tail elevators, fins) need it too, and they have no parent structure
to inherit from. Putting pitch on the rotor only would force fixed-wing
builds to fake pitch via mounting tilt, which is exactly the failure
mode this plan exists to fix.

### 3.2 Rotors get a "Collective Pitch" that overrides adopted foils by default

The `RotorBlock` gains a `CollectivePitch` parameter (degrees). When a
foil is adopted by a rotor in `AdoptAdjacentAerofoils`, the foil's
effective pitch defaults to the rotor's collective rather than the
foil's own stored pitch.

This matches the real-helicopter mental model (one collective lever,
all blades follow) and enforces rotor-disc symmetry by default —
asymmetric blade pitch breaks the rotor-disc-balance assumption that
lets us skip cyclic and reaction-torque modelling.

### 3.3 Each foil has an "Override rotor collective" toggle, default off

A per-foil bool: when off, an adopted foil reads pitch from the parent
rotor's collective. When on, the foil uses its own stored pitch
independent of the rotor.

This is the depth-opt-in for tinkerers who want asymmetric rotors
(e.g. cyclic-pitch approximations, stylised swashplates). Default-off
keeps 99% of players inside the symmetric-rotor happy path.

### 3.4 UI structure: preset cards + primary slider + Advanced expander + live readout

Variant panels (foil and rotor) follow the same four-zone shape as
race-game tuning panels:

1. **Header** — block name + small live 3D thumbnail of the foil.
2. **Preset cards** — 3–5 named buttons that snap dims and pitch to
   sensible defaults for that role. For the foil panel: "Heli Blade",
   "Plane Main Wing", "Tail Stabilizer", "Vertical Fin". For the
   rotor panel: "Heavy Lift", "Standard", "Light".
3. **Primary slider** — single dominant slider that maps to the
   parameter most likely to be tuned. For the foil panel: pitch (or
   "Lift power" if we want to obscure the term). For the rotor
   panel: collective pitch.
4. **Advanced expander** — explicit sliders for Span, Chord,
   Thickness, Pitch (foil) or RPM, Direction (rotor), plus the
   override toggle (foil only).
5. **Live consequence readout** — text like
   `Estimated lift: 124 N at 240 RPM` or
   `Stall margin: 6° before lift drops`. Updates as sliders move.

The preset row serves the 90% case. Most players pick a preset and
never touch the rest. Power players reach the deep end by opening
the Advanced expander.

### 3.5 Default pitch values per foil role

| Role | Default pitch | Rationale |
|---|---|---|
| Helicopter blade | +8° | Real helis: 6–10° collective at hover |
| Plane main wing | +2° | Standard wing incidence + camber bias |
| Tail elevator / horizontal stabiliser | −1° | Slight downforce for pitch stability |
| Vertical fin / rudder | 0° | Symmetric; controlled by yaw input |
| Default rotor collective | +8° | So a rotor with default-config foils flies |

These are starting points, not destinations. Playtest will sharpen.

### 3.6 Stall handling at the UI

The pitch slider's max is +18° / −18° (soft limit, inside the code's
20° hard stall). Past 18° the readout flashes red with text like
`STALL — lift dropping`. The slider doesn't snap or disable past the
soft limit — players can push past for stunt builds — but the warning
is unambiguous.

### 3.7 Build-mode visual feedback

The foil mesh visually rotates to match its current pitch. A tilted
blade looks tilted. This is how the player reads the parameter
without numbers. Cheap to implement (a single localRotation on
`_wingMesh`) and a permanent quality-of-life win.

## 4. Phased work plan

Each phase is independently shippable. Each ends with a session
entry under `docs/changes/NN-slug.md`.

### Phase 0 — Audit ✓ landed (session 42)

Confirm the current state. Specifically:

- How is pitch currently expressed for adopted rotor foils? Read
  `RotorBlock.AdoptAdjacentAerofoils` and `ConfigureRotorMode`.
- What does `BlueprintBuilder.RotorWithFoils` produce? Is there
  a hard-coded tilt today?
- Is `VariantConfigPanel` currently capable of rendering pitch
  controls, or does it need a structural extension?
- Spec the smaller of the two implementation paths from §3.1
  (Vector4 Dims vs. configBlob). Recommend Vector4 if configBlob
  is more than a sprint away.

**Exit criterion:** a written audit at
`docs/changes/NN-foil-pitch-audit.md` listing what's wired today,
what default-tilt magic exists, and the chosen storage path.

### Phase 1 — Per-foil pitch in the data model ✓ landed (session 42)

> Storage chose option (c) from §3.1: a separate `float Pitch` field
> on `ChassisBlueprint.Entry` rather than extending `Dims` to
> `Vector4`. Smaller diff (~no Dims-consumer churn) and trivial to
> fold into a real `configBlob` later. See session 42 audit for the
> reasoning. Schema bumped to v3 with `pitch` per entry; v1/v2 saves
> load with pitch=0.

- Extend `Entry.Dims` to `Vector4` (or land configBlob foil payload).
- Update `ChassisBlueprint.Entry` constructors and the comment spec.
- Update `BlueprintSerializer` for the new field with a versioned
  loader for old blueprints (default pitch from foil role by block
  ID).
- `AeroSurfaceBlock.Configure` reads pitch and applies it as a local
  rotation around the chord axis. Verify with EditMode test that the
  foil's transform.up tilts by the expected amount.
- `BlueprintValidator` validates pitch within ±18° (configurable).

**Exit criterion:** a foil placed in a unit test with pitch=8°
produces 8° of effective AoA at zero airspeed in `FixedUpdate`'s
local-frame math. Save/load round-trips pitch.

### Phase 2 — Rotor collective + override toggle ⚠ partial (session 42)

> Rotor collective lands; **override toggle deferred**. Adopted foils
> always read from the rotor's collective in v1 (symmetric-rotor
> invariant). Re-add the per-foil `OverrideRotorCollective` bool when
> a tinkerer asks for asymmetric rotors. RotorBlock's adopt-pass
> writes `EffectiveCollectivePitchDeg` onto each blade via
> `BlockBehaviour.SetPitch`; the prior radial-axis transform-rotation
> baking is gone.

- Add `RotorBlock.CollectivePitch` field (per-instance, lives on the
  rotor's blueprint entry; same Vector4-or-configBlob mechanism).
- Add a per-foil `OverrideRotorCollective` bool. Same storage.
- `RotorBlock.AdoptAdjacentAerofoils` writes effective pitch onto
  each adopted foil at adopt time: `effective = override ? foil.pitch : rotor.collective`.
- PlayMode test: a rotor with two foils, one with override on, the
  other off — confirm the off-foil tracks rotor collective when the
  rotor's collective is changed at runtime.

**Exit criterion:** changing a rotor's collective slider visibly
re-tilts every adopted foil except those with override on. Asymmetric
rotor builds work in a sandbox arena.

### Phase 3 — Build-mode UI: presets + primary slider + Advanced ⚠ partial (sessions 43, 44)

> Foil panel: 4 preset cards, three primary dim sliders (span /
> thickness / chord), Advanced expander with the pitch slider. Rotor
> panel: 3 preset cards + collective slider. Rope panel unchanged.
>
> **Per user feedback** (session 44): pitch is in Advanced; dims are
> primary. The original plan §3.4 had pitch as primary — flipped after
> testing showed players reach for dims first.
>
> **Deferred**: per-rotor RPM / direction sliders (need data-model
> fields first); per-blade override toggle on the foil's Advanced
> expander; modifier-held continuous mode (Shift) for fine-grained
> control; 3D foil thumbnail in the panel header.

This is the largest phase by effort. Structure per §3.4.

- Preset cards on the foil panel: 4 presets that write
  (span, chord, thickness, pitch) tuples.
- Preset cards on the rotor panel: 3 presets that write
  (collective, RPM) tuples.
- Primary slider on each (foil = pitch, rotor = collective).
- Advanced expander with explicit sliders for the rest.
- Snap increments: 0.25 m for length dims, 1° for angles.
- Modifier-held continuous mode (Shift?) for fine-grained control.
- Override toggle on the foil's Advanced expander, hidden when the
  foil isn't adopted by a rotor.

**Exit criterion:** a player can build a heli by selecting "Heli
Blade" preset on every foil + "Standard" preset on the rotor and
have it fly competently with zero numeric tuning. The same player
can open Advanced and tune to taste.

### Phase 4 — Live consequence readout + stall warning ✓ baseline landed (session 44)

> `EstimateFoilLift` mirrors `AeroSurfaceBlock.FixedUpdate`'s lift
> formula at a reference airspeed (30 m/s for free wings; ω×r at
> 240 RPM and 1 m radius for rotor blades). Foil readout:
> `≈ 124 N @ 30 m/s`. Rotor readout: `≈ 332 N disc (4 default
> blades, 240 RPM)`. Both update on slider change and section
> switch. Past ±18° pitch the foil readout turns red and appends
> `— STALL`.
>
> **Caveats**: the rotor disc readout assumes 4 default-dim blades.
> A player-built rotor with bigger blades will lift more than the
> readout suggests — intentional underestimate. Per-build calculation
> needs the live chassis (rotor's actual blade dims, RPM, etc.) which
> is more plumbing than the v1 readout warrants.

- Foil panel readout: estimated lift at the rotor's RPM (if adopted)
  or at "cruise speed" (if free wing). Cruise speed comes from the
  chassis kind — plane: 30 m/s, ground: N/A (suppress readout),
  helicopter: 0 m/s level hover.
- Rotor panel readout: total disc lift = sum of adopted foils'
  expected lift at the current RPM and collective.
- Stall warning: pitch slider readout flashes red past 18°.
- All readouts update live as sliders move.

**Exit criterion:** a player adjusting the collective slider sees
the disc-lift readout change in real-time and can intuit "okay,
that's enough lift to fly this 200 kg chassis."

### Phase 5 — Visual feedback polish ⚠ partial (bundled with Phase 1)

> Foil mesh applies pitch as a `localRotation` around its chord axis
> (foil-local +Z). Tilt is visible at rest and during rotor spin.
> Bundled into the Phase 1 commit because the math and visual share
> the same `BlockBehaviour.PitchDeg` source.
>
> **Deferred**: build-mode "spin-test" button on the rotor panel
> (briefly rotates the rotor in the garage at the configured RPM
> with no airflow); pitch tilt on the build-mode ghost preview
> (currently the ghost shows the foil flat — placed block tilts).

- Foil mesh applies pitch as a localRotation around its chord axis.
- Rotor's adopt pass propagates collective into the visual rotation
  too, so build-mode shows the geometry the simulator will use.
- Build-mode "spin-test" button on the rotor panel that briefly
  rotates the rotor in the garage at the configured RPM, no airflow,
  so the player sees the disc geometry in motion.

**Exit criterion:** the foil's tilt is visible at rest and during
spin-test. No mismatch between build-mode visuals and arena
behaviour.

## 5. Invariants to respect

Carried over from `CLAUDE.md`, `PHYSICS_PLAN.md`,
`NETCODE_PLAN.md`, and `SCALABLE_PARTS_PLAN.md`.

1. **Pitch affects gameplay outcomes (lift, stall, control authority),
   so it lives in per-block blueprint config, NOT in `Tweakables`.**
   The build-mode slider writes to `Entry.Dims` (or the configBlob)
   on the in-progress blueprint. Saved blueprints carry pitch through
   reload and over the wire when netcode lands.
2. **Building only in the garage.** Pitch is set in build mode and
   frozen at match start. No mid-arena pitch adjustments.
3. **`_zeroLiftBias` is a foil-shape property, not player-tunable.**
   Symmetric (vertical fin) foils get 0 bias; cambered (horizontal
   wing / blade) foils get the existing 0.12. Block ID picks; player
   can't override.
4. **Single Rigidbody per chassis.** Pitch tilts a foil's local
   transform; it does not introduce new Rigidbodies or joints.
5. **Symmetric rotor-disc assumption.** The rotor mode in
   `AeroSurfaceBlock.FixedUpdate` skips drag and sideslip "because a
   symmetric blade ring would otherwise apply equal-and-opposite
   tangential drags." Asymmetric pitch (override toggle on) breaks
   that assumption. Phase 2 must not regress symmetric behaviour;
   asymmetric is opt-in and accepts the consequences.
6. **No per-frame allocations.** Pitch state is cached on the
   `AeroSurfaceBlock` like the other dims. The `FixedUpdate` lift
   path stays allocation-free.
7. **MP debt awareness.** Pitch is a blueprint-config value. When
   netcode lands, it rides the same `BlueprintBlob` as span / chord
   / thickness. No additional networking work specific to pitch.

## 6. Open questions — answered

Decisions committed during the implementation arc; recorded here so
they don't get re-litigated.

- **Vector4 Dims vs. configBlob → neither.** Picked option (c): a
  separate `float Pitch` field on `ChassisBlueprint.Entry`. Smaller
  diff than Vector4 (zero ripple through Dims consumers); easy to
  fold into a real `configBlob` later. See session 42 audit doc for
  the reasoning.
- **Default pitch values from §3.5.** Used as-is. Heli blade +8°,
  plane wing +2°, tail stab −1°, vert fin 0°, rotor collective +8°
  (raised from prior 6° for the SO default). Tuning pass against
  the shipped helicopter / plane is **still needed** — flagged in
  § 10.
- **Naming the primary slider → "Pitch".** Technically correct;
  builders recognise it. Soften later if it confuses non-flight-sim
  players.
- **Negative pitch slider behaviour → symmetric ±18°.** Tail
  elevators need −1° out of the box; the "Tail Stab" preset writes
  it. Slider clamps to ±18° soft / ±20° hard.
- **Stall warning threshold → 18° soft / 20° hard.** Hardcoded as
  `BlueprintValidator.PitchSoftLimitDeg` / `PitchHardLimitDeg`. If
  `AeroSurfaceBlock._stallAoA` changes, update both constants in
  step.
- **Sweep / dihedral → out of scope.** Confirmed.

## 7. Non-goals

- **Cyclic pitch.** Real helicopters use cyclic (per-blade
  rotation-dependent pitch) for lateral control. We explicitly don't
  model this; lateral control comes from chassis tilt instead. The
  rotor mode docstring already says "we explicitly don't model
  reaction torque." Cyclic stays out.
- **Sweep / dihedral / twist.** §2.3 items 3 and 4 are not in scope.
- **Player-tunable camber.** `_zeroLiftBias` stays a foil-shape
  property.
- **Live in-arena pitch adjustments.** Garage-only.
- **Auto-tune button.** Tempting, but a "compute optimal pitch for
  this build" feature has design weight (what's the optimisation
  target — hover lift? max climb rate? cruise efficiency?) and is
  better deferred until preset coverage isn't enough.

## 8. References

- `Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs` — the lift
  formula. Constants at lines 53–62. AoA computation at line 315.
  Rotor-mode handling at `ConfigureRotorMode` (line 219) and
  `FixedUpdate` (line 254).
- `Assets/_Project/Scripts/Movement/RotorBlock.cs` — adopt-pass and
  hub kinematics. Phase 2 work lives here.
- `Assets/_Project/Scripts/Block/ChassisBlueprint.cs` — `Entry.Dims`
  (lines 58–64). Vector4 extension lands here in Phase 1.
- `Assets/_Project/Scripts/Block/BlueprintSerializer.cs` —
  versioned loader for the new field.
- `Assets/_Project/Scripts/Block/BlueprintValidator.cs` — pitch
  range validation.
- `Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs` — Phase
  3 UI surface.
- `docs/PHYSICS_PLAN.md` §5 — Tweakables-vs-blueprint contract.
  Pitch is gameplay-affecting, lives on the blueprint.
- `docs/PHYSICS_PLAN.md` §6 — `configBlob` generalisation roadmap.
- `docs/SCALABLE_PARTS_PLAN.md` — companion doc. Pitch is structurally
  another scalable-parts dim; the Phase 1 work overlaps with the
  Vector4-Dims extension proposed there.
- `docs/GAME_DESIGN_PILLARS.md` §"Generic propulsion primitives,
  no special-case archetype blocks" — the pillar this operationalises.
- `docs/NETCODE_PLAN.md` §6 Bucket B — blueprint replication. Pitch
  rides the existing blueprint blob.

## 9. How to start

The original phase-0 → phase-5 plan landed across sessions
[42](changes/42-foil-pitch-audit.md), [43](changes/43-foil-pitch-phase3-ui.md),
and [44](changes/44-foil-pitch-phase4-and-fixes.md). For NEW work on
this feature, see § 10 Carry-forward.

## 10. Carry-forward — what's still needed

Ordered roughly by impact / effort. Pick whatever's biting playtest.

### A. Live mid-edit collective propagation
**Why it bites.** Tuning a rotor's collective slider in build mode
only affects the next rotor placement; existing adopted blades keep
their old collective until the player re-places the rotor. Confusing
UX — the slider feels like it's not doing anything for already-built
helicopters.
**Sketch.** Hook `BlockBehaviour.PitchChanged` on the rotor; on each
change, walk `RotorBlock._adoptedFoils` and `SetPitch(newCollective)`
on each blade's `BlockBehaviour`. AeroSurfaceBlock subscribes to
`PitchChanged` already → blades re-cache `_pitchRad` and re-tilt
their wing mesh automatically.

### B. "Select existing block + retune" UX (Phase 1.b carry)
**Why it bites.** Variant panel only writes the next-placement
cache. To re-tune an existing wing, the player has to right-click
remove + re-place. Persistent carry from session 38 (scalable parts).
**Sketch.** Build-mode click-mode toggle: select-mode highlights an
existing block, populates the variant panel from it, sliders write
back to that BlockBehaviour via SetDims / SetPitch. Most of the
machinery is already there (`SetDims` and `SetPitch` exist; the
gap is the UX flow + a per-block "selected" highlight).

### C. Pitch ghost preview tilt
**Why it bites.** Build-mode ghost shows the wing flat; placed block
tilts. Visual disconnect grows with pitch magnitude.
**Sketch.** One-line fix in `BlockGhostFactory.BuildWing`: take a
`pitchDeg` param and apply the same `localRotation` around foil-local
+Z that `AeroSurfaceBlock.ApplyOrientationToVisual` does. Thread
the pitch from `BlockEditor.EnsureGhost` (it already knows the
candidate pitch via `_variantPanel.GetPitchForBlock`).

### D. Per-blade override toggle (deferred from Phase 2)
**Why it bites.** Tinkerers can't build asymmetric rotors —
swashplate sims, stylised builds where one blade has a different
pitch. The plan §3.3 anticipated this but I dropped it for v1.
**Sketch.** Add `bool OverrideRotorCollective` to
`ChassisBlueprint.Entry` (or a typed configBlob). Default false.
RotorBlock.AdoptAdjacentAerofoils calls SetPitch only when override
is false. Foil's Advanced expander gets a checkbox, hidden when the
foil isn't adopted by a rotor. Plan §5.5 invariant becomes
"asymmetric pitch is opt-in and accepts the consequences."

### E. Per-rotor RPM / direction
**Why it bites.** Plan §3.4 mentions both in the rotor's Advanced
expander; today both fall back to `Tweakables.RotorRpm` (a global)
and `RotorBlock._spinAxisLocal` (a SerializeField). No per-instance
storage means two rotors on the same chassis can't have different
RPMs.
**Sketch.** Audit needed — RPM-per-rotor is a substantive data-model
extension and worth thinking about netcode (RPM is gameplay-affecting,
must live on the blueprint per PHYSICS_PLAN §5). Probably a new
`Vector3 RotorConfig` field on Entry that stuffs (rpm, axisX, axisZ)
or similar; or extend the configBlob path that this plan + the
scalable-parts plan have both been deferring.

### F. Tuning pass against shipped chassis
**Why it bites.** The default pitch values from §3.5 are "sensible
starting points" — no one has flown a heli with the post-Phase-1
adopt-pass + +8° collective default and confirmed it hovers
properly. Same for the new-paradigm plane with span-4 main wings
at +0° pitch.
**Sketch.** In-engine playtest pass. If the heli barely climbs,
bump the rotor's authored collective (or add small per-blade
pitch). If the plane noses down, add +1° to the main wing presets.
Likely needs only one or two iterations.

### G. Modifier-held continuous mode (Shift)
**Why it bites.** Sliders snap (1° pitch, 0.25 m length). Power
users can't tune to 0.5° or 0.13 m. Low priority.
**Sketch.** In each slider callback, check
`Keyboard.current.shiftKey.isPressed`; if held, skip the snap. ~10
lines per slider.

### H. Build-mode "spin-test" button on the rotor panel
**Why it bites.** Player tunes collective in build mode, but doesn't
see the disc actually spin. Plan §5 mentioned a spin-test button.
Low priority — the lift readout is most of the answer.
**Sketch.** Button in the rotor panel's Advanced section;
on-press, briefly enable `RotorBlock.BuildLiftRig` for a few
seconds with no airflow, then unwind.

### I. Validator pitch check coverage in shipped presets
**Why it bites.** Soft / hard limits are checked at validate time;
the test suite covers them in isolation. Worth a quick audit that
all shipped blueprints (Plane / Bomber / PropPlane / Helicopter /
etc.) have pitch values within range. Currently they're all 0
(default), so vacuously fine — but if anyone authors a chassis
with custom pitch, double-check the validator catches out-of-range.

---

*Last updated: 2026-05-09 (handoff after sessions 42-44). Update
this file when phases land — it's the source of truth for the
feature, not a write-once doc.*
