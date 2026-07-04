# artgen/cannon_paperpunk.py — paper-punk cannon turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig, applied at export:
#   root "Cannon_Paper" (yaws) -> "Turret" pitch yoke -> "ShootPoint".
# In-scene names: CAN_Yoke / CAN_Muzzle.
#
# Concept: the heavy of the family. Telescoping rolled-card barrel (nested
# poster tubes with wrap-band joints), chunky laminated receiver with a
# rolled breech cylinder, recuperator tube offset above-right (asymmetry),
# and one big red-nosed shell cradled on the left flank.

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
                      tube, gear_profile, brad, ref_block, export_tree)

DO_EXPORT = True
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\Cannon_Paper.fbx"

LOCATION = (-1.8, 0.0, 0.0)  # scene slot left of the SMG; zeroed at export
PIVOT_Z = 0.55
BASE_PLATE = 0.72
GEAR_Z0 = 0.05
GEAR_TOP = 0.18              # 4 laminate layers — heavier bearing

clear_objects(prefixes=("CAN_",), names=("Cannon_Paper",))
hide_default_cube()
m = materials()
white, kraft, brass, channel = m["white"], m["kraft"], m["brass"], m["channel"]

root = bpy.data.objects.new("Cannon_Paper", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("CAN_RefBlock", m["gray"], root)

# base sheet + 4-layer yaw gear
h = BASE_PLATE / 2
card_panel("CAN_BasePlate", [(-h, -h), (h, -h), (h, h), (-h, h)],
           0.05, 'Z', 0.025, [white, kraft], parent=root)
card_panel("CAN_GearBottom", ngon_pts(12, 0.30), 0.03, 'Z', GEAR_Z0 + 0.015,
           [white, kraft], parent=root)
card_panel("CAN_GearTeeth", gear_profile(10, 0.27, 0.33), 0.04, 'Z',
           GEAR_Z0 + 0.05, [white, kraft], parent=root)
card_panel("CAN_GearMid", ngon_pts(12, 0.26), 0.03, 'Z', GEAR_Z0 + 0.085,
           [kraft, kraft], parent=root)
card_panel("CAN_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.115,
           [white, kraft], parent=root)

# heavy A-frame: thicker card, taller reach
strut_front = [(-0.30, GEAR_TOP), (-0.18, GEAR_TOP),
               (-0.02, PIVOT_Z + 0.05), (-0.10, PIVOT_Z + 0.05)]
strut_rear = [(0.18, GEAR_TOP), (0.30, GEAR_TOP),
              (0.10, PIVOT_Z + 0.05), (0.02, PIVOT_Z + 0.05)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"CAN_StrutFront{side}", strut_front, 0.035, 'X', sign * 0.13,
               [white, kraft], parent=root)
    card_panel(f"CAN_StrutRear{side}", strut_rear, 0.035, 'X', sign * 0.13,
               [white, kraft], parent=root)
card_panel("CAN_GussetSpacer", [(0.18, GEAR_TOP), (0.28, GEAR_TOP),
                                (0.28, GEAR_TOP + 0.10), (0.18, GEAR_TOP + 0.10)],
           0.225, 'X', 0.0, [white, kraft], parent=root)
brad("CAN_BradR", 0.148, 0.175, 0.065, PIVOT_Z, brass, parent=root)
brad("CAN_BradL", -0.148, -0.175, 0.065, PIVOT_Z, brass, parent=root)

# ---------------------------------------------------------------- turret --
yoke = bpy.data.objects.new("CAN_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

# receiver: chunky laminated box with a stepped top deck
core_profile = [(0.28, -0.14), (0.28, 0.14), (0.20, 0.20), (-0.04, 0.20),
                (-0.10, 0.15), (-0.30, 0.15), (-0.38, 0.06), (-0.38, -0.06),
                (-0.24, -0.14)]
card_panel("CAN_ReceiverCore", core_profile, 0.20, 'X', 0.0,
           [white, kraft], parent=yoke)
plate_r = [(0.31, -0.12), (0.31, 0.12), (0.23, 0.175), (-0.02, 0.175),
           (-0.08, 0.125), (-0.33, 0.125), (-0.40, 0.03), (-0.40, -0.08),
           (-0.20, -0.12)]
card_panel("CAN_SidePlateR", plate_r, 0.025, 'X', 0.115,
           [white, kraft], parent=yoke)
plate_l = [(0.31, -0.12), (0.31, 0.12), (0.23, 0.175), (-0.02, 0.175),
           (-0.08, 0.125), (-0.26, 0.125), (-0.32, 0.04), (-0.32, -0.07),
           (-0.14, -0.12)]
card_panel("CAN_SidePlateL", plate_l, 0.025, 'X', -0.115,
           [white, kraft], parent=yoke)

# breech: rolled kraft cylinder out the rear + brass fastener center
tube("CAN_Breech", 0.10, 0.018, 0.40, 0.27, 10, [kraft, kraft, kraft],
     parent=yoke)
card_panel("CAN_BreechCap", ngon_pts(10, 0.095, phase=tau / 20), 0.03, 'Y',
           0.405, [kraft, kraft], parent=yoke)
hex_pts = [(0.045 * cos(i * tau / 6), 0.045 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.42, z) for x, z in hex_pts],
               [(x, 0.45, z) for x, z in hex_pts]])
