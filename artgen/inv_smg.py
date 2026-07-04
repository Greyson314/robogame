# artgen/inv_smg.py — inventor study: SMG in wood + linen.
# Same cutesy bones as smg_paperpunk.py (fourth-revision proportions,
# copied numbers) so the comparison isolates material language, like the
# reclaimed/steampunk studies did. Transposition: card laminate -> wood
# laminate (spruce faces, walnut cut edges), kraft -> walnut, reel and
# gauge faces -> linen, cord lashings at the barrel root, brass and the
# vermilion projectile channel unchanged. Blender-only, no export.

import bpy
from math import radians, cos, sin, tau

import paperlib as pl
import inventorlib as il

PFX = "InvSMG_"

PIVOT_Z = 0.47
GEAR_TOP = 0.095
GEAR_R_OUT = 0.32
SCALE = 0.5 / GEAR_R_OUT
RAIL_ROOT_Y = -0.48
RAIL_TIP_Y = -0.78
BARREL_R = 0.07
SHELL_T = 0.018
GAP_DEG = 16.0
RAKE_DEG = 4.0


def build(loc=(7.0, -5.0, 0.0)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    wood, dark = m["wood"], m["wood_dark"]
    linen, cord = m["linen"], m["cord"]
    brass, channel = m["brass"], m["channel"]

    root = il.root_empty(PFX + "Root", loc)

    # yaw gear — wood laminate take on the family rotary base
    pl.card_panel(f"{PFX}GearBottom", pl.ngon_pts(12, 0.29), 0.03, 'Z',
                  0.015, [wood, dark], parent=root)
    pl.card_panel(f"{PFX}GearTeeth", pl.gear_profile(10, 0.26, GEAR_R_OUT),
                  0.035, 'Z', 0.0475, [wood, dark], parent=root)
    pl.card_panel(f"{PFX}GearTop", pl.ngon_pts(12, 0.24), 0.03, 'Z', 0.08,
                  [wood, dark], parent=root)

    # A-frame yoke struts + brass pitch brads
    strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
                   (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
    strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
                  (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}StrutFront{side}", strut_front, 0.022, 'X',
                      sign * 0.10, [wood, dark], parent=root)
        pl.card_panel(f"{PFX}StrutRear{side}", strut_rear, 0.022, 'X',
                      sign * 0.10, [wood, dark], parent=root)
    pl.card_panel(f"{PFX}GussetSpacer",
                  [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                   (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
                  0.178, 'X', 0.0, [wood, dark], parent=root)
    pl.brad(f"{PFX}BradR", 0.111, 0.132, 0.05, PIVOT_Z, brass, parent=root)
    pl.brad(f"{PFX}BradL", -0.111, -0.132, 0.05, PIVOT_Z, brass, parent=root)

    yoke = il.root_empty(PFX + "Yoke", (0, 0, PIVOT_Z))
    yoke.parent = root
    yoke.location = (0, 0, PIVOT_Z)

    body = il.root_empty(PFX + "Body", (0, 0, 0))
    body.parent = yoke
    body.location = (0, 0, 0)
    body.rotation_euler = (radians(RAKE_DEG), 0, 0)

    # receiver loaf: wood laminate core + proud walnut side plates
    core_loaf = [(0.22, -0.13), (0.22, 0.13), (0.14, 0.19), (-0.14, 0.19),
                 (-0.24, 0.11), (-0.34, 0.05), (-0.34, -0.07),
                 (-0.26, -0.13)]
    pl.card_panel(f"{PFX}ReceiverCore", core_loaf, 0.13, 'X', 0.0,
                  [wood, dark], parent=body)
    plate_loaf = [(0.245, -0.11), (0.245, 0.11), (0.155, 0.165),
                  (-0.125, 0.165), (-0.215, 0.09), (-0.30, 0.03),
                  (-0.30, -0.06), (-0.235, -0.11)]
    for sign, side in ((1, "R"), (-1, "L")):
        pl.card_panel(f"{PFX}SidePlate{side}", plate_loaf, 0.02, 'X',
                      sign * 0.075, [dark, wood], parent=body)

    # rear closure: walnut cap + brass fastener
    pl.card_panel(f"{PFX}RearCap",
                  [(-0.06, -0.10), (0.06, -0.10), (0.06, 0.10),
                   (-0.06, 0.10)],
                  0.025, 'Y', 0.2325, [dark, dark], parent=body)
    hex_pts = [(0.04 * cos(i * tau / 6), 0.04 * sin(i * tau / 6))
               for i in range(6)]
    pv, pf = pl.loft([[(x, 0.245, z) for x, z in hex_pts],
                      [(x, 0.28, z) for x, z in hex_pts]])
    pl.make_object(f"{PFX}RearBrad", pv, pf, [brass], parent=body)

    # brass strap belt over the loaf
    pl.card_panel(f"{PFX}Strap",
                  [(-0.095, -0.14), (0.095, -0.14), (0.095, 0.20),
                   (-0.095, 0.20)],
                  0.03, 'Y', -0.12, [brass, brass], parent=body)

    # gauge dial on top — brass ring, linen face
    pl.card_panel(f"{PFX}GaugeRing", pl.ngon_pts(10, 0.075, cy=0.02), 0.05,
                  'Z', 0.215, [brass, brass], parent=body)
    pl.card_panel(f"{PFX}GaugeFace", pl.ngon_pts(10, 0.065, cy=0.02), 0.014,
                  'Z', 0.247, [linen, dark], parent=body)

    # ammo-reel canister LEFT — linen drum, walnut rims, red stripe kept
    pl.card_panel(f"{PFX}ReelNeck", pl.ngon_pts(8, 0.06, cx=-0.16), 0.05,
                  'X', -0.10, [dark, dark], parent=body)
    pl.card_panel(f"{PFX}Reel",
                  [(y - 0.16, z) for y, z in pl.ngon_pts(12, 0.15)],
                  0.13, 'X', -0.18, [linen, dark], parent=body)
    pl.card_panel(f"{PFX}ReelStripe",
                  [(y - 0.16, z) for y, z in pl.ngon_pts(12, 0.158)],
                  0.035, 'X', -0.18, [channel, channel], parent=body)
    pl.brad(f"{PFX}ReelHub", -0.245, -0.27, 0.05, 0.0, brass, parent=body,
            y=-0.16)

    # casing chute RIGHT — walnut duct, red slot
    pl.card_panel(f"{PFX}Chute",
                  [(0.04, -0.08), (-0.08, -0.12), (-0.08, -0.19),
                   (0.04, -0.15)],
                  0.035, 'X', 0.10, [dark, dark, channel], cap_slots=(0, 2),
                  parent=body)

    # collar: turned-wood laminate rings stepping down to the barrel
    pl.card_panel(f"{PFX}Collar0",
                  pl.ngon_pts(12, 0.115, cy=-0.01, phase=tau / 24), 0.05,
                  'Y', -0.37, [wood, dark], parent=yoke)
    pl.card_panel(f"{PFX}Collar1",
                  pl.ngon_pts(12, 0.088, cy=-0.01, phase=tau / 24), 0.045,
                  'Y', -0.43, [dark, dark], parent=yoke)

    # barrel: steam-bent wood half-shells — groove + red channel signature
    for top, name in ((True, f"{PFX}BarrelTop"), (False, f"{PFX}BarrelBot")):
        a0, a1 = GAP_DEG, 180.0 - GAP_DEG
        if not top:
            a0, a1 = -a0, -a1
        pl.arc_shell(name, BARREL_R, SHELL_T, RAIL_ROOT_Y, RAIL_TIP_Y,
                     a0, a1, 10, [wood, dark, channel], slots=(0, 1, 2),
                     center=(0.0, -0.01), parent=yoke)

    # cord lashings where the barrel meets the collar — the inventor tell
    for k, yy in enumerate((-0.485, -0.515)):
        il.torus(f"{PFX}Lash{k}", BARREL_R + 0.008, 0.009, [cord],
                 center=(0, yy, -0.01), axis='Y', segs=16, sides=5,
                 parent=yoke)

    # bulbous muzzle ring + brass bead
    pl.card_panel(f"{PFX}MuzzleRing",
                  pl.ngon_pts(12, 0.10, cy=-0.01, phase=tau / 24), 0.06,
                  'Y', -0.70, [wood, dark], parent=yoke)
    pl.card_panel(f"{PFX}SightBead", pl.ngon_pts(6, 0.024, cy=-0.70), 0.05,
                  'Z', 0.115, [brass, brass], parent=yoke)

    pl.scale_tree(root, SCALE)
    return root
