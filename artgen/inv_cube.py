# artgen/inv_cube.py — inventor study: structure cube.
# The mass-repetition block, so the language must survive being tiled by
# the thousand: dark timber frame (4 posts + 8 rails), light linen panels
# inset in every face. "Solid where you shoot it" — no daylight through
# structure. Ink part-number stamps are texture-stage work, not geometry.

import paperlib as pl
import inventorlib as il

PFX = "InvCube_"


def build(loc=(-4.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    P = 0.455   # post/rail centreline offset from centre
    B = 0.09    # beam cross-section

    for sx in (-1, 1):
        for sy in (-1, 1):
            il.box(f"{PFX}Post_{sx}{sy}", (sx * P, sy * P, 0.0),
                   (B, B, 1.0), [m["wood_dark"]], parent=root)
    for sz in (-1, 1):
        for sy in (-1, 1):
            il.box(f"{PFX}RailX_{sy}{sz}", (0.0, sy * P, sz * P),
                   (1.0 - 2 * B, B, B), [m["wood_dark"]], parent=root)
        for sx in (-1, 1):
            il.box(f"{PFX}RailY_{sx}{sz}", (sx * P, 0.0, sz * P),
                   (B, 1.0 - 2 * B, B), [m["wood_dark"]], parent=root)

    # Linen infill panels, recessed 0.06 from the beam outer face so each
    # face keeps a shadow line — the read that this is skin on a frame.
    S = 0.84
    T = 0.03
    D = 0.44
    il.box(f"{PFX}PanelT", (0, 0, D), (S, S, T), [m["linen"]], parent=root)
    il.box(f"{PFX}PanelB", (0, 0, -D), (S, S, T), [m["linen"]], parent=root)
    il.box(f"{PFX}PanelN", (0, D, 0), (S, T, S), [m["linen"]], parent=root)
    il.box(f"{PFX}PanelS", (0, -D, 0), (S, T, S), [m["linen"]], parent=root)
    il.box(f"{PFX}PanelE", (D, 0, 0), (T, S, S), [m["linen"]], parent=root)
    il.box(f"{PFX}PanelW", (-D, 0, 0), (T, S, S), [m["linen"]], parent=root)

    # One quiet fitting per corner: brass peg caps on the post tops.
    for sx in (-1, 1):
        for sy in (-1, 1):
            il.lathe(f"{PFX}Peg_{sx}{sy}",
                     [(0.028, 0.0), (0.028, 0.018), (0.016, 0.028)],
                     [m["brass"]], segs=10,
                     center=(sx * P, sy * P, 0.5), parent=root)
    return root
