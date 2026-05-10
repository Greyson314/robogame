# 54 — Session wrap: building-architecture refactor + rotor/rope follow-ups

> Two-day session that started with the structural refactor in
> [BUILDING_ARCHITECTURE_REVIEW.md](../BUILDING_ARCHITECTURE_REVIEW.md)
> and ended in a focused rope/rotor pass driven by user playtest.
> Sessions 45–53 are the per-step records; this entry is the
> "what's true now" digest for any future contributor.

## What landed

### Building architecture refactor (sessions 45 + 46)

All eight steps from the §4 sketch in
[BUILDING_ARCHITECTURE_REVIEW.md](../BUILDING_ARCHITECTURE_REVIEW.md):

- **[`BlockEntries.SortCanonical`](../../Assets/_Project/Scripts/Block/BlockEntries.cs)**
  enforced inside `ChassisBlueprint.SetEntries`. Every blueprint
  mutation chokepoint now produces canonical-sorted entries —
  the netcode contract from
  [NETCODE_PLAN.md](../NETCODE_PLAN.md) §6.
- **[`BlockGraph`](../../Assets/_Project/Scripts/Block/BlockGraph.cs)**
  is the single BFS / orphan / CPU-locator primitive. The four
  duplicate BFS implementations (in `BlockEditor`, `BlueprintValidator`,
  `BlockGrid`) collapsed into reusable buffers + one BfsFrom
  body. `BlockEditor.IsValidPlacement` no longer allocates per
  frame.
- **[`PlacementRules`](../../Assets/_Project/Scripts/Block/PlacementRules.cs)**
  is the shared rule engine. Editor + validator agree on what
  a "legal placement" means — the prior overlap/divergence is gone.
  Each rule returns a discrete `PlacementError` so the new
  `PlacementFeedbackHud` (bottom-right overlay) can show the
  player *which* rule rejected and *which* cell.
- **[`IBlueprintEntryTransform`](../../Assets/_Project/Scripts/Block/IBlueprintEntryTransform.cs)** +
  **[`BlockMirror`](../../Assets/_Project/Scripts/Block/BlockMirror.cs)**'s
  `MirrorTransform` give a compile-time guard against subsystems
  silently dropping new `Entry` fields.
- **[`BlockVariants`](../../Assets/_Project/Scripts/Block/BlockVariants.cs)** +
  the `HasVariantConfigRaw` flag on `BlockDefinition` make the
  hotbar's "VAR" badge schema-driven.
- **[`FoilDefaults`](../../Assets/_Project/Scripts/Block/FoilDefaults.cs)**
  is the single source for foil shape constants;
  `AeroSurfaceBlock.Default*` and `BlockOccupancy.FoilDefault*`
  now alias these instead of duplicating.
- **[`BuildSession`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs)**
  is the plain-C# build-mode model. Variant cache + mirror state
  + `TryPlace` / `TryRemove` verbs live here, testable without
  a scene. `BlockEditor` is the thin driver.
- **[`BlockGhostRenderer`](../../Assets/_Project/Scripts/Gameplay/BlockGhostRenderer.cs)** +
  **[`PlacementFeedbackHud`](../../Assets/_Project/Scripts/Gameplay/PlacementFeedbackHud.cs)**
  extracted from `BlockEditor` (which dropped from 783 → ~600 lines).
- **[`ChassisAssembler`](../../Assets/_Project/Scripts/Gameplay/ChassisAssembler.cs)**
  unifies the prior `ChassisFactory.Build` / `BuildTarget` split
  behind a single `Assemble(root, blueprint, library, AssemblyOptions)`
  + a `ChassisHandle` return record.
- **[`BuildModeController.Exit`](../../Assets/_Project/Scripts/Gameplay/BuildModeController.cs)**
  no longer reaches into `GarageController` via
  `FindAnyObjectByType` — fires `Exited` and the garage subscribes.

### Rotor + foil pass (sessions 47–51)

Driven by playtest reports across two sessions:

- **[Auto-companion mechanism cube](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)**
  on rotor placement: the cube auto-spawns at `rotor + spinAxis`,
  giving the player a connective face for blade attachment.
  Cascade-removal handles "user removes the rotor" without
  triggering the orphan check.
- **Rotor's spin-axis face accepts the mechanism cube** via
  [`BlockConnectivity.IsConnectiveFace`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)
  — a per-face exception to the leaf rule. The mechanism cube's
  four lateral faces accept *only* aero/aerofin/rope (the rotor
  adoption code only adopts those).
- **`RotorsGenerateLift`** auto-derives at
  [`BuildSession.SyncBlueprint`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs)
  time: any rotor on the chassis flips the flag. Per-rotor opt-in
  remains the eventual fix when per-cell blueprint config lands.
