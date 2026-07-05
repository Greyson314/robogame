# artgen/inv_capycube.py — inventor study: command block with capybara pilot.
# v3: snout rounded (converging end rings instead of a flat cap), cockpit
# opened up (0.72 well, lower coaming), cuteness pass (bigger head, big
# highlighted eyes, cheek tufts, u-mouth, rounder ears, round paws).
# Layout: 1x1x2. Bottom cell is a planked structure cube with an inset
# open-air cockpit well (coaming rail + rolled linen pad, ship-hatch
# language). Top cell is nothing but the capybara from the shoulders up.
# The cyan spark sits flush in the deck as a small brass-ringed binnacle
# disc, keeping cyan = CPU vocabulary without cluttering the silhouette.
#
# Capybara build note: head and body are lofted from rounded-rectangle
# (superellipse) cross-sections, not lathes — the capybara read lives in
# the boxy skull, the wide barely-tapering snout, and eyes/ears set high
# and far back. Root local z: bottom cell -0.5..0.5, top cell 0.5..1.5.

import bpy
from math import tau, cos, sin

import paperlib as pl
import inventorlib as il
import inv_cpu

PFX = "InvCapyCube_"

FUR = (0.23, 0.125, 0.055, 1.0)        # tawny capybara brown (linear)
FUR_LIGHT = (0.31, 0.185, 0.09, 1.0)   # paws / cheeks / chest


def fur_materials():
    fur = pl.get_material("InvCapyFur", FUR, roughness=0.93)
    il._weave(fur, scale=90.0, strength=0.18)
    fur_l = pl.get_material("InvCapyFurLight", FUR_LIGHT, roughness=0.93)
    il._weave(fur_l, scale=90.0, strength=0.18)
    return fur, fur_l


def _rring_xz(y, w, h, cz, n=18, exp=0.48):
    """Rounded-rectangle ring in the XZ plane at depth y (head sections).
    exp is the superellipse power: lower = boxier, higher = rounder."""
    pts = []
    for k in range(n):
        t = k * tau / n
        c, s = cos(t), sin(t)
        x = (abs(c) ** exp) * (1 if c >= 0 else -1) * w / 2
        z = (abs(s) ** exp) * (1 if s >= 0 else -1) * h / 2
        pts.append((x, y, cz + z))
    return pts


def _rring_xy(z, w, d, cy, n=18, exp=0.50):
    """Rounded-rectangle ring in the XY plane at height z (body sections)."""
    pts = []
    for k in range(n):
        t = k * tau / n
        c, s = cos(t), sin(t)
        x = (abs(c) ** exp) * (1 if c >= 0 else -1) * w / 2
        y = (abs(s) ** exp) * (1 if s >= 0 else -1) * d / 2
        pts.append((x, cy + y, z))
    return pts


