# Session 33 — Hook / Mace placeable in build mode

> Status: **shipped, untested in-engine.** Rope now skips its live
> Verlet rig when the chassis is parked-kinematic, so adjacent
> Hook / Mace blocks stay at their grid cells with native colliders
> instead of being reparented to a scene-root tip Rigidbody. Ghost
> previews for Hook / Mace / Rope / Rotor are no longer cube
> fallbacks.

## Why this session

User report: *"there is no way to add a hook or a mace to the end of
a rope in the garage's build mode. There is also not hitbox on those
assets to be able to remove them from the bot from build mode."*

Diagnosis: Hook + Mace were already in the BlockDefinitionLibrary
(15/15 defs wired) and the BuildHotbar enumerates them under the
Weapons tab. The real bug was upstream — `RopeBlock.Build()` ran the
full live Verlet rig + scene-root tip Rigidbody + tip-block adoption
the moment the chassis spawned, *even on a parked-kinematic chassis
in the garage*. Adoption reparents the Hook / Mace under the rope's
scene-root tip body, which makes
`BlockEditor.UpdateTarget`'s `block.transform.IsChildOf(_buildMode.Chassis)`
guard reject raycasts on the now-orphan tip block. Right-click
removal silently failed. The rope's own host cube also had its mesh
hidden by `BlockVisuals.HideHostMesh`, so there was no visible target
to aim at when adding more blocks.

`RotorBlock.BuildLiftRig` solved the same shape of problem in
session 18 with an `if (chassis.isKinematic) return;` early-out plus
a Rebuild-on-toggle path. RopeBlock just lacked the gate.

## What changed

### `Movement/RopeBlock.cs`

1. New `_builtKinematic` field captures the chassis's
   `isKinematic` state at Build time.
2. `Build()` short-circuits to a new `BuildStaticVisual` path when
   `_hubRb.isKinematic == true`. No tip Rigidbody, no chassis-tip
   joint, no Verlet registration, no `TryAdoptTipBlock`.
3. `BuildStaticVisual` parents one cylinder under the host transform
   (so it tracks chassis transforms without a solver), hanging from
   the cell's bottom face for the full `segLen × N` length, tinted
   to `_segmentColor`. The host cube's MeshRenderer is re-enabled so
   the cell itself is a visible target.
4. `Update()` now also rebuilds when `current.isKinematic !=
   _builtKinematic`. This catches the garage's spawn → park
   transition: blocks first OnEnable while the chassis is
   non-kinematic (live rig builds), then `GarageController.ParkChassis`
   flips `isKinematic` true, the next Update detects the mismatch,
   `Rebuild()` runs `DestroyChain(reparentTip:true)` (which restores
   the Hook / Mace to its original chassis grid parent via
   `ReleaseAdoptedTip`) and reconstructs the static path. One frame
   of dangling-rope visible at startup; acceptable.

### `Block/BlockVisuals.cs`

`HideHostMesh` no longer nulls the MeshFilter's `sharedMesh` — only
disables the renderer. Frees up a `SetHostMeshVisible(host, bool)`
helper for blocks (rope) that want to toggle the host cell's
visibility between live and build modes. All other callers remain
behavior-identical (host invisible).

### `Gameplay/BlockGhostFactory.cs`

New ghost-shape methods for Hook (J of three cubes), Mace (sphere +
6 axial spike cubes), Rope (compact cube + short stub cylinder),
Rotor (mast + thin disc with crossbar). Replaces the cube fallback
for those four IDs. BombBay and Cannon stay on the cube fallback
intentionally — their bodies are cube-shaped, and the cannon's
barrel is too small to read at unit-cell scale.

## Audit findings (all 15 BlockIds)

Concrete bugs: only the rope cluster (Rope + Hook + Mace).
Cosmetic: Rope, Rotor, BombBay, Cannon all rendered as cube ghosts
during placement preview — fixed Rope and Rotor in this pass. The
cube fallback remains for BombBay (cube body, no distinguishing
external feature at preview scale) and Cannon (small barrel doesn't
help at preview scale; revisit if it becomes confusing in playtest).

## Latent issue flagged for follow-up

`RotorBlock.BuildLiftRig`'s kinematic check fires on a chassis that
isn't yet parked (since `ParkChassis` runs after `ChassisFactory.Build`
returns). The session-18 comment claims "foils stay under the chassis
grid root in the garage" but the actual flow has BuildLiftRig running
with a non-kinematic chassis, then the chassis flips kinematic with
no rebuild trigger. The rotor's FixedUpdate runtime gate suppresses
*lift application* under kinematic chassis, but adopted foils have
already been reparented to the scene-root hub. Same fix shape would
work — track `_builtKinematic`, rebuild on transition. Not pulled
into this session to keep the change scoped to the user's report.
