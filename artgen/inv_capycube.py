# artgen/inv_capycube.py — inventor study: command block with capybara pilot.
# v4 (user direction, with reference art): no applied face geometry — the
# face is PAINTED onto the head mesh via per-facet material indices
# (make_object's face_mat_idx). One pure-black facet per side = the eyes;
# two dark facet pairs flanking the top of the nose = nostril slits. The
# head widened so it reads as an extension of the body loaf (no neck
# step), both get a subtle scalloped "fluff" modulation, and the ears are
# thin, slightly pointed flaps angled notably outward.
# Layout: 1x1x2. Bottom cell is a planked structure cube with an inset
# open-air cockpit well (coaming rail + rolled linen pad). Top cell is
# nothing but the capybara. Cyan spark = flush deck binnacle.
# Root local z: bottom cell -0.5..0.5, top cell 0.5..1.5.
#
# Facet-painting note: feature windows below are tuned to the ring
# spec (n=34 head sections). If you change ring counts or sizes, expect
# to re-tune the windows — they select facets by center position.

import bpy
from math import tau, cos, sin

from mathutils import Vector

import paperlib as pl
import inventorlib as il
import inv_cpu

PFX = "InvCapyCube_"

FUR = (0.23, 0.125, 0.055, 1.0)        # tawny capybara brown (linear)
FUR_LIGHT = (0.31, 0.185, 0.09, 1.0)   # paws
BLACK = (0.005, 0.005, 0.006, 1.0)     # painted eyes / nose slits


def fur_materials():
    fur = pl.get_material("InvCapyFur", FUR, roughness=0.93)
    il._weave(fur, scale=90.0, strength=0.18)
    fur_l = pl.get_material("InvCapyFurLight", FUR_LIGHT, roughness=0.93)
    il._weave(fur_l, scale=90.0, strength=0.18)
    black = pl.get_material("InvCapyBlack", BLACK, roughness=0.75)
    return fur, fur_l, black


def _rring_xz(y, w, h, cz, n=34, exp=0.48, fluff=0.0, lobes=7, ph=0.0):
    """Rounded-rectangle ring in the XZ plane at depth y (head sections).
    fluff modulates the radius with soft lobes for the fluffy outline.
    Half-segment phase shift: one facet lands dead-center on each SIDE
    (the painted eye) and a vertex lands at top-center so the two facets
    flanking it become the painted nostril slits."""
    pts = []
    for k in range(n):
        t = (k + 0.5) * tau / n
        c, s = cos(t), sin(t)
        f = 1.0 + fluff * cos(lobes * t + ph)
        x = (abs(c) ** exp) * (1 if c >= 0 else -1) * w / 2 * f
        z = (abs(s) ** exp) * (1 if s >= 0 else -1) * h / 2 * f
        pts.append((x, y, cz + z))
    return pts


def _rring_xy(z, w, d, cy, n=24, exp=0.50, fluff=0.0, lobes=8, ph=0.0):
    """Rounded-rectangle ring in the XY plane at height z (body sections)."""
    pts = []
    for k in range(n):
        t = k * tau / n
        c, s = cos(t), sin(t)
        f = 1.0 + fluff * cos(lobes * t + ph)
        x = (abs(c) ** exp) * (1 if c >= 0 else -1) * w / 2 * f
        y = (abs(s) ** exp) * (1 if s >= 0 else -1) * d / 2 * f
        pts.append((x, cy + y, z))
    return pts


def _ear(name, s, mats, parent):
    """Thin, slightly pointed flap angled notably outward — capy ears.
    Lofted flattened ellipses along an out-and-up axis; thickness runs
    front-to-back (Y)."""
    base = Vector((s * 0.160, -0.130, 1.115))
    axis = Vector((s * 0.82, -0.12, 0.55)).normalized()
    ydir = Vector((0.0, 1.0, 0.0))
    wdir = axis.cross(ydir).normalized()
    length = 0.102
    rings = []
    for t, hw, hd in ((0.0, 0.042, 0.013), (0.35, 0.038, 0.012),
                      (0.65, 0.028, 0.010), (0.85, 0.015, 0.007),
                      (1.0, 0.003, 0.003)):
        c = base + axis * (length * t)
        rings.append([tuple(c + wdir * (cos(a * tau / 10) * hw)
                            + ydir * (sin(a * tau / 10) * hd))
                      for a in range(10)])
    pv, pf = pl.loft(rings)
    return pl.make_object(name, pv, pf, mats, parent=parent)


