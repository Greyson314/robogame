# 136 — Variant sliders no longer rewrite every placed block of a type

## Problem

Changing an aerofoil's span changed the span of every aerofoil on the bot.
Same for every variable parameter on every variable block type (pitch,
teeter, rotor RPM, thruster config): in normal placement mode,
`BlockEditor.PropagateVariantToLiveBlocks` pushed each variant-cache write
onto **all** placed blocks of the matching id. That was the session-96
"live mid-edit" feature, built before per-instance editing existed. Session
125 added instance-edit mode (Edit-mode click binds one block to the
sliders) but kept propagate-to-all as the unbound fallback, so:

- Dragging a slider in normal placement mode retuned the whole fleet.
- The middle-click eyedropper (`TryPickBlock` → `LoadBlockSettingsIntoCache`
  with no instance bound) stamped the picked block's settings onto every
  sibling — despite its doc comment promising "next placement only".

## Fix

`PropagateVariantToLiveBlocks` now applies only to the bound
`EditingInstance` (id-matched); with none bound it is a no-op. The variant
cache is purely "next placement" state — the ghost previews it — and placed
blocks change only through Edit mode. No grid walk anymore; the method
touches the single bound block directly.

Comment/docs trued up in `BlockEditor.cs` (propagation header, bind-order
note, place-clears-instance-edit note) and `BuildSession.EditingInstance`.

## Behavior change (deliberate)

The session-96 "drag a slider, watch all placed rotors respin" behavior is
retired. Batch retune now means: bind each block in Edit mode, or re-place.
Rotor adopted-foil live sync (`RotorBlock.OnEnable` → `PitchChanged`) is
unaffected — it hangs off `SetPitch`, which instance-edit still drives.

## Tests

`BuildSessionInstanceEditTests` propagation-contract tests updated to the
new guard shape (shared `SimulatePropagation` helper mirroring the method):

- `WhenInstanceBound_OnlyBoundBlockUpdates` — kept.
- `WhenNoInstanceBound_AllMatchingBlocksUpdate` → renamed
  `WhenNoInstanceBound_NoPlacedBlockUpdates`, asserts the inverse.
- `ClearInstanceEdit_RestoresAllBlockPropagation` → renamed
  `ClearInstanceEdit_StopsAllLivePropagation`.
- New: `BoundInstanceOfOtherType_DoesNotUpdate` (id filter inside
  instance-edit).
