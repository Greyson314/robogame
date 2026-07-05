# artgen/inv_tips.py — inventor studies: the three tip blocks.
# Rope-end tools stay small and iconic: a three-fluke iron grapnel
# with cord whipping, a laminated-wood mace head with iron studs (the
# cannonball lamination language on a stick), and THE cartoon
# horseshoe magnet — vermilion body, pale pole shoes, brass rope eye.
# horseshoe() is shared with the grapple launcher study.

from math import cos, sin, tau, radians

import paperlib as pl
import inventorlib as il

HOOK = "InvHook_"
MACE = "InvMace_"
MAG = "InvMagnet_"


def horseshoe(pfx, parent, m, s=1.0):
    """Classic U magnet, legs pointing local -Z. Returns nothing;
    builds under `parent` (rotate the parent to aim it)."""
    path = [(0, -0.14 * s, -0.20 * s), (0, -0.14 * s, -0.06 * s)]
    for i in range(9):
        a = radians(180 - i * 22.5)
        path.append((0, cos(a) * 0.14 * s, sin(a) * 0.13 * s))
    path.append((0, 0.14 * s, -0.06 * s))
    path.append((0, 0.14 * s, -0.20 * s))
    il.sweep(f"{pfx}Body", path, 0.052 * s, [m["channel"]], sides=8,
             parent=parent)
    for sy in (-1, 1):
        il.lathe(f"{pfx}Pole{sy}",
                 [(0.058 * s, -0.205 * s), (0.062 * s, -0.30 * s),
                  (0.050 * s, -0.315 * s)],
                 [m["white"]], segs=10,
                 center=(0, sy * 0.14 * s, 0), parent=parent)
    il.torus(f"{pfx}Eye", 0.038 * s, 0.012 * s, [m["brass"]],
             center=(0, 0, 0.18 * s), axis='Y', segs=12, sides=5,
             parent=parent)


def build_hook(loc=(6.4, -8.0, 0.5)):
    pl.clear_objects(prefixes=(HOOK,))
    m = il.materials()
    root = il.root_empty(HOOK + "Root", loc)
    il.rod(f"{HOOK}Shaft", (0, 0, 0.30), (0, 0, -0.04), 0.034,
           [m["iron"]], parent=root)
    il.torus(f"{HOOK}Eye", 0.05, 0.016, [m["brass"]],
             center=(0, 0, 0.345), axis='Y', segs=12, sides=6,
             parent=root)
    for k, z in ((0, 0.245), (1, 0.195)):
        il.torus(f"{HOOK}Whip{k}", 0.042, 0.011, [m["cord"]],
                 center=(0, 0, z), axis='Z', segs=12, sides=5,
                 parent=root)
    for k in range(3):
        a = k * tau / 3
        d = (cos(a), sin(a))
        path = [(0, 0, 0.02), (0.05 * d[0], 0.05 * d[1], -0.05)]
        for i in range(8):
            th = radians(15 + i * 20)
            r, c = 0.19, (0.06, 0.10)
            px = (c[0] + r * sin(th)) * d[0]
            py = (c[0] + r * sin(th)) * d[1]
            pz = c[1] - r * cos(th)
            path.append((px, py, pz))
        il.sweep(f"{HOOK}Fluke{k}", path, 0.026, [m["iron"]], sides=7,
                 parent=root)
        tip = path[-1]
        il.lathe(f"{HOOK}FlukeTip{k}",
                 [(0.003, -0.03), (0.024, -0.012), (0.028, 0.0),
                  (0.02, 0.014), (0.003, 0.028)],
                 [m["brass"]], segs=8, center=tip, parent=root)
    return root


def build_mace(loc=(7.4, -8.0, 0.5)):
    pl.clear_objects(prefixes=(MACE,))
    m = il.materials()
    root = il.root_empty(MACE + "Root", loc)
    il.rod(f"{MACE}Haft", (0, 0, 0.34), (0, 0, 0.02), 0.028,
           [m["wood_dark"]], parent=root)
    il.torus(f"{MACE}Whip", 0.036, 0.010, [m["cord"]],
             center=(0, 0, 0.29), axis='Z', segs=12, sides=5,
             parent=root)
    pl.disc_ball(f"{MACE}Head", 0.19, (0, 0, -0.09),
                 [m["wood"], m["wood_dark"]], parent=root)
    dirs = [(cos(k * tau / 6), sin(k * tau / 6), 0) for k in range(6)]
    dirs.append((0, 0, -1))
    for k, d in enumerate(dirs):
        c = (0, 0, -0.09)
        p0 = tuple(c[i] + d[i] * 0.155 for i in range(3))
        p1 = tuple(c[i] + d[i] * 0.27 for i in range(3))
        il.rod(f"{MACE}Stud{k}", p0, p1, 0.026, [m["iron"]],
               r1=0.005, sides=6, parent=root)
    return root


def build_magnet(loc=(8.4, -8.0, 0.55)):
    pl.clear_objects(prefixes=(MAG,))
    m = il.materials()
    root = il.root_empty(MAG + "Root", loc)
    root.rotation_euler = (0, 0, radians(90))   # U-plane faces the row front
    horseshoe(MAG, root, m, s=1.0)
    il.rod(f"{MAG}RopeStub", (0, 0, 0.21), (0.03, 0.02, 0.40), 0.018,
           [m["cord"]], sides=6, parent=root)
    return root


def build():
    build_hook()
    build_mace()
    build_magnet()
