# artgen/paperlib.py — shared helpers for the paper-punk asset family.
# Not a Blender addon; plain functions imported by the weapon scripts
# (they sys.path-insert this folder). Language: white card faces, kraft
# cut edges, brass paper-fastener brads, vermilion "printed ink" accents.

import bpy
import bmesh
import os
from math import radians, cos, sin, tau
from mathutils import Vector

PAPER_WHITE = (0.93, 0.91, 0.85, 1.0)
KRAFT = (0.52, 0.36, 0.20, 1.0)
BRASS = (0.75, 0.58, 0.22, 1.0)
CHANNEL = (0.86, 0.20, 0.10, 1.0)
REF_GRAY = (0.35, 0.35, 0.38, 1.0)


def get_material(name, color, roughness=0.65):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def materials():
    return {
        "white": get_material("PaperWhite", PAPER_WHITE),
        "kraft": get_material("PaperKraft", KRAFT, roughness=0.85),
        "brass": get_material("PaperBrass", BRASS, roughness=0.35),
        "channel": get_material("PaperChannel", CHANNEL),
        "gray": get_material("RefGray", REF_GRAY, roughness=0.9),
    }


def clear_objects(prefixes=(), names=()):
    doomed = [o.name for o in bpy.data.objects
              if (prefixes and o.name.startswith(tuple(prefixes)))
              or o.name in names]
    for name in doomed:
        obj = bpy.data.objects.get(name)
        if obj:
            bpy.data.objects.remove(obj, do_unlink=True)


def hide_default_cube():
    cube = bpy.data.objects.get("Cube")
    if cube:
        cube.hide_set(True)
        cube.hide_render = True


def make_object(name, verts, faces, mats, face_mat_idx=None, parent=None):
    """Flat-shaded mesh with recalculated normals and a hairline bevel for
    crisp cut-edge highlights."""
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


def loft(rings, cap_start=True, cap_end=True):
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


def card_panel(name, profile, thickness, axis, offset, mats,
               cap_slots=(0, 0), edge_slot=1, parent=None):
    """The paper-punk workhorse: a 2D silhouette extruded into a thin card
    sheet. Caps are the card faces, the rim is the cut edge.
    profile: 2D points in the plane perpendicular to axis —
    'X' -> (y, z), 'Y' -> (x, z), 'Z' -> (x, y)."""
    lo, hi = offset - thickness / 2, offset + thickness / 2
    if axis == 'X':
        rings = [[(w, a, b) for a, b in profile] for w in (lo, hi)]
    elif axis == 'Y':
        rings = [[(a, w, b) for a, b in profile] for w in (lo, hi)]
    else:
        rings = [[(a, b, w) for a, b in profile] for w in (lo, hi)]
    pv, pf = loft(rings)
    n = len(profile)
    fmi = {j: edge_slot for j in range(n)}
    fmi[n] = cap_slots[0]
    fmi[n + 1] = cap_slots[1]
    return make_object(name, pv, pf, mats, face_mat_idx=fmi, parent=parent)


def ngon_pts(n, r, cx=0.0, cy=0.0, phase=0.0):
    return [(cx + r * cos(phase + i * tau / n), cy + r * sin(phase + i * tau / n))
            for i in range(n)]


def arc_shell(name, r_out, wall, y0, y1, a0_deg, a1_deg, segs, mats,
              slots=(0, 1, 2), center=(0.0, 0.0), parent=None):
    """Rolled-card tube shell swept along Y between two arc angles.
    slots = (outer surface, cut edges incl. end rings, inner surface).
    center = (x, z) of the tube axis. A partial arc leaves an open groove;
    a near-full arc leaves the rolled sheet's seam."""
    a0, a1 = radians(a0_deg), radians(a1_deg)
    angs = [a0 + (a1 - a0) * i / segs for i in range(segs + 1)]
    cx, cz = center
    prof = [(cx + cos(a) * r_out, cz + sin(a) * r_out) for a in angs]
    prof += [(cx + cos(a) * (r_out - wall), cz + sin(a) * (r_out - wall))
             for a in reversed(angs)]
    rings = [[(x, y, z) for x, z in prof] for y in (y0, y1)]
    pv, pf = loft(rings)
    n = len(prof)
    fmi = {}
    for j in range(n):
        if j < segs:
            fmi[j] = slots[0]
        elif j == segs or j == n - 1:
            fmi[j] = slots[1]
        else:
            fmi[j] = slots[2]
    fmi[n] = slots[1]
    fmi[n + 1] = slots[1]
    return make_object(name, pv, pf, mats, face_mat_idx=fmi, parent=parent)


def tube(name, r_out, wall, y0, y1, segs, mats, slots=(0, 1, 2),
         center=(0.0, 0.0), parent=None):
    """Full rolled tube with a 3° seam slit at the bottom — where the rolled
    sheet's edges meet. The seam is a feature, not a gap."""
    return arc_shell(name, r_out, wall, y0, y1, -88.5, 268.5, segs, mats,
                     slots=slots, center=center, parent=parent)


def gear_profile(teeth, r_root, r_out):
    pts = []
    for i in range(teeth):
        a = i * tau / teeth
        step = tau / teeth
        pts += [
            (r_root * cos(a), r_root * sin(a)),
            (r_out * cos(a + 0.12 * step), r_out * sin(a + 0.12 * step)),
            (r_out * cos(a + 0.38 * step), r_out * sin(a + 0.38 * step)),
            (r_root * cos(a + 0.50 * step), r_root * sin(a + 0.50 * step)),
        ]
    return pts


def brad(name, x_in, x_out, r, z, brass, parent=None):
    """Brass paper-fastener head on the pitch axis. x signs give the side."""
    hex_pts = [(r * cos(i * tau / 6), r * sin(i * tau / 6)) for i in range(6)]
    ring_in = [(x_in, y, z + zz) for y, zz in hex_pts]
    ring_out = [(x_out, y, z + zz) for y, zz in hex_pts]
    pv, pf = loft([ring_in, ring_out])
    return make_object(name, pv, pf, [brass], parent=parent)


def ref_block(name, gray, parent):
    """1 m reference block the weapon mounts on. Excluded from export."""
    quad = ((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5))
    pv, pf = loft([[(x, y, -1.0) for x, y in quad],
                   [(x, y, 0.0) for x, y in quad]])
    return make_object(name, pv, pf, [gray], parent=parent)


def export_tree(root, path, yoke=None, muzzle=None):
    """Export root's tree as FBX, renaming yoke/muzzle to the WeaponModelRig
    convention names (Turret / ShootPoint) for the duration of the export.
    In-scene the empties keep per-weapon names, since Blender object names
    are global and three weapons coexist in the scene."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    renames = []

    def force_name(obj, want):
        clash = bpy.data.objects.get(want)
        if clash and clash is not obj:
            renames.append((clash, clash.name))
            clash.name = want + ".tmp"
        renames.append((obj, obj.name))
        obj.name = want

    if yoke is not None:
        force_name(yoke, "Turret")
    if muzzle is not None:
        force_name(muzzle, "ShootPoint")

    saved_loc = root.location.copy()
    root.location = (0.0, 0.0, 0.0)
    bpy.ops.object.select_all(action="DESELECT")

    def sel(o):
        if o.name.endswith("RefBlock"):
            return
        o.select_set(True)
        for c in o.children:
            sel(c)

    sel(root)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        add_leaf_bones=False,
    )
    root.location = saved_loc
    for obj, name in reversed(renames):
        obj.name = name
    print(f"{root.name} exported -> {path}")
