# artgen/inv_modules.py — inventor studies: module blocks.
# Family rule: every module is the same alchemical apparatus — walnut
# plinth, oak deck, brass-collared GLASS BELL JAR — and only the
# contraption inside changes. Full set (session 133): EMP (mini tesla
# coil, cyan spark), Repair (mint draught), Blink (plasma hourglass),
# Shield (linen parasol), Smoke (censer + carved wisp), Invis (empty
# jar, specimen tag — the joke IS the contents), Mines (bomb pyramid).

import bpy
from math import cos, sin, tau

import paperlib as pl
import inventorlib as il
from inv_cpu import spark_material

PFX_E = "InvModEmp_"
PFX_R = "InvModRep_"
PFX_B = "InvModBlink_"
PFX_S = "InvModShield_"
PFX_K = "InvModSmoke_"
PFX_I = "InvModInvis_"
PFX_M = "InvModMines_"

PLASMA = (0.33, 0.09, 0.86, 1.0)   # linear ~ WorldPalette Plasma


def plasma_material():
    mat = bpy.data.materials.get("InvPlasma")
    if mat is None:
        mat = bpy.data.materials.new("InvPlasma")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = PLASMA
        bsdf.inputs["Roughness"].default_value = 0.35
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = PLASMA
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = 1.6
    return mat


def apparatus(pfx, loc, m):
    root = il.root_empty(pfx + "Root", loc)
    il.box(f"{pfx}Plinth", (0, 0, -0.42), (0.42, 0.42, 0.06),
           [m["wood_dark"]], parent=root)
    il.box(f"{pfx}Step", (0, 0, -0.365), (0.34, 0.34, 0.05),
           [m["wood"]], parent=root)
    il.lathe(f"{pfx}Deck",
             [(0.185, -0.34), (0.19, -0.315), (0.155, -0.30)],
             [m["wood"]], segs=14, parent=root)
    il.torus(f"{pfx}Collar", 0.158, 0.015, [m["brass"]],
             center=(0, 0, -0.295), axis='Z', segs=16, sides=6,
             parent=root)
    il.lathe(f"{pfx}Jar",
             [(0.150, -0.295), (0.165, -0.10), (0.148, 0.04),
              (0.10, 0.13), (0.034, 0.17)],
             [m["glass"]], segs=16, parent=root)
    il.lathe(f"{pfx}Finial",
             [(0.005, 0.168), (0.026, 0.188), (0.029, 0.212),
              (0.016, 0.232), (0.003, 0.245)],
             [m["brass"]], segs=10, parent=root)
    return root


def build_emp(loc=(11.6, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX_E,))
    m = il.materials()
    spark = spark_material()
    root = apparatus(PFX_E, loc, m)
    il.rod(f"{PFX_E}Coil", (0, 0, -0.30), (0, 0, -0.03), 0.017,
           [m["brass"]], parent=root)
    for k, (z, R) in enumerate(((-0.20, 0.055), (-0.12, 0.042))):
        il.torus(f"{PFX_E}Ring{k}", R, 0.011, [m["brass"]],
                 center=(0, 0, z), axis='Z', segs=14, sides=6,
                 parent=root)
    il.lathe(f"{PFX_E}Spark",
             [(0.005, -0.045), (0.034, -0.022), (0.043, 0.0),
              (0.034, 0.022), (0.005, 0.045)],
             [spark], segs=12, center=(0, 0, 0.035), parent=root)
    return root


def build_repair(loc=(12.9, -8.0, 0.5)):
    pl.clear_objects(prefixes=(PFX_R,))
    m = il.materials()
    root = apparatus(PFX_R, loc, m)
    il.rod(f"{PFX_R}Stand", (0, 0, -0.30), (0, 0, -0.13), 0.013,
           [m["brass"]], parent=root)
    il.lathe(f"{PFX_R}Draught",
             [(0.006, -0.13), (0.045, -0.10), (0.058, -0.05),
              (0.045, 0.0), (0.006, 0.03)],
             [m["mint"]], segs=12, parent=root)
    il.torus(f"{PFX_R}Wrap", 0.060, 0.010, [m["linen"]],
             center=(0, 0, -0.05), axis='Z', segs=12, sides=5,
             parent=root)
    return root


def build_blink(loc=(14.2, -8.0, 0.5)):
    # A tiny hourglass: blink = skipping the middle of the journey.
    pl.clear_objects(prefixes=(PFX_B,))
    m = il.materials()
    plasma = plasma_material()
    root = apparatus(PFX_B, loc, m)
    for z in (-0.30, -0.055):
        il.lathe(f"{PFX_B}Disc{int(z * 100)}",
                 [(0.062, z), (0.062, z + 0.014), (0.045, z + 0.020)],
                 [m["brass"]], segs=12, parent=root)
    for k in range(3):
        a = k * tau / 3
        il.rod(f"{PFX_B}Post{k}",
               (0.056 * cos(a), 0.056 * sin(a), -0.288),
               (0.056 * cos(a), 0.056 * sin(a), -0.055),
               0.006, [m["brass"]], sides=6, parent=root)
    il.lathe(f"{PFX_B}BulbLow",
             [(0.006, -0.284), (0.043, -0.262), (0.048, -0.235),
              (0.020, -0.196), (0.005, -0.176)],
             [m["glass"]], segs=12, parent=root)
    il.lathe(f"{PFX_B}BulbHigh",
             [(0.005, -0.168), (0.020, -0.148), (0.048, -0.109),
              (0.043, -0.082), (0.006, -0.060)],
             [m["glass"]], segs=12, parent=root)
    # plasma sand: heap in the lower bulb + a falling thread
    il.lathe(f"{PFX_B}Sand",
             [(0.005, -0.280), (0.034, -0.262), (0.024, -0.243),
              (0.004, -0.232)],
             [plasma], segs=10, parent=root)
    il.rod(f"{PFX_B}Thread", (0, 0, -0.232), (0, 0, -0.172), 0.004,
           [plasma], sides=5, parent=root)
    return root


