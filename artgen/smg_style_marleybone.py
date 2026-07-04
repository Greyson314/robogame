# artgen/smg_style_marleybone.py — SMG style study 3: Marleybone.
# Blender-only exploration (no FBX export) — the paper-punk SMG stays the
# in-game version. Same cutesy bones as smg_paperpunk.py; the language is
# steampunk + nighttime + British: midnight-navy enamel with cream coach-
# lining, gold fittings, a clock face for a gauge, a checkered muzzle
# band, and a gaslamp-amber glow in the projectile groove with a little
# lantern on top.

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

LOCATION = (7.2, 0.0, 0.0)
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


def mat(name, color, rough=0.6, metallic=0.0, emission=None, strength=0.0):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = color
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metallic
        if emission is not None:
            b.inputs["Emission Color"].default_value = emission
            b.inputs["Emission Strength"].default_value = strength
    return m


navy = mat("MbnNavy", (0.06, 0.08, 0.19, 1), rough=0.3)
cream = mat("MbnCream", (0.90, 0.86, 0.75, 1), rough=0.5)
gold = mat("MbnGold", (0.85, 0.66, 0.26, 1), rough=0.22, metallic=1.0)
iron = mat("MbnIron", (0.10, 0.10, 0.12, 1), rough=0.5, metallic=0.6)
glow = mat("MbnGaslight", (0.35, 0.22, 0.08, 1), rough=0.5,
           emission=(1.0, 0.62, 0.22, 1), strength=4.0)
gray = paperlib.get_material("RefGray", paperlib.REF_GRAY, roughness=0.9)

clear_objects(prefixes=("MBN_",), names=("SMG_Marleybone",))
hide_default_cube()

root = bpy.data.objects.new("SMG_Marleybone", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("MBN_RefBlock", gray, root)

# yaw gear: navy discs, gold clockwork tooth ring
card_panel("MBN_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [navy, cream], parent=root)
card_panel("MBN_GearTeeth", gear_profile(10, 0.26, GEAR_R_OUT), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [gold, gold], parent=root)
card_panel("MBN_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [navy, cream], parent=root)

strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"MBN_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.10,
               [iron, gold], parent=root)
    card_panel(f"MBN_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.10,
               [iron, gold], parent=root)
card_panel("MBN_GussetSpacer", [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                                (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
           0.178, 'X', 0.0, [iron, iron], parent=root)
brad("MBN_BradR", 0.111, 0.132, 0.05, PIVOT_Z, gold, parent=root)
brad("MBN_BradL", -0.111, -0.132, 0.05, PIVOT_Z, gold, parent=root)

yoke = bpy.data.objects.new("MBN_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

body = bpy.data.objects.new("MBN_Body", None)
body.empty_display_size = 0.06
bpy.context.scene.collection.objects.link(body)
body.parent = yoke
body.rotation_euler = (radians(RAKE_DEG), 0, 0)

# loaf: midnight-navy enamel with cream cut edges (coach-lined carriage)
core_loaf = [(0.22, -0.13), (0.22, 0.13), (0.14, 0.19), (-0.14, 0.19),
             (-0.24, 0.11), (-0.34, 0.05), (-0.34, -0.07), (-0.26, -0.13)]
card_panel("MBN_ReceiverCore", core_loaf, 0.13, 'X', 0.0,
           [navy, navy], parent=body)
plate_loaf = [(0.245, -0.11), (0.245, 0.11), (0.155, 0.165), (-0.125, 0.165),
              (-0.215, 0.09), (-0.30, 0.03), (-0.30, -0.06), (-0.235, -0.11)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"MBN_SidePlate{side}", plate_loaf, 0.02, 'X', sign * 0.075,
               [navy, cream], parent=body)

# coach-lining: slim cream pinstripe panels tracing the deck line
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"MBN_Pinstripe{side}", [(0.19, 0.145), (0.13, 0.175),
                                        (-0.11, 0.175), (-0.19, 0.11),
                                        (-0.11, 0.155), (0.13, 0.155)],
               0.006, 'X', sign * 0.088, [cream, cream], parent=body)

# rear cap: navy with a gold fastener
card_panel("MBN_RearCap", [(-0.06, -0.10), (0.06, -0.10), (0.06, 0.10),
                           (-0.06, 0.10)],
           0.025, 'Y', 0.2325, [navy, cream], parent=body)
hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.245, z) for x, z in hex_pts],
               [(x, 0.28, z) for x, z in hex_pts]])
