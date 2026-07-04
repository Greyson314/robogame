# artgen/smg_paperpunk.py — paper-punk SMG turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig (session 120 convention), applied at export:
#   root "SMG_Paper" (yaws) -> "Turret" pitch yoke -> "ShootPoint" muzzle.
# In-scene the empties are SMG_Yoke / SMG_Muzzle (names are global in
# Blender and the weapon family shares the scene).
#
# Concept (session 131, fourth revision — cutesy pass): the pew-pew gun
# should feel lighthearted, in sync with the cartoon cannon and bombard.
# Cartoon = proportion, not decoration: a chubby rounded-loaf receiver
# (chamfered card box, shorter + taller), one oversized lovable detail per
# view — big ammo-reel canister (left), big gauge dial (top), fat stubby
# barrel with a bulbous muzzle ring — plus the signature open groove.

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
RAIL_ROOT_Y = -0.48     # stubby: the barrel is a fat little pellet spitter
RAIL_TIP_Y = -0.78
BARREL_R = 0.07
SHELL_T = 0.018
GAP_DEG = 16.0
ARC_SEGS = 10
RAKE_DEG = 4.0          # receiver leans into the action; barrel stays level

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

# body sub-frame: the whole receiver assembly rakes nose-down a few
# degrees while the barrel stays on the aim line — cartoon lean-in
body = bpy.data.objects.new("SMG_Body", None)
body.empty_display_size = 0.06
bpy.context.scene.collection.objects.link(body)
body.parent = yoke
body.rotation_euler = (radians(RAKE_DEG), 0, 0)

# receiver: chubby rounded loaf — chamfered card box, short and tall.
# Cartoon proportions carry the cuteness; the facets stay paper.
core_loaf = [(0.22, -0.13), (0.22, 0.13), (0.14, 0.19), (-0.14, 0.19),
             (-0.24, 0.11), (-0.34, 0.05), (-0.34, -0.07), (-0.26, -0.13)]
card_panel("SMG_ReceiverCore", core_loaf, 0.13, 'X', 0.0,
           [white, kraft], parent=body)
plate_loaf = [(0.245, -0.11), (0.245, 0.11), (0.155, 0.165), (-0.125, 0.165),
              (-0.215, 0.09), (-0.30, 0.03), (-0.30, -0.06), (-0.235, -0.11)]
for sign, side in ((1, "R"), (-1, "L")):
    card_panel(f"SMG_SidePlate{side}", plate_loaf, 0.02, 'X', sign * 0.075,
               [white, kraft], parent=body)

# rear closure: kraft cap plate pinned with a brass fastener
card_panel("SMG_RearCap", [(-0.06, -0.10), (0.06, -0.10), (0.06, 0.10),
                           (-0.06, 0.10)],
           0.025, 'Y', 0.2325, [kraft, kraft], parent=body)
hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
           for i in range(6)]
pv, pf = loft([[(x, 0.245, z) for x, z in hex_pts],
               [(x, 0.28, z) for x, z in hex_pts]])
make_object("SMG_RearBrad", pv, pf, [brass], parent=body)

# one brass strap over the loaf — a little belt
card_panel("SMG_Strap", [(-0.095, -0.14), (0.095, -0.14),
                         (0.095, 0.20), (-0.095, 0.20)],
           0.03, 'Y', -0.12, [brass, brass], parent=body)

# big gauge dial on top — oversized, the "top view" lovable detail
card_panel("SMG_GaugeRing", ngon_pts(10, 0.075, cy=0.02), 0.05, 'Z', 0.215,
           [brass, brass], parent=body)
card_panel("SMG_GaugeFace", ngon_pts(10, 0.065, cy=0.02), 0.014, 'Z', 0.247,
           [white, kraft], parent=body)

# oversized ammo-reel canister on the LEFT — chubby, red-striped, brass
# hub. Mounted clear of the strut plane so it never clips through pitch.
card_panel("SMG_ReelNeck", ngon_pts(8, 0.06, cx=-0.16), 0.05, 'X', -0.10,
           [kraft, kraft], parent=body)
card_panel("SMG_Reel", [(y - 0.16, z) for y, z in ngon_pts(12, 0.15)],
           0.13, 'X', -0.18, [white, kraft], parent=body)
card_panel("SMG_ReelStripe", [(y - 0.16, z) for y, z in ngon_pts(12, 0.158)],
           0.035, 'X', -0.18, [channel, channel], parent=body)
brad("SMG_ReelHub", -0.245, -0.27, 0.05, 0.0, brass, parent=body, y=-0.16)

# casing chute low on the RIGHT — angled kraft duct with a red slot
card_panel("SMG_Chute", [(0.04, -0.08), (-0.08, -0.12), (-0.08, -0.19),
                         (0.04, -0.15)],
           0.035, 'X', 0.10, [kraft, kraft, channel], cap_slots=(0, 2),
           parent=body)

# collar: two fat laminate rings stepping the loaf down to the barrel
card_panel("SMG_Collar0", ngon_pts(12, 0.115, cy=-0.01, phase=tau / 24),
           0.05, 'Y', -0.37, [white, kraft], parent=yoke)
card_panel("SMG_Collar1", ngon_pts(12, 0.088, cy=-0.01, phase=tau / 24),
           0.045, 'Y', -0.43, [kraft, kraft], parent=yoke)

# barrel: fat stubby split tube — open groove, red channel, kraft edges
for top, name in ((True, "SMG_BarrelTop"), (False, "SMG_BarrelBottom")):
    a0, a1 = GAP_DEG, 180.0 - GAP_DEG
    if not top:
        a0, a1 = -a0, -a1
    arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y, a0, a1,
              ARC_SEGS, [white, kraft, channel], slots=(0, 1, 2),
              center=(0.0, -0.01), parent=yoke)

# bulbous muzzle ring near the tip — the cartoon "boop"
card_panel("SMG_MuzzleRing", ngon_pts(12, 0.10, cy=-0.01, phase=tau / 24),
           0.06, 'Y', -0.70, [white, kraft], parent=yoke)

# chunky brass bead sight on the muzzle ring
card_panel("SMG_SightBead", ngon_pts(6, 0.024, cy=-0.70), 0.05, 'Z', 0.115,
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
