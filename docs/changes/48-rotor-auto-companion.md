# 48 — Rotor placement parity with the preset

> Follow-up to session 47. The user pointed out that the from-scratch
> rotor still didn't behave like the helicopter preset's rotor: a
> placed rotor came with no mechanism cube + no obvious place to mount
> blades, so the player ended up with a single-cell rotor whose visual
> bars/disc lived at a different cell than the rotor itself, and
> aiming at the bars hit empty space (or the rotor's leaf-rejected
> lateral face).

## What changed

### Auto-place the mechanism cube on rotor placement

[`BlockEditor.AutoPlaceCompanionsOf`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
runs after every successful primary or mirror block placement. For a
`Rotor`, it places a structural `Cube` at `rotorCell + rotorUp` (the
spin-axis cell, the only face the rotor accepts a host on). Skips
silently if the cell is already occupied. This makes a placed rotor
immediately match what `BlueprintBuilder.RotorWithFoils` produces in
the helicopter preset — bars + disc visually overlap the cube cell,
the player aims at the bars, and the targeting hits the cube's
collider with the right face normal for blade placement.

### Cascade-remove the auto-cube when its only dependent is the rotor

The auto-placed cube sits on the rotor's spin-axis face and starts
with no other neighbours. Without cascade logic, the orphan check
permanently blocks rotor removal: removing the rotor would orphan
the cube. New
[`BlockEditor.ResolveRotorCascadeCell`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
detects that exact case (rotor → cube → no other neighbours) and
returns the cube cell so `TryRemove` can co-remove both. When the
cube *does* have other neighbours (blades, chained structure), the
cascade is skipped and the regular orphan check correctly blocks the
rotor removal until the dependents are cleared.

[`BlockGraph.WouldOrphanIfRemoved`](../../Assets/_Project/Scripts/Block/BlockGraph.cs)
gains a two-cell-ignore overload for the cascade orphan check. The
mirror-removal path uses the same logic so a mirrored rotor + cube
pair tears down cleanly.

### Re-show the mechanism cube when its rotor is destroyed

[`RotorBlock.OnDestroy`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
restores the mechanism cube's `MeshRenderer.enabled` if it was hidden.
Without this, removing a rotor in build mode (without cascade) leaves
the cube permanently invisible. Cheap — the call is a single bool
toggle, harmless on chassis-teardown destroy paths because the
cube's own GameObject is also being destroyed.

## On the user's "differently-functioning" framing

After this change, a from-scratch rotor and a preset rotor reach the
same configured state at placement time:

- 1 rotor cell at the rotor's grid position.
- 1 invisible structural cube at `rotor + spinAxis`.
- 0 blades (the user adds these manually).

The preset adds 4 blades on top of that for convenience; the
build-mode editor leaves blade count to the player. Per-side blade
placement now works identically in both: aim at the bar tips, click,
done.

## Files

`BlockEditor.cs`, `BlockGraph.cs`, `RotorBlock.cs`. No new tests
(the cascade logic touches a Unity-component path that needs a scene
to exercise — covered by manual play-testing per the verification
checklist below).

## Verification

1. New blueprint → place CPU + cube → place rotor on cube's +Y face
   (default spin axis). Confirm: a cube auto-spawns on top of the
   rotor (invisible, but the rotor disc + bars sit at the correct
   cell).
2. Same scene → place blades on the four perpendicular faces of the
   auto-placed cube (aim at bar tips). All four should accept.
3. Remove all four blades, then right-click the rotor. Both the
   rotor *and* the cube should disappear (cascade).
4. Rebuild: place rotor, then place a structural cube on top of the
   auto-cube (extending the chassis upward). Right-click the rotor.
   It should now reject with "would orphan" — the auto-cube has a
   real dependent and the cascade correctly stands down.
5. Mirror mode on, place a rotor on a side cell. Both the primary
   and mirrored rotor should each get an auto-cube. Removing one
   removes both rotor + cube on its side via cascade.
