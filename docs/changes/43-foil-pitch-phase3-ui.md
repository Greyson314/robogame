# Session 43 — Foil pitch Phase 3: build-mode UI rebuild

> Status: **shipped, untested in-engine.** Phase 3 of the
> [Foil Rotation Plan](../FOIL_ROTATION_PLAN.md). Rebuilds
> `VariantConfigPanel` per §3.4: header → preset cards → primary slider
> → Advanced expander. Adds a rotor section with collective slider +
> 3 presets. Phase 4 (live consequence readout + stall warning) and
> Phase 1.b (select existing block + retune) are still deferred.

## What changed

### `VariantConfigPanel` rewrite

[`Gameplay/VariantConfigPanel.cs`](../../Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs)
restructured around the new four-zone shape. Three sections share the
panel, switched by selected hotbar block:

**Foil** (Aero / AeroFin):
- 4 preset cards: **Heli Blade** (span 1.5, chord 0.6, thickness 0.06,
  pitch +8°), **Plane Wing** (4.0 / 0.9 / 0.08 / +2°), **Tail Stab**
  (2.0 / 0.7 / 0.08 / -1°), **Vert Fin** (2.0 / 0.9 / 0.08 / 0°).
- Primary slider: **Pitch** (-18° to +18°, snap 1°). Value text turns
  red past ±18° (`BlueprintValidator.PitchSoftLimitDeg`) for the
  stall warning.
- Advanced expander (collapsed by default, click to toggle): Span,
  Thickness, Chord sliders. Snap 0.25 m.

**Rotor** (Rotor):
- 3 preset cards: **Heavy Lift** (12°), **Standard** (8°), **Light** (5°).
- Primary slider: **Collective** (0° to 18°, snap 1°). Per-rotor
  RPM / direction deferred — fall back to the SO defaults until a
  player needs them.

**Rope** (Rope):
- Segment count slider (unchanged from the prior panel).

### Public API

- `VariantConfigPanel.GetPitchForBlock(blockId)` — new, mirrors
  `GetDimsForBlock`. Returns the cached "next placement" pitch in
  degrees (0 = use block default).
- `IsVariableBlock(id)` — extended to include `BlockIds.Rotor` so the
  selection-driven panel visibility kicks in when the player picks
  the rotor from the hotbar.

### `BlockEditor` wire-up

[`Gameplay/BlockEditor.cs`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs):
- `TryPlace` reads pitch from `_variantPanel.GetPitchForBlock(id)`
  and threads it into `_grid.PlaceBlock(def, cell, up, dims, pitch)`.
- `TryMirrorPlace` takes the new pitch parameter and passes it
  through unchanged — pitch is a scalar, mirrors as-is so symmetric
  foil trim survives the mirror.
- Existing `IsValidPlacement` is pitch-agnostic (placement validity
  depends on cell + up + dims, not on pitch).

### Snap behaviour

- Length dims (span / thickness / chord): 0.25 m increments.
- Angles (foil pitch / rotor collective): 1° increments.
- Segment count (rope): 1 segment.

Each slider's `onValueChanged` callback rounds the slider value to
the snap grid and updates the cache. No "Shift for fine grain"
modifier — deferred. The slider visibly snaps to integer / quarter
positions as the player drags.

### Stall warning

The pitch slider's value text turns red past ±18°. No separate
banner / message yet — that's Phase 4's "live consequence readout"
where the readout will say `STALL — lift dropping`.

## Notes for the next session

- **No live readout (Phase 4).** The plan adds an estimated-lift
  display under the slider that updates as the player drags. Useful
  for "okay this gives 124 N at 240 RPM, that's enough for a 200 kg
  chassis." Defer until a player has built a few chassis with the
  new sliders and we know what they want to see.
- **No "select existing block + retune" UX.** This is the persistent
  Phase 1.b carry-forward — the variant panel only writes to the
  next-placement cache; you can't click an existing wing and re-tune
  it. The closest workaround is right-click to remove + re-place.
- **Pitch ghost preview.** The build-mode ghost factory doesn't
  render pitch tilt — the ghost shows the wing flat, then the
  placed block tilts. Discrepancy is small for small pitch values
  but obvious past ±10°. Single-source-of-truth fix: have
  `BlockGhostFactory.BuildWing` apply the same `localRotation`
  around foil-local +Z that `AeroSurfaceBlock.ApplyOrientationToVisual`
  does. One-liner; just hasn't been threaded through the ghost
  factory's caller yet (would need to take a `pitchDeg` arg).
- **Per-rotor RPM / direction.** The rotor section currently has
  collective only. The plan §3.4 mentions RPM and direction in the
  Advanced expander; both need a data-model extension first
  (currently `Tweakables.RotorRpm` is global). Worth a real audit
  before adding — RPM-per-rotor is a separate "blocks have stats"
  conversation.
- **Live mid-edit collective changes.** Today, the rotor's adopt
  pass writes the collective onto blades only at chassis build
  time. So the player tunes the collective slider, but they have
  to leave + re-enter build mode (or re-place the rotor) to see
  the new collective applied to existing blades. Solvable: hook
  `BlockBehaviour.PitchChanged` on the rotor and re-broadcast to
  adopted foils. Hold off until a player asks — the in-build
  experience is fine for now.
- **Variant panel can't select Rotor today.** The hotbar's
  `IsVariableBlock` gating is updated, but the rotor UI is only
  visible when the rotor block is the *currently-selected* hotbar
  item. That works for "I'm about to place a rotor" but not for
  "I want to retune an existing rotor." Same Phase 1.b carry as
  foils.