- **Pitch is world-intent**, normalized to local-frame at placement.
  [`BlockOrientation.NormalizePitchForUp`](../../Assets/_Project/Scripts/Block/BlockOrientation.cs)
  converts based on the foil's local-X world-Y component; the
  variant panel value now means "tilt tip toward sky by N degrees"
  on every face, not just under mirror mode.
- **Rotor blade mesh shift always extends outward**:
  [`AeroSurfaceBlock.ComputeWingShift`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)
  in rotor mode is now unconditionally `(-magnitude, 0, 0)`. The
  previous `signX = sign(cellPos.x)` sent +X-side blades inward,
  which made `span > 1` blades visibly cross the rotor hub.
- **Rope adoption by rotor**: ropes placed on a mechanism cube's
  lateral face reparent to the spinning hub, swing out
  centrifugally in arena. Rule of cool.
- **`StrictOverlap` epsilon** (1e-3) tolerates quaternion-rotation
  FP error in foil swept bounds — fixed the false-positive
  "swept volume overlaps" rejection on side-mounted blades.

### Rope redesign (sessions 52 + 53)

- **No "rope base block".** [`BuildStaticVisual`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
  and `Build`'s live path keep the host cube hidden in every mode.
  The rope is just the chain.
- **Chain extends outward from the chassis face**, not toward
  it. Live anchor at chassis-side face; tip body at free end;
  initial verlet particles laid out along `+transform.up`.
  Static visual cylinder spans chassis-side → free end. Fixes
  the "rope on bottom of chassis pushes through the top" bug.
- **Hologram = full chain length** matching live tweakables
  (segment-length / radius read from `Tweakables` so the ghost
  doesn't drift from the placed rope).
- **Static cylinder is interactable.** Its collider is preserved
  (was being destroyed); aiming at the chain hits resolve to
  the rope's `BlockBehaviour`. The free-end cap is the placement
  target for hooks and maces.
- **Tip-face accepts at `+up`** (mount-up = chain's free-end
  direction), per the new chain convention. Was `-up` for the
  pre-redesign chain-toward-chassis direction.

## Open: tip-block placement is grid-cell-adjacent, not chain-end

The hook / mace can be placed at `rope.cell + up` (one grid cell
beyond the rope cell). The rope's chain visual, however, extends
`segments × segLen` cells from the chassis face — at default
`segments = 8` and `segLen = 0.5`, that's 4 cells.

So a default-length rope with a hook ends up:

```
rope cell → chain extends 4 cells → free end at world (...)
                ↓
hook cell (1 cell beyond rope) sits at the START of that 4-cell
chain, leaving 3 cells of chain dangling past it with nothing
attached.
```

The user's framing: "leaving a thread of child ropes with no
purpose."

This is the fundamental tension between:
- One `BlockBehaviour` per grid cell (the placement model).
- A multi-cell chain visual (the rope's actual reach).

Three resolution options, none of which fit in this session:

1. **Snap chain length to integer cells, place tip at the
   chain's end cell.** Slider becomes "cells" instead of
   "segments"; tip placement targets `rope.cell + (length) * up`.
   The chain's mid-cells aren't claimed in the grid (so other
   blocks could overlap visually) — call those out as "ropes
   pass through" and accept it. Probably the cleanest UX.
2. **Make the rope occupy multiple grid cells.** Each cell from
   `rope.cell` through `rope.cell + (length-1) * up` is a
   `BlockBehaviour` entry. Tip mounts at `rope.cell + length * up`.
   Heavy — every other system (damage propagation, connectivity,
   serialization) has to reason about multi-cell ropes.
3. **Allow tip placement at any cell along the chain's visual
   extent.** Targeting raycast hits the chain anywhere, computes
   the tip position from the hit; the chain's verlet sim
   shortens/lengthens as needed. Most flexible UX, but the
   placement rules engine becomes "find the cell along the
   chain closest to the hit position", not the standard
   "host face → adjacent cell" model.

Option 1 is the recommended next move when the user comes
back to ropes. Option 2 is a netcode-readiness item too —
multi-cell blocks would generalize for other "long" parts.

## Files

Sessions 45–53 own the per-step diffs. This entry is the
narrative pointer.

## Next sessions could (in rough priority order)

- Resolve the rope tip-placement-cell-adjacency limitation
  per Option 1 above.
- Repro the rope-chain-not-visualising-in-garage report from
  session 51 if it still happens (the session-53 collider fix
  might have closed it; needs verification).
- Per-rotor opt-in for `RotorsGenerateLift` (currently
  chassis-wide auto-derive). Requires per-cell blueprint config.
- Continue the structural-refactor follow-ups in
  [BUILDING_ARCHITECTURE_REVIEW.md](../BUILDING_ARCHITECTURE_REVIEW.md)
  Step 5 — schema-side dispatch tables for `BlockOccupancy` and
  `BlockGhostFactory` (required when a second scalable shape
  lands per `SCALABLE_PARTS_PLAN.md` Phase 2).
