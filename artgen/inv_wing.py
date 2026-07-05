# artgen/inv_wing.py — inventor study: aero wing.
# Session-132 revision (user steer): bigger, bonier, bat-wing-ish in
# PROFILE only — a fan of tapered spars radiating from the root, linen
# membrane stretched between them, deep scallops on the outer edge.
# The bones sit centred in the membrane and are fatter than it, so the
# skeleton reads from both faces. Spans ~2 blocks: components are sized
# to their mechanic, not to one cell; the mount is the root corner.

from math import cos, sin, pi, tau, radians

import paperlib as pl
import inventorlib as il

PFX = "InvWing_"

# Fan origin sits just inboard of the mount. Spars fan from along +X
# (leading, longest) around to trailing-root (shortest). Angles in
# degrees from +X, lengths in metres.
ORIGIN = (-0.72, 0.28)
SPAR_A = [0.0, -24.0, -47.0, -70.0, -92.0]
SPAR_L = [1.62, 1.46, 1.16, 0.84, 0.58]
SCALLOP = 0.17          # edge pull-in between spar tips
THICK = 0.014           # membrane thickness
N_TH, N_T = 29, 11      # membrane samples: angular, radial


def edge_len(a_deg):
    """Outer-edge radius at an angle: lerp between spar lengths with a
    scallop dip between neighbours."""
    for i in range(len(SPAR_A) - 1):
        a0, a1 = SPAR_A[i], SPAR_A[i + 1]
        if a1 <= a_deg <= a0:
            u = (a0 - a_deg) / (a0 - a1)
            base = SPAR_L[i] + (SPAR_L[i + 1] - SPAR_L[i]) * u
            return base * (1.0 - SCALLOP * sin(pi * u))
    return SPAR_L[-1]


def surf_z(t, a_deg):
    """Membrane top surface: gentle span-wise arch, drooping toward the
    trailing spars."""
    arch = 0.09 * t * (1.0 - t) * 2.0
    droop = -0.07 * t * (abs(a_deg) / 92.0) ** 1.5
    return arch + droop


def build(loc=(0.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)
    ox, oy = ORIGIN

    # Membrane: polar rows out from the fan origin.
    rows = []
    for i in range(N_T):
        t = 0.06 + (1.0 - 0.06) * i / (N_T - 1)
        row = []
        for j in range(N_TH):
            a = SPAR_A[0] + (SPAR_A[-1] - SPAR_A[0]) * j / (N_TH - 1)
            r = t * edge_len(a)
            ar = radians(a)
            row.append((ox + r * cos(ar), oy + r * sin(ar),
                        surf_z(t, a) + THICK / 2))
        rows.append(row)
    il.ribbon(f"{PFX}Membrane", rows, THICK, [m["linen"]], parent=root)

    # Bones: swept along the membrane surface so a straight rod never
    # dives through the arch. Fatter than the membrane — visible both
    # sides. Leading spar is the thickest; the fan tapers.
    for k, (a, L) in enumerate(zip(SPAR_A, SPAR_L)):
        ar = radians(a)
        r_bone = 0.040 - 0.005 * k
        path = []
        for s in range(13):
            t = 0.02 + 0.98 * s / 12
            path.append((ox + t * L * cos(ar), oy + t * L * sin(ar),
                         surf_z(t, a)))
        il.sweep(f"{PFX}Spar{k}", path, r_bone,
                 [m["wood_dark"] if k == 0 else m["wood"]],
                 sides=7, parent=root)
        # Brass bead capping each spar tip (direction-agnostic).
        tip = path[-1]
        rb = r_bone * 1.25
        il.lathe(f"{PFX}Tip{k}",
                 [(0.004, -rb), (rb * 0.8, -rb * 0.5), (rb, 0.0),
                  (rb * 0.8, rb * 0.5), (0.004, rb)],
                 [m["brass"]], segs=10, center=tip, parent=root)

    # One curved cross-batten mid-span tying the fan together.
    batten = []
    for j in range(N_TH):
        a = SPAR_A[0] + (SPAR_A[-1] - SPAR_A[0]) * j / (N_TH - 1)
        ar = radians(a)
        r = 0.55 * edge_len(a)
        batten.append((ox + r * cos(ar), oy + r * sin(ar),
                       surf_z(0.55, a) - THICK))
    il.sweep(f"{PFX}Batten", batten, 0.016, [m["wood"]], sides=6,
             parent=root)

    # Root boss where the fan converges: turned walnut disc + cord
    # whipping + brass mount plate below (the one block-sized part).
    il.lathe(f"{PFX}Boss",
             [(0.055, -0.055), (0.105, -0.03), (0.115, 0.02),
              (0.08, 0.06), (0.03, 0.08)],
             [m["wood_dark"]], segs=12, center=(ox, oy, 0.0), parent=root)
    il.torus(f"{PFX}Whip", 0.108, 0.012, [m["cord"]],
             center=(ox, oy, -0.01), axis='Z', segs=16, sides=5,
             parent=root)
    il.box(f"{PFX}Mount", (ox, oy, -0.085), (0.20, 0.20, 0.026),
           [m["brass"]], parent=root)
    return root
