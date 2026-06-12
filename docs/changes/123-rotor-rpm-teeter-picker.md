# 123 — Rotor RPM restored (+CPU pricing), foil teeter axis, middle-click picker

## Why

Rotors produced no usable lift: the per-block config migration (P4) removed
the `Rotor.RPM` Tweakable but deferred the replacement slider, so nothing
ever wrote a rotor's `ConfigValue` and every rotor ran at the 60 RPM
fallback — ~6% of the lift the variant panel's readout advertised at its
assumed 240 RPM. Separately, foil pitch physics (AoA offset = span-axis
feathering) and pitch visuals (chord-axis teeter) were on different axes,
and the tilt rotated about the outward-shifted mesh center so high angles
visually detached the wing root from its host. Garage sky rotation was
also making the user motion-sick.

## What changed

**Garage sky** — `GarageAmbience.SkyDegPerSec` 0.25 → 0 (stars hold still);
asteroid orbit 0.15 → 0.4 °/s so the sky still reads alive via parallax.

**Rotor RPM** (`RotorDefaults` is new, schema-side per the FoilDefaults
precedent):

- `RotorDefaults.DefaultRpm = 240` (config 0 = default). Stock heli + prop
  plane fly again with no blueprint change.
- Variant panel rotor section gains an RPM slider (30–600, 10-steps,
  default 240) writing `SetVariantConfig`; presets now carry RPM
  (Heavy Lift 12°/360, Standard 8°/240, Light 5°/150).
- Rotor readout uses the dialed RPM (it was hardcoded to 240 while rotors
  ran at 60 — overstating lift ~16×) and shows the live CPU price.
- **CPU scales with (RPM/240)²** — lift goes with tip-speed², so
  lift-per-CPU stays constant (600 RPM ≈ 6.25× sticker; floor 1; authored-
  free rotors stay free per INV-5). Routed through
  `CpuBudget.EffectiveCpuCost` (new shared core + a live-`BlockBehaviour`
  overload), so the garage spend bar, hotbar stats, and spawn-time
  `TrimToFit` all agree. The live HUD paths previously ignored concoction
  surcharges too — fixed by the same routing.
- `BlockEditor.PropagateVariantToLiveBlocks` also pushes `ConfigValue`, so
  RPM changes apply to placed rotors live (audio pitch tracks it).

**Foil second axis + pivot** (blueprint schema v9):

- `PitchDeg`'s visual now matches its physics: feathering about the SPAN
  axis (foil-local +Y; rotor blades ±X). Physics unchanged — every saved
  blueprint flies identically; pitched foils just *look* feathered now.
  Rotor collective finally looks like blade feathering.
- New per-block `TeeterDeg` (`Entry.Teeter`, serializer v9, absent = 0):
  the old chord-axis teeter-totter as its own channel. **Visual-only in
  v1** — see Known unknowns. Slider ±45° in the foil Advanced section;
  world-intent normalized per side with the same `NormalizePitchForUp`
  rule (it was originally derived for exactly this rotation). Rotor
  adoption does NOT override blade teeter (it's the player's coning
  angle); collective keeps overriding pitch.
- Pivot fix: `AeroSurfaceBlock.ComputeWingPose` (new, shared with
  `BlockGhostFactory.BuildWing` so ghost = placed mesh structurally)
  anchors tilt rotations at the attachment face — root face center,
  invariant in span — so no angle detaches the root ("attaches to air").
- Drive-by fixes: live slider propagation now normalizes world-intent →
  local per block mount (it previously pushed the raw world value,
  flipping signs on lateral foils vs. what placement wrote); RepairPad
  block regen now restores `ConfigValue` / `ConcoctionId` / `Teeter`
  (previously dropped all per-instance config except dims/pitch).

**Middle-click picker** — middle-click on any placed block in build mode
selects its type in the hotbar (`BuildHotbar.SelectByBlockId`) and loads
that instance's dims / pitch / teeter / config into the session caches
(local → world-intent inverted via the involutive normalize), refreshing
the panel (`VariantConfigPanel.RefreshForBlock`). Non-hotbar blocks (auto
mechanism cube) decline with the invalid-placement cue; success plays
UiClick.

## Tests

- `RotorBlockTests.RotorBlock_BuildLiftRig_AdoptsFourLateralAerofoils…` now
  pins `RpmOverride = 0` — it measures reparent displacement, and at 240
  RPM the old form legitimately swept ~0.5 m of orbital motion in one
  fixed step.
- New (test-drafter): `RotorDefaultsTests` + `RotorCpuBudgetIntegrationTests`
  (pricing curve, INV-5 zero-baseline, TrimToFit ordering),
  `BlockOrientationPickerTests` (normalization involution = picker
  round-trip).

## Known unknowns / deferred

- **Teeter physics** (user-flagged for later): wiring teeter into lift
  geometry = dihedral roll stability for planes, coning for rotors.
  Touches plane roll behaviour; needs its own tested pass.
- Feathering visual signs were derived analytically (see
  `ComputeWingPose` remarks); verify in-editor on a lateral-mounted foil
  and a rotor blade at large pitch that the leading edge tilts toward the
  lift direction. Flip the per-mode sign there if not.
- `BlockDef_Rotor.asset` has `_hasVariantConfig: 0` while `BlockVariants`'
  hardcoded list says rotors are variant blocks (panel works via the
  list). Cosmetic inconsistency, flagged by the test-drafter.
- Per-rotor spin *direction* remains deferred (was deferred alongside RPM).
- **Pre-existing `BlueprintEntryTransform.Apply` gap** (found while adding
  `TransformTeeter` to `IBlueprintEntryTransform` + `MirrorTransform`):
  `Apply` rebuilds entries via the 5-arg ctor, so `BlockConfig`,
  `ConcoctionId`, and `Yaw` are silently dropped on the mirrored side of
  `BlueprintBuilder.Mirror` (editor preset authoring) — the exact bug
  class the interface exists to prevent; it drifted across schema v4/v7/v8.
  Teeter is now routed properly (mirror parity = pitch's, new test in
  `BlueprintEntryTransformTests`); the three older fields need deliberate
  rules (yaw especially — a reflected yaw isn't a copied yaw). Flagged in
  an `Apply` comment.
