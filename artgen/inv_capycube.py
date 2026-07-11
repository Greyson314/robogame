# artgen/inv_capycube.py — inventor study: command block with capybara pilot.
# v7: crow-style pass (reference: user's Crow_rig.blend — smooth-shaded
# subsurf blobs, satin Principled colors, dimensional glossy eye with a
# warm iris ring). The capy meshes go smooth + subsurf level 2; the fur
# drops the weave bump for a satin toy finish; the painted-facet eyes
# are replaced by geometry (gloss-black dome seated in a brown iris
# torus). Nostril slits stay facet-painted — subsurf melts them into
# soft ovals. v6 (full-facet painted face, flat-shaded) is in git if
# the style gets dialed back. Cockpit cube untouched: still paper-punk.
# Layout: 1x1x2. Bottom cell is a planked structure cube with an inset
# open-air cockpit well (coaming rail + rolled linen pad). Top cell is
# nothing but the capybara. Cyan spark = flush deck binnacle.
# Root local z: bottom cell -0.5..0.5, top cell 0.5..1.5.
#
# Facet-painting note: the nose windows below are tuned to the ring
# spec (n=34 head sections). If you change ring counts or sizes, expect
# to re-tune the windows — they select facets by center position.

import bmesh
import bpy
from math import tau, pi, cos, sin

from mathutils import Vector

import paperlib as pl
import inventorlib as il
import inv_cpu

PFX = "InvCapyCube_"

FUR = (0.23, 0.125, 0.055, 1.0)        # tawny capybara brown (linear)
FUR_LIGHT = (0.31, 0.185, 0.09, 1.0)   # paws
BLACK = (0.005, 0.005, 0.006, 1.0)     # painted nose slits
EYE = (0.008, 0.008, 0.008, 1.0)       # gloss eyeball (crow: rough 0.43 body,
IRIS = (0.152, 0.041, 0.016, 1.0)      # eye glossier; iris warm brown)


def fur_materials():
    # Crow-style satin: flat Principled color, no weave bump. The weave
    # nodes are name-idempotent and persist on re-run in a live session,
    # so an existing InvCapyFur gets its bump link torn down explicitly.
    fur = pl.get_material("InvCapyFur", FUR, roughness=0.50)
    _unweave(fur)
    fur_l = pl.get_material("InvCapyFurLight", FUR_LIGHT, roughness=0.50)
    _unweave(fur_l)
    black = pl.get_material("InvCapyBlack", BLACK, roughness=0.60)
    eye = pl.get_material("InvCapyEye", EYE, roughness=0.15)
    iris = pl.get_material("InvCapyIris", IRIS, roughness=0.55)
    return fur, fur_l, black, eye, iris


def _unweave(mat):
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        for link in list(bsdf.inputs["Normal"].links):
            mat.node_tree.links.remove(link)
    return mat


def _rring_xz(y, w, h, cz, n=34, exp=0.48, fluff=0.0, lobes=7, ph=0.0,
              extra_ts=()):
    """Rounded-rectangle ring in the XZ plane at depth y (head sections).
    fluff modulates the radius with soft lobes for the fluffy outline.
    Half-segment phase shift: one facet lands dead-center on each SIDE
    (the painted eye) and a vertex lands at top-center so the two facets
    flanking it become the painted nostril slits. extra_ts injects
    additional vertex rows at chosen angles — used to split the eye
    facet so the painted part can be shorter than a full segment."""
    pts = []
    ts = sorted([(k + 0.5) * tau / n for k in range(n)] + list(extra_ts))
    for t in ts:
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


# head ring spec: (y, w, h, cz)
HZ = 0.95   # head vertical center
HEAD_SPEC = [
    (-0.24, 0.28, 0.30, HZ - 0.010),
    (-0.16, 0.38, 0.40, HZ + 0.000),
    (-0.04, 0.42, 0.44, HZ + 0.010),
    (0.10, 0.41, 0.43, HZ + 0.000),
    (0.18, 0.37, 0.36, HZ - 0.030),
    (0.26, 0.32, 0.30, HZ - 0.055),
    (0.32, 0.28, 0.26, HZ - 0.065),
    (0.35, 0.25, 0.24, HZ - 0.068),
    (0.359, 0.21, 0.20, HZ - 0.069),
    (0.368, 0.17, 0.17, HZ - 0.070),
]


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
    return _crow(pl.make_object(name, pv, pf, mats, parent=parent))


