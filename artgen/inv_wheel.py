# artgen/inv_wheel.py — inventor study: wheel.
# Cart-wright wheel: six wooden felloe segments (the joints read at a
# glance), turned tapered spokes, walnut hub with a brass cap, and a
# wound hemp-rope tire — the rope both explains grip and puts the cord
# material on the most-seen movement block. Axle along X.

from math import cos, sin, tau, radians

import paperlib as pl
import inventorlib as il

PFX = "InvWheel_"


def build(loc=(-2.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)

    R_OUT = 0.40    # felloe outer radius
    WALL = 0.075    # felloe depth
    HW = 0.055      # felloe half-width along X

    # Six walnut felloes with visible joint gaps — dark rim so the light
    # spokes read against it.
    for i in range(6):
        a0 = i * 60 + 3.5
        a1 = (i + 1) * 60 - 3.5
        il.arc_seg_x(f"{PFX}Felloe{i}", R_OUT, WALL, -HW, HW, a0, a1, 8,
                     [m["wood_dark"]], parent=root)

    # Rope tire: three cord windings side by side, proud of the rim.
    for k, x in enumerate((-0.036, 0.0, 0.036)):
        il.torus(f"{PFX}Rope{k}", 0.432, 0.032, [m["cord"]],
                 center=(x, 0, 0), axis='X', segs=30, sides=7, parent=root)

    # Eight turned spokes, thick at the hub, tapering outward.
    for i in range(8):
        a = radians(i * 45 + 22.5)
        y, z = cos(a), sin(a)
        il.rod(f"{PFX}Spoke{i}", (0, y * 0.10, z * 0.10),
               (0, y * 0.335, z * 0.335), 0.034, [m["wood"]],
               r1=0.024, parent=root)

    # Walnut hub, turned profile, with a brass hub cap + finial nut.
    il.lathe(f"{PFX}Hub",
             [(0.055, -0.105), (0.105, -0.075), (0.115, 0.0),
              (0.105, 0.075), (0.055, 0.105)],
             [m["wood_dark"]], segs=18, axis='X', parent=root)
    il.lathe(f"{PFX}HubCap",
             [(0.062, 0.105), (0.055, 0.135), (0.028, 0.158),
              (0.003, 0.165)],
             [m["brass"]], segs=14, axis='X', parent=root)
    # Axle stub out the back face.
    il.lathe(f"{PFX}Axle", [(0.035, -0.175), (0.035, -0.105)],
             [m["iron"]], segs=10, axis='X', parent=root)
    return root
