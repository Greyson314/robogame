# artgen/inv_bombbay.py — inventor study: bomb bay.
# An open-bottomed shipping crate: walnut posts, oak slats, hemp-era
# X-bracing, two trapdoors caught mid-swing with a laminated cartoon
# bomb dropping through. The joke and the mechanic are the same shape.

from math import radians

import bpy

import paperlib as pl
import inventorlib as il

PFX = "InvBay_"

P = 0.38   # post centreline
B = 0.07   # post cross-section


def build(loc=(0.0, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    for sx in (-1, 1):
        for sy in (-1, 1):
            il.box(f"{PFX}Post{sx}{sy}", (sx * P, sy * P, 0.0),
                   (B, B, 0.86), [m["wood_dark"]], parent=root)
    for sz in (-1, 1):
        for sy in (-1, 1):
            il.box(f"{PFX}RailX{sy}{sz}", (0, sy * P, sz * 0.40),
                   (2 * P - B, B, B), [m["wood_dark"]], parent=root)
        for sx in (-1, 1):
            il.box(f"{PFX}RailY{sx}{sz}", (sx * P, 0, sz * 0.40),
                   (B, 2 * P - B, B), [m["wood_dark"]], parent=root)

    # Oak slats on the four sides; lid planks on top.
    for sy in (-1, 1):
        for k, z in enumerate((-0.16, 0.10)):
            il.box(f"{PFX}SlatY{sy}{k}", (0, sy * (P + 0.005), z),
                   (0.62, 0.028, 0.20), [m["wood"]], parent=root)
    for sx in (-1, 1):
        for k, z in enumerate((-0.16, 0.10)):
            il.box(f"{PFX}SlatX{sx}{k}", (sx * (P + 0.005), 0, z),
                   (0.028, 0.62, 0.20), [m["wood"]], parent=root)
        # Cord X-brace on the side faces.
        il.rod(f"{PFX}BraceA{sx}", (sx * (P + 0.03), -0.30, -0.32),
               (sx * (P + 0.03), 0.30, 0.32), 0.014, [m["cord"]],
               sides=5, parent=root)
        il.rod(f"{PFX}BraceB{sx}", (sx * (P + 0.03), -0.30, 0.32),
               (sx * (P + 0.03), 0.30, -0.32), 0.014, [m["cord"]],
               sides=5, parent=root)
    for k, x in enumerate((-0.22, 0.0, 0.22)):
        il.box(f"{PFX}Lid{k}", (x, 0, 0.445), (0.20, 2 * P - B, 0.025),
               [m["wood"]], parent=root)

    # Trapdoors caught mid-swing (hinge lines at the bottom rails).
    for sy, ang in ((1, -55.0), (-1, 235.0)):
        door = il.box(f"{PFX}Door{sy}", (0, 0.175, 0), (0.60, 0.35, 0.022),
                      [m["wood"]], parent=root)
        door.rotation_euler = (radians(ang), 0, 0)
        door.location = (0, sy * (P - 0.02), -0.42)
        for hx in (-0.22, 0.22):
            il.rod(f"{PFX}Hinge{sy}{int(hx*100)}",
                   (hx - 0.04, sy * (P - 0.02), -0.425),
                   (hx + 0.04, sy * (P - 0.02), -0.425),
                   0.020, [m["brass"]], sides=6, parent=root)

    # The bomb, mid-drop.
    pl.disc_ball(f"{PFX}Bomb", 0.145, (0, 0, -0.60),
                 [m["iron"], m["ink"]], parent=root)
    il.lathe(f"{PFX}BombStud", [(0.032, -0.47), (0.030, -0.445),
                                (0.012, -0.43)],
             [m["brass"]], segs=8, parent=root)
    return root
