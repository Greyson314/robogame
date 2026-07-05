# artgen/inv_capycube.py — inventor study: command block with capybara pilot.
# v2 (user direction): a 1x1x2 setup. Bottom cell is a planked structure
# cube with a slightly inset open-air cockpit well (coaming rail + rolled
# linen pad, ship-hatch language). Top cell is nothing but the capybara
# from the shoulders up — head out of the hatch, paws on the rim. The
# cyan spark sits flush in the deck as a small brass-ringed binnacle disc,
# keeping the cyan = CPU vocabulary without cluttering the silhouette.
#
# Capybara build note: the head is lofted from rounded-rectangle
# (superellipse) cross-sections, not a lathe — the capybara read lives in
# the boxy skull, the flat-fronted snout, and eyes/ears set high and far
# back. Root local z: bottom cell -0.5..0.5, top cell 0.5..1.5.

import bpy
from math import tau, cos, sin

import paperlib as pl
import inventorlib as il
import inv_cpu

PFX = "InvCapyCube_"

FUR = (0.23, 0.125, 0.055, 1.0)        # tawny capybara brown (linear)
FUR_LIGHT = (0.31, 0.185, 0.09, 1.0)   # paws / chest


def fur_materials():
    fur = pl.get_material("InvCapyFur", FUR, roughness=0.93)
    il._weave(fur, scale=90.0, strength=0.18)
    fur_l = pl.get_material("InvCapyFurLight", FUR_LIGHT, roughness=0.93)
    il._weave(fur_l, scale=90.0, strength=0.18)
    return fur, fur_l


def _rring_xz(y, w, h, cz, n=18, exp=0.45):
    """Rounded-rectangle ring in the XZ plane at depth y (head sections).
    exp is the superellipse power: 1.0 = diamond-ish, 0.5 ~ rounded box,
    lower = boxier."""
    pts = []
    for k in range(n):
        t = k * tau / n
        c, s = cos(t), sin(t)
        x = (abs(c) ** exp) * (1 if c >= 0 else -1) * w / 2
        z = (abs(s) ** exp) * (1 if s >= 0 else -1) * h / 2
        pts.append((x, y, cz + z))
    return pts


