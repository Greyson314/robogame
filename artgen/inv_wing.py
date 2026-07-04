# artgen/inv_wing.py — inventor study: aero wing.
# The composition rule made literal: rib-and-membrane. Walnut leading-edge
# spar, five spruce ribs, linen membrane stretched over the top with a
# scalloped trailing edge sagging between rib tips — the one silhouette
# cue that says "fabric wing" from 30 m. Membrane rows follow the rib
# camber so the skin visibly drapes on the skeleton.

from math import cos, sin, pi, tau

import paperlib as pl
import inventorlib as il

PFX = "InvWing_"

Y_LE = 0.42     # leading edge
Y_TE = -0.44    # trailing edge at ribs
SCALLOP = 0.08  # trailing-edge pull-in between ribs
BAYS = 4
RIB_X = [-0.5 + i * 0.25 for i in range(5)]


def z_top(c):
    """Camber curve, c in [0, 1] leading -> trailing."""
    return 0.105 * sin(pi * c ** 0.85)


def y_te(x):
    u = (x + 0.5) / (1.0 / BAYS)
    return Y_TE + SCALLOP * (0.5 - 0.5 * cos(tau * u))


def build(loc=(0.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Ribs: closed (y, z) profiles, deep at the leading edge, knife-thin
    # at the trailing edge.
    NP = 11
    top, bot = [], []
    for i in range(NP):
        c = i / (NP - 1)
        y = Y_LE - (Y_LE - Y_TE) * c
        depth = 0.055 * (1.0 - 0.75 * c) + 0.012
        top.append((y, z_top(c) - 0.004))
        bot.append((y, z_top(c) - depth))
    profile = top + list(reversed(bot))
    for i, x in enumerate(RIB_X):
        pl.card_panel(f"{PFX}Rib{i}", profile, 0.022, 'X', x,
                      [m["wood"]], cap_slots=(0, 0), edge_slot=0,
                      parent=root)

    # Membrane: rows at span stations, chord shortened by the scallop.
    rows = []
    NROW, NCH = 33, 9
    for i in range(NROW):
        x = -0.5 + i / (NROW - 1)
        yt = y_te(x)
        row = []
        for j in range(NCH):
            c = j / (NCH - 1)
            y = 0.38 - (0.38 - yt) * c
            cc = (Y_LE - y) / (Y_LE - Y_TE)
            row.append((x, y, z_top(cc) + 0.004))
        rows.append(row)
    il.ribbon(f"{PFX}Membrane", rows, 0.012, [m["linen"]], parent=root)

    # Spars: fat walnut round at the leading edge (proud ends with brass
    # caps), slimmer stringer at mid-chord under the membrane.
    il.rod(f"{PFX}SparLE", (-0.53, 0.40, 0.015), (0.53, 0.40, 0.015),
           0.038, [m["wood_dark"]], sides=10, parent=root)
    for s, x in ((0, -0.545), (1, 0.545)):
        il.lathe(f"{PFX}SparCap{s}",
                 [(0.040, x), (0.040, x + (0.02 if s == 0 else -0.02))],
                 [m["brass"]], segs=10, axis='X', parent=root)
    il.rod(f"{PFX}SparMid", (-0.5, -0.02, 0.055), (0.5, -0.02, 0.055),
           0.020, [m["wood"]], sides=8, parent=root)

    # Cord lacing along the leading edge: short diagonal wraps spar->rib.
    for i, x in enumerate(RIB_X):
        il.torus(f"{PFX}Lash{i}", 0.052, 0.008, [m["cord"]],
                 center=(x, 0.40, 0.015), axis='X', segs=12, sides=5,
                 parent=root)

    # Brass mount plate under the mid-chord — where it bolts to the bot.
    il.box(f"{PFX}Mount", (0.0, 0.0, -0.055), (0.16, 0.16, 0.022),
           [m["brass"]], parent=root)
    return root
