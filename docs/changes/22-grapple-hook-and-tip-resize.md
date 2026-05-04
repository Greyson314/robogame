# Session 22 — Grapple hook: scaled-up tips, dumbbell target, joint-based latch

> Status: **in progress.** Three phases:
> A — visual + collider scale-up (hook now J-shaped, ~2 m tall;
> mace 1 m diameter; barbell → dumbbell shape).
> B — physics grapple: hook-tip joint attach to contacted bots.
> C — wrap-around feasibility analysis (deferred decision).

## Intent

The user wants the Hook tip block to actually grab other bots via
physics. The previous hook was too small (~0.55 m) to wrap around a
chassis-scale target, the barbell test dummy turned out to be
better-named "dumbbell," and the mace also needed scaling up. Phase A
addresses the sizing; Phase B adds joint-based grapple attach; Phase C
documents the feasibility of true wrap-around vs the simpler
tip-attach approach actually shipped.

## Phase A — Sizing (this commit)

### `HookBlock` redesigned

The hook is now a J-shape sized to a chassis cell:

- **Shaft** (vertical, going down the rope): 0.45 × 0.40 × 1.70 m,
  spans segment-local Z ∈ [0, 1.70]. The rope attaches at the top.
- **Barb arm** (horizontal, extending forward at the shaft's bottom):
  0.45 × 1.70 × 0.40 m, spans Y ∈ [0, 1.70] at Z ∈ [1.70, 2.10].
- **Barb tip** (vertical, going back up at the end of the arm):
  0.45 × 0.40 × 1.50 m, spans Z ∈ [0.20, 1.70] at Y ∈ [1.70, 2.10].

The trap zone (the open volume inside the J) is roughly 1.5 m × 1.5 m,
big enough to fit a 1 m chassis cell.

Compound collider: three matching `BoxCollider`s on the host GameObject
— one per visual cube. Together they approximate the J's hit volume so
contact resolves correctly against the J's silhouette (not against a
single bounding box that would catch from any direction).

`BlockDef_Hook.mass` bumped 0.5 → 1.5 kg, max HP 60 → 120 to match.

### `MaceBlock` redesigned

- **Ball**: 1.0 m diameter (was 0.55).
- **Spikes**: 0.20 × 0.20 × 0.55 m each (was 0.10 × 0.10 × 0.30).
- **Sphere collider** radius 0.65 m on the host (was 0.40).

`BlockDef_Mace.mass` bumped 2.0 → 5.0 kg, max HP 90 → 180. The
~3.3× hook-to-mace mass ratio is preserved, so the kinetic-energy
differential between the two tip types is unchanged.

### Barbell → Dumbbell

The previous "barbell" preset was 13 cells long (3×3×3 ends + 9-cell
rod). The user clarified they meant a dumbbell — short handle between
chunky end weights.

New `Blueprint_DumbbellDummy.asset` shape:

- **End weight A**: 3×3×3 cube cluster at z ∈ [-3, -1].
- **Handle**: single CPU cell at (0, 0, 0) — exactly 1 cell wide, the
  hook's natural grip target.
- **End weight B**: 3×3×3 cube cluster at z ∈ [1, 3].

Total 55 cells, 7 m long along Z (vs 13 m for the old barbell).

### Renames

- `Blueprint_BarbellDummy.asset` → `Blueprint_DumbbellDummy.asset`
  (via `git mv` so the asset GUID is preserved).
- `BarbellDummyTests.cs` → `DumbbellDummyTests.cs` (rewritten for the
  new shape: tests now assert the 1-cell handle and 27-cell end
  weights instead of the 9-cell rod).
- `GameplayScaffolder.BuildBarbellDummyEntries` → `BuildDumbbellDummyEntries`,
  `BarbellDummyPath` → `DumbbellDummyPath`, "Barbell Dummy" display
  name → "Dumbbell Dummy".
- `ArenaController._barbell{Blueprint,Position,Name}` →
  `_dumbbell{Blueprint,Position,Name}`. `[FormerlySerializedAs]`
  attributes preserve the existing scene wire-up — Arena.unity's
  serialized `_barbellBlueprint` value carries over to
  `_dumbbellBlueprint` automatically. No Build Everything required
  for the field rename.
- `PresetBlueprintTests.PresetPaths` updated to point at the new
  asset path.

### `TipBlock.IgnoreChassisColliders` multi-collider fix

The old `GetComponent<Collider>()` only returned the first collider
on the host. With the hook's new compound collider (3 BoxColliders),
the other two would have collided with the chassis as the rope
swung. Switched to `GetComponents<Collider>()` and pair every host
collider against every chassis collider. Same pattern, scaled.

## Phase B + C — coming

Phase B implements the joint-based grapple attach per the planner's
plan: hook collision creates a `ConfigurableJoint` between the rope's
last segment Rigidbody and the contacted chassis Rigidbody, with locked
linear motion + free angular motion + tunable break force. Already
designed; will land in the next commit.

Phase C is a feasibility analysis comparing simple tip-attach (what
Phase B ships) to true wrap-around grapple (defers via PHYSICS_PLAN §2
Verlet migration triggers).
