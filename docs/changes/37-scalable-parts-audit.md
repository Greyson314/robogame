# Session 37 — Scalable parts: Phase 0 audit

> Status: **audit only, no code changes.** First step of the
> [Scalable Parts Plan](../SCALABLE_PARTS_PLAN.md). Catalogues what's
> already wired for per-instance `Dims`, what's stubbed but not load-
> bearing, and the one place the plan's prose disagrees with the code.
> Read before starting Phase 1 (swept-volume occupancy check).

## What's wired today

The `Dims` data path is end-to-end intact for save/load and placement:

- **[`ChassisBlueprint.Entry.Dims`](../../Assets/_Project/Scripts/Block/ChassisBlueprint.cs:64)**
  carries a `Vector3` per entry. `Vector3.zero` means "use block
  defaults". Three constructors expose it.
- **[`BlockBehaviour`](../../Assets/_Project/Scripts/Block/BlockBehaviour.cs:50)**
  stores `Dims`, exposes `SetDims(Vector3)` + `DimsChanged` event,
  and `Initialize(def, pos, dims)` accepts it from the grid.
- **[`BlockGrid.PlaceBlock(def, pos, up, dims)`](../../Assets/_Project/Scripts/Block/BlockGrid.cs:122)**
  threads dims through to `BlockBehaviour.Initialize`.
- **[`BlueprintSerializer`](../../Assets/_Project/Scripts/Block/BlueprintSerializer.cs:50)**
  is on schema v2 with `dx/dy/dz` per entry; v1 saves migrate via
  zero-default fall-through. Round-trip works.
- **[`VariantConfigPanel`](../../Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs)**
  scaffolds the in-garage UI for foils (span/thickness/chord) and
  ropes (segment count). Caches "next placement" dims per block id;
  resets on build-mode entry. Writes only the cache, never a
  Tweakable — so invariant 1 is honoured.
- **[`BlockEditor.TryPlace`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs:413)**
  reads `_variantPanel.GetDimsForBlock(id)` and feeds it into
  `BlockGrid.PlaceBlock`. The ghost preview rebuilds on dims/cell
  change. `SyncBlueprintFromGrid` writes `b.Dims` back into the
  blueprint, so saves carry per-block sizing.
- **[`AeroSurfaceBlock.ApplyOrientationToVisual`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs:396)**
  reads `bb.Dims`, falls back to `Default*` consts, scales the
  mesh and computes the outward shift. `ResolveDims` is a public
  static helper.
- **`RopeBlock`** reads `Dims.x` for segment count.

## The one gap: lift is not actually scaled by dims

The plan's §2 claims "the lift formula in `FixedUpdate` consumes the
per-instance dims, not the `Aero.*` Tweakables". That's half right.
Tweakables are correctly out of the lift path (good — invariant 1).
But dims are also out of the lift path:
[`AeroSurfaceBlock.cs:309`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs:309)
is

```
float liftMag = speedSqr * _liftCoef * liftFactor * Mathf.Sign(forward);
```

`_liftCoef` is a fixed `[SerializeField]` default (0.95). Span,
chord, and thickness drive only the **visual** mesh. A 5× wing
visually is not a 5× lift wing physically. No `Configure(dims)`
method exists; only `ConfigureRotorMode` for the rotor-blade case.

**Implication for the plan.** Phase 1 (occupancy) is fine to land
without touching this. But before Phase 3 (cost model) the lift
path needs a real `chord × span` term — otherwise larger-and-cheaper
becomes a strict dominance trap. Recommend a small Phase 1.5 ("lift
scales with planform area") before cost balancing. The math is
trivial (`liftMag *= span * chord / (DefaultSpan * DefaultChord)`
keeps the default chassis numerically identical) but the regression
surface is non-trivial — the four shipped presets all need a fly-
test pass after the change.

## Other gaps the plan calls out, confirmed

- **No `IScalableBlock` / `GetSweptBounds`.** Phase 1's job.
- **`BlueprintValidator` has no overlap rule.** Phase 1's job.
- **No occupancy index in `BlockGrid`.** Phase 1's job.
- **No serializer round-trip test for `Dims`.** Should land with
  Phase 1 since validator tests will need similar fixtures.
- **Cost model is flat per-definition.** `BlockDefinition.CpuCost`
  is an int. Phase 3.
- **No "select existing block and modify" UX.** The variant panel
  only writes the next-placement cache. Editing an already-placed
  foil's dims requires a select-mode that doesn't exist yet —
  flagged in the plan's Phase 1 scale-time validation bullet but
  it's a UX feature first, validation second.

## Phase 1 readiness

Green light. The data path is solid; Phase 1 is additive. Suggested
entry order:

1. `IScalableBlock { GetSweptBounds(Vector3 dims, Vector3Int up) }`
   on a sibling interface, implemented by `AeroSurfaceBlock` first.
   AABB in chassis-local space is fine for v1.
2. `BlockOccupancy` helper (sibling of `BlockGrid`, not inside it —
   keeps the netcode-relevant grid claim tidy).
3. Wire `BlockEditor.IsValidPlacement` to query the occupancy union.
4. Validator rule + tests.
5. `VariantConfigPanel` slider clamp at first overlap (depends on
   the select-existing UX, so probably defers to Phase 1.b).

## Out of scope for this session

Per "doc brevity" — no rewrites of the plan doc itself. Updates
land alongside the implementing phase. The lift-scaling gap will
get its own bullet in the plan when Phase 1.5 ships.
