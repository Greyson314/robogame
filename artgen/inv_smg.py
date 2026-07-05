# artgen/inv_smg.py — inventor SMG: the crank-organ pellet gun.
# Session-132 full reroll (user steer: fit the inventor universe, old
# SMG shapes discarded — the paper-punk loaf bones are gone). Concept:
# a hand-cranked pellet thrower straight off the workshop bench. A
# wine-cask receiver tumbles the pellets, a linen pellet sack hangs
# off a brass feed pipe on the LEFT (the oversized lovable detail), a
# crank + exposed brass gear on the RIGHT sell rate-of-fire as a
# mechanism, and a turned barrel with a flared lip carries the
# vermilion bore. Asymmetric by rule; forward -Y; family yaw-gear base.

import bpy
from math import radians, cos, sin, tau

import paperlib as pl
import inventorlib as il

PFX = "InvSMG_"

PIVOT_Z = 0.47
GEAR_TOP = 0.095
GEAR_R_OUT = 0.32
SCALE = 0.5 / GEAR_R_OUT
RAKE_DEG = 3.0
CZ = 0.03            # cask/barrel axis height above the pivot


def build(loc=(7.0, -5.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Family yaw gear — laminated oak, walnut cut edges.
    pl.card_panel(f"{PFX}GearBottom", pl.ngon_pts(12, 0.29), 0.03, 'Z',
                  0.015, [m["wood"], m["wood_dark"]], parent=root)
    pl.card_panel(f"{PFX}GearTeeth", pl.gear_profile(10, 0.26, GEAR_R_OUT),
                  0.035, 'Z', 0.0475, [m["wood"], m["wood_dark"]],
                  parent=root)
    pl.card_panel(f"{PFX}GearTop", pl.ngon_pts(12, 0.24), 0.03, 'Z', 0.08,
                  [m["wood"], m["wood_dark"]], parent=root)

    # Yoke: two turned columns (furniture legs, not card gussets) with
    # brass pivot bosses facing inward.
    for sign, side in ((1, "R"), (-1, "L")):
        il.lathe(f"{PFX}Column{side}",
                 [(0.062, GEAR_TOP), (0.050, GEAR_TOP + 0.035),
                  (0.038, 0.30), (0.046, 0.42), (0.056, 0.45),
                  (0.038, 0.49), (0.012, 0.52)],
                 [m["wood_dark"]], segs=12,
                 center=(sign * 0.15, 0.0, 0.0), parent=root)
        pl.brad(f"{PFX}Pivot{side}", sign * 0.105, sign * 0.155, 0.042,
                PIVOT_Z, m["brass"], parent=root)

    yoke = il.root_empty(PFX + "Yoke", (0, 0, 0))
    yoke.parent = root
    yoke.location = (0, 0, PIVOT_Z)

    body = il.root_empty(PFX + "Body", (0, 0, 0))
    body.parent = yoke
    body.location = (0, 0, 0)
    body.rotation_euler = (radians(RAKE_DEG), 0, 0)

    # Cask receiver: staved barrel lying fore-aft (12 flat facets read
    # as staves), brass hoops, walnut rear cap + brass button.
    il.lathe(f"{PFX}Cask",
             [(0.140, -0.12), (0.163, -0.03), (0.170, 0.06),
              (0.162, 0.15), (0.138, 0.30)],
             [m["wood"]], segs=12, axis='Y', center=(0, 0, CZ),
             parent=body)
    il.torus(f"{PFX}Hoop0", 0.172, 0.013, [m["brass"]],
             center=(0, -0.05, CZ), axis='Y', segs=18, sides=6,
             parent=body)
    il.torus(f"{PFX}Hoop1", 0.158, 0.013, [m["brass"]],
             center=(0, 0.21, CZ), axis='Y', segs=18, sides=6,
             parent=body)
    il.lathe(f"{PFX}RearCap",
             [(0.100, 0.30), (0.085, 0.345), (0.042, 0.375),
              (0.012, 0.385)],
             [m["wood_dark"]], segs=12, axis='Y', center=(0, 0, CZ),
             parent=body)
    il.lathe(f"{PFX}RearButton",
             [(0.004, 0.385), (0.024, 0.40), (0.030, 0.415),
              (0.020, 0.43), (0.004, 0.44)],
             [m["brass"]], segs=10, axis='Y', center=(0, 0, CZ),
             parent=body)

    # Turned barrel: swell at the breech, slim waist, flared lip.
    il.lathe(f"{PFX}Barrel",
             [(0.095, -0.10), (0.078, -0.20), (0.056, -0.38),
              (0.050, -0.52), (0.068, -0.60), (0.075, -0.63)],
             [m["wood"]], segs=14, axis='Y', center=(0, 0, CZ),
             parent=body)
    il.torus(f"{PFX}MuzzleBand", 0.071, 0.011, [m["brass"]],
             center=(0, -0.575, CZ), axis='Y', segs=14, sides=6,
             parent=body)
    il.lathe(f"{PFX}Bore", [(0.046, -0.647), (0.046, -0.630)],
             [m["channel"]], segs=12, axis='Y', center=(0, 0, CZ),
             parent=body)

    # LEFT: brass feed pipe out of the cask, linen pellet sack hanging
    # from it, hemp cord tied at the neck. The lovable detail.
    il.rod(f"{PFX}FeedPipe", (-0.13, 0.02, CZ + 0.09),
           (-0.33, 0.02, CZ + 0.13), 0.028, [m["brass"]], sides=8,
           parent=body)
    # Bottom-heavy droop: fat low bulge, pinched neck reaching up to
    # the pipe — hangs beside the cask instead of riding on it.
    il.lathe(f"{PFX}Sack",
             [(0.025, -0.20), (0.125, -0.15), (0.165, -0.05),
              (0.120, 0.03), (0.058, 0.09), (0.042, 0.15)],
             [m["linen"]], segs=10, center=(-0.33, 0.02, CZ - 0.04),
             parent=body)
    il.torus(f"{PFX}SackTie", 0.048, 0.011, [m["cord"]],
             center=(-0.33, 0.02, CZ + 0.085), axis='Z', segs=12,
             sides=5, parent=body)

    # RIGHT: exposed brass drive gear + the crank that spins it.
    gear_pts = [(y + 0.06, z + CZ) for y, z in
                pl.gear_profile(10, 0.048, 0.068)]
    pl.card_panel(f"{PFX}DriveGear", gear_pts, 0.03, 'X', 0.175,
                  [m["brass"], m["brass"]], parent=body)
    il.rod(f"{PFX}CrankAxle", (0.15, 0.06, CZ), (0.27, 0.06, CZ),
           0.017, [m["brass"]], parent=body)
    il.rod(f"{PFX}CrankArm", (0.265, 0.06, CZ - 0.02),
           (0.265, 0.06, CZ + 0.17), 0.022, [m["wood_dark"]],
           parent=body)
    il.rod(f"{PFX}CrankGrip", (0.26, 0.06, CZ + 0.155),
           (0.37, 0.06, CZ + 0.155), 0.018, [m["brass"]], parent=body)

    # Brass sight bead riding the barrel, near the muzzle.
    pl.card_panel(f"{PFX}SightBead", pl.ngon_pts(6, 0.018, cy=-0.55),
                  0.045, 'Z', 0.105, [m["brass"], m["brass"]],
                  parent=body)

    # Muzzle marker for a future export (WeaponModelRig parity).
    muzzle = il.root_empty(PFX + "Muzzle", (0, 0, 0))
    muzzle.parent = body
    muzzle.location = (0, -0.66, CZ)

    pl.scale_tree(root, SCALE)
    return root
