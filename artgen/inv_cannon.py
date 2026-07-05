# artgen/inv_cannon.py — inventor study: cannon.
# The cartoon-cannon archetype in the inventor material set: rolled
# IRON barrel with brass hoops (guns are the one place iron leads),
# stepped walnut carriage cheeks, scalloped oak disc wheels, walnut
# cascabel + brass knob, vermilion bore. Family yaw-gear base.
# Forward -Y, barrel raked up a few degrees.

from math import radians, tau, cos, sin

import paperlib as pl
import inventorlib as il

PFX = "InvCannon_"


def flower_pts(n, r0, r1, cy=0.0, cz=0.0):
    pts = []
    for i in range(2 * n):
        r = r1 if i % 2 == 0 else r0
        a = i * tau / (2 * n)
        pts.append((cy + r * cos(a), cz + r * sin(a)))
    return pts


def build(loc=(1.8, -8.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Yaw gear base.
    pl.card_panel(f"{PFX}GearBottom", pl.ngon_pts(12, 0.29), 0.03, 'Z',
                  0.015, [m["wood"], m["wood_dark"]], parent=root)
    pl.card_panel(f"{PFX}GearTeeth", pl.gear_profile(10, 0.26, 0.32),
                  0.035, 'Z', 0.0475, [m["wood"], m["wood_dark"]],
                  parent=root)
    pl.card_panel(f"{PFX}GearTop", pl.ngon_pts(12, 0.24), 0.03, 'Z',
                  0.08, [m["wood"], m["wood_dark"]], parent=root)

    # Stepped carriage cheeks.
    cheek = [(0.42, 0.10), (0.42, 0.22), (0.27, 0.22), (0.27, 0.33),
             (0.11, 0.33), (0.11, 0.44), (-0.15, 0.44), (-0.31, 0.30),
             (-0.42, 0.17), (-0.42, 0.10)]
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}Cheek{side}", cheek, 0.05, 'X', sign * 0.15,
                      [m["wood_dark"]], cap_slots=(0, 0), edge_slot=0,
                      parent=root)
    il.box(f"{PFX}Bed", (0, 0.05, 0.14), (0.26, 0.7, 0.06),
           [m["wood_dark"]], parent=root)

    # Scalloped oak disc wheels on a brass axle.
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}Wheel{side}",
                      flower_pts(9, 0.135, 0.175, cy=0.24, cz=0.27),
                      0.045, 'X', sign * 0.215, [m["wood"], m["wood_dark"]],
                      parent=root)
        pl.brad(f"{PFX}WheelCap{side}", sign * 0.238, sign * 0.268,
                0.045, 0.27, m["brass"], parent=root, y=0.24)
    il.rod(f"{PFX}Axle", (-0.22, 0.24, 0.27), (0.22, 0.24, 0.27),
           0.028, [m["iron"]], parent=root)

    # Barrel group, raked up.
    muzzle = il.root_empty(PFX + "Pitch", (0, 0, 0))
    muzzle.parent = root
    muzzle.location = (0, 0, 0.44)
    muzzle.rotation_euler = (radians(-7), 0, 0)

    il.lathe(f"{PFX}Barrel",
             [(0.150, 0.30), (0.140, 0.10), (0.118, -0.25),
              (0.102, -0.55), (0.128, -0.66), (0.138, -0.70)],
             [m["iron"]], segs=14, axis='Y', parent=muzzle)
    for k, (yy, R) in enumerate(((0.06, 0.148), (-0.22, 0.126),
                                 (-0.50, 0.112))):
        il.torus(f"{PFX}Hoop{k}", R, 0.014, [m["brass"]],
                 center=(0, yy, 0), axis='Y', segs=16, sides=6,
                 parent=muzzle)
    il.lathe(f"{PFX}Bore", [(0.088, -0.712), (0.088, -0.695)],
             [m["channel"]], segs=12, axis='Y', parent=muzzle)
    # Cascabel: walnut ball + brass knob off the breech.
    il.lathe(f"{PFX}Cascabel",
             [(0.045, 0.30), (0.085, 0.34), (0.092, 0.385),
              (0.06, 0.425), (0.012, 0.45)],
             [m["wood_dark"]], segs=12, axis='Y', parent=muzzle)
    il.lathe(f"{PFX}Knob",
             [(0.010, 0.45), (0.030, 0.47), (0.033, 0.495),
              (0.018, 0.515), (0.003, 0.525)],
             [m["brass"]], segs=10, axis='Y', parent=muzzle)
    return root
