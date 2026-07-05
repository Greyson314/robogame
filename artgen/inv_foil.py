# artgen/inv_foil.py — inventor study: the EXISTING plain aerofoil.
# Deliberately NOT the bat-wing (inv_wing) and NOT the aerial screw
# (inv_rotor): this is the neutral rectangular blade the game ships
# today — AeroSurfaceBlock at FoilDefaults (span 1.0, chord 0.9,
# thickness 0.08) — translated into the rib-and-membrane language so
# the composed rotor+foils mechanic can keep its current geometry
# while the big authored-macro-component decisions stay parked.
# Straight walnut leading spar, four cambered spruce ribs, taut linen
# membrane with a light fabric scallop at the trailing edge, hemp
# trailing cord (tension member), brass mount disc at the root.
# Root/mount at x = -0.5; span runs +X, chord runs Y (leading +Y).

from math import sin, pi

import paperlib as pl
import inventorlib as il

PFX = "InvFoil_"

SPAN = 1.00           # FoilDefaults.DefaultSpan
Y_LE = 0.42           # leading membrane edge (spar rides just proud)
Y_TE = -0.42          # trailing membrane edge before scallop
SCALLOP = 0.05        # fabric pull-in between rib stations
CAMBER = 0.058        # membrane arch height
THICK = 0.013         # membrane thickness
RIB_X = [-0.47, -0.155, 0.16, 0.47]
N_X, N_C = 13, 9      # membrane samples: spanwise, chordwise


def _te(x):
    """Trailing-edge y at a span station: scalloped between ribs."""
    for x0, x1 in zip(RIB_X, RIB_X[1:]):
        if x0 <= x <= x1:
            u = (x - x0) / (x1 - x0)
            return Y_TE + SCALLOP * sin(pi * u)
    return Y_TE


def _camber(c):
    """Membrane arch at chord fraction c (0 = leading, 1 = trailing).
    Peak sits forward of mid-chord, like a real cambered sail."""
    return CAMBER * sin(pi * min(1.0, max(0.0, c)) ** 0.85)


def build(loc=(0.0, -11.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    # Membrane: spanwise stations, chordwise samples, arched + scalloped.
    rows = []
    for i in range(N_X):
        x = -0.47 + 0.94 * i / (N_X - 1)
        y_te = _te(x)
        row = []
        for j in range(N_C):
            c = j / (N_C - 1)
            row.append((x, Y_LE + (y_te - Y_LE) * c, _camber(c)))
        rows.append(row)
    il.ribbon(f"{PFX}Membrane", rows, THICK, [m["linen"]], parent=root)

    # Leading spar: straight tapered walnut, riding the leading edge.
    il.rod(f"{PFX}LeadSpar", (-0.50, 0.435, 0.012), (0.50, 0.435, 0.012),
           0.030, [m["wood_dark"]], r1=0.021, parent=root)

    # Ribs: cambered spruce sweeps from the spar to the trailing edge,
    # brass bead on each trailing tip.
    for k, xr in enumerate(RIB_X):
        r_rib = 0.020 if k == 0 else 0.016
        path = []
        for s in range(7):
            c = 0.02 + 0.97 * s / 6
            path.append((xr, Y_LE + (_te(xr) - Y_LE) * c, _camber(c)))
        il.sweep(f"{PFX}Rib{k}", path, r_rib, [m["wood"]], sides=6,
                 parent=root)
        tip = path[-1]
        il.lathe(f"{PFX}RibTip{k}",
                 [(0.004, -0.020), (0.016, -0.010), (0.020, 0.0),
                  (0.016, 0.010), (0.004, 0.020)],
                 [m["brass"]], segs=8, axis='Y', center=tip, parent=root)
        # hemp lashing where the rib meets the leading spar
        if k > 0:
            il.torus(f"{PFX}Lash{k}", 0.036, 0.007, [m["cord"]],
                     center=(xr, 0.435, 0.012), axis='X', segs=10,
                     sides=5, parent=root)

    # Trailing cord: hemp tension member following the scallops.
    path = []
    for i in range(N_X):
        x = -0.47 + 0.94 * i / (N_X - 1)
        path.append((x, _te(x), _camber(1.0)))
    il.sweep(f"{PFX}TrailCord", path, 0.008, [m["cord"]], sides=5,
             parent=root)

    # Root: walnut chord beam + brass mount disc on the root face +
    # cord whipping at the spar root.
    il.box(f"{PFX}RootBeam", (-0.475, 0.0, 0.005), (0.05, 0.86, 0.055),
           [m["wood_dark"]], parent=root)
    il.lathe(f"{PFX}Mount",
             [(0.10, 0.0), (0.10, -0.028), (0.045, -0.048)],
             [m["brass"]], segs=14, axis='X', center=(-0.50, 0.0, 0.0),
             parent=root)
    il.torus(f"{PFX}Whip", 0.040, 0.009, [m["cord"]],
             center=(-0.455, 0.435, 0.012), axis='X', segs=10, sides=5,
             parent=root)
    return root
