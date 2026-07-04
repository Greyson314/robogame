# artgen/smg_style_steampunk.py — SMG style study 2: classic steampunk.
# Blender-only exploration (no FBX export) — the paper-punk SMG stays the
# in-game version. Same cutesy bones as smg_paperpunk.py; the material
# language goes full brass-and-iron: riveted plates, enamel green, copper
# pipe run, valve handwheel, mahogany rear cap, stubby smokestack.

import sys

ARTGEN = r"C:\Users\Grey\Desktop\mutedtuple\robogame\artgen"
if ARTGEN not in sys.path:
    sys.path.insert(0, ARTGEN)

import importlib
import bpy
from math import radians, cos, sin, tau
import paperlib
importlib.reload(paperlib)
from paperlib import (clear_objects, hide_default_cube, make_object, loft,
                      card_panel, ngon_pts, arc_shell, gear_profile, brad,
                      ref_block, scale_tree, disc_ball)

LOCATION = (5.4, 0.0, 0.0)
PIVOT_Z = 0.47
GEAR_Z0 = 0.0
GEAR_TOP = 0.095
GEAR_R_OUT = 0.32
SCALE = 0.5 / GEAR_R_OUT
RAIL_ROOT_Y = -0.48
RAIL_TIP_Y = -0.78
BARREL_R = 0.07
SHELL_T = 0.018
GAP_DEG = 16.0
ARC_SEGS = 10
RAKE_DEG = 4.0


def mat(name, color, rough=0.6, metallic=0.0):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = color
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metallic
    return m


iron = mat("StmIron", (0.14, 0.14, 0.16, 1), rough=0.5, metallic=0.7)
brass = mat("StmBrass", (0.82, 0.62, 0.24, 1), rough=0.25, metallic=1.0)
copper = mat("StmCopper", (0.74, 0.44, 0.27, 1), rough=0.3, metallic=1.0)
mahog = mat("StmMahogany", (0.32, 0.15, 0.08, 1), rough=0.55)
enamel = mat("StmEnamel", (0.10, 0.28, 0.20, 1), rough=0.3)
cream = mat("StmCream", (0.90, 0.87, 0.78, 1), rough=0.5)
gray = paperlib.get_material("RefGray", paperlib.REF_GRAY, roughness=0.9)

clear_objects(prefixes=("STM_",), names=("SMG_Steampunk",))
hide_default_cube()

