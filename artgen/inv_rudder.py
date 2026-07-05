# artgen/inv_rudder.py — inventor study: rudder.
# Boat furniture: a walnut sternpost with brass gudgeon hinges, a
# vertical rib-and-membrane blade fanning aft off the post, and a
# little tiller arm on top — steering read as a mechanism, not a slab.
# Blade trails +Y; hinge axis vertical.

from math import cos, sin, pi, radians

import paperlib as pl
import inventorlib as il

PFX = "InvRudder_"

POST_Y = 0.12
SPAR_A = [78.0, 30.0, -14.0, -55.0]     # degrees in the (y, z) plane
SPAR_L = [0.62, 0.72, 0.66, 0.50]
SCALLOP = 0.15
ORIGIN = (POST_Y + 0.03, 0.06)          # (y, z) fan origin at the post
THICK = 0.012


def edge_len(a_deg):
    for i in range(len(SPAR_A) - 1):
        a0, a1 = SPAR_A[i], SPAR_A[i + 1]
        if a1 <= a_deg <= a0:
            u = (a0 - a_deg) / (a0 - a1)
            base = SPAR_L[i] + (SPAR_L[i + 1] - SPAR_L[i]) * u
            return base * (1.0 - SCALLOP * sin(pi * u))
    return SPAR_L[-1]


def build(loc=(-6.8, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)
    oy, oz = ORIGIN

    # Sternpost + brass gudgeons + finial.
    il.rod(f"{PFX}Post", (0, POST_Y, -0.42), (0, POST_Y, 0.46), 0.042,
           [m["wood_dark"]], sides=10, parent=root)
    for k, z in ((0, -0.32), (1, 0.30)):
        il.torus(f"{PFX}Gudgeon{k}", 0.052, 0.014, [m["brass"]],
                 center=(0, POST_Y, z), axis='Z', segs=14, sides=6,
                 parent=root)
    # Tiller arm sweeping forward off the post top.
    il.rod(f"{PFX}Tiller", (0, POST_Y - 0.02, 0.42),
           (0, POST_Y - 0.46, 0.52), 0.026, [m["wood"]], r1=0.018,
           parent=root)
    il.lathe(f"{PFX}TillerKnob",
             [(0.004, 0.0), (0.026, 0.014), (0.030, 0.036),
              (0.018, 0.056), (0.003, 0.066)],
             [m["brass"]], segs=10,
             center=(0, POST_Y - 0.47, 0.50), parent=root)

    # Blade membrane: vertical sheet in the x=0 plane, thickness along X.
    rows = []
    for i in range(7):
        t = 0.10 + 0.90 * i / 6
        row = []
        for j in range(17):
            a = SPAR_A[0] + (SPAR_A[-1] - SPAR_A[0]) * j / 16
            r = t * edge_len(a)
            ar = radians(a)
            row.append((THICK / 2, oy + r * cos(ar), oz + r * sin(ar)))
        rows.append(row)
    il.ribbon(f"{PFX}Membrane", rows, THICK, [m["linen"]], parent=root,
              axis='X')

    # Blade spars, swept in-plane, proud of both faces.
    for k, (a, L) in enumerate(zip(SPAR_A, SPAR_L)):
        ar = radians(a)
        path = [(0.0, oy + t * L * cos(ar), oz + t * L * sin(ar))
                for t in [0.02 + 0.98 * s / 8 for s in range(9)]]
        il.sweep(f"{PFX}Spar{k}", path, 0.020, [m["wood"]], sides=6,
                 parent=root)
        tip = path[-1]
        il.lathe(f"{PFX}Tip{k}",
                 [(0.003, -0.026), (0.021, -0.013), (0.026, 0.0),
                  (0.021, 0.013), (0.003, 0.026)],
                 [m["brass"]], segs=8, center=tip, parent=root)
    return root
