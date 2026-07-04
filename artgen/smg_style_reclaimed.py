# artgen/smg_style_reclaimed.py — SMG style study: reclaimed metal & plant.
# Blender-only exploration (no FBX export) — the paper-punk SMG stays the
# in-game version. Same cutesy bones as smg_paperpunk.py; the material
# language is metal-first salvage (dark iron core, steel and galvanized
# plates, rust) with VINES wrapping the barrel, body, and a strut leg,
# sprouting leaves as they climb. Second revision: the potted plant is
# gone — the plants live ON the machine, reclaiming it.

import sys

ARTGEN = r"C:\Users\Grey\Desktop\mutedtuple\robogame\artgen"
if ARTGEN not in sys.path:
    sys.path.insert(0, ARTGEN)

import importlib
import bpy
from math import radians, cos, sin, tau, pi
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


iron = mat("RclIron", (0.16, 0.17, 0.19, 1), rough=0.45, metallic=0.85)
steel = mat("RclSteel", (0.45, 0.47, 0.50, 1), rough=0.38, metallic=0.95)
galv = mat("RclGalv", (0.62, 0.64, 0.66, 1), rough=0.28, metallic=1.0)
rust = mat("RclRust", (0.42, 0.20, 0.09, 1), rough=0.9)
verdi = mat("RclVerdigris", (0.33, 0.55, 0.46, 1), rough=0.5, metallic=0.6)
pale = mat("RclGaugeFace", (0.80, 0.78, 0.68, 1), rough=0.6)
vine_m = mat("RclVineStem", (0.19, 0.28, 0.11, 1), rough=0.8)
leaf_g = mat("RclLeaf", (0.27, 0.52, 0.19, 1), rough=0.7)
leaf_d = mat("RclLeafDark", (0.16, 0.34, 0.12, 1), rough=0.75)
gray = paperlib.get_material("RefGray", paperlib.REF_GRAY, roughness=0.9)

clear_objects(prefixes=("RCL_",), names=("SMG_Reclaimed",))
hide_default_cube()

root = bpy.data.objects.new("SMG_Reclaimed", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("RCL_RefBlock", gray, root)

# yaw gear: iron ring, rust laminate in the middle
card_panel("RCL_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [iron, iron], parent=root)
card_panel("RCL_GearTeeth", gear_profile(10, 0.26, GEAR_R_OUT), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [rust, rust], parent=root)
card_panel("RCL_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [steel, steel], parent=root)

strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"RCL_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.10,
               [steel, iron], parent=root)
    card_panel(f"RCL_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.10,
               [galv, iron], parent=root)
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

# loaf: dark iron core, mismatched metal plates (rusted left, galvanized
# right), rivet-studded patch plates
core_loaf = [(0.22, -0.13), (0.22, 0.13), (0.14, 0.19), (-0.14, 0.19),
             (-0.24, 0.11), (-0.34, 0.05), (-0.34, -0.07), (-0.26, -0.13)]
card_panel("RCL_ReceiverCore", core_loaf, 0.13, 'X', 0.0,
           [iron, iron], parent=body)
plate_loaf = [(0.245, -0.11), (0.245, 0.11), (0.155, 0.165), (-0.125, 0.165),
              (-0.215, 0.09), (-0.30, 0.03), (-0.30, -0.06), (-0.235, -0.11)]
card_panel("RCL_SidePlateR", plate_loaf, 0.02, 'X', 0.075,
           [galv, steel], parent=body)
card_panel("RCL_SidePlateL", plate_loaf, 0.02, 'X', -0.075,
           [rust, iron], parent=body)

patches = [
    ("RCL_Patch0", [(0.05, 0.02), (0.19, 0.02), (0.19, 0.11), (0.05, 0.11)],
     0.088, steel),
    ("RCL_Patch1", [(-0.20, -0.09), (-0.06, -0.09), (-0.06, -0.01), (-0.20, -0.01)],
     0.088, rust),
    ("RCL_Patch2", [(-0.02, 0.03), (0.10, 0.03), (0.10, 0.10), (-0.02, 0.10)],
     -0.088, galv),
]
for nm, prof, off, pm in patches:
    card_panel(nm, prof, 0.012, 'X', off, [pm, pm], parent=body)
    cx = (prof[0][0] + prof[2][0]) / 2
    cz = (prof[0][1] + prof[2][1]) / 2
    for i, (dy, dz) in enumerate(((-0.05, -0.03), (0.05, -0.03),
                                  (0.05, 0.03), (-0.05, 0.03))):
        s = 1 if off > 0 else -1
        brad(f"{nm}_Rivet{i}", off, off + s * 0.008, 0.008, cz + dz,
             iron, parent=body, y=cx + dy)

# rear cap: rusted plate + steel bolt
card_panel("RCL_RearCap", [(-0.06, -0.10), (0.06, -0.10), (0.06, 0.10),
                           (-0.06, 0.10)],
           0.025, 'Y', 0.2325, [rust, rust], parent=body)
hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.245, z) for x, z in hex_pts],
               [(x, 0.28, z) for x, z in hex_pts]])
