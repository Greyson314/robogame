# artgen/inv_rotor.py — inventor study: rotor as da Vinci's aerial screw.
# Session-132 revision: components are sized to the mechanic they replace,
# not to one cell — this visually replaces the whole helicopter assembly
# (rotor + spun foils), so it's a WIDE, THIN screw: ~2.9 m sail disc,
# low helical rise, short mast. The 1 m yaw gear stays the mount read;
# the sail deliberately overhangs the cell.
# Walnut mast, helical linen sail with fabric sag, oak spiral batten,
# radial ribs, hemp rigging from the masthead. Brass collars + finial.

from math import cos, sin, tau

import paperlib as pl
import inventorlib as il

PFX = "InvRotor_"

TURNS = 1.6
Z0, Z1 = 0.30, 0.60           # low rise — a spun disc, not a tower
R_IN = 0.06
R_OUT0, R_OUT1 = 1.45, 1.34   # slight cone, wider at the base


def helix(t):
    """t in [0,1] -> (angle, z, r_out)."""
    a = t * TURNS * tau
    return a, Z0 + (Z1 - Z0) * t, R_OUT0 + (R_OUT1 - R_OUT0) * t


def build(loc=(4.5, -5.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Yaw gear: laminated oak, walnut cut edge (rotating things stand on
    # gears — weapon-family base language), turned walnut collar hub.
    pl.card_panel(f"{PFX}Gear", pl.gear_profile(20, 0.40, 0.46), 0.07,
                  'Z', 0.035, [m["wood"], m["wood_dark"]],
                  cap_slots=(0, 0), edge_slot=1, parent=root)
    il.lathe(f"{PFX}Collar",
             [(0.17, 0.07), (0.15, 0.11), (0.10, 0.15), (0.068, 0.18)],
             [m["wood_dark"]], segs=16, parent=root)

    # Short mast — just enough to carry the screw and its rigging.
    il.rod(f"{PFX}Mast", (0, 0, 0.07), (0, 0, 0.86), 0.05,
           [m["wood_dark"]], sides=10, parent=root)
    for k, z in ((0, Z0), (1, Z1)):
        il.torus(f"{PFX}MastRing{k}", 0.060, 0.013, [m["brass"]],
                 center=(0, 0, z), axis='Z', segs=14, sides=6, parent=root)
    il.lathe(f"{PFX}Finial",
             [(0.014, 0.86), (0.036, 0.885), (0.044, 0.915),
              (0.030, 0.945), (0.005, 0.965)],
             [m["brass"]], segs=12, parent=root)

    # Helical linen sail: wide and shallow. Fabric sag at mid-radius.
    N = 110
    rows = []
    for i in range(N + 1):
        t = i / N
        a, z, ro = helix(t)
        ca, sa = cos(a), sin(a)
        rows.append([
            (R_IN * ca, R_IN * sa, z),
            (0.78 * ca, 0.78 * sa, z - 0.055),
            (ro * ca, ro * sa, z),
        ])
    il.ribbon(f"{PFX}Sail", rows, 0.012, [m["linen"]], parent=root)

    # Spiral batten stiffening the outer edge.
    batten = []
    for i in range(N + 1):
        t = i / N
        a, z, ro = helix(t)
        batten.append((ro * cos(a), ro * sin(a), z + 0.004))
    il.sweep(f"{PFX}Batten", batten, 0.027, [m["wood"]], parent=root)

    # Radial ribs under the sail — more of them at this span.
    for k in range(11):
        t = k / 10
        a, z, ro = helix(t)
        ca, sa = cos(a), sin(a)
        il.rod(f"{PFX}Rib{k}", (0.055 * ca, 0.055 * sa, z - 0.020),
               (ro * ca, ro * sa, z - 0.016), 0.020, [m["wood"]],
               sides=6, r1=0.014, parent=root)

    # Hemp rigging from the masthead down to the batten — only on the
    # final upper turn, so no cord crosses over a lower sail turn (at
    # this squashed aspect crossing cords read as an umbrella frame).
    for k, t in enumerate((0.78, 0.90, 1.0)):
        a, z, ro = helix(t)
        il.rod(f"{PFX}Rig{k}", (0, 0, 0.84),
               (ro * cos(a), ro * sin(a), z + 0.012), 0.0075,
               [m["cord"]], sides=5, parent=root)
    return root
