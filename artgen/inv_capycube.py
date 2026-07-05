# artgen/inv_capycube.py — inventor study: command cube with capybara pilot.
# A walnut timber-frame cage (the frame+panel language kept for special
# one-off blocks per the session-132 cube call), oak plank floor, cambered
# linen canopy, a little ship's helm at the open front — and sitting
# behind it, entirely unbothered, the capybara. The cyan spark hangs in a
# brass lantern from the canopy: same "idea that keeps the machine alive"
# vocabulary as inv_cpu, because this is where the machine gets its ideas.

import bpy
from math import tau, cos, sin, pi

import paperlib as pl
import inventorlib as il
import inv_cpu

PFX = "InvCapyCube_"

FUR = (0.21, 0.115, 0.05, 1.0)        # tawny capybara brown (linear)
FUR_LIGHT = (0.30, 0.175, 0.085, 1.0)  # muzzle / feet


def fur_materials():
    fur = pl.get_material("InvCapyFur", FUR, roughness=0.93)
    il._weave(fur, scale=90.0, strength=0.22)   # fuzz, not linen weave
    fur_l = pl.get_material("InvCapyFurLight", FUR_LIGHT, roughness=0.93)
    il._weave(fur_l, scale=90.0, strength=0.22)
    return fur, fur_l