make_object("RCL_RearBolt", pv, pf, [steel], parent=body)

# weathered gauge back on top — rusted bezel, faded face
card_panel("RCL_GaugeRing", ngon_pts(10, 0.075, cy=0.02), 0.05, 'Z', 0.215,
           [rust, rust], parent=body)
card_panel("RCL_GaugeFace", ngon_pts(10, 0.065, cy=0.02), 0.014, 'Z', 0.247,
           [pale, rust], parent=body)

# reel: verdigris drum banded in rust, steel hub
card_panel("RCL_ReelNeck", ngon_pts(8, 0.06, cx=-0.16), 0.05, 'X', -0.10,
           [iron, iron], parent=body)
card_panel("RCL_Reel", [(y - 0.16, z) for y, z in ngon_pts(12, 0.15)],
           0.13, 'X', -0.18, [verdi, verdi], parent=body)
card_panel("RCL_ReelStripe", [(y - 0.16, z) for y, z in ngon_pts(12, 0.158)],
           0.035, 'X', -0.18, [rust, rust], parent=body)
brad("RCL_ReelHub", -0.245, -0.27, 0.05, 0.0, steel, parent=body, y=-0.16)

# casing chute: bare rust duct
card_panel("RCL_Chute", [(0.04, -0.08), (-0.08, -0.12), (-0.08, -0.19),
                         (0.04, -0.15)],
           0.035, 'X', 0.10, [rust, rust], parent=body)

# collar + barrel: steel shells, rusted muzzle ring, galvanized bead
card_panel("RCL_Collar0", ngon_pts(12, 0.115, cy=-0.01, phase=tau / 24),
           0.05, 'Y', -0.37, [steel, rust], parent=yoke)
card_panel("RCL_Collar1", ngon_pts(12, 0.088, cy=-0.01, phase=tau / 24),
           0.045, 'Y', -0.43, [rust, rust], parent=yoke)
