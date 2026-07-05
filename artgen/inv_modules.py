# artgen/inv_modules.py — inventor studies: module blocks.
# Family rule: every module is the same alchemical apparatus — walnut
# plinth, oak deck, brass-collared GLASS BELL JAR — and only the
# contraption inside changes. Two exemplars here: EMP (a mini tesla
# coil with a cyan spark — the note's sanctioned anachronism) and
# Repair (a mint draught on a brass stand). Blink/Shield/Smoke/Invis/
# Mines follow the same recipe with different jar contents.

import paperlib as pl
import inventorlib as il
from inv_cpu import spark_material

PFX_E = "InvModEmp_"
PFX_R = "InvModRep_"


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


def build():
    build_emp()
    build_repair()