def _crow(obj, levels=2):
    """Crow-style finish for the organic capy meshes: drop make_object's
    hairline EdgeSoften bevel (ring bands are narrower than the bevel
    width — applying it at export explodes the geometry), then smooth
    shading + a subsurf. The exporter runs use_mesh_modifiers=True, so
    the subsurf bakes into the FBX; levels/render_levels kept equal so
    viewport, render, and export all see the same mesh."""
    for mod in list(obj.modifiers):
        obj.modifiers.remove(mod)
    # weld coincident verts: il.torus closes its hoop with a duplicated
    # ring, and subsurf pulls unwelded seams open into a visible crack
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(obj.data)
    bm.free()
    for p in obj.data.polygons:
        p.use_smooth = True
    sub = obj.modifiers.new("CrowSmooth", "SUBSURF")
    sub.levels = levels
    sub.render_levels = levels
    return obj


def build(loc=(10.4, -5.0, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    m = il.materials()
    oak_ax, oak_bx = il.oak_grain('X')
    oak_ay, oak_by = il.oak_grain('Y')
    fur, fur_l, black, eye, iris = fur_materials()
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
    # reads as its continuation, not a separate ball on a neck. Crow
    # pass: rings inflated ~7% (subsurf pulls the loft inward) and the
    # fluff pushed up so the smoothed lobes read plush, not faceted.
    body_rings = [
        _rring_xy(0.20, 0.38, 0.42, -0.02, fluff=0.030, ph=0.3),
        _rring_xy(0.30, 0.54, 0.58, -0.02, fluff=0.030, ph=1.1),
        _rring_xy(0.44, 0.58, 0.62, -0.02, fluff=0.032, ph=1.9),
        _rring_xy(0.58, 0.54, 0.58, -0.02, fluff=0.032, ph=2.7),
        _rring_xy(0.70, 0.47, 0.51, -0.02, fluff=0.030, ph=3.5),
        _rring_xy(0.78, 0.37, 0.43, -0.02, fluff=0.024, ph=4.3),
    ]
    pv, pf = pl.loft(body_rings)
    _crow(pl.make_object(f"{PFX}CapyBody", pv, pf, [fur], parent=root))

    # head: wide boxy loaf sunk into the body top, sloping into a blunt
    # face plate. Rear rings carry a light fluff; face rings stay clean
    # so the painted nose windows land where computed. Head dims stay at
    # the v6 spec — the nose windows are tuned to them.
    fluff_by_ring = {0: (0.022, 0.5), 1: (0.022, 1.3), 2: (0.018, 2.1)}
    head_rings = []
    for ri, (y, w, h, cz) in enumerate(HEAD_SPEC):
        a, ph = fluff_by_ring.get(ri, (0.0, 0.0))
        head_rings.append(_rring_xz(y, w, h, cz, fluff=a, ph=ph))
    pv, pf = pl.loft(head_rings)
    # paint the nose onto the mesh: slot 0 = fur, slot 1 = black
    fmi = {}
    for n_f, f in enumerate(pf):
        cx = sum(pv[i][0] for i in f) / len(f)
        cy = sum(pv[i][1] for i in f) / len(f)
        cz = sum(pv[i][2] for i in f) / len(f)
        # nose: two vertical slit facet-pairs on the nose front, one
        # facet-column of fur left between them
        if cy > 0.345 and cz > HZ - 0.01 and 0.044 < abs(cx) < 0.064:
            fmi[n_f] = 1
    _crow(pl.make_object(f"{PFX}CapyHead", pv, pf, [fur, black],
                         face_mat_idx=fmi, parent=root))

    # eyes: crow-style dimensional — a gloss-black ball proud of the
    # head side, seated in a warm-brown iris torus (the crow's single
    # strongest style carrier). Head side surface sits near |x|=0.21 at
    # eye height; ball centre is embedded 0.02 inside it.
    for i, s in enumerate((1, -1)):
        ball = [(0.003, -0.058), (0.028, -0.051), (0.045, -0.037),
                (0.055, -0.018), (0.058, 0.0), (0.055, 0.018),
                (0.045, 0.037), (0.028, 0.051), (0.003, 0.058)]
        _crow(il.lathe(f"{PFX}CapyEye{i}", ball, [eye], segs=16, axis='X',
                       center=(s * 0.185, 0.045, HZ + 0.035), parent=root))
        _crow(il.torus(f"{PFX}CapyIris{i}", 0.054, 0.013, [iris],
                       center=(s * 0.205, 0.045, HZ + 0.035), axis='X',
                       segs=20, sides=8, parent=root))

    # ears: thin pointed flaps, notably outward-facing
    for i, s in enumerate((1, -1)):
        _ear(f"{PFX}CapyEar{i}", s, [fur], parent=root)
        # round paws resting on the front pad
        _crow(pl.disc_ball(f"{PFX}CapyPaw{i}", 0.052,
                           (s * 0.15, 0.385, 0.60), [fur_l],
                           parent=root, bands=6, segs=10))

    return root
