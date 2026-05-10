# 50 — Rotor blade-slot restriction + rope tip-block placement

> Two related rules-engine fixes following the user's "anything
> attached to a rotor should spin / nothing attached to a rope works"
> reports.

## What changed

### Rotor mechanism cube only accepts aero on its lateral faces

The user noted that placing a non-aero block on a rotor "spoke" (the
mechanism cube's perpendicular cells, where the four blades go in
the helicopter preset) leaves the placed block static — the rotor's
adoption pass only adopts `AeroSurfaceBlock` components into the
spinning hub. Their fallback request: if making everything spin is
too invasive, just don't allow non-aero placement there.

[`BlockConnectivity.AcceptsPlacement`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)
is a new richer check (parallel to the simpler
`IsConnectiveFace`) that takes the live grid + the host
`BlockBehaviour` + the placement's `BlockDefinition`. For a cube
hosted on a rotor's spin-axis face — the geometric definition of a
"mechanism cube" — its four lateral faces only accept
`Aero` / `AeroFin`. The top face (further along the spin axis)
remains open for chassis extension.

### Rope's tip face accepts hook / mace

Same general pattern as the rotor's spin-axis exception: the rope
is a leaf for its lateral faces and top, but its tip face (opposite
the mount-up) IS the natural host for a hook or mace. The
`AcceptsPlacement` rope branch checks `placementUp == -hostUp`
+ `placementDef.Id is Hook|Mace` and returns `None` (allow). All
other rope faces return `HostIsLeaf`. The simpler
`IsConnectiveFace` (used by the validator) gets the same exception
without the block-id check, so blueprint-level validation passes
for any tip-face mount.

### `PlacementError.HostFaceRejectsBlockType` (new)

Distinct from `HostIsLeaf` because the failure mode is different —
the face *is* connective, just not for this block type. The HUD
overlay describes both flavours so the player can see why
("doesn't accept this block type on that face — try a different
block, e.g. aero on a rotor mechanism, hook/mace below a rope").

### Rope static visual rebuilds on tip placement

[`RopeBlock.OnGridBlockPlaced`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
previously bailed early on the static (build-mode) path because the
adoption hook needs a `_tipRb` and the static path doesn't create
one. Without the rebuild, the cylinder visual stayed at its
build-time length even after the player placed a hook adjacent.
Now the kinematic path triggers `Rebuild()` so `BuildStaticVisual`
re-runs `TryGetAdjacentTipCellLocal` and spans the cylinder to the
freshly-placed tip cell.

## On the broader "rules-based, anything spins" framing

The user's preferred design ("anything attached to a rotor spins
with it") would mean adopting any `BlockBehaviour` (cubes, weapons,
etc.) into the rotor's kinematic hub, not just `AeroSurfaceBlock`.
That's a real change to the rotor's adoption logic — weapons
spinning is interesting but visually chaotic, and rope/hook on a
rotor would interact badly with the chain solver. Going with the
fallback the user offered: restrict placement to aero-only on the
slot. If the spinning-anything design becomes a priority later,
revisit `RotorBlock.AdoptAdjacentAerofoils` to widen its component
filter.

## Files

`BlockConnectivity.cs`, `PlacementRules.cs`, `PlacementFeedbackHud.cs`,
`RopeBlock.cs`. No new tests (the existing `IsConnectiveFace` tests
still cover the legacy signature; `AcceptsPlacement` needs a live
`BlockGrid` + `BlockBehaviour` to drive, harder to fixture in
EditMode).

## Verification

1. Default helicopter preset → remove a blade → try to place a Cube
   on the same mechanism-cube face. HUD should show
   "doesn't accept this block type on that face — aero only";
   placement rejected.
2. Same scenario → place an Aero foil instead → accepted.
3. From-scratch chassis → CPU + cube + Rope on cube's +Y face.
   Should see the rope cube + a downward cylinder reaching to full
   default length.
4. Aim at the rope's bottom face → place a Hook. Accepted; the
   cylinder now spans rope → hook cell instead of dangling past it.
5. Try placing a Cube on the rope's bottom face → HUD shows the
   block-type rejection.