def _rring_xy(z, w, d, cy, n=18, exp=0.45):
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

    # deck frame around the 0.60 x 0.60 well, plank seam mid-front/back
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}DeckF{i}", (s * 0.23, 0.38, 0.47),
               (0.46, 0.16, 0.06), [oak_ax if i == 0 else oak_bx],
               parent=root)
        il.box(f"{PFX}DeckB{i}", (s * 0.23, -0.38, 0.47),
               (0.46, 0.16, 0.06), [oak_bx if i == 0 else oak_ax],
               parent=root)
        il.box(f"{PFX}DeckSide{i}", (s * 0.38, 0, 0.47),
               (0.16, 0.60, 0.06), [oak_ay if i == 0 else oak_by],
               parent=root)

    # well: walnut lining + floor, slightly inset
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}WellWallY{i}", (0, s * 0.30, 0.325),
               (0.60, 0.035, 0.29), [m["wood_dark"]], parent=root)
        il.box(f"{PFX}WellWallX{i}", (s * 0.30, 0, 0.325),
               (0.035, 0.60, 0.29), [m["wood_dark"]], parent=root)
    il.box(f"{PFX}WellFloor", (0, 0, 0.17), (0.60, 0.60, 0.04),
           [oak_ax], parent=root)

    # coaming rails + rolled linen pad (the cozy hatch rim)
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}CoamY{i}", (0, s * 0.315, 0.535),
               (0.73, 0.05, 0.07), [m["wood_dark"]], parent=root)
        il.box(f"{PFX}CoamX{i}", (s * 0.315, 0, 0.535),
               (0.05, 0.63, 0.07), [m["wood_dark"]], parent=root)
        il.rod(f"{PFX}PadY{i}", (-0.315, s * 0.315, 0.578),
               (0.315, s * 0.315, 0.578), 0.026, [m["linen"]],
               parent=root)
        il.rod(f"{PFX}PadX{i}", (s * 0.315, -0.315, 0.578),
               (s * 0.315, 0.315, 0.578), 0.026, [m["linen"]],
               parent=root)
    for i, (sx, sy) in enumerate(((1, 1), (1, -1), (-1, 1), (-1, -1))):
        pl.disc_ball(f"{PFX}PadCorner{i}", 0.026,
                     (sx * 0.315, sy * 0.315, 0.578), [m["linen"]],
                     parent=root, bands=5, segs=8)

    # flush spark binnacle on the front deck
    il.torus(f"{PFX}SparkRing", 0.05, 0.009, [m["brass"]],
             center=(0, 0.40, 0.503), axis='Z', segs=14, sides=6,
             parent=root)
    il.lathe(f"{PFX}Spark",
             [(0.040, 0.0), (0.042, 0.008), (0.018, 0.016)],
             [spark], segs=12, center=(0, 0.40, 0.50), parent=root)

    # ---- the capybara (shoulders up, head out of the hatch) -----------
    # body loaf, mostly hidden in the well
    body_rings = [
        _rring_xy(0.20, 0.30, 0.34, -0.02),
        _rring_xy(0.26, 0.42, 0.46, -0.02),
        _rring_xy(0.40, 0.46, 0.50, -0.02),
        _rring_xy(0.54, 0.42, 0.46, -0.02),
        _rring_xy(0.62, 0.34, 0.40, -0.02),
        _rring_xy(0.66, 0.22, 0.28, -0.02),
    ]
    pv, pf = pl.loft(body_rings)
    pl.make_object(f"{PFX}CapyBody", pv, pf, [fur], parent=root)

    # head: boxy skull, straight top sloping to a flat blunt nose,
    # deep jowls. Sections run back (-y) to nose (+y); flat end cap IS
    # the nose front.
    HZ = 0.87   # head vertical center
    head_rings = [
        _rring_xz(-0.20, 0.20, 0.24, HZ + 0.000),
        _rring_xz(-0.14, 0.27, 0.32, HZ + 0.000),
        _rring_xz(-0.04, 0.30, 0.36, HZ + 0.010),
        _rring_xz(0.06, 0.29, 0.35, HZ + 0.000),
        _rring_xz(0.12, 0.27, 0.30, HZ - 0.020),
        _rring_xz(0.20, 0.255, 0.26, HZ - 0.045),
        _rring_xz(0.26, 0.245, 0.235, HZ - 0.055),
    ]
    head_rings = [[(x, y + 0.02, z) for (x, y, z) in r] for r in head_rings]
    pv, pf = pl.loft(head_rings)
    pl.make_object(f"{PFX}CapyHead", pv, pf, [fur], parent=root)

    # nostrils: two dots on the top-front of the snout
    for i, s in enumerate((1, -1)):
        pl.disc_ball(f"{PFX}CapyNostril{i}", 0.013,
                     (s * 0.055, 0.265, HZ + 0.045), [m["ink"]],
                     parent=root, bands=4, segs=8)
        # eyes: small, high, FAR back — this placement is the capybara
        pl.disc_ball(f"{PFX}CapyEye{i}", 0.017,
                     (s * 0.147, 0.000, HZ + 0.095), [m["ink"]],
                     parent=root, bands=5, segs=10)
        # ears: little rounded flaps at the top-back corners
        il.lathe(f"{PFX}CapyEar{i}",
                 [(0.010, 0.0), (0.038, 0.020), (0.043, 0.052),
                  (0.018, 0.070)],
                 [fur], segs=10,
                 center=(s * 0.108, -0.095, HZ + 0.155), parent=root)
    # paws resting on the front coaming pad
    for i, s in enumerate((1, -1)):
        il.box(f"{PFX}CapyPaw{i}", (s * 0.14, 0.325, 0.605),
               (0.095, 0.095, 0.05), [fur_l], parent=root)

    return root
