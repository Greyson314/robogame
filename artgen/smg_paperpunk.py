# artgen/smg_paperpunk.py — paper-punk SMG turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig (session 120 convention), applied at export:
#   root "SMG_Paper" (yaws) -> "Turret" pitch yoke -> "ShootPoint" muzzle.
# In-scene the empties are SMG_Yoke / SMG_Muzzle (names are global in
# Blender and the weapon family shares the scene).
#
# Paper-punk: the design is a machine, the fabrication material is paper.
# Asymmetry pass (session 131): stepped feed-cover deck, Sten-style
# horizontal left magazine, right ejection port + charging handle, offset
# sights. Blocks are symmetric; the weapons shouldn't be.

import sys

ARTGEN = r"C:\Users\Grey\Desktop\mutedtuple\robogame\artgen"
if ARTGEN not in sys.path:
    sys.path.insert(0, ARTGEN)

import importlib
import bpy
from math import radians, cos, sin, tau
import paperlib
importlib.reload(paperlib)
from paperlib import (materials, clear_objects, hide_default_cube,
                      make_object, loft, card_panel, ngon_pts, arc_shell,
                      gear_profile, brad, ref_block, export_tree)

DO_EXPORT = True
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\SMG_Paper.fbx"

# Forward = -Y, up = +Z. Units: meters. Block = 1 m cube. Root at origin.
LOCATION = (0.0, 0.0, 0.0)
PIVOT_Z = 0.52
BASE_PLATE = 0.72
GEAR_Z0 = 0.05
GEAR_TOP = 0.145
RAIL_ROOT_Y = -0.77
RAIL_TIP_Y = -1.22
BARREL_R = 0.055
SHELL_T = 0.016
GAP_DEG = 16.0
ARC_SEGS = 10

clear_objects(prefixes=("SMG_",), names=("SMG_Paper",))
hide_default_cube()
m = materials()
white, kraft, brass, channel = m["white"], m["kraft"], m["brass"], m["channel"]

root = bpy.data.objects.new("SMG_Paper", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("SMG_RefBlock", m["gray"], root)

# base sheet
h = BASE_PLATE / 2
card_panel("SMG_BasePlate", [(-h, -h), (h, -h), (h, h), (-h, h)],
           0.05, 'Z', 0.025, [white, kraft], parent=root)

# yaw gear: 3 laminated card layers, middle one cut as a gear
card_panel("SMG_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [white, kraft], parent=root)
card_panel("SMG_GearTeeth", gear_profile(10, 0.26, 0.32), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [white, kraft], parent=root)
card_panel("SMG_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [white, kraft], parent=root)

# A-frame yoke: open-middle card struts per side + brass pitch brads
strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"SMG_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.10,
               [white, kraft], parent=root)
    card_panel(f"SMG_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.10,
               [white, kraft], parent=root)
card_panel("SMG_GussetSpacer", [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                                (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
           0.178, 'X', 0.0, [white, kraft], parent=root)
brad("SMG_BradR", 0.111, 0.132, 0.05, PIVOT_Z, brass, parent=root)
brad("SMG_BradL", -0.111, -0.132, 0.05, PIVOT_Z, brass, parent=root)

# ---------------------------------------------------------------- turret --
yoke = bpy.data.objects.new("SMG_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

# receiver core: stepped top deck — raised feed-cover hump over the pivot,
# lower deck running out to the nose (vertical variation)
core_profile = [(0.30, -0.11), (0.30, 0.11), (0.24, 0.17), (-0.02, 0.17),
                (-0.08, 0.115), (-0.40, 0.115), (-0.55, 0.045),
                (-0.55, -0.03), (-0.30, -0.11)]
card_panel("SMG_ReceiverCore", core_profile, 0.13, 'X', 0.0,
           [white, kraft], parent=yoke)

# side plates: deliberately different silhouettes per side. Right plate
# runs long (carries the ejection port); left plate stops short where the
# magazine enters.
plate_r = [(0.33, -0.09), (0.33, 0.09), (0.27, 0.15), (0.0, 0.15),
           (-0.06, 0.095), (-0.44, 0.095), (-0.52, 0.02), (-0.52, -0.055),
           (-0.28, -0.09)]
card_panel("SMG_SidePlateR", plate_r, 0.02, 'X', 0.075,
           [white, kraft], parent=yoke)
plate_l = [(0.33, -0.09), (0.33, 0.09), (0.27, 0.15), (0.0, 0.15),
           (-0.06, 0.095), (-0.38, 0.095), (-0.44, 0.03), (-0.44, -0.05),
           (-0.10, -0.09)]
card_panel("SMG_SidePlateL", plate_l, 0.02, 'X', -0.075,
           [white, kraft], parent=yoke)

# ejection port: kraft box proud of the right plate, red slot outboard
card_panel("SMG_EjectPort", [(-0.04, 0.0), (-0.20, 0.0), (-0.20, 0.075),
                             (-0.04, 0.075)],
           0.025, 'X', 0.0975, [kraft, kraft, channel], cap_slots=(0, 2),
           parent=yoke)

# charging handle: kraft L-tab on the right rear
card_panel("SMG_ChargeHandle", [(0.16, 0.06), (0.10, 0.06), (0.10, 0.13),
                                (0.13, 0.13), (0.13, 0.09), (0.16, 0.09)],
           0.018, 'X', 0.105, [kraft, kraft], parent=yoke)

# magazine: Sten-style horizontal box out the LEFT flank — the loudest
# asymmetry read, and it frees the underside for pitch clearance. Red rims
# ("printed" ammo) with kraft card ends.
card_panel("SMG_Magazine", [(-0.13, -0.035), (-0.27, -0.035),
                            (-0.27, 0.045), (-0.13, 0.045)],
           0.235, 'X', -0.2025, [kraft, channel], parent=yoke)

# collar: laminated card discs bridging receiver nose -> barrel
disc_y = -0.57
for i in range(6):
    r = 0.075 if i % 2 == 0 else 0.055
    mm = [white, kraft] if i % 2 == 0 else [kraft, kraft]
    card_panel(f"SMG_Collar{i}", ngon_pts(12, r, cy=-0.01, phase=tau / 24),
               0.036, 'Y', disc_y - i * 0.044, mm, parent=yoke)

# barrel: rolled-card tube split horizontally into two half-shells — open
# groove down the middle, red channel interior, kraft cut edges
for top, name in ((True, "SMG_BarrelTop"), (False, "SMG_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [white, kraft, channel], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)

# sights: offset LEFT of the bore line — a thin aiming fin pair
card_panel("SMG_SightFront", [(-0.34, 0.115), (-0.41, 0.115), (-0.41, 0.19),
                              (-0.36, 0.19)],
           0.016, 'X', -0.03, [white, kraft], parent=yoke)
card_panel("SMG_SightRear", [(0.28, 0.17), (0.23, 0.17), (0.23, 0.235),
                             (0.28, 0.235)],
           0.016, 'X', -0.03, [white, kraft], parent=yoke)

# muzzle marker, inside the open groove
muzzle = bpy.data.objects.new("SMG_Muzzle", None)
muzzle.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = yoke
muzzle.location = (0, RAIL_TIP_Y + 0.04, -0.01)

if DO_EXPORT:
    export_tree(root, EXPORT_PATH, yoke=yoke, muzzle=muzzle)

print("SMG_Paper built.")
