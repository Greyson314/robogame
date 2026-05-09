# Session 44 — Foil pitch Phase 4 + three side fixes

> Status: **shipped, untested in-engine.** Phase 4 of the
> [Foil Rotation Plan](../FOIL_ROTATION_PLAN.md) (live consequence
> readouts) plus three bug fixes from the user's session-43 testing:
> rotor / adopted-foil deletion, foil placement near the rotor
> mechanism cube, and the variant panel's slider arrangement.

## Bug 1 — adopted foils + rotor undeletable

**Cause.** `BlockEditor.UpdateTarget` rejected raycast hits on blocks
whose transform wasn't a child of the chassis transform. The rotor
adopt-pass reparents foils to a kinematic hub at scene root, and the
rotor's own visual rig (mast + disc) likewise lives under the hub.
So none of those parts pass `block.transform.IsChildOf(chassis)` →
the editor treats the click as "not our chassis" and silently drops
it. Right-click does nothing.

**Fix.** Replace the transform-parenting check with a grid-membership
check: the block is a legitimate edit target iff its
`GridPosition` is in the chassis's `BlockGrid.Blocks` AND that
dictionary entry IS the same BlockBehaviour instance. Adopted foils
stay in the grid (rotor's adopt-pass doesn't `RemoveBlock`), so
they pass the new check regardless of where their transform got
reparented.

## Bug 2 — `<1`-span foils rejected on most faces near the rotor

**Cause.** `BlockEditor.BuildCpuReachableSet` was skipping leaves as
BFS bridges (a defense-in-depth measure from session 40's
"no building on wings" rule). On the helicopter, the rotor is a
leaf; the mechanism cube above it is non-leaf BUT only physically
connected to CPU through the rotor. With the leaf-skip, the
mechanism cube ended up "unreachable" — placements anywhere off
its faces failed the strict-host check.

The user observed this as "foils only placeable on left/right side
of the helicopter" because only the fuselage cubes (which DO have
direct non-leaf paths to CPU) showed up as valid hosts.

**Fix.** Drop the leaf-skip from `BuildCpuReachableSet`. Plain
physical-adjacency BFS now. The strict-host check
(`IsLeaf(host) → reject`) still prevents building ON wings —
that's the load-bearing rule. Skipping leaves as bridges was
overkill that broke authored chassis where leaves sit between
non-leaf cells.

## Bug 3 — foil panel layout flipped

User wanted the foil dimensions (span / thickness / chord) as the
always-visible primary sliders and the rotation (pitch) tucked
behind the Advanced expander. Pitch is the power-user knob; the
size sliders are what most builders reach for. Rebuilt
`VariantConfigPanel.BuildFoilSection` accordingly:

- Slot 0: 4 preset cards.
- Slots 1–3: Span / Thickness / Chord sliders (always visible).
- Below: live lift readout (Phase 4).
- Below: ADVANCED ▼ toggle.
- Behind toggle: Pitch slider.

Rotor panel unchanged (collective is the only knob — keep it
primary).

## Phase 4 — live consequence readouts

[`VariantConfigPanel`](../../Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs)
gains `EstimateFoilLift` — a static helper that mirrors
`AeroSurfaceBlock.FixedUpdate`'s lift formula at a reference
airspeed, so the readout reflects exactly what FixedUpdate will
compute. Reference values:

- **Free-wing cruise**: 30 m/s (typical plane forward speed).
- **Rotor blade**: ω×r at 240 RPM and 1 m radius (matches the
  shipped helicopter's blade config). Disc lift assumes 4
  default-dim blades — bigger blades will lift more than the
  readout suggests; intentional underestimate.

Readout texts:
- Foil panel: `≈ 124 N @ 30 m/s` under the chord slider. Updates
  on every span / thickness / chord / pitch change. Past ±18°
  pitch it appends `— STALL` and turns red.
- Rotor panel: `≈ 332 N disc (4 default blades, 240 RPM)` under
  the collective slider.

Both readouts update live as sliders move and refresh on section
switch.

## Notes for the next session

- **Validator no longer enforces leaf-bridge connectivity.** The
  defense-in-depth rule was already absent from
  `BlueprintValidator` (it just does plain BFS). With the
  matching change in `BlockEditor.BuildCpuReachableSet`, the
  invariants are consistent: build-mode placement and validator
  agree on what "reachable from CPU" means. Worth a quick
  in-engine smoke test that the helicopter still passes
  `BlueprintValidator.Validate` (it should — nothing structural
  changed).
- **Live mid-edit collective.** Still a Phase 1.b carry — tuning
  the rotor's collective in the variant panel only affects new
  rotor placements; existing adopted blades keep their old
  collective until you re-place the rotor.
- **No Shift-for-fine-grain modifier.** Sliders snap (1° pitch,
  0.25 m length). If a player wants finer control they're stuck
  with the snap grid for now.
- **Rotor adopt-pass also writes pitch onto blades at adopt
  time.** Means a blueprint authored with a custom collective
  rotor + custom-pitch blades has the rotor's collective
  override the blades' authored pitch (per session 42's
  symmetric-rotor invariant). No per-blade override toggle yet.
