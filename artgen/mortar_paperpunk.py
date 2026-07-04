# artgen/mortar_paperpunk.py — paper-punk mortar turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig, applied at export:
#   root "Mortar_Paper" (yaws) -> "Turret" pitch yoke -> "ShootPoint".
# In-scene names: MOR_Yoke / MOR_Muzzle. MortarBlock drives the yoke to a
# launch elevation at runtime, so the tube is authored LEVEL (forward -Y)
# like every other weapon — the game supplies the lob angle.
#
# Concept: fireworks mortar. Real firework mortars ARE rolled cardboard
# tubes, so the paper-punk translation is nearly literal: a fat kraft-
# seamed tube with laminated wrap bands, a laminated breech, and a card
# rack of red-nosed paper shells on the left flank (asymmetry + "this
# throws objects").

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
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\Mortar_Paper.fbx"

LOCATION = (1.8, 0.0, 0.0)   # scene slot next to the SMG; zeroed at export
PIVOT_Z = 0.46
BASE_PLATE = 0.72
GEAR_Z0 = 0.05
GEAR_TOP = 0.145
TUBE_R = 0.14
TUBE_WALL = 0.02
TUBE_REAR_Y = 0.18
TUBE_TIP_Y = -0.75

clear_objects(prefixes=("MOR_",), names=("Mortar_Paper",))
hide_default_cube()
m = materials()
white, kraft, brass, channel = m["white"], m["kraft"], m["brass"], m["channel"]

root = bpy.data.objects.new("Mortar_Paper", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("MOR_RefBlock", m["gray"], root)

# base sheet + yaw gear (family-standard rotary base)
h = BASE_PLATE / 2
card_panel("MOR_BasePlate", [(-h, -h), (h, -h), (h, h), (-h, h)],
           0.05, 'Z', 0.025, [white, kraft], parent=root)
card_panel("MOR_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [white, kraft], parent=root)
card_panel("MOR_GearTeeth", gear_profile(10, 0.26, 0.32), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [white, kraft], parent=root)
card_panel("MOR_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [white, kraft], parent=root)

# squat wide A-frame — the fat tube needs shoulder room
strut_front = [(-0.28, GEAR_TOP), (-0.16, GEAR_TOP),
               (-0.02, PIVOT_Z + 0.04), (-0.10, PIVOT_Z + 0.04)]
strut_rear = [(0.16, GEAR_TOP), (0.28, GEAR_TOP),
              (0.10, PIVOT_Z + 0.04), (0.02, PIVOT_Z + 0.04)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"MOR_StrutFront{side}", strut_front, 0.025, 'X', sign * 0.165,
               [white, kraft], parent=root)
    card_panel(f"MOR_StrutRear{side}", strut_rear, 0.025, 'X', sign * 0.165,
               [white, kraft], parent=root)
card_panel("MOR_GussetSpacer", [(0.16, GEAR_TOP), (0.26, GEAR_TOP),
                                (0.26, GEAR_TOP + 0.09), (0.16, GEAR_TOP + 0.09)],
           0.305, 'X', 0.0, [white, kraft], parent=root)
brad("MOR_BradR", 0.178, 0.202, 0.06, PIVOT_Z, brass, parent=root)
brad("MOR_BradL", -0.178, -0.202, 0.06, PIVOT_Z, brass, parent=root)

# ---------------------------------------------------------------- turret --
yoke = bpy.data.objects.new("MOR_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

# the tube: fat rolled card, seam under, open red bore at the muzzle
tube("MOR_Tube", TUBE_R, TUBE_WALL, TUBE_REAR_Y, TUBE_TIP_Y, 12,
     [white, kraft, channel], parent=yoke)

# laminated wrap bands (solid card discs around the tube)
for i, y in enumerate((-0.02, -0.58)):
    card_panel(f"MOR_Band{i}", ngon_pts(12, TUBE_R + 0.015, phase=tau / 24),
               0.06, 'Y', y, [white, kraft], parent=yoke)

# breech: laminated card discs closing the rear + a brass fastener center
card_panel("MOR_Breech0", ngon_pts(12, TUBE_R + 0.01, phase=tau / 24),
           0.05, 'Y', TUBE_REAR_Y + 0.01, [kraft, kraft], parent=yoke)
card_panel("MOR_Breech1", ngon_pts(12, TUBE_R - 0.03, phase=tau / 24),
           0.04, 'Y', TUBE_REAR_Y + 0.05, [white, kraft], parent=yoke)
hex_pts = [(0.045 * cos(i * tau / 6), 0.045 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, TUBE_REAR_Y + 0.07, z) for x, z in hex_pts],
               [(x, TUBE_REAR_Y + 0.10, z) for x, z in hex_pts]])
make_object("MOR_BreechBrad", pv, pf, [brass], parent=yoke)

# shell rack on the LEFT flank: card tray + three red-nosed paper shells.
# Asymmetric, and it says "projectiles" louder than any muzzle ever could.
card_panel("MOR_RackPlate", [(-0.02, -0.17), (-0.46, -0.17), (-0.46, 0.17),
                             (-0.02, 0.17)],
           0.016, 'X', -0.165, [white, kraft], parent=yoke)
for i, z in enumerate((-0.11, 0.0, 0.11)):
    sy = -0.06 - 0.02 * i           # slight stagger, hand-racked not machined
    tube(f"MOR_Shell{i}", 0.042, 0.012, sy, sy - 0.26, 8,
         [white, kraft, kraft], center=(-0.215, z), parent=yoke)
    # red nose cone
    nose0 = [(-0.215 + 0.042 * cos(a), sy - 0.26, z + 0.042 * sin(a))
             for a in [k * tau / 8 for k in range(8)]]
    nose1 = [(-0.215 + 0.006 * cos(a), sy - 0.34, z + 0.006 * sin(a))
             for a in [k * tau / 8 for k in range(8)]]
    pv, pf = loft([nose0, nose1])
    make_object(f"MOR_ShellNose{i}", pv, pf, [channel], parent=yoke)

# sight: kraft tab on the RIGHT of the muzzle rim (opposite the rack)
card_panel("MOR_Sight", [(-0.60, 0.12), (-0.68, 0.12), (-0.68, 0.20),
                         (-0.63, 0.20)],
           0.016, 'X', 0.10, [kraft, kraft], parent=yoke)

# muzzle marker, just inside the bore
muzzle = bpy.data.objects.new("MOR_Muzzle", None)
muzzle.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = yoke
muzzle.location = (0, TUBE_TIP_Y + 0.05, 0)

if DO_EXPORT:
    export_tree(root, EXPORT_PATH, yoke=yoke, muzzle=muzzle)

print("Mortar_Paper built.")
