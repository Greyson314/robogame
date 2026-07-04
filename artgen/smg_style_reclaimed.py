# artgen/smg_style_reclaimed.py — SMG style study 1: reclaimed metal & plant.
# Blender-only exploration (no FBX export) — the paper-punk SMG stays the
# in-game version. Same cutesy bones as smg_paperpunk.py; the material
# language swaps to salvage: mismatched metals, rivet-studded patch
# plates, and greenery reclaiming the seams. The gauge becomes a potted
# sprout.

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

LOCATION = (3.6, 0.0, 0.0)
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


steel = mat("RclSteel", (0.42, 0.44, 0.47, 1), rough=0.55, metallic=0.8)
rust = mat("RclRust", (0.44, 0.21, 0.10, 1), rough=0.92)
galv = mat("RclGalv", (0.61, 0.63, 0.64, 1), rough=0.35, metallic=0.9)
verdi = mat("RclVerdigris", (0.34, 0.57, 0.47, 1), rough=0.6, metallic=0.4)
leaf_g = mat("RclLeaf", (0.27, 0.52, 0.19, 1), rough=0.7)
leaf_d = mat("RclLeafDark", (0.16, 0.34, 0.12, 1), rough=0.75)
soil = mat("RclSoil", (0.22, 0.15, 0.10, 1), rough=1.0)
gray = paperlib.get_material("RefGray", paperlib.REF_GRAY, roughness=0.9)

clear_objects(prefixes=("RCL_",), names=("SMG_Reclaimed",))
hide_default_cube()

