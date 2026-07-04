# artgen/inv_rotor.py — inventor study: rotor as da Vinci's aerial screw.
# The flagship image of the direction. Walnut mast on a laminated wood
# yaw gear (rotating things stand on gears — carries the weapon-family
# base language), helical linen sail with visible thickness and a slight
# fabric sag, spruce spiral batten on the outer edge, radial ribs
# underneath, hemp rigging from the masthead. Brass collars + finial.

from math import cos, sin, tau

import paperlib as pl
import inventorlib as il

PFX = "InvRotor_"

TURNS = 1.75
Z0, Z1 = 0.42, 1.30       # sail rise
R_IN = 0.055
R_OUT0, R_OUT1 = 0.68, 0.60   # slight cone, wider at the base


def helix(t):
    """t in [0,1] -> (angle, z, r_out)."""
    a = t * TURNS * tau
    return a, Z0 + (Z1 - Z0) * t, R_OUT0 + (R_OUT1 - R_OUT0) * t


def build(loc=(4.5, -5.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Yaw gear: laminated spruce, walnut cut edge (wood take on the
    # weapon yaw-gear ring), with a turned walnut collar over the hub.
    pl.card_panel(f"{PFX}Gear", pl.gear_profile(20, 0.40, 0.46), 0.07,
                  'Z', 0.035, [m["wood"], m["wood_dark"]],
                  cap_slots=(0, 0), edge_slot=1, parent=root)
    il.lathe(f"{PFX}Collar",
             [(0.17, 0.07), (0.15, 0.12), (0.10, 0.16), (0.065, 0.20)],
             [m["wood_dark"]], segs=16, parent=root)

    # Mast.
    il.rod(f"{PFX}Mast", (0, 0, 0.07), (0, 0, 1.52), 0.045,
           [m["wood_dark"]], sides=10, parent=root)
    for k, z in ((0, Z0), (1, Z1)):
        il.torus(f"{PFX}MastRing{k}", 0.055, 0.013, [m["brass"]],
                 center=(0, 0, z), axis='Z', segs=14, sides=6, parent=root)
    il.lathe(f"{PFX}Finial",
             [(0.012, 1.52), (0.034, 1.55), (0.042, 1.585),
              (0.028, 1.62), (0.004, 1.645)],
             [m["brass"]], segs=12, parent=root)

    # Helical linen sail: rows sweep from mast to outer edge with a
    # fabric sag at mid-radius. Ribbon gives it edge thickness.
    N = 96
    rows = []
    for i in range(N + 1):
        t = i / N
        a, z, ro = helix(t)
        ca, sa = cos(a), sin(a)
        rows.append([
            (R_IN * ca, R_IN * sa, z),
            (0.38 * ca, 0.38 * sa, z - 0.032),
            (ro * ca, ro * sa, z),
        ])
    il.ribbon(f"{PFX}Sail", rows, 0.011, [m["linen"]], parent=root)

    # Spiral batten stiffening the outer edge.
    batten = []
    for i in range(N + 1):
        t = i / N
        a, z, ro = helix(t)
        batten.append((ro * cos(a), ro * sin(a), z + 0.004))
    il.sweep(f"{PFX}Batten", batten, 0.024, [m["wood"]], parent=root)

    # Radial ribs under the sail.
    for k in range(9):
        t = k / 8
        a, z, ro = helix(t)
        ca, sa = cos(a), sin(a)
        il.rod(f"{PFX}Rib{k}", (0.05 * ca, 0.05 * sa, z - 0.018),
               (ro * ca, ro * sa, z - 0.014), 0.017, [m["wood"]],
               sides=6, r1=0.013, parent=root)

    # Hemp rigging from the masthead down to the batten, upper half.
    for k, t in enumerate((0.55, 0.70, 0.85, 1.0)):
        a, z, ro = helix(t)
        il.rod(f"{PFX}Rig{k}", (0, 0, 1.50),
               (ro * cos(a), ro * sin(a), z + 0.01), 0.0075,
               [m["cord"]], sides=5, parent=root)
    return root