make_object("CAN_BreechBrad", pv, pf, [brass], parent=yoke)

# breech handle: kraft L-tab on the right of the breech
card_panel("CAN_BreechHandle", [(0.30, 0.02), (0.36, 0.02), (0.36, -0.10),
                                (0.33, -0.10), (0.33, -0.01), (0.30, -0.01)],
           0.02, 'X', 0.10, [kraft, kraft], parent=yoke)

# barrel: telescoping rolled tubes, wrap-band joints, open red bore
tube("CAN_BarrelA", 0.075, 0.016, -0.35, -0.72, 12,
     [white, kraft, channel], parent=yoke)
card_panel("CAN_BandA", ngon_pts(12, 0.088, phase=tau / 24), 0.05, 'Y',
           -0.70, [white, kraft], parent=yoke)
tube("CAN_BarrelB", 0.062, 0.014, -0.70, -1.05, 12,
     [white, kraft, channel], parent=yoke)
card_panel("CAN_BandB", ngon_pts(12, 0.074, phase=tau / 24), 0.045, 'Y',
           -1.03, [white, kraft], parent=yoke)
tube("CAN_BarrelC", 0.050, 0.012, -1.03, -1.35, 12,
     [white, kraft, channel], parent=yoke)
card_panel("CAN_BandRoot", ngon_pts(12, 0.09, phase=tau / 24), 0.05, 'Y',
           -0.37, [white, kraft], parent=yoke)

# recuperator: slim rolled tube riding above-right of the barrel root —
# the asymmetric mechanism read
tube("CAN_Recuperator", 0.032, 0.010, -0.28, -0.78, 8,
     [white, kraft, kraft], center=(0.058, 0.108), parent=yoke)
card_panel("CAN_RecupCap", ngon_pts(8, 0.036, cx=0.058, cy=0.108), 0.025,
           'Y', -0.785, [kraft, kraft], parent=yoke)

# spare shell cradled on the LEFT flank: card tray + one big red-nosed round
card_panel("CAN_CradlePlate", [(0.06, -0.16), (-0.44, -0.16), (-0.44, 0.10),
                               (0.06, 0.10)],
           0.016, 'X', -0.145, [white, kraft], parent=yoke)
tube("CAN_Shell", 0.055, 0.014, -0.02, -0.36, 10,
     [white, kraft, kraft], center=(-0.21, -0.03), parent=yoke)
nose0 = [(-0.21 + 0.055 * cos(a), -0.36, -0.03 + 0.055 * sin(a))
         for a in [k * tau / 10 for k in range(10)]]
nose1 = [(-0.21 + 0.007 * cos(a), -0.47, -0.03 + 0.007 * sin(a))
         for a in [k * tau / 10 for k in range(10)]]
pv, pf = loft([nose0, nose1])
make_object("CAN_ShellNose", pv, pf, [channel], parent=yoke)

# sight: card tab offset RIGHT on the receiver deck
card_panel("CAN_Sight", [(-0.16, 0.20), (-0.23, 0.20), (-0.23, 0.28),
                         (-0.18, 0.28)],
           0.016, 'X', 0.04, [white, kraft], parent=yoke)

# muzzle marker, just inside the bore
muzzle = bpy.data.objects.new("CAN_Muzzle", None)
muzzle.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = yoke
muzzle.location = (0, -1.31, 0)

if DO_EXPORT:
    export_tree(root, EXPORT_PATH, yoke=yoke, muzzle=muzzle)

print("Cannon_Paper built.")