def build(loc=(10.4, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    oak_ax, oak_bx = il.oak_grain('X')
    oak_ay, oak_by = il.oak_grain('Y')
    fur, fur_l, black = fur_materials()
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

    # ---- the capybara ---------------------------------------------------
    # body loaf with a soft fluff scallop; wide at the top so the head
    # reads as its continuation, not a separate ball on a neck
    body_rings = [
        _rring_xy(0.20, 0.36, 0.40, -0.02, fluff=0.02, ph=0.3),
        _rring_xy(0.30, 0.50, 0.54, -0.02, fluff=0.02, ph=1.1),
        _rring_xy(0.44, 0.54, 0.58, -0.02, fluff=0.022, ph=1.9),
        _rring_xy(0.58, 0.50, 0.54, -0.02, fluff=0.022, ph=2.7),
        _rring_xy(0.70, 0.44, 0.48, -0.02, fluff=0.02, ph=3.5),
        _rring_xy(0.78, 0.34, 0.40, -0.02, fluff=0.016, ph=4.3),
    ]
    pv, pf = pl.loft(body_rings)
    pl.make_object(f"{PFX}CapyBody", pv, pf, [fur], parent=root)

    # head: wide boxy loaf sunk into the body top, sloping into a blunt
    # face plate. Rear rings carry a light fluff; face rings stay clean
    # so the painted-facet windows land where computed.
    HZ = 0.95   # head vertical center
    head_rings = [
        _rring_xz(-0.24, 0.28, 0.30, HZ - 0.010, fluff=0.015, ph=0.5),
        _rring_xz(-0.16, 0.38, 0.40, HZ + 0.000, fluff=0.015, ph=1.3),
        _rring_xz(-0.04, 0.42, 0.44, HZ + 0.010, fluff=0.012, ph=2.1),
        _rring_xz(0.10, 0.41, 0.43, HZ + 0.000),
        _rring_xz(0.18, 0.37, 0.36, HZ - 0.030),
        _rring_xz(0.26, 0.32, 0.30, HZ - 0.055),
        _rring_xz(0.32, 0.28, 0.26, HZ - 0.065),
        _rring_xz(0.35, 0.25, 0.24, HZ - 0.068),
        _rring_xz(0.359, 0.21, 0.20, HZ - 0.069),
        _rring_xz(0.368, 0.17, 0.17, HZ - 0.070),
    ]
    pv, pf = pl.loft(head_rings)
    # paint the face onto the mesh: slot 0 = fur, slot 1 = black
    fmi = {}
    for n_f, f in enumerate(pf):
        cx = sum(pv[i][0] for i in f) / len(f)
        cy = sum(pv[i][1] for i in f) / len(f)
        cz = sum(pv[i][2] for i in f) / len(f)
        # eyes: the flat side facet at mid-skull, one per side
        if abs(cx) > 0.19 and -0.05 < cy < 0.11 \
                and abs(cz - (HZ + 0.005)) < 0.045:
            fmi[n_f] = 1
        # nose: two vertical slit facet-pairs on the nose front, one
        # facet-column of fur left between them
        if cy > 0.345 and cz > HZ - 0.01 and 0.044 < abs(cx) < 0.064:
            fmi[n_f] = 1
    pl.make_object(f"{PFX}CapyHead", pv, pf, [fur, black],
                   face_mat_idx=fmi, parent=root)

    # ears: thin pointed flaps, notably outward-facing
    for i, s in enumerate((1, -1)):
        _ear(f"{PFX}CapyEar{i}", s, [fur], parent=root)
        # round paws resting on the front pad
        pl.disc_ball(f"{PFX}CapyPaw{i}", 0.052,
                     (s * 0.15, 0.385, 0.60), [fur_l],
                     parent=root, bands=6, segs=10)

    return root
