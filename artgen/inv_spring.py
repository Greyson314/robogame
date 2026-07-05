# artgen/inv_spring.py — inventor study: spring (jump block).
# A full-elliptic carriage spring: two opposed stacks of laminated oak
# leaves forming the classic eye shape, iron centre clamps to walnut
# mount plates, hemp whipping binding the leaf tips. Period-right and
# it *reads* springy — the compressed-energy silhouette.

import paperlib as pl
import inventorlib as il

PFX = "InvSpring_"


def leaf_profile(chord, bow, th, z_join):
    """Closed (y, z) arc-strip profile. bow>0 bows up, <0 down."""
    n = 9
    top, bot = [], []
    for i in range(n):
        u = i / (n - 1)
        y = -chord / 2 + chord * u
        z = z_join + bow * (1 - (2 * u - 1) ** 2)
        top.append((y, z))
        bot.append((y, z - th if bow > 0 else z + th))
    return top + list(reversed(bot))


def build(loc=(-3.4, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Walnut mount plates top (jump pad) and bottom (chassis face).
    il.box(f"{PFX}PlateB", (0, 0, -0.30), (0.60, 0.46, 0.05),
           [m["wood_dark"]], parent=root)
    il.box(f"{PFX}PlateT", (0, 0, 0.30), (0.60, 0.46, 0.05),
           [m["wood"]], parent=root)

    # Leaf stacks: three lengths per side, widths shrinking. Leaves run
    # along X so the elliptic eye faces the viewer/row front.
    leaves = [(0.88, 0.16), (0.64, 0.13), (0.42, 0.105)]
    for s, sign in (("Up", 1), ("Dn", -1)):
        for i, (chord, w) in enumerate(leaves):
            bow = sign * (0.24 - 0.03 * i)
            zj = -sign * 0.02
            pl.card_panel(f"{PFX}Leaf{s}{i}",
                          leaf_profile(chord, bow, 0.035, zj), w, 'Y',
                          0.0, [m["wood"], m["wood_dark"]],
                          cap_slots=(0, 0), edge_slot=1, parent=root)

    # Iron centre clamps tying leaf middles to the plates.
    il.box(f"{PFX}ClampT", (0, 0, 0.245), (0.19, 0.13, 0.075),
           [m["iron"]], parent=root)
    il.box(f"{PFX}ClampB", (0, 0, -0.245), (0.19, 0.13, 0.075),
           [m["iron"]], parent=root)

    # Hemp whipping where the leaf tips meet (the spring's "eyes").
    for k, x in ((0, -0.44), (1, 0.44)):
        il.torus(f"{PFX}Whip{k}", 0.105, 0.016, [m["cord"]],
                 center=(x, 0, 0), axis='X', segs=14, sides=6,
                 parent=root)
    return root
