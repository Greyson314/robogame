# artgen/inv_hoverblade.py — inventor study: hover blade.
# A flat paddle fan: shallow oak hoop, six pitched linen vanes on
# turned spokes, brass hub finial. The aerial screw's stubby cousin —
# hover = a fan pushing straight down, so it reads low and disc-like.

from math import cos, sin, tau

import paperlib as pl
import inventorlib as il

PFX = "InvHover_"

R_HOOP = 0.44
Z = -0.12          # fan plane below block centre — downdraft machine


def vane(name, a0, a1, m, parent):
    """One pitched linen paddle: gentle uniform pitch, blades overlap
    pinwheel-style so the rotation order reads at a glance."""
    lift = 0.024
    pts_top, pts_bot = [], []
    for (r, aa, dz) in ((0.11, a0, lift), (0.11, a1, -lift),
                        (0.40, a1, -lift), (0.40, a0, lift)):
        x, y = r * cos(aa), r * sin(aa)
        pts_top.append((x, y, Z + dz + 0.006))
        pts_bot.append((x, y, Z + dz - 0.006))
    verts = pts_top + pts_bot
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2),
             (2, 6, 7, 3), (3, 7, 4, 0)]
    pl.make_object(name, verts, faces, [m["linen"]], parent=parent)


def build(loc=(-1.8, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    il.torus(f"{PFX}Hoop", R_HOOP, 0.042, [m["wood"]],
             center=(0, 0, Z), axis='Z', segs=28, sides=8, parent=root)
    il.lathe(f"{PFX}Hub",
             [(0.045, Z - 0.07), (0.10, Z - 0.045), (0.11, Z + 0.01),
              (0.08, Z + 0.05), (0.03, Z + 0.075)],
             [m["wood_dark"]], segs=12, parent=root)
    il.lathe(f"{PFX}Finial",
             [(0.006, Z + 0.075), (0.024, Z + 0.095),
              (0.028, Z + 0.12), (0.016, Z + 0.145), (0.003, Z + 0.16)],
             [m["brass"]], segs=10, parent=root)

    span = tau / 6
    for i in range(6):
        a0 = i * span
        a1 = (i + 1) * span + 0.09      # slight overlap = pinwheel read
        vane(f"{PFX}Vane{i}", a0, a1, m, root)
        # Turned spoke under each vane's leading edge.
        am = a0 + 0.02
        il.rod(f"{PFX}Spoke{i}",
               (0.10 * cos(am), 0.10 * sin(am), Z + 0.03),
               (R_HOOP * cos(am), R_HOOP * sin(am), Z + 0.055),
               0.017, [m["wood"]], r1=0.012, parent=root)
    return root