def build(loc=(10.4, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    oak_a, oak_b = il.oak_grain('X')
    fur, fur_l = fur_materials()
    spark = inv_cpu.spark_material()
    root = il.root_empty(PFX + "Root", loc)

    # ---- the cube: walnut frame, open sides, plank floor -------------
    for i, (sx, sy) in enumerate(((1, 1), (1, -1), (-1, 1), (-1, -1))):
        il.box(f"{PFX}Post{i}", (sx * 0.45, sy * 0.45, 0),
               (0.10, 0.10, 1.0), [m["wood_dark"]], parent=root)
        # brass peg cap on each post top
        il.lathe(f"{PFX}Cap{i}",
                 [(0.030, 0.0), (0.030, 0.022), (0.013, 0.034)],
                 [m["brass"]], segs=12,
                 center=(sx * 0.45, sy * 0.45, 0.50), parent=root)
    for i, s in enumerate((1, -1)):
        for z in (0.45, -0.45):
            tag = "Top" if z > 0 else "Skirt"
            il.box(f"{PFX}{tag}X{i}{'a' if z > 0 else 'b'}",
                   (0, s * 0.45, z), (1.0, 0.10, 0.10),
                   [m["wood_dark"]], parent=root)
            il.box(f"{PFX}{tag}Y{i}{'a' if z > 0 else 'b'}",
                   (s * 0.45, 0, z), (0.10, 1.0, 0.10),
                   [m["wood_dark"]], parent=root)

    # floor planks, grain along X, alternating oak tones
    for i, y in enumerate((-0.30, 0.0, 0.30)):
        il.box(f"{PFX}Plank{i}", (0, y, -0.42), (0.82, 0.27, 0.05),
               [oak_a if i % 2 == 0 else oak_b], parent=root)

    # mid rails on back + sides (front open — that's the bridge)
    il.box(f"{PFX}RailBack", (0, -0.45, 0.02), (0.80, 0.055, 0.055),
           [m["wood"]], parent=root)
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}RailSide{i}", (s * 0.45, 0, 0.02),
               (0.055, 0.80, 0.055), [m["wood"]], parent=root)

    # cambered linen canopy under the top frame
    xs = [-0.46 + 0.92 * k / 6 for k in range(7)]
    rows = []
    for j in range(5):
        y = -0.44 + 0.88 * j / 4
        rows.append([(x, y, 0.365 + 0.065 * (1.0 - (x / 0.46) ** 2))
                     for x in xs])
    il.ribbon(f"{PFX}Canopy", rows, 0.018, [m["linen"]], parent=root,
              axis='Z')

    # ---- the helm ----------------------------------------------------
    il.rod(f"{PFX}HelmPost", (0, 0.34, -0.42), (0, 0.36, -0.16),
           0.022, [m["wood_dark"]], parent=root)
    il.torus(f"{PFX}HelmRim", 0.11, 0.013, [m["wood"]],
             center=(0, 0.36, -0.10), axis='Y', segs=22, sides=7,
             parent=root)
    for i in range(6):
        a = i * tau / 6
        d = (cos(a), sin(a))
        il.rod(f"{PFX}HelmSpoke{i}",
               (0.030 * d[0], 0.36, -0.10 + 0.030 * d[1]),
               (0.105 * d[0], 0.36, -0.10 + 0.105 * d[1]),
               0.008, [m["wood"]], sides=6, parent=root)
        il.rod(f"{PFX}HelmHandle{i}",
               (0.110 * d[0], 0.36, -0.10 + 0.110 * d[1]),
               (0.148 * d[0], 0.36, -0.10 + 0.148 * d[1]),
               0.0095, [m["wood_dark"]], sides=6, parent=root)
    il.lathe(f"{PFX}HelmHub",
             [(0.012, -0.030), (0.026, -0.012), (0.026, 0.012),
              (0.012, 0.030)],
             [m["brass"]], segs=12, axis='Y', center=(0, 0.36, -0.10),
             parent=root)

    # ---- the spark lantern -------------------------------------------
    il.rod(f"{PFX}LanternCord", (0, 0, 0.375), (0, 0, 0.305),
           0.006, [m["cord"]], sides=6, parent=root)
    il.lathe(f"{PFX}LanternCap",
             [(0.008, 0.0), (0.032, -0.010), (0.034, -0.020),
              (0.006, -0.026)],
             [m["brass"]], segs=12, center=(0, 0, 0.305), parent=root)
    il.torus(f"{PFX}LanternRing", 0.026, 0.0045, [m["brass"]],
             center=(0, 0, 0.228), axis='Z', segs=12, sides=6, parent=root)
    for i in range(3):
        a = i * tau / 3
        il.rod(f"{PFX}LanternBar{i}",
               (0.030 * cos(a), 0.030 * sin(a), 0.288),
               (0.026 * cos(a), 0.026 * sin(a), 0.228),
               0.004, [m["brass"]], sides=5, parent=root)
    pl.disc_ball(f"{PFX}Spark", 0.024, (0, 0, 0.262), [spark],
                 parent=root, bands=6, segs=10)

    # ---- the capybara (sitting upright, facing the helm) -------------
    floor_top = -0.395

    # body: chonky egg, big rump, narrowing chest
    il.lathe(f"{PFX}CapyBody",
             [(0.035, floor_top), (0.115, floor_top + 0.010),
              (0.150, floor_top + 0.095), (0.145, floor_top + 0.175),
              (0.115, floor_top + 0.265), (0.075, floor_top + 0.320),
              (0.028, floor_top + 0.350)],
             [fur], segs=18, center=(0, -0.08, 0), parent=root)

    # head: blunt-nosed loaf along +Y — the capybara profile IS the snout
    il.lathe(f"{PFX}CapyHead",
             [(0.010, -0.090), (0.058, -0.078), (0.074, -0.030),
              (0.072, 0.015), (0.062, 0.055), (0.055, 0.085),
              (0.050, 0.105), (0.010, 0.113)],
             [fur], segs=16, axis='Y', center=(0, 0.03, -0.035),
             parent=root)

    # nose pad on the snout tip
    il.lathe(f"{PFX}CapyNose",
             [(0.020, -0.006), (0.026, 0.000), (0.014, 0.008)],
             [m["ink"]], segs=10, axis='Y', center=(0, 0.143, -0.045),
             parent=root)

    # eyes: small, high, far apart, judging nothing
    for i, s in enumerate((1, -1)):
        pl.disc_ball(f"{PFX}CapyEye{i}", 0.012,
                     (s * 0.065, 0.055, 0.005), [m["ink"]],
                     parent=root, bands=5, segs=8)
        # ears: little cups on top-back of the head
        il.lathe(f"{PFX}CapyEar{i}",
                 [(0.006, 0.0), (0.019, 0.014), (0.021, 0.030),
                  (0.010, 0.040)],
                 [fur], segs=10, center=(s * 0.042, -0.010, 0.030),
                 parent=root)
        # front legs planted between the hind feet
        il.rod(f"{PFX}CapyLeg{i}",
               (s * 0.060, 0.020, -0.20), (s * 0.062, 0.050, -0.385),
               0.020, [fur], parent=root)
        il.box(f"{PFX}CapyFoot{i}", (s * 0.062, 0.075, -0.382),
               (0.048, 0.065, 0.026), [fur_l], parent=root)
        # hind feet poking forward from under the rump — the sitting cue
        il.box(f"{PFX}CapyHindFoot{i}", (s * 0.105, 0.010, -0.382),
               (0.052, 0.110, 0.026), [fur_l], parent=root)

    return root
