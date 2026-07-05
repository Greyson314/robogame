# artgen/inv_drill.py — inventor study: drill.
# The Archimedes auger — the most da Vinci tool in the roster. Iron
# helical flight around a tapered oak shaft, laminated drive collar
# with a wooden gear ring (the family "mechanism = gears" read), brass
# band, iron point. Long: components size to their mechanic, and this
# one eats terrain. Points -Y.

from math import cos, sin, tau

import paperlib as pl
import inventorlib as il

PFX = "InvDrill_"

TURNS = 3.4
Y0, Y1 = -0.14, -1.18


def build(loc=(-5.0, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Drive collar: laminated discs + wood gear ring + brass band.
    pl.card_panel(f"{PFX}Collar0", pl.ngon_pts(12, 0.20), 0.09, 'Y',
                  0.10, [m["wood"], m["wood_dark"]], parent=root)
    pl.card_panel(f"{PFX}CollarGear", pl.gear_profile(14, 0.185, 0.235),
                  0.06, 'Y', 0.02, [m["wood"], m["wood_dark"]],
                  parent=root)
    pl.card_panel(f"{PFX}Collar1", pl.ngon_pts(12, 0.155), 0.07, 'Y',
                  -0.05, [m["wood_dark"], m["wood_dark"]], parent=root)
    il.torus(f"{PFX}Band", 0.135, 0.016, [m["brass"]],
             center=(0, -0.11, 0), axis='Y', segs=16, sides=6,
             parent=root)

    # Tapered oak shaft.
    il.lathe(f"{PFX}Shaft",
             [(0.115, -0.10), (0.085, -0.40), (0.055, -0.80),
              (0.030, -1.16)],
             [m["wood"]], segs=12, axis='Y', parent=root)

    # Iron helical flight. Ribbon thickness along Y (the screw axis).
    N = 100
    rows = []
    for i in range(N + 1):
        t = i / N
        a = t * TURNS * tau
        y = Y0 + (Y1 - Y0) * t
        r_in = 0.105 * (1 - t) + 0.028 * t
        r_out = 0.30 * (1 - t) + 0.055 * t
        ca, sa = cos(a), sin(a)
        rows.append([
            (r_in * ca, y, r_in * sa),
            (r_out * ca, y - 0.012, r_out * sa),
        ])
    il.ribbon(f"{PFX}Flight", rows, 0.016, [m["iron"]], parent=root,
              axis='Y')

    # Iron point.
    il.lathe(f"{PFX}Point", [(0.032, -1.15), (0.020, -1.24),
                             (0.003, -1.30)],
             [m["iron"]], segs=10, axis='Y', parent=root)
    return root