make_object("MBN_RearBrad", pv, pf, [gold], parent=body)

# the clock: gold bezel, cream face, iron hands at ten-past-ten
card_panel("MBN_ClockRing", ngon_pts(12, 0.08, cy=0.02), 0.05, 'Z', 0.215,
           [gold, gold], parent=body)
card_panel("MBN_ClockFace", ngon_pts(12, 0.068, cy=0.02), 0.014, 'Z', 0.247,
           [cream, gold], parent=body)
for nm, length, angle in (("MBN_HandHour", 0.034, 300.0),
                          ("MBN_HandMinute", 0.052, 60.0)):
    h = card_panel(nm, [(-0.005, 0.0), (0.005, 0.0), (0.003, length),
                        (-0.003, length)],
                   0.008, 'Z', 0.258, [iron, iron], parent=body)
    h.location = (0.0, 0.02, 0.0)
    h.rotation_euler = (0.0, 0.0, radians(angle))
brad_pts = [(0.008 * cos(i * tau / 6), 0.008 * sin(i * tau / 6))
            for i in range(6)]
pv, pf = loft([[(x, 0.02 + y, 0.258) for x, y in brad_pts],
               [(x, 0.02 + y, 0.268) for x, y in brad_pts]])
make_object("MBN_ClockStud", pv, pf, [gold], parent=body)

# reel: navy clock-drum, gold rim stripe and hub
card_panel("MBN_ReelNeck", ngon_pts(8, 0.06, cx=-0.16), 0.05, 'X', -0.10,
           [iron, iron], parent=body)
card_panel("MBN_Reel", [(y - 0.16, z) for y, z in ngon_pts(12, 0.15)],
           0.13, 'X', -0.18, [navy, cream], parent=body)
card_panel("MBN_ReelStripe", [(y - 0.16, z) for y, z in ngon_pts(12, 0.158)],
           0.035, 'X', -0.18, [gold, gold], parent=body)
brad("MBN_ReelHub", -0.245, -0.27, 0.05, 0.0, gold, parent=body, y=-0.16)

# casing chute: iron with a gaslight slot
card_panel("MBN_Chute", [(0.04, -0.08), (-0.08, -0.12), (-0.08, -0.19),
                         (0.04, -0.15)],
           0.035, 'X', 0.10, [iron, iron, glow], cap_slots=(0, 2),
           parent=body)

# collar + barrel: navy shells, gaslamp-amber groove
card_panel("MBN_Collar0", ngon_pts(12, 0.115, cy=-0.01, phase=tau / 24),
           0.05, 'Y', -0.37, [navy, gold], parent=yoke)
card_panel("MBN_Collar1", ngon_pts(12, 0.088, cy=-0.01, phase=tau / 24),
           0.045, 'Y', -0.43, [iron, iron], parent=yoke)
for top, name in ((True, "MBN_BarrelTop"), (False, "MBN_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [navy, cream, glow], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)

# checkered muzzle band — alternating navy/cream rim segments
ring = card_panel("MBN_MuzzleRing", ngon_pts(12, 0.10, cy=-0.01, phase=tau / 24),
                  0.06, 'Y', -0.70, [cream, navy], parent=yoke)
for j in range(12):
    ring.data.polygons[j].material_index = j % 2

# gaslamp where the bead was: gold cage ring + glowing mantle
card_panel("MBN_LampRing", ngon_pts(6, 0.028, cy=-0.70), 0.02, 'Z', 0.105,
           [gold, gold], parent=yoke)
disc_ball("MBN_LampMantle", 0.024, (0.0, -0.70, 0.135), [glow, glow],
          parent=yoke)

scale_tree(root, SCALE)
print("SMG_Marleybone built.")