for top, name in ((True, "RCL_BarrelTop"), (False, "RCL_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [steel, iron, rust], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)
card_panel("RCL_MuzzleRing", ngon_pts(12, 0.10, cy=-0.01, phase=tau / 24),
           0.06, 'Y', -0.70, [rust, steel], parent=yoke)
card_panel("RCL_SightBead", ngon_pts(6, 0.024, cy=-0.70), 0.05, 'Z', 0.115,
           [galv, galv], parent=yoke)


# ------------------------------------------------------------------ vines --
def vine(name, points, radius, parent):
    """Rounded vine stem following a 3D polyline (smoothed NURBS sweep,
    converted to mesh so scale_tree handles it like everything else)."""
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = radius
    cu.bevel_resolution = 3
    cu.use_fill_caps = True
    sp = cu.splines.new('NURBS')
    sp.points.add(len(points) - 1)
    for p, (x, y, z) in zip(sp.points, points):
        p.co = (x, y, z, 1.0)
    sp.use_endpoint_u = True
    obj = bpy.data.objects.new(name, cu)
    obj.data.materials.append(vine_m)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    with bpy.context.temp_override(active_object=obj, selected_objects=[obj],
                                   selected_editable_objects=[obj]):
        bpy.ops.object.convert(target='MESH')
    return obj


def leaf_pair(prefix, parent, loc, yaw_deg, tilt=50.0, length=0.075, width=0.038):
    for i in range(2):
        prof = [(0.0, 0.0), (width * 0.5, length * 0.35), (0.0, length),
                (-width * 0.5, length * 0.35)]
        o = card_panel(f"{prefix}_{i}", prof, 0.007, 'X', 0.0,
                       [leaf_g, leaf_d], parent=parent)
        o.location = loc
        o.rotation_euler = (radians(tilt), 0.0,
                            radians(yaw_deg + 140.0 * i))


# vine 1: spirals up a front strut leg from the gear to the pivot brad
pts = []
n = 14
for i in range(n):
    t = i / (n - 1)
    cx, cy_, cz = -0.10, -0.20 + t * 0.16, GEAR_TOP + t * (PIVOT_Z - GEAR_TOP)
    a = t * tau * 1.6
    pts.append((cx + 0.035 * cos(a), cy_ + 0.055 * sin(a), cz))
vine("RCL_VineStrut", pts, 0.011, root)
leaf_pair("RCL_LeafStrut0", root, (-0.135, -0.16, 0.22), 210)
leaf_pair("RCL_LeafStrut1", root, (-0.075, -0.09, 0.38), 30, tilt=60)

# vine 2: wraps the loaf diagonally, riding the raked body
pts = []
n = 16
for i in range(n):
    t = i / (n - 1)
    y = 0.16 - t * 0.40
    a = t * tau * 1.25 + 0.6
    pts.append((0.10 * cos(a), y, 0.02 + 0.175 * sin(a)))
vine("RCL_VineBody", pts, 0.012, body)
leaf_pair("RCL_LeafBody0", body, (0.095, 0.05, 0.13), 320, tilt=55)
leaf_pair("RCL_LeafBody1", body, (-0.10, -0.10, -0.10), 140, tilt=115)
leaf_pair("RCL_LeafBody2", body, (0.02, -0.22, 0.185), 70, tilt=25)

# vine 3: coils along the barrel and reaches over the muzzle ring
pts = []
n = 16
for i in range(n):
    t = i / (n - 1)
    y = -0.42 - t * 0.34
    a = t * tau * 1.8 + pi
    r = 0.085 if y > -0.67 else 0.112
    pts.append((r * cos(a), y, -0.01 + r * sin(a)))
vine("RCL_VineBarrel", pts, 0.009, yoke)
leaf_pair("RCL_LeafBarrel0", yoke, (-0.082, -0.50, 0.04), 250, tilt=75)
leaf_pair("RCL_LeafBarrel1", yoke, (0.09, -0.64, -0.04), 60, tilt=105)

# vine 4: creeps over the gear ring onto the base
pts = []
n = 12
for i in range(n):
    t = i / (n - 1)
    a = 2.2 + t * 2.4
    r = 0.34 - t * 0.10
    pts.append((r * cos(a), r * sin(a), 0.02 + t * 0.10))
vine("RCL_VineBase", pts, 0.012, root)
leaf_pair("RCL_LeafBase0", root, (-0.24, 0.16, 0.10), 100, tilt=70)
leaf_pair("RCL_LeafBase1", root, (-0.16, -0.20, 0.13), 200, tilt=65)

# moss where water pools
m0 = disc_ball("RCL_Moss0", 0.05, (-0.08, 0.12, 0.185), [leaf_d, leaf_g],
               parent=body)
m0.scale = (1.0, 1.0, 0.3)
m1 = disc_ball("RCL_Moss1", 0.06, (0.16, -0.24, GEAR_TOP + 0.005),
               [leaf_d, leaf_g], parent=root)
m1.scale = (1.0, 1.0, 0.3)

scale_tree(root, SCALE)
print("SMG_Reclaimed rebuilt with vines.")
