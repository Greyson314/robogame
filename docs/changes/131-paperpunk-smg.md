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
- **Silhouettes are steampunk / old-timey, not WWII** (user steer, second
  revision): boiler drums, brass bands, pressure gauges, hooped tapered
  tubes, flared muzzles, carriage cheeks + scalloped wheels, cascabel
  balls, cartoon bombs. No Sten mags, no field-gun telescoping, no
  finned mortar shells.
- **Cartoonish / lighthearted, via proportion not decoration** (user
  steer: the whole game should feel fairly cartoonish/cutesy; a pew-pew
  weapon should feel lighthearted): chubby chamfered volumes, one
  oversized lovable detail per view, stubby fat barrels. Candidate
  headline language for art-direction.md when the direction is committed.

## What shipped

- `artgen/paperlib.py` — shared helpers for the family: `card_panel`
  (2D silhouette → extruded card sheet with face/edge materials),
  `arc_shell`/`tube` (rolled-card tubes; full tubes carry a 3° seam slit at
  the bottom — the rolled sheet's edge), gear/brad/loft/export. Exporter
  renames per-weapon yoke/muzzle empties to `Turret`/`ShootPoint` at export
  time (Blender object names are global; three weapons share the scene).
- `artgen/smg_paperpunk.py` → `SMG_Paper.fbx`. Square-ish mechanism, short
  barrel, cutesy proportions (user steers: not cannon-shaped, and the
  pew-pew gun should feel lighthearted): chubby chamfered-loaf card
  receiver, oversized LEFT ammo-reel canister (red stripe, brass hub),
  oversized gauge dial on top, brass strap belt, rear kraft cap + brass
  fastener, RIGHT casing chute with red slot, fat stubby split-tube
  barrel with bulbous muzzle ring, chunky brass bead. Groove + red
  channel signature intact. Asymmetry preserved (user: blocks are
  symmetric, weapons shouldn't be).
- `artgen/mortar_paperpunk.py` → `Mortar_Paper.fbx`. Old bombard on the
  fireworks-tube honesty (real firework mortars are cardboard): short fat
  tapered tube, hoop rings, flared mouth with red bore, laminated breech +
  cascabel ball, LEFT shelf of cartoon paper bombs with brass fuse studs.
  Authored level; MortarBlock lobs the yoke at runtime.
- `artgen/cannon_paperpunk.py` → `Cannon_Paper.fbx`. The cartoon cannon
  archetype: stepped carriage cheeks + scalloped card wheels on brass
  axles, one long tapered rolled cone with hoops, flared muzzle lip,
  cascabel ball + brass knob, cannonball pyramid (laminated disc balls)
  on the deck corner.
- All exported with `FBX_SCALE_ALL`, meshes + empties, WeaponModelRig
  convention (root yaws → `Turret` pitch yoke → `ShootPoint`). SMG pitch
  sweep visually verified ±22° in Blender. Supersedes the deleted
  `smg_origami.py` exploration.

## Unity wiring (done, same session)

- All three definitions point at the paper models: scale 1, offset
  (0, −0.5, 0) — model origin is the base-plate bottom at the block's
  bottom face. Verified live in the garage (SMG on the Tank bot tracking
  the reticle; mortar/cannon spot-instantiated). The CoplayDev MCP server
  wasn't in this Claude session (Unity opened mid-session), so the wiring
  ran over direct JSON-RPC to `http://localhost:8080/mcp`.
- **Axis-conversion finding:** Blender FBX export needed a manual frame
  conversion (now in `paperlib.export_tree`): mesh data + all local
  matrices conjugated into Unity's frame (up +Y, fwd +Z), exporter told
  to do no conversion. `bake_space_transform=True` mangles children of
  empties; the exporter's default file-level conversion only adds a root
  compensation rotation that `WeaponModelRig` strips (it forces identity
  on root and `Turret`, so the data itself must be Y-up with identity
  node rotations). Caveat: the imported prefab root still shows a
  −90°X default rotation from the Z-up file header — harmless for
  weapons (rig strips it), but raw `Instantiate` callers must reset
  `localRotation` to identity.
- **Stale-editor clobber struck again**, same value as last time: the
  editor's `SaveAssets` reverted `Cannon_Default._damage` 110 → 60
  (session-127 buff). Caught by the git-status inspection rule, restored
  to 110 in-editor. Scene/material churn (symmetric re-serialization
  noise) committed separately as chore.

## Open / next
- Texture-stage ideas parked: paper grain, corrugation on thick cut edges,
  printed stencils/part numbers on the big white side plates.
- If the profile read of the barrel feels muddy in-game, laminate each
  half-shell (white-kraft-white striping on the groove lips).
- Direction not yet committed in `art-direction.md` — this is one asset's
  trial; palette/style banner still in exploration mode.
