# 52 — Rope chain extends outward + chain-only hologram

> User reported ropes placed on the bottom of crafts pushed the chain
> UP through the chassis. They asked for a redesign: no visible "rope
> base" cube, chain extends outward from the chassis face the rope
> was placed on, hologram shows the entire rope, both ends symmetric.

## What changed

### Chain extends along mount-up (away from chassis), not toward it

Three call sites in [`RopeBlock.cs`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
flipped from `-transform.up` to `+transform.up`:
- **Live anchor.** `hubAnchorWorld` was `position + transform.up * 0.5`
  (outer face of the rope cell). Now `position - transform.up * 0.5`
  (chassis-side face).
- **Tip spawn.** `hubAnchorWorld - transform.up * fullLen` was
  spawning the tip body BACK toward the chassis. Now
  `hubAnchorWorld + transform.up * fullLen` — tip at the chain's
  free end, away from chassis.
- **Initial particle layout.** Was spaced along `-transform.up`
  (toward chassis). Now along `+transform.up` (away).

For a default up=+Y rope (existing presets), the chain now extends
UP instead of DOWN in static mode. In arena, gravity takes over and
the chain settles however it physically wants. For the bug the user
hit — rope placed on a chassis -Y face — the chain now correctly
hangs DOWN below the chassis instead of crossing back up through it.

### Host cube hidden in every mode

[`BuildStaticVisual`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
no longer re-shows the rope's host cube. The cube collider stays
for damage / build-mode targeting (per `BlockVisuals.HideHostMesh`'s
contract), but the visible rope is just the chain. Matches the
user's "no rope base block, just a rope" framing.

### Hologram = full chain length

[`BlockGhostFactory.BuildRope`](../../Assets/_Project/Scripts/Gameplay/BlockGhostFactory.cs)
no longer renders a small cube-plus-stub. It now spans a single
cylinder from the chassis-side face of the placement cell along
local +Y by `segments × segLen` cells, where `segments` comes from
the variant panel's dim (defaults to 8 if not set). A length-4 rope
hologram is ~1.6 cells long; the player sees the full extent of
what they're about to place.

### Tip-block placement direction flipped

[`BlueprintBuilder.RopeWithHook`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)
and `RopeWithMace` now place the tip at `ropeCell + Vector3Int.up`
(default mount-up direction) instead of `ropeCell + Vector3Int.down`.
Matches the new "tip lives at the chain's free end" convention. Tests
updated accordingly.

## What this doesn't (yet) do

The user's third bullet — "the rope segment that attaches the rope
to the bot should use the same physics/operating function as the
bottom segment" — interpreted as visual symmetry. Both ends of the
chain are now uniform cylinder ends; nothing special about the
top. Physics-side, the chassis-anchor end stays anchored to the
chassis Rigidbody and the free end is a kinematic tip body — that
asymmetry is fundamental to the verlet sim and intentional. If the
user wanted a true "two free ends" rope (e.g. ungrounded chain
between two attached chassis points), that's a separate feature.

The shipped `StressRopeTower` preset still places ropes with
default up=+Y; with the new convention, those chains extend
upward in static mode. They'll fall to gravity in arena. Re-running
the scaffolder would let an author set per-rope mount-ups for the
desired chain orientation.

## Files

`RopeBlock.cs`, `BlockGhostFactory.cs`, `BlueprintBuilder.cs`,
`BlueprintBuilderTests.cs`. Session log here.

## Verification

1. Build mode → place a CPU + cube → place a rope on the cube's
   -Y (bottom) face. The chain should now dangle DOWN below the
   chassis instead of crossing through it.
2. Place a rope on the cube's +X face. The chain should extend
   sideways along +X.
3. Look at the rope's host cube — should be invisible. Only the
   chain cylinder is rendered.
4. Adjust the rope's segment-count slider in the variant panel.
   The hologram should scale to match.
5. Use `RopeWithHook` in a test or scaffolder — hook lands at
   `rope.cell + (0, 1, 0)` for default up=+Y.
