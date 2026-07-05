# artgen/inv_rope.py — inventor study: rope block.
# Deck furniture: a flanged walnut spool wound with hemp, on two
# uprights over an oak base plate, brass axle, a cleat at the front
# with the loose end draped toward it. The block that IS its material.

import paperlib as pl
import inventorlib as il

PFX = "InvRope_"


def build(loc=(5.2, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    il.box(f"{PFX}Base", (0, 0, -0.44), (0.46, 0.46, 0.05),
           [m["wood"]], parent=root)

    # Uprights + brass axle + flanged spool.
    for sx in (-1, 1):
        il.rod(f"{PFX}Post{sx}", (sx * 0.20, 0, -0.42),
               (sx * 0.20, 0, 0.0), 0.036, [m["wood_dark"]],
               parent=root)
    il.rod(f"{PFX}Axle", (-0.25, 0, 0.0), (0.25, 0, 0.0), 0.022,
           [m["brass"]], parent=root)
    il.lathe(f"{PFX}Spool",
             [(0.145, -0.165), (0.085, -0.13), (0.085, 0.13),
              (0.145, 0.165)],
             [m["wood_dark"]], segs=14, axis='X', parent=root)

    # The hemp winding.
    for k, x in enumerate((-0.096, -0.048, 0.0, 0.048, 0.096)):
        il.torus(f"{PFX}Coil{k}", 0.112, 0.027, [m["cord"]],
                 center=(x, 0, 0), axis='X', segs=22, sides=6,
                 parent=root)

    # Loose end draping down to a little cleat on the base.
    il.sweep(f"{PFX}End",
             [(0.09, 0.0, 0.11), (0.15, -0.10, 0.02),
              (0.12, -0.19, -0.15), (0.02, -0.225, -0.30),
              (0.0, -0.20, -0.38)],
             0.020, [m["cord"]], sides=6, parent=root)
    il.rod(f"{PFX}CleatStem", (0, -0.185, -0.415), (0, -0.185, -0.345),
           0.020, [m["wood"]], parent=root)
    il.rod(f"{PFX}CleatHorn", (-0.10, -0.185, -0.345),
           (0.10, -0.185, -0.345), 0.019, [m["wood"]], parent=root)
    return root
