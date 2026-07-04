# artgen/smg_paperpunk.py — paper-punk SMG turret.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic + idempotent: re-running replaces the previous build.
#
# Contract with WeaponModelRig (session 120 convention):
#   root "SMG_Paper"            — yaws with the block
#   └─ empty "Turret"           — pitch yoke pivot
#      └─ empty "ShootPoint"    — muzzle
#
# Paper-punk language (supersedes the origami/fold experiment): the design
# is a machine, the fabrication material is paper. Tells: parts read as cut
# sheets with visible thickness, laminated layers, kraft-brown cut edges on
# white card faces, a gear cut from card, brass paper-fastener brads at the
# pitch joint. No fold/crease storytelling.

import bpy
import bmesh
import os
from math import radians, cos, sin, tau
from mathutils import Euler, Vector

DO_EXPORT = True  # look approved (paper-punk, rolled-tube barrel) — session 131
EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons\SMG_Paper.fbx"

# ---------------------------------------------------------------- palette --
PAPER_WHITE = (0.93, 0.91, 0.85, 1.0)   # card stock face
KRAFT = (0.52, 0.36, 0.20, 1.0)         # cut cardboard edge
BRASS = (0.75, 0.58, 0.22, 1.0)         # paper-fastener brads
CHANNEL = (0.86, 0.20, 0.10, 1.0)       # rail groove interior
REF_GRAY = (0.35, 0.35, 0.38, 1.0)

# ------------------------------------------------------------- dimensions --
# Forward = -Y (Blender convention), up = +Z. Units: meters. Block = 1 m cube.
PIVOT_Z = 0.52          # pitch axis height above block top face
BASE_PLATE = 0.72       # card base sheet, square
GEAR_Z0 = 0.05          # laminated yaw gear sits on the base sheet
GEAR_TOP = 0.145        # (3 laminate layers, see build below)
RAIL_ROOT_Y = -0.77     # barrel plugs into the laminated collar
RAIL_TIP_Y = -1.22      # open muzzle end
BARREL_R = 0.055        # rolled-tube outer radius
SHELL_T = 0.016         # rolled card wall thickness
GAP_DEG = 16.0          # half-angle of the open groove slot
ARC_SEGS = 10           # facets per half-shell arc


