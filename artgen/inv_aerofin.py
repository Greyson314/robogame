# artgen/inv_aerofin.py — inventor study: small aero fin.
# The wing language at pennant scale: three tapered spars fanning from
# a root boss, linen between, scalloped edge, brass tip beads. Reads
# as the wing's little sibling at 30 m.

from math import cos, sin, pi, radians

import paperlib as pl
import inventorlib as il

PFX = "InvFin_"

ORIGIN = (-0.42, 0.18)
SPAR_A = [0.0, -40.0, -82.0]
SPAR_L = [0.98, 0.80, 0.52]
SCALLOP = 0.16
THICK = 0.012


def edge_len(a_deg):
    for i in range(len(SPAR_A) - 1):
        a0, a1 = SPAR_A[i], SPAR_A[i + 1]
        if a1 <= a_deg <= a0:
            u = (a0 - a_deg) / (a0 - a1)
            base = SPAR_L[i] + (SPAR_L[i + 1] - SPAR_L[i]) * u
            return base * (1.0 - SCALLOP * sin(pi * u))
    return SPAR_L[-1]


def surf_z(t, a_deg):
    return 0.055 * t * (1.0 - t) * 2.0 - 0.04 * t * (abs(a_deg) / 82.0) ** 1.5


def build(loc=(-8.4, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)
    ox, oy = ORIGIN

    rows = []
    for i in range(7):
        t = 0.08 + 0.92 * i / 6
        row = []
        for j in range(15):
            a = SPAR_A[0] + (SPAR_A[-1] - SPAR_A[0]) * j / 14
            r = t * edge_len(a)
            ar = radians(a)
            row.append((ox + r * cos(ar), oy + r * sin(ar),
                        surf_z(t, a) + THICK / 2))
        rows.append(row)
    il.ribbon(f"{PFX}Membrane", rows, THICK, [m["linen"]], parent=root)

    for k, (a, L) in enumerate(zip(SPAR_A, SPAR_L)):
        ar = radians(a)
        r_bone = 0.030 - 0.005 * k
        path = [(ox + t * L * cos(ar), oy + t * L * sin(ar), surf_z(t, a))
                for t in [0.02 + 0.98 * s / 10 for s in range(11)]]
        il.sweep(f"{PFX}Spar{k}", path, r_bone,
                 [m["wood_dark"] if k == 0 else m["wood"]], sides=7,
                 parent=root)
        tip = path[-1]
        rb = r_bone * 1.25
        il.lathe(f"{PFX}Tip{k}",
                 [(0.003, -rb), (rb * 0.8, -rb * 0.5), (rb, 0.0),
                  (rb * 0.8, rb * 0.5), (0.003, rb)],
                 [m["brass"]], segs=10, center=tip, parent=root)

    il.lathe(f"{PFX}Boss",
             [(0.04, -0.045), (0.075, -0.02), (0.08, 0.02),
              (0.05, 0.05), (0.02, 0.062)],
             [m["wood_dark"]], segs=10, center=(ox, oy, 0.0), parent=root)
    il.box(f"{PFX}Mount", (ox, oy, -0.062), (0.15, 0.15, 0.022),
           [m["brass"]], parent=root)
    return root
