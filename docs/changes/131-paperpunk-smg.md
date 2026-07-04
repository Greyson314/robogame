# 131 — Paper-punk weapon family: SMG, mortar, cannon

**Date:** July 4, 2026
**Intent:** Test a design-vibe theory on the SMG using the session-130 Blender
pipeline. Started as "origami", user re-framed mid-session to **paper-punk**:
the design is a machine, the fabrication material is paper. No fold/crease
storytelling.

## The paper-punk language (candidate art direction)

- Every part reads as a **cut sheet with visible thickness** — flat card faces
  in warm white, **kraft-brown cut edges** on every rim.
- **Lamination** as construction: stacked card layers with stepped silhouettes
  (receiver core + proud side plates, 3-layer yaw gear, glued collar discs).
- **Fasteners are the mechanism read:** brass paper-fastener brads pin the
  pitch axis through A-frame card gussets (open middle — daylight through the
  yoke).
- **Ink accents only where they mean something:** vermilion = projectile
  channel interior + magazine ("printed" ammo read at gameplay distance).
- Rounded parts are **rolled card**: the barrel is a tube split horizontally
  into two half-shells, open groove down the middle, muzzle showing the
  kraft annular cut + red channel bore (projectile theme, explicitly not
  hitscan-laser).

## What shipped

- `artgen/paperlib.py` — shared helpers for the family: `card_panel`
  (2D silhouette → extruded card sheet with face/edge materials),
  `arc_shell`/`tube` (rolled-card tubes; full tubes carry a 3° seam slit at
  the bottom — the rolled sheet's edge), gear/brad/loft/export. Exporter
  renames per-weapon yoke/muzzle empties to `Turret`/`ShootPoint` at export
  time (Blender object names are global; three weapons share the scene).
- `artgen/smg_paperpunk.py` → `SMG_Paper.fbx`. Asymmetry pass on the
  receiver (user: blocks are symmetric, weapons shouldn't be): stepped
  feed-cover deck, Sten-style horizontal LEFT magazine (red rims), RIGHT
  ejection port + charging handle, offset sight fins. Underslung mag gone.
- `artgen/mortar_paperpunk.py` → `Mortar_Paper.fbx`. Fireworks-mortar
  concept (real firework mortars are cardboard tubes): fat seamed tube,
  wrap bands, laminated breech + brass center, LEFT card rack of three
  red-nosed paper shells. Authored level; MortarBlock lobs the yoke at
  runtime.
- `artgen/cannon_paperpunk.py` → `Cannon_Paper.fbx`. Heavy of the family:
  4-layer yaw gear, telescoping rolled barrel with band joints, kraft
  breech cylinder + handle (right), recuperator tube above-right, single
  cradled shell (left).
- All exported with `FBX_SCALE_ALL`, meshes + empties, WeaponModelRig
  convention (root yaws → `Turret` pitch yoke → `ShootPoint`). SMG pitch
  sweep visually verified ±22° in Blender. Supersedes the deleted
  `smg_origami.py` exploration.

## Open / next

- **Unity wiring pending an editor session:** import the three FBX files,
  then point `Weapon_Smg` / `Mortar_Default` / `Cannon_Default` `_turretModel`
  at them (GUIDs exist only after import), set scale/offset (authored at
  real size for a 1 m block, so scale ≈ 1, offset ≈ 0 vs. the Fatty
  placeholders' 0.35 / −0.45), verify aim tracking + commit `.meta`s.
- Texture-stage ideas parked: paper grain, corrugation on thick cut edges,
  printed stencils/part numbers on the big white side plates.
- If the profile read of the barrel feels muddy in-game, laminate each
  half-shell (white-kraft-white striping on the groove lips).
- Direction not yet committed in `art-direction.md` — this is one asset's
  trial; palette/style banner still in exploration mode.