def _get_material(name, color, roughness=0.65):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def _make_object(name, verts, faces, mats, face_mat_idx=None, parent=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([Vector(v) for v in verts], [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    for m in mats:
        obj.data.materials.append(m)
    if face_mat_idx:
        for fi, slot in face_mat_idx.items():
            mesh.polygons[fi].material_index = slot
    for p in mesh.polygons:
        p.use_smooth = False
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    bev = obj.modifiers.new("EdgeSoften", "BEVEL")
    bev.width = 0.003
    bev.segments = 1
    bev.limit_method = "ANGLE"
    bev.angle_limit = radians(25)
    return obj


def _loft(rings, cap_start=True, cap_end=True):
    n = len(rings[0])
    verts = [v for ring in rings for v in ring]
    faces = []
    for r in range(len(rings) - 1):
        a, b = r * n, (r + 1) * n
        for j in range(n):
            k = (j + 1) % n
            faces.append((a + j, a + k, b + k, b + j))
    if cap_start:
        faces.append(tuple(reversed(range(n))))
    if cap_end:
        base = (len(rings) - 1) * n
        faces.append(tuple(base + j for j in range(n)))
    return verts, faces


def _card_panel(name, profile, thickness, axis, offset, mats,
                cap_slots=(0, 0), edge_slot=1, parent=None):
    """The paper-punk workhorse: a 2D silhouette extruded into a thin card
    sheet. Caps are the card faces, the rim is the cut edge (kraft).
    profile: 2D points in the plane perpendicular to axis —
    'X' -> (y, z), 'Y' -> (x, z), 'Z' -> (x, y)."""
    lo, hi = offset - thickness / 2, offset + thickness / 2
    if axis == 'X':
        rings = [[(w, a, b) for a, b in profile] for w in (lo, hi)]
    elif axis == 'Y':
        rings = [[(a, w, b) for a, b in profile] for w in (lo, hi)]
    else:
        rings = [[(a, b, w) for a, b in profile] for w in (lo, hi)]
    pv, pf = _loft(rings)
    n = len(profile)
    fmi = {j: edge_slot for j in range(n)}
    fmi[n] = cap_slots[0]
    fmi[n + 1] = cap_slots[1]
    return _make_object(name, pv, pf, mats, face_mat_idx=fmi, parent=parent)


def _ngon_pts(n, r, cx=0.0, cy=0.0, phase=0.0):
    return [(cx + r * cos(phase + i * tau / n), cy + r * sin(phase + i * tau / n))
            for i in range(n)]


# ------------------------------------------------------------ scene reset --
for name in [o.name for o in bpy.data.objects
             if o.name.startswith(("SMG_", "SMG_Origami", "SMG_Paper"))
             or o.name in ("Turret", "ShootPoint", "RefBlock")]:
    obj = bpy.data.objects.get(name)
    if obj:
        bpy.data.objects.remove(obj, do_unlink=True)

cube = bpy.data.objects.get("Cube")  # default cube shares the build origin
if cube:
    cube.hide_set(True)
    cube.hide_render = True

white = _get_material("PaperWhite", PAPER_WHITE)
kraft = _get_material("PaperKraft", KRAFT, roughness=0.85)
brass = _get_material("PaperBrass", BRASS, roughness=0.35)
channel = _get_material("PaperChannel", CHANNEL)
gray = _get_material("RefGray", REF_GRAY, roughness=0.9)

# ------------------------------------------------------------------ root --
root = bpy.data.objects.new("SMG_Paper", None)
root.empty_display_size = 0.1
bpy.context.scene.collection.objects.link(root)

# reference block the weapon mounts on (not exported)
rv, rf = _loft([
    [(x, y, -1.0) for x, y in ((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5))],
    [(x, y, 0.0) for x, y in ((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5))],
])
_make_object("RefBlock", rv, rf, [gray], parent=root)

# base sheet: one thick card square
h = BASE_PLATE / 2
_card_panel("SMG_BasePlate", [(-h, -h), (h, -h), (h, h), (-h, h)],
            0.05, 'Z', 0.025, [white, kraft], parent=root)

# ------------------------------------------------- yaw gear (3 laminates) --
# middle layer is a card-cut gear; plain discs above and below. The exposed
# teeth say "this ring turns".
gear_profile = []
for i in range(10):
    a = i * tau / 10
    step = tau / 10
    gear_profile += [
        (0.26 * cos(a), 0.26 * sin(a)),
        (0.32 * cos(a + 0.12 * step), 0.32 * sin(a + 0.12 * step)),
        (0.32 * cos(a + 0.38 * step), 0.32 * sin(a + 0.38 * step)),
        (0.26 * cos(a + 0.50 * step), 0.26 * sin(a + 0.50 * step)),
    ]
_card_panel("SMG_GearBottom", _ngon_pts(12, 0.29), 0.03, 'Z', GEAR_Z0 + 0.015,
            [white, kraft], parent=root)
_card_panel("SMG_GearTeeth", gear_profile, 0.035, 'Z', GEAR_Z0 + 0.0475,
            [white, kraft], parent=root)
_card_panel("SMG_GearTop", _ngon_pts(12, 0.24), 0.03, 'Z', GEAR_Z0 + 0.08,
            [white, kraft], parent=root)

# ------------------------------------------- gusset brackets + pitch brads --
# each side of the yoke is an A-frame of two card struts — open middle so
# daylight shows through the mount and the mechanism reads as assembled
# parts, not a solid wall. Brass brad heads pin the pitch joint outside.
strut_front = [(-0.25, GEAR_TOP), (-0.15, GEAR_TOP),
               (-0.005, PIVOT_Z + 0.045), (-0.075, PIVOT_Z + 0.045)]
strut_rear = [(0.15, GEAR_TOP), (0.25, GEAR_TOP),
              (0.075, PIVOT_Z + 0.045), (0.005, PIVOT_Z + 0.045)]
for sign, side in ((1, "R"), (-1, "L")):
    _card_panel(f"SMG_StrutFront{side}", strut_front, 0.022, 'X', sign * 0.10,
                [white, kraft], parent=root)
    _card_panel(f"SMG_StrutRear{side}", strut_rear, 0.022, 'X', sign * 0.10,
                [white, kraft], parent=root)
# spacer bar keeping the gussets true (a glued card box) — placed low and
# rearward so the receiver clears it through the full pitch sweep
_card_panel("SMG_GussetSpacer", [(0.14, GEAR_TOP), (0.24, GEAR_TOP),
                                 (0.24, GEAR_TOP + 0.10), (0.14, GEAR_TOP + 0.10)],
            0.178, 'X', 0.0, [white, kraft], parent=root)
for sign, name in ((1, "SMG_BradR"), (-1, "SMG_BradL")):
    hex_pts = [(0.05 * cos(i * tau / 6), 0.05 * sin(i * tau / 6))
               for i in range(6)]
    ring_in = [(sign * 0.111, y, PIVOT_Z + z) for y, z in hex_pts]
    ring_out = [(sign * 0.132, y, PIVOT_Z + z) for y, z in hex_pts]
    pv, pf = _loft([ring_in, ring_out])
    _make_object(name, pv, pf, [brass], parent=root)

# ---------------------------------------------------------------- turret --
turret = bpy.data.objects.new("Turret", None)
turret.empty_display_size = 0.08
bpy.context.scene.collection.objects.link(turret)
turret.parent = root
turret.location = (0, 0, PIVOT_Z)

# receiver: laminated card box — thick core, proud outer side plates with a
# stepped silhouette so the layering reads at a glance
core_profile = [(0.30, -0.11), (0.30, 0.08), (0.22, 0.14), (-0.40, 0.14),
                (-0.55, 0.05), (-0.55, -0.03), (-0.30, -0.11)]
_card_panel("SMG_ReceiverCore", core_profile, 0.13, 'X', 0.0,
            [white, kraft], parent=turret)
side_profile = [(0.33, -0.09), (0.33, 0.06), (0.25, 0.12), (-0.36, 0.12),
                (-0.47, 0.02), (-0.47, -0.06), (-0.26, -0.09)]
for sign, name in ((1, "SMG_SidePlateR"), (-1, "SMG_SidePlateL")):
    _card_panel(name, side_profile, 0.02, 'X', sign * 0.075,
                [white, kraft], parent=turret)

# magazine: raked card box under the receiver, printed-ink red faces so the
# ammo read survives at gameplay distance
mag_profile = [(-0.10, -0.10), (-0.26, -0.10), (-0.30, -0.34), (-0.14, -0.34)]
_card_panel("SMG_Magazine", mag_profile, 0.09, 'X', 0.0,
            [white, kraft, channel], cap_slots=(2, 2), parent=turret)

# collar: laminated card discs bridging receiver nose -> barrel (glued stack)
disc_y = -0.57
for i in range(6):
    r = 0.075 if i % 2 == 0 else 0.055
    m = [white, kraft] if i % 2 == 0 else [kraft, kraft]
    _card_panel(f"SMG_Collar{i}", _ngon_pts(12, r, cy=-0.01, phase=tau / 24),
                0.036, 'Y', disc_y - i * 0.044, m, parent=turret)

# barrel: a rolled-card tube split horizontally into two half-shells, so an
# open groove runs down the middle and reads in profile. Outer face is
# white card, the channel interior is printed red, and every cut edge —
# groove lips and the open muzzle ring — is kraft.
for top, name in ((True, "SMG_BarrelTop"), (False, "SMG_BarrelBottom")):
    a0, a1 = radians(GAP_DEG), tau / 2 - radians(GAP_DEG)
    angs = [a0 + (a1 - a0) * i / ARC_SEGS for i in range(ARC_SEGS + 1)]
    if not top:
        angs = [-a for a in angs]
    prof = [(cos(a) * BARREL_R, -0.01 + sin(a) * BARREL_R) for a in angs]
    prof += [(cos(a) * (BARREL_R - SHELL_T), -0.01 + sin(a) * (BARREL_R - SHELL_T))
             for a in reversed(angs)]
    rings = [[(x, y, z) for x, z in prof] for y in (RAIL_ROOT_Y, RAIL_TIP_Y)]
    pv, pf = _loft(rings)
    n = len(prof)
    fmi = {}
    for j in range(n):
        if j < ARC_SEGS:
            fmi[j] = 0                      # outer: rolled card
        elif j == ARC_SEGS or j == n - 1:
            fmi[j] = 1                      # groove lips: cut edge
        else:
            fmi[j] = 2                      # channel interior
    fmi[n] = 1                              # end caps: annular cut edge
    fmi[n + 1] = 1
    _make_object(name, pv, pf, [white, kraft, channel], face_mat_idx=fmi,
                 parent=turret)

# sight: a little card tab slotted into the receiver top
_card_panel("SMG_Sight", [(-0.34, 0.13), (-0.41, 0.13), (-0.41, 0.21), (-0.36, 0.21)],
            0.016, 'X', 0.0, [white, kraft], parent=turret)

# muzzle marker, inside the open groove
shoot = bpy.data.objects.new("ShootPoint", None)
shoot.empty_display_size = 0.05
bpy.context.scene.collection.objects.link(shoot)
shoot.parent = turret
shoot.location = (0, RAIL_TIP_Y + 0.04, -0.01)

# ---------------------------------------------------------------- export --
if DO_EXPORT:
    os.makedirs(os.path.dirname(EXPORT_PATH), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    skip = {"RefBlock"}
    def _select_tree(o):
        if o.name in skip:
            return
        o.select_set(True)
        for c in o.children:
            _select_tree(c)
    _select_tree(root)
    bpy.ops.export_scene.fbx(
        filepath=EXPORT_PATH,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        add_leaf_bones=False,
    )
    print(f"SMG_Paper exported -> {EXPORT_PATH}")

print("SMG_Paper built.")
