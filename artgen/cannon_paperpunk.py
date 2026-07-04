# artgen/cannon_paperpunk.py — paper-punk cannon turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig, applied at export:
#   root "Cannon_Paper" (yaws) -> "Turret" pitch yoke -> "ShootPoint".
# In-scene names: CAN_Yoke / CAN_Muzzle.
#
# Concept (session 131 revision, steering away from WWII field-gun shapes):
# the cartoon cannon archetype — fat tapered barrel with hoop rings, flared
# muzzle lip, cascabel ball at the breech, riding on stepped carriage
# cheeks with scalloped wheels, cannonball pyramid stacked on the deck.
# All still paper: rolled cone tubes, card cheeks, laminated-disc balls.

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
                      export_tree)

DO_EXPORT = True
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\Cannon_Paper.fbx"

LOCATION = (-1.8, 0.0, 0.0)  # scene slot left of the SMG; zeroed at export
PIVOT_Z = 0.50               # trunnion height
BASE_PLATE = 0.72
GEAR_Z0 = 0.05
GEAR_TOP = 0.18

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

# carriage: stepped card cheeks (old naval-carriage profile) that carry the
# trunnion brads, plus scalloped card wheels on brass axles
cheek = [(-0.30, 0.18), (0.44, 0.18), (0.44, 0.26), (0.30, 0.26),
         (0.30, 0.38), (0.14, 0.38), (0.14, 0.56), (-0.12, 0.56),
         (-0.30, 0.42)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"CAN_Cheek{side}", cheek, 0.05, 'X', sign * 0.16,
               [white, kraft], parent=root)
card_panel("CAN_CheekSpacer", [(0.20, 0.18), (0.40, 0.18),
                               (0.40, 0.30), (0.20, 0.30)],
           0.27, 'X', 0.0, [white, kraft], parent=root)
wheel_prof = [(y + 0.16, z + 0.21) for y, z in gear_profile(8, 0.125, 0.16)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"CAN_Wheel{side}", wheel_prof, 0.035, 'X', sign * 0.215,
               [white, kraft], parent=root)
    brad(f"CAN_Axle{side}", sign * 0.21, sign * 0.26, 0.05, 0.21, brass,
         parent=root, y=0.16)
brad("CAN_TrunnionR", 0.185, 0.21, 0.06, PIVOT_Z, brass, parent=root)
brad("CAN_TrunnionL", -0.185, -0.21, 0.06, PIVOT_Z, brass, parent=root)

# cannonball pyramid on the front-right deck corner (clear of the gear
# teeth ring — the corner sits in a tooth gap)
card_panel("CAN_BallShelf", [(0.20, -0.20), (0.32, -0.20), (0.32, -0.32),
                             (0.20, -0.32)],
           0.016, 'Z', 0.068, [white, kraft], parent=root)
disc_ball("CAN_Ball0", 0.065, (0.26, -0.26, 0.141), [kraft, white],
          parent=root)
disc_ball("CAN_Ball1", 0.065, (0.31, -0.31, 0.141), [kraft, white],
          parent=root)
disc_ball("CAN_Ball2", 0.065, (0.285, -0.285, 0.251), [kraft, white],
          parent=root)

# ---------------------------------------------------------------- turret --
yoke = bpy.data.objects.new("CAN_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

# the gun: one long rolled cone, breech-fat to muzzle-slim, hooped
tube("CAN_Barrel", 0.130, 0.018, 0.26, -0.86, 12,
     [white, kraft, kraft], parent=yoke, r_end=0.082)
card_panel("CAN_BreechCap", ngon_pts(12, 0.118, phase=tau / 24), 0.03, 'Y',
           0.265, [kraft, kraft], parent=yoke)
card_panel("CAN_Hoop0", ngon_pts(12, 0.132, phase=tau / 24), 0.05, 'Y',
           -0.12, [white, kraft], parent=yoke)
card_panel("CAN_Hoop1", ngon_pts(12, 0.112, phase=tau / 24), 0.05, 'Y',
           -0.55, [white, kraft], parent=yoke)

# flared muzzle lip, open red bore
tube("CAN_Flare", 0.082, 0.016, -0.86, -1.02, 12,
     [white, kraft, channel], parent=yoke, r_end=0.14)

# cascabel: laminated paper ball + brass knob off the breech
disc_ball("CAN_Cascabel", 0.075, (0, 0.335, 0), [white, kraft], parent=yoke)
hex_pts = [(0.035 * cos(i * tau / 6), 0.035 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.40, z) for x, z in hex_pts],
               [(x, 0.46, z) for x, z in hex_pts]])
make_object("CAN_CascabelKnob", pv, pf, [brass], parent=yoke)

# muzzle marker, just inside the flare
muzzle = bpy.data.objects.new("CAN_Muzzle", None)
muzzle.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = yoke
muzzle.location = (0, -0.98, 0)

if DO_EXPORT:
    export_tree(root, EXPORT_PATH, yoke=yoke, muzzle=muzzle)

print("Cannon_Paper built.")