def build_shield(loc=(15.5, -8.0, 0.5)):
    # A little linen parasol — the politest possible force field.
    pl.clear_objects(prefixes=(PFX_S,))
    m = il.materials()
    root = apparatus(PFX_S, loc, m)
    il.rod(f"{PFX_S}Pole", (0, 0, -0.30), (0, 0, -0.045), 0.008,
           [m["wood"]], sides=6, parent=root)
    il.lathe(f"{PFX_S}Canopy",
             [(0.005, -0.036), (0.055, -0.058), (0.092, -0.092),
              (0.098, -0.112)],
             [m["linen"]], segs=14, parent=root)
    il.lathe(f"{PFX_S}Ferrule",
             [(0.004, -0.044), (0.011, -0.030), (0.010, -0.016),
              (0.003, -0.006)],
             [m["brass"]], segs=8, parent=root)
    for k in range(6):
        a = k * tau / 6
        il.rod(f"{PFX_S}RibTip{k}",
               (0.096 * cos(a), 0.096 * sin(a), -0.110),
               (0.104 * cos(a), 0.104 * sin(a), -0.122),
               0.004, [m["wood_dark"]], sides=5, parent=root)
    return root


def build_smoke(loc=(16.8, -8.0, 0.5)):
    # Brass censer with a carved wisp frozen mid-rise.
    pl.clear_objects(prefixes=(PFX_K,))
    m = il.materials()
    root = apparatus(PFX_K, loc, m)
    il.lathe(f"{PFX_K}Censer",
             [(0.020, -0.30), (0.052, -0.284), (0.060, -0.252),
              (0.046, -0.228), (0.018, -0.220)],
             [m["brass"]], segs=12, parent=root)
    il.torus(f"{PFX_K}Rim", 0.032, 0.006, [m["brass"]],
             center=(0, 0, -0.222), axis='Z', segs=12, sides=5,
             parent=root)
    path = []
    for s in range(11):
        t = s / 10.0
        path.append((0.042 * sin(t * tau * 0.9),
                     0.030 * sin(t * tau * 0.6 + 1.2),
                     -0.215 + 0.30 * t))
    il.sweep(f"{PFX_K}Wisp", path, 0.012, [m["gray"]], sides=7,
             parent=root)
    pl.disc_ball(f"{PFX_K}Puff", 0.030,
                 (path[-1][0], path[-1][1], path[-1][2] + 0.02),
                 [m["gray"]], parent=root, bands=5, segs=9)
    return root


def build_invis(loc=(18.1, -8.0, 0.5)):
    # The empty jar. Contents: officially none. A specimen tag lies on
    # the deck where the contraption should be.
    pl.clear_objects(prefixes=(PFX_I,))
    m = il.materials()
    root = apparatus(PFX_I, loc, m)
    il.box(f"{PFX_I}Tag", (0.040, -0.025, -0.289), (0.095, 0.065, 0.008),
           [m["white"]], parent=root)
    il.box(f"{PFX_I}TagInk", (0.040, -0.025, -0.284), (0.06, 0.008, 0.004),
           [m["ink"]], parent=root)
    il.rod(f"{PFX_I}TagCord", (-0.005, 0.005, -0.288),
           (-0.055, 0.055, -0.286), 0.0035, [m["cord"]], sides=5,
           parent=root)
    return root


def build_mines(loc=(19.4, -8.0, 0.5)):
    # A neat little pyramid of laminated bombs, fuses up.
    pl.clear_objects(prefixes=(PFX_M,))
    m = il.materials()
    root = apparatus(PFX_M, loc, m)
    spots = ((0.055, 0.0, -0.245), (-0.045, 0.045, -0.245),
             (-0.045, -0.045, -0.245), (-0.012, 0.0, -0.152))
    for k, c in enumerate(spots):
        pl.disc_ball(f"{PFX_M}Bomb{k}", 0.052, c, [m["wood_dark"]],
                     parent=root, bands=6, segs=10)
        il.torus(f"{PFX_M}Band{k}", 0.052, 0.007, [m["brass"]],
                 center=c, axis='Z', segs=12, sides=5, parent=root)
        il.rod(f"{PFX_M}Fuse{k}", (c[0], c[1], c[2] + 0.048),
               (c[0] + 0.012, c[1] + 0.008, c[2] + 0.075), 0.005,
               [m["cord"]], sides=5, parent=root)
    return root


def build():
    build_emp()
    build_repair()
    build_blink()
    build_shield()
    build_smoke()
    build_invis()
    build_mines()
