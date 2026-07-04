# artgen/inv_cpu.py — inventor study: CPU block.
# A gimballed gyroscope on a turned walnut pedestal: two interlocked
# wooden gimbal rings, a brass flywheel, and at the very center a small
# glowing cyan spark — the idea that keeps the machine alive. The spark
# is the direction note's sanctioned anachronism and keeps the game's
# cyan = CPU/energy vocabulary alive inside the wood + linen world.

import bpy
from math import tau, cos, sin

import paperlib as pl
import inventorlib as il

PFX = "InvCpu_"

SPARK = (0.02, 0.55, 0.75, 1.0)   # linear cyan


def spark_material():
    mat = bpy.data.materials.get("InvSpark")
    if mat is None:
        mat = bpy.data.materials.new("InvSpark")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = SPARK
        bsdf.inputs["Roughness"].default_value = 0.3
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = SPARK
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = 3.0
    return mat


def build(loc=(9.2, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    spark = spark_material()
    root = il.root_empty(PFX + "Root", loc)

    # Laminated base plinth + turned walnut pedestal.
    il.box(f"{PFX}Plinth", (0, 0, -0.455), (0.52, 0.52, 0.07),
           [m["wood_dark"]], parent=root)
    il.box(f"{PFX}Plinth2", (0, 0, -0.405), (0.40, 0.40, 0.05),
           [m["wood"]], parent=root)
    il.lathe(f"{PFX}Pedestal",
             [(0.13, -0.40), (0.09, -0.37), (0.05, -0.335),
              (0.045, -0.315), (0.075, -0.30)],
             [m["wood_dark"]], segs=14, parent=root)
    il.torus(f"{PFX}PedCollar", 0.052, 0.012, [m["brass"]],
             center=(0, 0, -0.302), axis='Z', segs=12, sides=6, parent=root)

    # Gimbal rings: spruce outer hoop (vertical, facing forward), walnut
    # inner hoop, brass pivot beads where the axes pass through.
    il.torus(f"{PFX}RingOuter", 0.285, 0.027, [m["wood"]],
             center=(0, 0, 0), axis='Y', segs=28, sides=7, parent=root)
    il.torus(f"{PFX}RingInner", 0.215, 0.023, [m["wood_dark"]],
             center=(0, 0, 0), axis='X', segs=24, sides=7, parent=root)
    for k, (x, y, z) in enumerate(((0, 0, 0.285), (0, 0, -0.285),
                                   (0.215, 0, 0), (-0.215, 0, 0))):
        il.lathe(f"{PFX}Pivot{k}",
                 [(0.012, -0.028), (0.030, -0.012), (0.030, 0.012),
                  (0.012, 0.028)],
                 [m["brass"]], segs=10, center=(x, y, z),
                 axis='Z' if k < 2 else 'X', parent=root)

    # Brass flywheel: open ring + spokes, so daylight passes through the
    # mechanism and the spark stays visible from every angle.
    il.torus(f"{PFX}Flywheel", 0.15, 0.021, [m["brass"]],
             center=(0, 0, 0), axis='Y', segs=22, sides=7, parent=root)
    for i in range(6):
        a = i * tau / 6
        il.rod(f"{PFX}FlySpoke{i}",
               (0.05 * cos(a), 0, 0.05 * sin(a)),
               (0.145 * cos(a), 0, 0.145 * sin(a)),
               0.011, [m["brass"]], sides=6, parent=root)

    # The spark. Small, loud, alive.
    il.lathe(f"{PFX}Spark",
             [(0.012, -0.055), (0.045, -0.028), (0.058, 0.0),
              (0.045, 0.028), (0.012, 0.055)],
             [spark], segs=14, parent=root)
    return root
