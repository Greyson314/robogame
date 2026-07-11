# 140 — Wing block: bat-wing graduates from study to placeable part

**Intent.** Ship the animated bat-wing (session 139 study) as its own
"Wing" block: side-mounted and foil-configurable (span / thickness /
chord / pitch / teeter sliders), rest-posed in the garage, flapping in
arenas. Plus the first cut of "hitboxes aren't cubes": placement
reserves the flap's swept airspace and the build ghost shows it.

## Blender → FBX (first skinned export)

- `paperlib.export_tree` grew an `armature=` param: adds ARMATURE to
  `object_types` + `bake_anim` args; static callers byte-identical.
  Gotcha: `bpy.types.Armature` has NO object-mode `.transform()` (Mesh
  does) — new `paperlib.transform_armature_data` round-trips through
  edit mode and uses `EditBone.transform`. Rigid rest-frame changes
  keep bone-local pose keys valid, so the baked flap survives the
  session-131 frame conversion unchanged.
- `inv_export.export_wing_anim()`: builds the `InvSwim_` rig at origin,
  bakes the foil Wing-frame (`Rz(180) @ Ry(-90)`) into mesh AND
  armature, recentres on the rest-pose bbox, **measures and prints the
  authored dims** (they are the `WingDefaults` constants — rerun the
  export to re-derive on any reshape), bakes frames 1..49 (49 == 1) so
  the Unity clip loops seamlessly, writes `Wing_Inv.fbx`. The static
  `inv_wing` STATICS entry is gone — the rigged export owns the name.
- Measured @ export: span 1.828, thickness 0.195, chord 1.004; flap
  sweep along camber [-0.595, +0.533] m → `SweepHalfExtentPerSpan =
  0.33` (symmetrised, conservative).

## Unity — the aero family grows a second member

New `BlockIds.Wing` (`block.movement.wing`), `BlockDef_Wing` (wizard:
70 HP / 1.2 mass / 18 CPU), wired to `Wing_Inv.fbx` (component-driven).
Registered in: `BlockVariants` (variant sliders), `BlockConnectivity`
(leaf), `BlockOrientation` (world-intent pitch), `BlueprintAsciiDump`
(`w` — `W` is Wheel). NOT rotor-adoptable (adoption whitelist stays
Aero/AeroFin, per the Foils-vs-Wings philosophy).

- **`AeroShape` (new, Block):** per-id resolver — Wing reads new
  `WingDefaults`, foils read `FoilDefaults`. `AeroSurfaceBlock`'s
  instance paths (visual scale, dims resolve) now resolve per id; its
  public static foil API is untouched. Lift-area denominator stays
  `FoilDefaults` for every id: it's the global "one unit of lift", so
  a default Wing lifts ~2× a default foil (its true planform ratio).
- **Binder:** Wing binds `AeroSurfaceBlock` (Vertical=true, same as
  foils) + new `WingFlapAnimator`.
- **Garage/arena gate:** `Core.SceneKind.IsArena()` (promoted from
  `ChassisInstancedRenderer`, which now delegates). `WingFlapAnimator`
  decides ONCE in `Start`: arena → Legacy `Animation.Play` (loop) +
  `AudioCue.WingFlapLoop` loop + faint tip `TrailRenderer`; garage →
  `Animation` fully disabled (zero cost, rest/bind pose). No Update,
  no per-frame allocs.
- **Import settings:** `WingModelImportSettings` (AssetPostprocessor,
  scoped to the FBX path) forces Legacy rig, looping clip,
  `playAutomatically=false` — survives every re-export.

## Swept-volume occupancy + envelope indicator (user-approved design)

`BlockOccupancy.ComputeFoilSweptBoundsLocal` now takes the block id;
for Wing the camber (foil-local +X) half-extent inflates to
`max(thickness/2, span × 0.33)` — placement reserves the airspace the
flap visits, scaling with span. Conservative single box (approved over
a piecewise wedge; revisit if root-adjacent placements feel too
restricted). The build ghost (`BlockGhostFactory.BuildBatWing`) draws
the rest-pose slab plus a translucent amber "GhostEnvelope" cube in
the exact occupancy frame; envelope keeps its own material through
valid/invalid tint swaps (skip in `ApplyToAll`).

## Invariants check

Zero new physics objects (flap is skinned-mesh visual only; collider
stays rest-pose). No Tweakables. No per-frame allocations. Invariant 8:
`WingFlapLoop` cue declared (no-op until authored in the cue library)
+ tip trail VFX. Perf: first skinned mesh in the project — profiler
capture with several wings in-arena REQUIRED before calling it within
budget (Animator cost claim otherwise unprofiled).

## Known limits / next steps

- Non-default dims shear the flap slightly (skinned mesh under ratio
  scale) — cosmetic, revisit only if it reads badly.
- Tip trail attaches at the `Flap2` bone node (~2/3 span), not the true
  tip; nudge outboard after a live look.
- Trail/loop-audio and door-swing-style sign checks need one live
  arena look (Unity bridge was down this session; code-only so far).
- "Blame the blocker" ghost flash (show WHICH block's envelope broke a
  placement) — designed, deferred.
- Piecewise multi-box envelope = the general non-cube upgrade, deferred
  until single-box conservatism annoys.
- Flap "when powered" still future work (always-on in arena for now).
- `AudioCue.WingFlapLoop` needs an authored clip row in AudioCueWizard.