root = bpy.data.objects.new("SMG_Steampunk", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("STM_RefBlock", gray, root)

# yaw gear: iron discs, polished brass tooth ring
card_panel("STM_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [iron, iron], parent=root)
card_panel("STM_GearTeeth", gear_profile(10, 0.26, GEAR_R_OUT), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [brass, brass], parent=root)
card_panel("STM_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [iron, iron], parent=root)

strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"STM_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.10,
               [iron, iron], parent=root)
    card_panel(f"STM_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.10,
               [iron, iron], parent=root)
card_panel("STM_GussetSpacer", [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                                (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
           0.178, 'X', 0.0, [iron, iron], parent=root)
brad("STM_BradR", 0.111, 0.132, 0.05, PIVOT_Z, brass, parent=root)
brad("STM_BradL", -0.111, -0.132, 0.05, PIVOT_Z, brass, parent=root)

yoke = bpy.data.objects.new("STM_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

body = bpy.data.objects.new("STM_Body", None)
body.empty_display_size = 0.06
bpy.context.scene.collection.objects.link(body)
body.parent = yoke
body.rotation_euler = (radians(RAKE_DEG), 0, 0)

# loaf: iron boiler core, enamel-green side plates, brass straps
core_loaf = [(0.22, -0.13), (0.22, 0.13), (0.14, 0.19), (-0.14, 0.19),
             (-0.24, 0.11), (-0.34, 0.05), (-0.34, -0.07), (-0.26, -0.13)]
card_panel("STM_ReceiverCore", core_loaf, 0.13, 'X', 0.0,
           [iron, iron], parent=body)
plate_loaf = [(0.245, -0.11), (0.245, 0.11), (0.155, 0.165), (-0.125, 0.165),
              (-0.215, 0.09), (-0.30, 0.03), (-0.30, -0.06), (-0.235, -0.11)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"STM_SidePlate{side}", plate_loaf, 0.02, 'X', sign * 0.075,
               [enamel, brass], parent=body)

# rivet rows along the side-plate edges — the boilerplate tell
for sign in (1, -1):
    for i, (ry, rz) in enumerate(((0.20, 0.05), (0.10, 0.13), (-0.02, 0.14),
                                  (-0.15, 0.115), (-0.25, 0.055),
                                  (0.20, -0.08), (0.0, -0.10), (-0.18, -0.075))):
        brad(f"STM_Rivet{'R' if sign > 0 else 'L'}{i}", sign * 0.085,
             sign * 0.097, 0.011, rz, brass, parent=body, y=ry)

# brass straps + mahogany rear cap + brass fastener
for i, y in enumerate((-0.14,)):
    card_panel(f"STM_Strap{i}", [(-0.095, -0.14), (0.095, -0.14),
                                 (0.095, 0.20), (-0.095, 0.20)],
               0.03, 'Y', y, [brass, brass], parent=body)
card_panel("STM_RearCap", [(-0.075, -0.115), (0.075, -0.115), (0.075, 0.115),
                           (-0.075, 0.115)],
           0.035, 'Y', 0.2375, [mahog, mahog], parent=body)
hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.255, z) for x, z in hex_pts],
               [(x, 0.29, z) for x, z in hex_pts]])
make_object("STM_RearBrad", pv, pf, [brass], parent=body)

# big brass gauge with a cream face
card_panel("STM_GaugeRing", ngon_pts(10, 0.08, cy=0.02), 0.055, 'Z', 0.2175,
           [brass, brass], parent=body)
card_panel("STM_GaugeFace", ngon_pts(10, 0.068, cy=0.02), 0.014, 'Z', 0.252,
           [cream, brass], parent=body)

# smokestack on the rear deck: copper, flared lip
card_panel("STM_Stack", ngon_pts(10, 0.030, cy=0.15), 0.13, 'Z', 0.245,
           [copper, copper], parent=body)
card_panel("STM_StackLip", ngon_pts(10, 0.044, cy=0.15), 0.028, 'Z', 0.322,
           [copper, copper], parent=body)

# copper pipe run: belly of the loaf down and forward into the collar
card_panel("STM_PipeDrop", ngon_pts(8, 0.024, cx=0.0, cy=-0.20), 0.10, 'Z',
           -0.21, [copper, copper], parent=body)
disc_ball("STM_PipeElbow", 0.032, (0.0, -0.20, -0.265), [copper, copper],
          parent=body)
card_panel("STM_PipeRun", ngon_pts(8, 0.024, cy=-0.265), 0.22, 'Y', -0.31,
           [copper, copper], parent=body)

# valve handwheel on the right flank, above the chute
wheel = card_panel("STM_Valve", gear_profile(5, 0.030, 0.046), 0.014, 'X',
                   0.104, [brass, brass], parent=body)
wheel.location = (0.0, 0.06, 0.0)
brad("STM_ValveHub", 0.104, 0.122, 0.012, 0.0, copper, parent=body, y=0.06)

# reel: polished brass drum, copper stripe, iron hub
card_panel("STM_ReelNeck", ngon_pts(8, 0.06, cx=-0.16), 0.05, 'X', -0.10,
           [iron, iron], parent=body)
card_panel("STM_Reel", [(y - 0.16, z) for y, z in ngon_pts(12, 0.15)],
           0.13, 'X', -0.18, [brass, brass], parent=body)
card_panel("STM_ReelStripe", [(y - 0.16, z) for y, z in ngon_pts(12, 0.158)],
           0.035, 'X', -0.18, [copper, copper], parent=body)
brad("STM_ReelHub", -0.245, -0.27, 0.05, 0.0, iron, parent=body, y=-0.16)

# casing chute: iron with a brass lip
card_panel("STM_Chute", [(0.04, -0.08), (-0.08, -0.12), (-0.08, -0.19),
                         (0.04, -0.15)],
           0.035, 'X', 0.10, [iron, iron, brass], cap_slots=(0, 2),
           parent=body)

# collar + barrel: iron shells, brass muzzle ring, copper band
card_panel("STM_Collar0", ngon_pts(12, 0.115, cy=-0.01, phase=tau / 24),
           0.05, 'Y', -0.37, [iron, brass], parent=yoke)
card_panel("STM_Collar1", ngon_pts(12, 0.088, cy=-0.01, phase=tau / 24),
           0.045, 'Y', -0.43, [copper, copper], parent=yoke)
for top, name in ((True, "STM_BarrelTop"), (False, "STM_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [iron, brass, copper], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)
card_panel("STM_MuzzleRing", ngon_pts(12, 0.10, cy=-0.01, phase=tau / 24),
           0.06, 'Y', -0.70, [brass, brass], parent=yoke)
card_panel("STM_SightBead", ngon_pts(6, 0.024, cy=-0.70), 0.05, 'Z', 0.115,
           [brass, brass], parent=yoke)

scale_tree(root, SCALE)
print("SMG_Steampunk built.")
