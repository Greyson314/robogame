# artgen/smg_paperpunk.py — paper-punk SMG turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig (session 120 convention), applied at export:
#   root "SMG_Paper" (yaws) -> "Turret" pitch yoke -> "ShootPoint" muzzle.
# In-scene the empties are SMG_Yoke / SMG_Muzzle (names are global in
# Blender and the weapon family shares the scene).
#
# Concept (session 131 revision, steering away from WWII shapes toward
# steampunk silhouettes): a boiler-drum receiver with brass bands and a
# pressure gauge, a side-mounted ammo reel on the left (film-canister
# read), a casing chute low on the right, and the signature split rolled-
# tube barrel with the open projectile groove. Asymmetry preserved —
# every flank tells a different story.

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
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\SMG_Paper.fbx"

# Forward = -Y, up = +Z. Units: meters. Block = 1 m cube. Root at origin.
LOCATION = (0.0, 0.0, 0.0)
PIVOT_Z = 0.52
BASE_PLATE = 0.72
GEAR_Z0 = 0.05
GEAR_TOP = 0.145
DRUM_R = 0.125          # boiler-drum receiver radius
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

# base sheet + yaw gear (family-standard rotary base)
h = BASE_PLATE / 2
card_panel("SMG_BasePlate", [(-h, -h), (h, -h), (h, h), (-h, h)],
           0.05, 'Z', 0.025, [white, kraft], parent=root)
card_panel("SMG_GearBottom", ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
           [white, kraft], parent=root)
card_panel("SMG_GearTeeth", gear_profile(10, 0.26, 0.32), 0.035, 'Z',
           GEAR_Z0 + 0.0475, [white, kraft], parent=root)
card_panel("SMG_GearTop", ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
           [white, kraft], parent=root)

# A-frame yoke, widened for the round drum receiver
strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"SMG_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.145,
               [white, kraft], parent=root)
    card_panel(f"SMG_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.145,
               [white, kraft], parent=root)
card_panel("SMG_GussetSpacer", [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                                (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
           0.268, 'X', 0.0, [white, kraft], parent=root)
brad("SMG_BradR", 0.157, 0.178, 0.05, PIVOT_Z, brass, parent=root)
brad("SMG_BradL", -0.157, -0.178, 0.05, PIVOT_Z, brass, parent=root)

# ---------------------------------------------------------------- turret --
yoke = bpy.data.objects.new("SMG_Yoke", None)
yoke.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(yoke)
yoke.parent = root
yoke.location = (0, 0, PIVOT_Z)

# receiver: rolled boiler drum along the gun axis, brass-banded, closed by
# a laminated rear disc with a brass fastener center
tube("SMG_Drum", DRUM_R, 0.02, 0.28, -0.48, 12, [white, kraft, kraft],
     parent=yoke)
card_panel("SMG_DrumRear", ngon_pts(12, 0.118, phase=tau / 24), 0.035, 'Y',
           0.285, [kraft, kraft], parent=yoke)
hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.30, z) for x, z in hex_pts],
               [(x, 0.335, z) for x, z in hex_pts]])
make_object("SMG_DrumBrad", pv, pf, [brass], parent=yoke)
for i, y in enumerate((0.10, -0.30)):
    card_panel(f"SMG_BoilerBand{i}", ngon_pts(12, DRUM_R + 0.008,
                                              phase=tau / 24),
               0.028, 'Y', y, [brass, brass], parent=yoke)

# pressure gauge on top — brass ring bezel + white card face
card_panel("SMG_GaugeRing", ngon_pts(10, 0.05), 0.045, 'Z', DRUM_R + 0.015,
           [brass, brass], parent=yoke)
card_panel("SMG_GaugeFace", ngon_pts(10, 0.042), 0.012, 'Z', DRUM_R + 0.043,
           [white, kraft], parent=yoke)

# ammo reel on the LEFT flank, forward-mounted so it feeds the collar:
# laminated card canister with a red mid-stripe and a brass hub
card_panel("SMG_ReelNeck", ngon_pts(8, 0.05, cx=-0.28), 0.08, 'X', -0.145,
           [kraft, kraft], parent=yoke)
card_panel("SMG_Reel", [(y - 0.28, z) for y, z in ngon_pts(12, 0.115)],
           0.11, 'X', -0.24, [white, kraft], parent=yoke)
card_panel("SMG_ReelStripe", [(y - 0.28, z) for y, z in ngon_pts(12, 0.122)],
           0.03, 'X', -0.24, [channel, channel], parent=yoke)
brad("SMG_ReelHub", -0.295, -0.318, 0.04, 0.0, brass, parent=yoke, y=-0.28)

# casing chute low on the RIGHT — angled kraft duct with a red slot
card_panel("SMG_Chute", [(0.02, -0.06), (-0.10, -0.10), (-0.10, -0.17),
                         (0.02, -0.13)],
           0.035, 'X', 0.13, [kraft, kraft, channel], cap_slots=(0, 2),
           parent=yoke)

# collar: laminated card discs stepping the drum down to the barrel
disc_y = -0.50
radii = (0.105, 0.075, 0.09, 0.065, 0.078, 0.055)
for i, r in enumerate(radii):
    mm = [white, kraft] if i % 2 == 0 else [kraft, kraft]
    card_panel(f"SMG_Collar{i}", ngon_pts(12, r, cy=-0.01, phase=tau / 24),
               0.036, 'Y', disc_y - i * 0.046, mm, parent=yoke)

# barrel: rolled-card tube split horizontally into two half-shells — open
# groove down the middle, red channel interior, kraft cut edges
for top, name in ((True, "SMG_BarrelTop"), (False, "SMG_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [white, kraft, channel], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)

# brass bead sight near the muzzle, standing on the top rail
card_panel("SMG_SightBead", ngon_pts(6, 0.015, cy=-1.10), 0.04, 'Z', 0.065,
           [brass, brass], parent=yoke)

# muzzle marker, inside the open groove
muzzle = bpy.data.objects.new("SMG_Muzzle", None)
muzzle.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(muzzle)
muzzle.parent = yoke
muzzle.location = (0, RAIL_TIP_Y + 0.04, -0.01)

if DO_EXPORT:
    export_tree(root, EXPORT_PATH, yoke=yoke, muzzle=muzzle)

print("SMG_Paper built.")
