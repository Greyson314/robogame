# 55 — Rope tip-at-chain-end (resolves session 54's open item)

> User reported: in the garage, placing a hook/mace at the bottom of a
> long rope wasn't possible. Only the "second-from-the-top" cell on the
> chain accepted a tip, and placing there visually collapsed the rope
> to length 2. At launch, the rope spawned full-length with the hook at
> the chain's free end — the user correctly diagnosed this as a garage
> visual / placement-grid bug.

This is exactly the open item from
[session 54](54-session-wrap.md) — "tip-block placement is
grid-cell-adjacent, not chain-end" — with Option 1 implemented
("snap chain length to integer cells, place tip at the chain's end
cell").

## What changed

### Rope variant slider now reads "Length (cells)"

`ChassisBlueprint.Entry.Dims.x` for a rope was "segment count"; it's
now "length in chassis cells", range 1–16, default 4. The runtime
verlet segment count is derived: `segments = cells / RopeSegmentLength`
(the Tweakable is now a sub-segment density knob, not a length knob).
Capped at 32 to keep the solver budget bounded.

Decoupling length-in-cells from the `RopeSegmentLength` Tweakable was
deliberate: the tip block's cell must be derivable purely from
per-block blueprint data so the netcode contract (CLAUDE.md
invariant §1) holds. A Tweakable cannot move a placement-relevant
cell.

### New `RopeGeometry` static (in `Robogame.Block`)

[`RopeGeometry`](../../Assets/_Project/Scripts/Block/RopeGeometry.cs)
exposes `DefaultLengthCells / Min / Max`, `ChainCellCount(rope)`,
and `TipCell(rope)` — for both live `BlockBehaviour`s and pre-
instantiation `ChassisBlueprint.Entry`s. Lives in the `Block`
assembly so `PlacementRules`, `BlockGraph`, and `BlueprintValidator`
can use it without a circular asmdef ref against `Robogame.Movement`.
`RopeBlock` re-exports the constants for legacy callers.

### Placement rules: rope-aware host resolution

[`PlacementRules.ResolveHostCell`](../../Assets/_Project/Scripts/Block/PlacementRules.cs)
is the new internal entry point used by `CheckHostExists`,
`CheckHostIsConnective`, and `CheckHostIsCpuReachable`. For Hook /
Mace candidates it walks back along `-c.Up`, looking for a rope
whose `ChainCellCount` matches the walk distance and whose mount-up
matches the tip's. Find → that rope is the host. For every other
case it falls back to the standard face-adjacent neighbour.

### BFS: rope-bridge virtual edge

[`BlockGraph`](../../Assets/_Project/Scripts/Block/BlockGraph.cs)'s
BFS treats `rope.cell ↔ rope.tipCell` as a single virtual edge — the
chain's intermediate cells stay unclaimed (the chain "passes through"
them, per session 54's call) but a tip block at the chain's free end
still resolves as CPU-reachable through the rope. Both the
live-grid overload and the positions-only overload (used by
`BlueprintValidator`) bridge symmetrically. The two-cell-ignore
variant used by cascade-removal also bridges.

### Targeting: rope-chain hit → tip cell

[`BlockEditor.UpdateTarget`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
adds a special case: when the hit collider belongs to a rope AND the
selected hotbar block is a Hook / Mace, the placement cell is
redirected to `rope.cell + ChainCellCount × rope.Up`. The ghost
previews at the chain's visual free end; a click lands the tip at
the chain's actual free-end cell.

Two triggers fire the redirect:

- **Generous aim sphere** at the tip cell. The chain cylinder's
  CapsuleCollider terminates in a tiny hemisphere of radius =
  rope-segment-radius (~16 cm Ø); threading the cursor through that
  was painful. A half-cell sphere collider at the tip cell, carrying
  a [`RopeTipAimTarget`](../../Assets/_Project/Scripts/Movement/RopeTipAimTarget.cs)
  marker, expands the aim cone to the full tip-cell volume. Only
  spawned when the tip cell is empty (otherwise its volume would
  coincide with — and steal right-click hits from — the placed hook
  or mace).
- **Cylinder-end-cap hit** with the rounded hit face equal to
  `rope.Up`. Kept as a fallback so the redirect still fires in any
  edge case where the sphere wasn't spawned.

Non-tip selections keep the standard adjacent-cell candidate so
"trying to place a cube on the chain's free-end cap" rejects with
the same `HostFaceRejectsBlockType` as before, at the same cell.

### Builder + tests

[`BlueprintBuilder.RopeWithHook`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)
and `RopeWithMace` now author `Dims.x = lengthCells` on the rope
entry and place the tip at `ropeCell + lengthCells × up`. New
overloads accept an explicit mount-up for ropes that don't dangle
along default +Y. Existing tests updated for the new tip cell;
added coverage for custom-length ropes and the negative case
(stranded hook fails validation).

### Plane preset

[`Blueprint_DefaultPlane.asset`](../../Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultPlane.asset)
and its scaffolder source had a rope + hook combination that was
already invalid under session 53's connectivity rules (default
up=+Y rope, hook at `rope - 1*up`). Both updated to the new
convention: rope's mount-up = `(0,-1,0)` (hanging below the tail
cube), 1-cell rope, hook at `rope + 1*up = (0,-2,-2)`.

## Files

- New: `Assets/_Project/Scripts/Block/RopeGeometry.cs`
- Edited:
  `RopeBlock.cs`, `PlacementRules.cs`, `BlockGraph.cs`,
  `BlueprintValidator.cs`, `BlueprintBuilder.cs`, `BlockEditor.cs`,
  `BlockGhostFactory.cs`, `VariantConfigPanel.cs`, `Tweakables.cs`,
  `ChassisBlueprint.cs` (tooltip), `GameplayScaffolder.cs` (plane
  preset).
- Asset: `Blueprint_DefaultPlane.asset` rope+hook entries.
- Tests: `BlueprintBuilderTests.cs` (4 tests updated, 3 added).

## Verification

1. **In garage:** place a CPU + cube, place a rope on the cube's
   `-Y` face (chain hangs down). Open variant panel, set length to
   8 cells. Chain should render 8 cells long. Select a hook; aim at
   the chain's bottom cap. The ghost should appear at the cell 8
   below the rope. Click — hook places at that cell, chain stays
   8 cells long with the hook at the free end.
2. **Launch into arena:** chain remains 8 cells with hook at free
   end, no visual jump. Trigger grapple — joint forms at the chain
   tip as before.
3. **Variant slider:** change a placed rope's length via the
   variant panel; the static chain rebuilds at the new length and
   any adopted tip block moves to the new free-end cell. (Slider
   range now 1–16, default 4.)
4. **Stress connectivity:** place a 4-cell rope + hook, remove the
   rope. Hook should orphan and break free as physics debris.
5. **Validator:** run `BuildValidated()` against
   `RopeWithHook(cell, lengthCells: 6)` — passes. Against a hand-
   authored entry array with the hook at the wrong cell — fails
   with a "not connected" error.
