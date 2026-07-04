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
# Concept (session 131 revision, steering away from WWII shapes): an old
# bombard. Short, fat, hooped rolled-card tube with a flared mouth, a
# laminated cascabel ball at the breech, and a side tray of cartoon paper
# bombs with brass fuse studs. Fireworks-mortar honesty retained: real
# firework mortars are cardboard tubes.

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
                      tube, gear_profile, brad, disc_ball, ref_block,
                      scale_tree, export_tree)

DO_EXPORT = True
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\Mortar_Paper.fbx"

LOCATION = (1.8, 0.0, 0.0)   # scene slot next to the SMG; zeroed at export
PIVOT_Z = 0.39
GEAR_Z0 = 0.0
GEAR_TOP = 0.095
GEAR_R_OUT = 0.32
SCALE = 0.5 / GEAR_R_OUT     # yaw-gear ring -> exactly 1 m diameter
TUBE_R = 0.16                # breech-end radius (tapers slightly forward)
TUBE_WALL = 0.022
TUBE_REAR_Y = 0.14
FLARE_Y = -0.48              # taper ends, mouth flare begins
TUBE_TIP_Y = -0.62

clear_objects(prefixes=("MOR_",), names=("Mortar_Paper",))
hide_default_cube()
m = materials()
white, kraft, brass, channel = m["white"], m["kraft"], m["brass"], m["channel"]

root = bpy.data.objects.new("Mortar_Paper", None)
root.empty_display_size = 0.1
root.location = LOCATION
bpy.context.scene.collection.objects.link(root)
ref_block("MOR_RefBlock", m["gray"], root)

# yaw gear — the bottom of the weapon (family-standard rotary base)
card_panel("MOR_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [white, kraft], parent=root)
card_panel("MOR_GearTeeth", gear_profile(10, 0.26, GEAR_R_OUT), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [white, kraft], parent=root)
card_panel("MOR_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [white, kraft], parent=root)

# squat wide A-frame — the fat tube needs shoulder room
strut_front = [(-0.28, GEAR_TOP), (-0.16, GEAR_TOP),
               (-0.02, PIVOT_Z + 0.04), (-0.10, PIVOT_Z + 0.04)]
strut_rear = [(0.16, GEAR_TOP), (0.28, GEAR_TOP),
              (0.10, PIVOT_Z + 0.04), (0.02, PIVOT_Z + 0.04)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"MOR_StrutFront{side}", strut_front, 0.025, 'X', sign * 0.195,
               [white, kraft], parent=root)
    card_panel(f"MOR_StrutRear{side}", strut_rear, 0.025, 'X', sign * 0.195,
               [white, kraft], parent=root)
card_panel("MOR_GussetSpacer", [(0.16, GEAR_TOP), (0.26, GEAR_TOP),
                                (0.26, GEAR_TOP + 0.09), (0.16, GEAR_TOP + 0.09)],
           0.365, 'X', 0.0, [white, kraft], parent=root)
brad("MOR_BradR", 0.208, 0.232, 0.06, PIVOT_Z, brass, parent=root)
brad("MOR_BradL", -0.208, -0.232, 0.06, PIVOT_Z, brass, parent=root)

# ---------------------------------------------------------------- turret --
yoke = bpy.data.objects.new("MOR_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

# the bombard: tapered rolled tube, hoop rings, flared mouth, red bore
tube("MOR_Tube", TUBE_R, TUBE_WALL, TUBE_REAR_Y, FLARE_Y, 12,
     [white, kraft, kraft], parent=yoke, r_end=0.145)
tube("MOR_Flare", 0.145, 0.02, FLARE_Y, TUBE_TIP_Y, 12,
     [white, kraft, channel], parent=yoke, r_end=0.19)
for i, (y, r) in enumerate(((-0.08, 0.172), (-0.38, 0.162))):
    card_panel(f"MOR_Hoop{i}", ngon_pts(12, r, phase=tau / 24), 0.05, 'Y',
               y, [white, kraft], parent=yoke)

# breech: laminated closing discs + cascabel ball
card_panel("MOR_Breech0", ngon_pts(12, 0.17, phase=tau / 24), 0.05, 'Y',
           TUBE_REAR_Y + 0.01, [kraft, kraft], parent=yoke)
card_panel("MOR_Breech1", ngon_pts(12, 0.13, phase=tau / 24), 0.04, 'Y',
           TUBE_REAR_Y + 0.05, [white, kraft], parent=yoke)
disc_ball("MOR_Cascabel", 0.06, (0, 0.22, 0), [white, kraft], parent=yoke)

# bomb tray on the LEFT flank: card shelf + two cartoon paper bombs with
# brass fuse studs. Placed forward so the bombs orbit clear of the struts
# through the full lob elevation sweep.
card_panel("MOR_BombShelf", [(-0.33, -0.10), (-0.19, -0.10), (-0.19, -0.46),
                             (-0.33, -0.46)],
           0.016, 'Z', -0.075, [white, kraft], parent=yoke)
for i, y in enumerate((-0.20, -0.38)):
    disc_ball(f"MOR_Bomb{i}", 0.075, (-0.26, y, 0.008), [kraft, white],
              parent=yoke)
    card_panel(f"MOR_BombFuse{i}", ngon_pts(6, 0.018, cx=-0.26, cy=y),
               0.035, 'Z', 0.10, [brass, brass], parent=yoke)

# muzzle marker, just inside the flare
muzzle = bpy.data.objects.new("MOR_Muzzle", None)
muzzle.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = yoke
muzzle.location = (0, TUBE_TIP_Y + 0.06, 0)

scale_tree(root, SCALE)

if DO_EXPORT:
    export_tree(root, EXPORT_PATH, yoke=yoke, muzzle=muzzle)

print("Mortar_Paper built.")