def build(loc=(10.4, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    oak_ax, oak_bx = il.oak_grain('X')
    oak_ay, oak_by = il.oak_grain('Y')
    fur, fur_l = fur_materials()
    spark = inv_cpu.spark_material()
    root = il.root_empty(PFX + "Root", loc)

    # ---- bottom cell: planked cube with cockpit well ------------------
    for i, (sx, sy) in enumerate(((1, 1), (1, -1), (-1, 1), (-1, -1))):
        il.box(f"{PFX}Post{i}", (sx * 0.46, sy * 0.46, 0),
               (0.08, 0.08, 1.0), [m["wood_dark"]], parent=root)
        il.lathe(f"{PFX}Cap{i}",
                 [(0.026, 0.0), (0.026, 0.018), (0.012, 0.028)],
                 [m["brass"]], segs=10,
                 center=(sx * 0.46, sy * 0.46, 0.50), parent=root)
    # side panels, grain along the visible face
    il.box(f"{PFX}PanelF", (0, 0.455, -0.03), (0.84, 0.055, 0.94),
           [oak_ax], parent=root)
    il.box(f"{PFX}PanelB", (0, -0.455, -0.03), (0.84, 0.055, 0.94),
           [oak_bx], parent=root)
    il.box(f"{PFX}PanelR", (0.455, 0, -0.03), (0.055, 0.84, 0.94),
           [oak_ay], parent=root)
    il.box(f"{PFX}PanelL", (-0.455, 0, -0.03), (0.055, 0.84, 0.94),
           [oak_by], parent=root)

    # deck frame around the 0.72 x 0.72 well, plank seam mid-front/back
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}DeckF{i}", (s * 0.23, 0.41, 0.47),
               (0.46, 0.10, 0.06), [oak_ax if i == 0 else oak_bx],
               parent=root)
        il.box(f"{PFX}DeckB{i}", (s * 0.23, -0.41, 0.47),
               (0.46, 0.10, 0.06), [oak_bx if i == 0 else oak_ax],
               parent=root)
        il.box(f"{PFX}DeckSide{i}", (s * 0.41, 0, 0.47),
               (0.10, 0.72, 0.06), [oak_ay if i == 0 else oak_by],
               parent=root)

    # well: walnut lining + floor, slightly inset
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}WellWallY{i}", (0, s * 0.36, 0.325),
               (0.72, 0.035, 0.29), [m["wood_dark"]], parent=root)
        il.box(f"{PFX}WellWallX{i}", (s * 0.36, 0, 0.325),
               (0.035, 0.72, 0.29), [m["wood_dark"]], parent=root)
    il.box(f"{PFX}WellFloor", (0, 0, 0.17), (0.72, 0.72, 0.04),
           [oak_ax], parent=root)

    # low coaming rails + rolled linen pad (the cozy hatch rim)
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}CoamY{i}", (0, s * 0.375, 0.5275),
               (0.83, 0.05, 0.055), [m["wood_dark"]], parent=root)
        il.box(f"{PFX}CoamX{i}", (s * 0.375, 0, 0.5275),
               (0.05, 0.75, 0.055), [m["wood_dark"]], parent=root)
        il.rod(f"{PFX}PadY{i}", (-0.375, s * 0.375, 0.565),
               (0.375, s * 0.375, 0.565), 0.026, [m["linen"]],
               parent=root)
        il.rod(f"{PFX}PadX{i}", (s * 0.375, -0.375, 0.565),
               (s * 0.375, 0.375, 0.565), 0.026, [m["linen"]],
               parent=root)
    for i, (sx, sy) in enumerate(((1, 1), (1, -1), (-1, 1), (-1, -1))):
        pl.disc_ball(f"{PFX}PadCorner{i}", 0.026,
                     (sx * 0.375, sy * 0.375, 0.565), [m["linen"]],
                     parent=root, bands=5, segs=8)

    # flush spark binnacle on the front deck
    il.torus(f"{PFX}SparkRing", 0.035, 0.008, [m["brass"]],
             center=(0, 0.415, 0.503), axis='Z', segs=14, sides=6,
             parent=root)
    il.lathe(f"{PFX}Spark",
             [(0.028, 0.0), (0.030, 0.008), (0.013, 0.015)],
             [spark], segs=12, center=(0, 0.415, 0.50), parent=root)

    # ---- the capybara (shoulders up, head out of the hatch) -----------
    # body loaf, mostly hidden in the well, chest showing above the pad
    body_rings = [
        _rring_xy(0.20, 0.34, 0.38, -0.02),
        _rring_xy(0.28, 0.48, 0.52, -0.02),
        _rring_xy(0.42, 0.52, 0.56, -0.02),
        _rring_xy(0.56, 0.48, 0.52, -0.02),
        _rring_xy(0.66, 0.40, 0.46, -0.02),
        _rring_xy(0.72, 0.26, 0.32, -0.02),
    ]
    pv, pf = pl.loft(body_rings)
    pl.make_object(f"{PFX}CapyBody", pv, pf, [fur], parent=root)

    # head: boxy skull, straight top sloping into a wide snout that
    # rounds off at the front (converging end rings, no flat cap edge).
    HZ = 0.90   # head vertical center
    head_rings = [
        _rring_xz(-0.19, 0.22, 0.26, HZ + 0.000),
        _rring_xz(-0.12, 0.30, 0.36, HZ + 0.000),
        _rring_xz(-0.01, 0.34, 0.40, HZ + 0.010),
        _rring_xz(0.09, 0.325, 0.39, HZ + 0.000),
        _rring_xz(0.16, 0.30, 0.335, HZ - 0.020),
        _rring_xz(0.24, 0.285, 0.295, HZ - 0.038),
        _rring_xz(0.30, 0.27, 0.265, HZ - 0.048),
        _rring_xz(0.335, 0.245, 0.24, HZ - 0.052),
        _rring_xz(0.355, 0.16, 0.16, HZ - 0.055),
    ]
    pv, pf = pl.loft(head_rings)
    pl.make_object(f"{PFX}CapyHead", pv, pf, [fur], parent=root)

    for i, s in enumerate((1, -1)):
        # nostrils, tucked just above the mouth
        pl.disc_ball(f"{PFX}CapyNostril{i}", 0.0115,
                     (s * 0.030, 0.355, HZ - 0.068), [m["ink"]],
                     parent=root, bands=4, segs=8)
        # eyes: big and glossy at mid-face (cheated forward from
        # true-capy side placement — the cartoon compromise)
        pl.disc_ball(f"{PFX}CapyEye{i}", 0.026,
                     (s * 0.070, 0.350, HZ - 0.028), [m["ink"]],
                     parent=root, bands=6, segs=12)
        pl.disc_ball(f"{PFX}CapyEyeGlint{i}", 0.009,
                     (s * 0.079, 0.362, HZ - 0.017), [m["white"]],
                     parent=root, bands=4, segs=8)
        # blush lenses hugging the muzzle sides, under the eyes
        il.lathe(f"{PFX}CapyCheek{i}",
                 [(0.004, 0.0), (0.036, s * 0.008), (0.042, s * 0.016),
                  (0.032, s * 0.024), (0.004, s * 0.030)],
                 [fur_l], segs=12, axis='X',
                 center=(s * 0.138, 0.22, HZ - 0.045), parent=root)
        # ears: rounded flaps at the top-back corners
        il.lathe(f"{PFX}CapyEar{i}",
                 [(0.012, 0.0), (0.042, 0.022), (0.046, 0.052),
                  (0.020, 0.072)],
                 [fur], segs=10,
                 center=(s * 0.115, -0.075, HZ + 0.170), parent=root)
        # round paws resting on the front pad
        pl.disc_ball(f"{PFX}CapyPaw{i}", 0.052,
                     (s * 0.15, 0.385, 0.60), [fur_l],
                     parent=root, bands=6, segs=10)

    # the little u-mouth on the face plate, below the nostrils
    il.sweep(f"{PFX}CapyMouth",
             [(-0.042, 0.353, HZ - 0.092), (-0.015, 0.359, HZ - 0.108),
              (0.015, 0.359, HZ - 0.108), (0.042, 0.353, HZ - 0.092)],
             0.006, [m["ink"]], sides=6, parent=root)

    return root
