# artgen/inv_wheel.py — inventor study: wheel.
# Cart-wright wheel: six walnut felloe segments, turned tapered spokes,
# walnut hub with a brass cap, wound hemp-rope tire. Axle along X.
# make_wheel() is reusable (steer fork, cannon carriage import it).

from math import cos, sin, tau, radians

import paperlib as pl
import inventorlib as il

PFX = "InvWheel_"


def make_wheel(pfx, parent, m, s=1.0, axle=True):
    """Build the wheel centred on `parent`'s origin, axle along X,
    uniformly scaled by s. ~0.46*s outer radius."""
    for i in range(6):
        a0 = i * 60 + 3.5
        a1 = (i + 1) * 60 - 3.5
        il.arc_seg_x(f"{pfx}Felloe{i}", 0.40 * s, 0.075 * s,
                     -0.055 * s, 0.055 * s, a0, a1, 8,
                     [m["wood_dark"]], parent=parent)
    for k, x in enumerate((-0.036, 0.0, 0.036)):
        il.torus(f"{pfx}Rope{k}", 0.432 * s, 0.032 * s, [m["cord"]],
                 center=(x * s, 0, 0), axis='X', segs=30, sides=7,
                 parent=parent)
    for i in range(8):
        a = radians(i * 45 + 22.5)
        y, z = cos(a), sin(a)
        il.rod(f"{pfx}Spoke{i}", (0, y * 0.10 * s, z * 0.10 * s),
               (0, y * 0.335 * s, z * 0.335 * s), 0.034 * s, [m["wood"]],
               r1=0.024 * s, parent=parent)
    il.lathe(f"{pfx}Hub",
             [(0.055 * s, -0.105 * s), (0.105 * s, -0.075 * s),
              (0.115 * s, 0.0), (0.105 * s, 0.075 * s),
              (0.055 * s, 0.105 * s)],
             [m["wood_dark"]], segs=18, axis='X', parent=parent)
    il.lathe(f"{pfx}HubCap",
             [(0.062 * s, 0.105 * s), (0.055 * s, 0.135 * s),
              (0.028 * s, 0.158 * s), (0.003, 0.165 * s)],
             [m["brass"]], segs=14, axis='X', parent=parent)
    if axle:
        il.lathe(f"{pfx}Axle", [(0.035 * s, -0.175 * s),
                                (0.035 * s, -0.105 * s)],
                 [m["iron"]], segs=10, axis='X', parent=parent)


def build(loc=(-2.0, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    root = il.root_empty(PFX + "Root", loc)
    make_wheel(PFX, root, m)
    return root
