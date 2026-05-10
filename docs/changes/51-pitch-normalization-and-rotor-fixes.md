# 51 — World-intent pitch + rotor blade shift fix + rope adoption

> Three real bugs traced and fixed; one (rope chain not visualising
> in the garage) still under investigation — couldn't reproduce from
> code reading.

## What changed

### 1. World-intent pitch on every placement (not just mirror mode)

The variant panel's pitch slider now means "tip tilts toward world
+Y by N degrees" on every face the foil can be placed on. Previously
it was a local-frame value, so the same hand-set pitch produced
opposite visual tilts on opposite faces — inconsistent unless the
player happened to be in mirror mode.

Implementation:
- New [`BlockOrientation.NormalizePitchForUp`](../../Assets/_Project/Scripts/Block/BlockOrientation.cs) static method converts world-intent ↔ local
  by checking the sign of the foil's local-X world-Y component (the
  geometric definition of "this rotation around chord by positive
  angle would tilt the tip toward world -Y instead of +Y").
- [`BlockEditor.TryPlace`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs),
  [`BlockEditor.TryMirrorPlace`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs),
  [`BlockEditor.DriveGhostRenderer`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs),
  and [`BuildSession.TryPlace`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs)
  all convert world → local at placement time using the placement's
  up direction. Both primary and mirror placements normalize
  independently, so the dedicated mirror-axis sign-flip rule
  (`BlockMirror.MirrorPitch`) becomes redundant for placement code
  (it stays for backward compat with `BlueprintBuilder.MirrorX/Z`'s
  pure-data mirror, where the math reduces to the same outcome).
- Rotors bypass the conversion — their pitch is "collective",
  intrinsically a local-frame value applied uniformly to adopted
  blades.

### 2. Rotor blade mesh shift always extends outward

The user reported foils on a `span > 1` rotor shifting *into* the
hub by ~1 cell and crossing each other. Traced to
[`AeroSurfaceBlock.ComputeWingShift`](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs)'s
rotor-mode branch: it used `signX = sign(cellPos.x)` to pick the
shift direction, but post-adoption foil-local +X is always
*-radial* (toward the hub) regardless of which lateral cell the
blade started on. So the previous logic shifted -X-side blades
outward (correct, by accident) and +X-side blades inward (wrong).
For span > 1 the inward-shifted blade visually crossed through the
rotor center.

Fix: rotor-mode shift is unconditionally `(-magnitude, 0, 0)` —
foil-local -X is the radial-outward direction for any blade adopted
by `RotorBlock.AdoptAdjacentAerofoils`, independent of which
lateral cell or which way the spin axis points.

### 3. Ropes attachable to rotors (rule of cool)

[`BlockConnectivity.AcceptsPlacement`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)'s
mechanism-cube lateral-face rule now allows `Rope` placements
alongside `Aero` / `AeroFin`.
[`RotorBlock.AdoptAdjacentAerofoils`](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
gains a parallel `AdoptRope` path: ropes get reparented to the
spinning kinematic hub the same way blades do. The rope's own
`OnTransformParentChanged` → `Rebuild` re-anchors its verlet chain
(or static visual in the garage) to the new hub Rigidbody, so the
rope swings outward via centrifugal effect when the rotor spins
in the arena.

`DestroyLiftRig` reparents adopted ropes back to their original
parent on rotor teardown, parallel to the foil reparent path.

## What's still under investigation

**Rope chain not visualising in the garage.** Traced the code path
end-to-end — `RopeBlock.OnEnable` → `Build` (chassis is kinematic
in garage → `_builtKinematic = true`) → `BuildStaticVisual`, which
should draw a tinted cylinder from the rope's bottom face dangling
to full default length. Without a repro of "what does it look like
when the user places a rope?" (screenshot or step-by-step), I can't
narrow further. Tangential fixes here may help (e.g. session 50's
tip-placement rebuild).

If the chain still doesn't draw after this commit, helpful next
info: (a) does the rope's host cube show as a visible block? (b)
when you place a hook below it, does the cylinder appear or
nothing? (c) anything in the Console about the rope?

## Files

`BlockOrientation.cs` (new), `BlockConnectivity.cs`,
`RotorBlock.cs`, `AeroSurfaceBlock.cs`, `BlockEditor.cs`,
`BuildSession.cs`. New EditMode test:
`BlockOrientationTests.cs`.

## Verification

1. From-scratch build → place CPU + cube + rotor (auto-cube on
   spin axis face) → set foil pitch slider to +18° → place foils
   on all four perpendicular cube faces. All four should visually
   tilt their tips toward +Y. (Same on both faces of any other
   block too — the rule is universal now.)
2. Same chassis → exit build mode → launch arena. The four blades
   should spin around the hub with consistent tilt.
3. Place a foil with `span > 1` (variant panel slider) on a rotor
   mechanism's +X face. The blade mesh should extend outward past
   the cell's outer face, *not* into the rotor center. Same for
   the other three lateral faces.
4. Place a rope on a rotor mechanism's lateral face (variant panel
   slider should now show the rope as placeable there). Launch to
   arena. The rope should rotate around the rotor with the spinning
   hub.
