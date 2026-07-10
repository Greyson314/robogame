# 139 — Bat-wing aerofoil "swimming" animation study (Blender)

**Intent.** Animate the bat-wing aerofoil study (`artgen/inv_wing.py`)
with a "swimming through the air" motion — flapping, but mechanical —
while the attach point stays static. Blender-only art study; nothing
in Unity changes.

## What shipped

`artgen/inv_wing_anim.py` — builds a fresh copy of the bat-wing via
`inv_wing.build()`, joins all parts into one mesh, rigs it, and keys a
looping 48-frame @ 24 fps cycle. `build(loc)` is idempotent like the
other studies.

How it works:

- **Rig.** A static `Base` stub plus a 3-bone chain (`Flap0..2`) from
  the root boss out along the leading spar (0°), hinge joints at radii
  0.5 / 1.05 from the pivot. First cut ran the chain down the mid-fan
  (−46°); user flagged the leading finger as crooked — oblique hinge
  axes made it skew, not hinge. Aligning the chain with the leading
  spar fixed it; the trailing fan inherits the wave via the membrane.
- **Skinning.** Procedural radial weights — distance from the fan
  origin in the wing plane, hat functions per segment with narrow
  (±0.09) blend zones at the hinges. Three near-rigid panels with
  tight hinge bands is what makes it read mechanical rather than
  organic. Boss / mount / cord whipping are fully weighted to `Base`,
  so the attach point never moves (verified: root-zone verts constant
  z across the whole cycle; tip sweeps ±0.45 m).
- **Motion.** Traveling wave: each bone runs the same sine flap with
  ~0.9 rad phase lag per segment (root leads, tip follows), a 22%
  second harmonic for a fast power stroke / slow recovery, and a
  quarter-cycle-lagged twist about the bone axis that grows toward the
  tip — the membrane sculls, which is the "swimming" read. Keys every
  2 frames, frame 49 == frame 1, clean loop.

## Current philosophy: Foils vs Wings (user call, this session)

Two distinct aero parts going forward:

- **Foils** — look and act like the current foils (`inv_foil.py` →
  `Foil_Inv.fbx` on `BlockDef_Aero`). Unchanged.
- **Wings** — the bat-wing shape (`inv_wing.py`). Still side-mounted
  like foils. Flapping (this swim animation) plays **when powered** —
  future feature; until powered-state exists, the animation is simply
  always on.

To match, `inv_wing.py` was changed from its under-boss brass plate to
a foil-style brass side-mount disc on the root face (−X). The animated
study inherits it automatically (it builds from `inv_wing`).

## Naming gotcha

The copy uses prefix `InvSwim_`, not `InvWingAnim_`:
`inv_wing.build()` clears every object starting `InvWing_`, and that
prefix-match would delete the animated copy on any static-study
rebuild.

## Rebuild

```python
import inv_wing_anim, inv_wing
inv_wing_anim.build(loc=(0.0, 2.5, 1.0))  # consumes a fresh wing copy
inv_wing.build(loc=(0.0, 0.0, 1.0))       # optional static side-by-side
```

(`inv_wing_anim.build` calls `inv_wing.build` internally, so run the
static study *after* if you want both in the scene.)

## Known limits / next steps

- Study only — not exported. If it graduates, the keys bake to FBX
  (armature + baked action) via the usual `inv_export` path, and the
  Wing needs its own block def (it is NOT `BlockDef_Aero` — that stays
  the foil). Wing-frame bake like `export_foil()` applies. Unity-side
  animation hookup: always-on until "powered" exists.
- Chain axis is a single ray along the leading spar; trailing spars
  (up to −92° off-axis) take oblique hinge bends, hidden well by the
  membrane. A per-spar rig would be the upgrade if a closer look ever
  demands it.
