# artgen/inv_mortar.py — inventor study: mortar.
# The fireworks-tube honesty (131) in wood: a fat staved oak TUB of a
# bombard, brass hoops, red bore, walnut breech ball, tilted ~42° on
# walnut wedge cheeks over the family gear base. A cartoon iron bomb
# waits on the deck. Lobbing = a bucket that throws.

from math import radians

import paperlib as pl
import inventorlib as il

PFX = "InvMortar_"


def build(loc=(3.6, -8.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    pl.card_panel(f"{PFX}GearBottom", pl.ngon_pts(12, 0.29), 0.03, 'Z',
                  0.015, [m["wood"], m["wood_dark"]], parent=root)
    pl.card_panel(f"{PFX}GearTeeth", pl.gear_profile(10, 0.26, 0.32),
                  0.035, 'Z', 0.0475, [m["wood"], m["wood_dark"]],
                  parent=root)
    pl.card_panel(f"{PFX}GearTop", pl.ngon_pts(12, 0.24), 0.03, 'Z',
                  0.08, [m["wood"], m["wood_dark"]], parent=root)

    # Wedge cheeks holding the tilted tub.
    wedge = [(0.30, 0.10), (-0.26, 0.10), (-0.26, 0.44)]
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}Wedge{side}", wedge, 0.045, 'X',
                      sign * 0.185, [m["wood_dark"]], cap_slots=(0, 0),
                      edge_slot=0, parent=root)

    # Tub group, tilted back 42° (mouth up-forward).
    tilt = il.root_empty(PFX + "Tilt", (0, 0, 0))
    tilt.parent = root
    tilt.location = (0, 0.02, 0.26)
    tilt.rotation_euler = (radians(-42), 0, 0)

    il.lathe(f"{PFX}Tub",
             [(0.150, -0.02), (0.210, 0.05), (0.250, 0.28),
              (0.258, 0.40)],
             [m["wood"]], segs=10, axis='Z', parent=tilt)
    il.torus(f"{PFX}HoopLow", 0.212, 0.016, [m["brass"]],
             center=(0, 0, 0.09), axis='Z', segs=18, sides=6, parent=tilt)
    il.torus(f"{PFX}HoopRim", 0.262, 0.018, [m["brass"]],
             center=(0, 0, 0.385), axis='Z', segs=18, sides=6,
             parent=tilt)
    il.lathe(f"{PFX}BoreDisc", [(0.195, 0.365), (0.195, 0.385)],
             [m["channel"]], segs=14, axis='Z', parent=tilt)
    il.lathe(f"{PFX}Breech",
             [(0.010, -0.16), (0.075, -0.125), (0.115, -0.06),
              (0.14, -0.01)],
             [m["wood_dark"]], segs=12, axis='Z', parent=tilt)

    # Cartoon bomb on the deck, brass fuse stud.
    pl.disc_ball(f"{PFX}Bomb", 0.125, (0.20, 0.24, 0.215),
                 [m["iron"], m["ink"]], parent=root)
    il.lathe(f"{PFX}BombStud", [(0.030, 0.335), (0.028, 0.36),
                                (0.012, 0.375)],
             [m["brass"]], segs=8, center=(0.20, 0.24, 0.0), parent=root)
    return root
