# Session 41 — Wheels: Robocraft-style side-mount stem + tyre

> Status: **shipped, untested in-engine.** Replaces the old "wheel sits
> at a chassis cell with an invisible host cube; suspension hangs the
> tyre below" pattern with the Robocraft model: the wheel block is a
> visible stem (axle) jutting out from a side face of a host cube, with
> the tyre at the end of the stem. Default Tank, Buggy, and the
> "+ New Robot" starter all updated. Existing user-saved blueprints
> still load but their wheels will look weird until re-saved (stem and
> tyre get the new visual at the old top-mount position).

## Why this session

User's note: *"In Robocraft, wheels were generally structured as a stem
jutting out of the side of a block, with a wheel of whatever size at the
end. Our existing wheels are attached to an invisible block above them.
Switch to Robocraft's pattern and find a more suitable temporary asset
than a gray cylinder."*

## What changed

### Wheel block visual + dynamics

[`Movement/WheelBlock.cs`](../../Assets/_Project/Scripts/Movement/WheelBlock.cs)
visual rig is now stem + hub + tyre + hub-cap, all built procedurally:

- **Stem** — gunmetal-grey cylinder along block-local +Y, from -0.5
  (host face) to 0 (cell centre). 0.18 m diameter, 0.5 m long. Static;
  doesn't move with steering or suspension.
- **Hub** (empty pivot) — yaws for steering, drops for suspension.
- **Spin** (empty pivot, child of hub) — rotates around the axle
  (hub-local +Y) for rolling.
- **Tyre** — near-black disc, full radius (0.7 m diameter default),
  0.18 m thick along the axle. Sits inside the spin pivot.
- **Hub cap** — silver cylinder ~half tyre diameter, slightly outboard
  so the wheel reads as "tyre + hub" rather than a plain dark disc.

Per-renderer colour is via `MaterialPropertyBlock` so we don't churn
per-instance materials and keep batching intact (mirrors
`BlockGrid.ApplyTint`).

The dynamics rewire to match the new mount semantics:

- **Suspension extension** is now along world-down (gravity), not
  block-local -Y. Converted to block-local via
  `transform.InverseTransformDirection(Vector3.down)` before applying
  to `_hub.localPosition`. So a side-mounted wheel's tyre still hangs
  toward the ground regardless of how the block is oriented on the
  chassis.
- **Steering** rotates around world-up, not block-local +Y. The hub's
  steered world rotation = `AngleAxis(yaw, Vector3.up) * transform.rotation`,
  converted back to local via `Inverse(transform.rotation)` so the
  smoothing slerp still operates in `localRotation`.
- **Spin** rotates around hub-local +Y (= the steered axle direction).
  Replaces the old "spin around X" assumption that only worked for
  top-mounted wheels.
- **Roll-direction sensing** uses `_hub.forward` (world space), which
  for a side-mounted wheel with no steering still resolves to chassis
  +Z thanks to `OrientationFromUp(±X)`'s convention of preserving local
  +Z → chassis +Z.

### Side-mount placement constraint

[`Block/BlockDefinition.cs`](../../Assets/_Project/Scripts/Block/BlockDefinition.cs)
gains a `SideMountOnlyRaw` SO field. New
[`BlockConnectivity.IsValidMountFace(def, up)`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)
returns false when the block requires side mount and the candidate up
is ±Y. Hardcoded fallback list covers `BlockIds.Wheel` /
`BlockIds.WheelSteer` so shipped assets behave correctly without
re-authoring.

[`Gameplay/BlockEditor.IsValidPlacement`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
calls the new check after the leaf-host check. Aiming at the top or
bottom of a cube with a wheel selected → red ghost.

### Ghost preview

[`Gameplay/BlockGhostFactory.BuildWheel`](../../Assets/_Project/Scripts/Gameplay/BlockGhostFactory.cs)
mirrors the placed-block layout (stem + tyre + hub cap) so the build-mode
preview matches what spawns. Still no shared helper here — the wheel rig
is more parts than the foil one and the ghost can be a rougher
approximation, but the proportions and offsets line up by hand.

### Default blueprints

Three layouts updated to side-mount the wheels and fill the
just-vacated floor cells with cubes:

- [`Tools/Editor/RobotLayouts.PopulateTestRobot`](../../Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs) (in-scene Tank).
- [`Tools/Editor/GameplayScaffolder.BuildGroundEntries`](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs) (persisted Tank blueprint).
- [`Tools/Editor/GameplayScaffolder.BuildBuggyEntries`](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs) (persisted Buggy).
- [`Block/StarterBlueprints.CreateGroundStarter`](../../Assets/_Project/Scripts/Block/StarterBlueprints.cs) (runtime "+ New Robot").

Tank chassis is now 5 cells wide (3 cubes + 2 wheels each side).
Buggy is 5 wide × 3 long with a 3×3 cube floor. Starter matches the
Buggy shape with 6 wheels (3 per side).

Side note: the starter blueprint had a latent bug — its old version
declared cubes at wheel cells THEN tried to add wheels at the same
cells. `BlockGrid.PlaceBlock` silently rejected the duplicates so the
starter actually had no wheels. Fixed by the layout rewrite.

## Notes for the next session

- **Existing user saves will look weird until re-saved.** Old blueprints
  with wheels at top-mount positions still load (validator unchanged),
  but the new visual rig draws a stem pointing down with the tyre
  hanging below — a stem-floating-in-air look. Function works
  (suspension still raycasts down). Re-save fixes the visual.
- **Visual asset is still procedural primitives.** No imported wheel
  mesh — just darker/lighter cylinders to give the wheel some
  visual variety. A real tyre asset (Bitgem, Synty, etc.) drops in by
  swapping `BlockVisuals.GetOrCreatePrimitiveChild` calls in
  `EnsureRig` for prefab spawns.
- **Wheel collider** is the auto-attached cylinder collider on the tyre
  primitive. It rotates around the hub's steered axle, so steering
  visually + dynamically agrees. Unity self-collision rules keep the
  tyre cylinder from interacting with the host cube collider (both
  attached to the chassis Rigidbody).
- **`SideMountOnly` is a binary placement constraint.** If a future
  block needs e.g. "top OR bottom only" or a more complex face-mask,
  upgrade to a per-axis bitmask on `BlockDefinition`. The `IsLeaf`
  flag could also be unified into the same mask.
- **Wheel spin axis depends on `OrientationFromUp` preserving local +Y
  → mount-up.** All current wheel placements use ±X / ±Z for up, which
  the rotation handles correctly. If a future blueprint authors a
  wheel with up=±Y, the side-mount placement check would have rejected
  it in build mode, but the validator still allows it for authored
  blueprints — the wheel would render with a vertical axle and look
  wrong. Add the side-mount rule to `BlueprintValidator` if shipped
  presets ever risk this.