root = bpy.data.objects.new("SMG_Reclaimed", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("RCL_RefBlock", gray, root)

# yaw gear: salvage iron ring, rust laminate in the middle
card_panel("RCL_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [steel, rust], parent=root)
card_panel("RCL_GearTeeth", gear_profile(10, 0.26, GEAR_R_OUT), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [rust, rust], parent=root)
card_panel("RCL_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [steel, steel], parent=root)

# A-frame: scavenged angle-iron
strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"RCL_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.10,
               [steel, rust], parent=root)
    card_panel(f"RCL_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.10,
               [galv, steel], parent=root)
card_panel("RCL_GussetSpacer", [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                                (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
           0.178, 'X', 0.0, [rust, rust], parent=root)
brad("RCL_BradR", 0.111, 0.132, 0.05, PIVOT_Z, steel, parent=root)
brad("RCL_BradL", -0.111, -0.132, 0.05, PIVOT_Z, steel, parent=root)

yoke = bpy.data.objects.new("RCL_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

body = bpy.data.objects.new("RCL_Body", None)
body.empty_display_size = 0.06
bpy.context.scene.collection.objects.link(body)
body.parent = yoke
body.rotation_euler = (radians(RAKE_DEG), 0, 0)

# loaf: steel core, MISMATCHED side plates (rust left, galvanized right)
core_loaf = [(0.22, -0.13), (0.22, 0.13), (0.14, 0.19), (-0.14, 0.19),
             (-0.24, 0.11), (-0.34, 0.05), (-0.34, -0.07), (-0.26, -0.13)]
card_panel("RCL_ReceiverCore", core_loaf, 0.13, 'X', 0.0,
           [steel, rust], parent=body)
plate_loaf = [(0.245, -0.11), (0.245, 0.11), (0.155, 0.165), (-0.125, 0.165),
              (-0.215, 0.09), (-0.30, 0.03), (-0.30, -0.06), (-0.235, -0.11)]
card_panel("RCL_SidePlateR", plate_loaf, 0.02, 'X', 0.075,
           [galv, steel], parent=body)
card_panel("RCL_SidePlateL", plate_loaf, 0.02, 'X', -0.075,
           [rust, rust], parent=body)

# patch plates: welded-on scraps with rivet studs
patches = [
    ("RCL_Patch0", [(0.05, 0.02), (0.19, 0.02), (0.19, 0.11), (0.05, 0.11)],
     0.088, galv),
    ("RCL_Patch1", [(-0.20, -0.09), (-0.06, -0.09), (-0.06, -0.01), (-0.20, -0.01)],
     0.088, rust),
    ("RCL_Patch2", [(-0.02, 0.03), (0.10, 0.03), (0.10, 0.10), (-0.02, 0.10)],
     -0.088, verdi),
]
for nm, prof, off, pm in patches:
    card_panel(nm, prof, 0.012, 'X', off, [pm, pm], parent=body)
    cx = (prof[0][0] + prof[2][0]) / 2
    cz = (prof[0][1] + prof[2][1]) / 2
    for i, (dy, dz) in enumerate(((-0.05, -0.03), (0.05, -0.03),
                                  (0.05, 0.03), (-0.05, 0.03))):
        s = 1 if off > 0 else -1
        brad(f"{nm}_Rivet{i}", off, off + s * 0.008, 0.008, cz + dz,
             steel, parent=body, y=cx + dy)

# rear cap: rusted plate + steel bolt
card_panel("RCL_RearCap", [(-0.06, -0.10), (0.06, -0.10), (0.06, 0.10),
                           (-0.06, 0.10)],
           0.025, 'Y', 0.2325, [rust, rust], parent=body)
hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.245, z) for x, z in hex_pts],
               [(x, 0.28, z) for x, z in hex_pts]])
make_object("RCL_RearBolt", pv, pf, [steel], parent=body)

# potted sprout where the gauge was — the reclaimed heart of the design
card_panel("RCL_Pot", ngon_pts(10, 0.055, cy=0.02), 0.07, 'Z', 0.225,
           [rust, rust], parent=body)
card_panel("RCL_PotSoil", ngon_pts(10, 0.047, cy=0.02), 0.012, 'Z', 0.262,
           [soil, soil], parent=body)


def leaf_fan(prefix, parent, loc, count, length, width, tilt=40.0, phase=0.0):
    for i in range(count):
        prof = [(0.0, 0.0), (width * 0.5, length * 0.35), (0.0, length),
                (-width * 0.5, length * 0.35)]
        o = card_panel(f"{prefix}{i}", prof, 0.008, 'X', 0.0,
                       [leaf_g, leaf_d], parent=parent)
        o.location = loc
        o.rotation_euler = (radians(tilt), 0.0,
                            radians(phase + i * 360.0 / count))


leaf_fan("RCL_Sprout", body, (0.0, 0.02, 0.265), 5, 0.13, 0.06, tilt=35)
leaf_fan("RCL_SeamSprig", body, (-0.30, -0.02, 0.02), 3, 0.08, 0.04,
         tilt=65, phase=180)
leaf_fan("RCL_BaseSprig", root, (0.20, 0.24, GEAR_TOP), 3, 0.09, 0.045,
         tilt=55, phase=40)

# moss: squashed blobs where water would sit
for i, (loc, r) in enumerate((((-0.10, 0.13, 0.185), 0.05),
                              ((0.14, -0.28, GEAR_TOP + 0.005), 0.06))):
    parent = body if i == 0 else root
    m0 = disc_ball(f"RCL_Moss{i}", r, loc, [leaf_d, leaf_g], parent=parent)
    m0.scale = (1.0, 1.0, 0.3)

# reel: verdigris drum banded in rust, steel hub
card_panel("RCL_ReelNeck", ngon_pts(8, 0.06, cx=-0.16), 0.05, 'X', -0.10,
           [rust, rust], parent=body)
card_panel("RCL_Reel", [(y - 0.16, z) for y, z in ngon_pts(12, 0.15)],
           0.13, 'X', -0.18, [verdi, verdi], parent=body)
card_panel("RCL_ReelStripe", [(y - 0.16, z) for y, z in ngon_pts(12, 0.158)],
           0.035, 'X', -0.18, [rust, rust], parent=body)
brad("RCL_ReelHub", -0.245, -0.27, 0.05, 0.0, steel, parent=body, y=-0.16)

# casing chute: bare rust duct
card_panel("RCL_Chute", [(0.04, -0.08), (-0.08, -0.12), (-0.08, -0.19),
                         (0.04, -0.15)],
           0.035, 'X', 0.10, [rust, rust], parent=body)

# collar + barrel: salvage steel, rusty muzzle ring
card_panel("RCL_Collar0", ngon_pts(12, 0.115, cy=-0.01, phase=tau / 24),
           0.05, 'Y', -0.37, [steel, rust], parent=yoke)
card_panel("RCL_Collar1", ngon_pts(12, 0.088, cy=-0.01, phase=tau / 24),
           0.045, 'Y', -0.43, [rust, rust], parent=yoke)
for top, name in ((True, "RCL_BarrelTop"), (False, "RCL_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [steel, rust, rust], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)
card_panel("RCL_MuzzleRing", ngon_pts(12, 0.10, cy=-0.01, phase=tau / 24),
           0.06, 'Y', -0.70, [rust, steel], parent=yoke)
card_panel("RCL_SightBead", ngon_pts(6, 0.024, cy=-0.70), 0.05, 'Z', 0.115,
           [galv, galv], parent=yoke)

scale_tree(root, SCALE)
print("SMG_Reclaimed built.")
