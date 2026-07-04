# 131 — Paper-punk SMG: first bespoke weapon model

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

- `artgen/smg_paperpunk.py` — parametric build, `_card_panel` helper
  (2D silhouette → extruded card sheet with face/edge materials). Idempotent;
  exports on run. Supersedes the deleted `smg_origami.py` exploration.
- `Assets/_Project/Art/Models/Weapons/SMG_Paper.fbx` — exported with
  `FBX_SCALE_ALL`, meshes + empties.
- Hierarchy matches the WeaponModelRig convention (session 120): root yaws,
  child empty `Turret` is the pitch yoke, `ShootPoint` marks the muzzle
  inside the open groove. Pitch sweep visually verified ±22° in Blender
  (receiver clears the gusset spacer; slight magazine/gear proximity at
  extreme depression is accepted).

## Open / next

- **Unity wiring pending an editor session:** import FBX, then point
  `Weapon_Smg.asset` `_turretModel` at it (GUID exists only after import),
  set scale/offset (model is authored at real size for a 1 m block, so scale
  ≈ 1, offset ≈ 0 vs. the Fatty placeholder's 0.35 / −0.45), verify aim
  tracking + commit `.meta`s.
- Texture-stage ideas parked: paper grain, corrugation on thick cut edges,
  printed stencils/part numbers on the big white side plates.
- If the profile read of the barrel feels muddy in-game, laminate each
  half-shell (white-kraft-white striping on the groove lips).
- Direction not yet committed in `art-direction.md` — this is one asset's
  trial; palette/style banner still in exploration mode.
