# artgen/inventorlib.py — shared helpers for the inventor-aesthetic studies
# (docs/research/inventor-aesthetic.md). Language: wood spars + linen
# membrane on movement blocks ("skeletal where it moves"), laminated wood
# volumes on solid blocks, brass fittings + hemp cord as the mechanism
# read, vermilion kept for projectile/thrust channels. Builds on paperlib
# for loft/make_object/card_panel; adds turned-and-rigged geometry.

import bpy
from math import radians, cos, sin, tau, pi
from mathutils import Vector

import paperlib as pl

# Colors are LINEAR (Principled base color) — picked so the sRGB result
# reads right in material preview; don't eyeball them as hex paint values.
# Session-132 revision: woods darkened a full step on user review.
WOOD = (0.24, 0.13, 0.055, 1.0)       # oiled oak — members, turned parts
WOOD_DARK = (0.062, 0.028, 0.013, 1.0)  # dark walnut — frames, boards, masts
LINEN = (0.87, 0.81, 0.66, 1.0)       # stretched membrane, wraps
CORD = (0.40, 0.28, 0.13, 1.0)        # hemp rigging, wound-rope tires
IRON = (0.085, 0.082, 0.095, 1.0)     # small dark fittings
INK = (0.045, 0.05, 0.07, 1.0)        # intake holes, dark interiors


def _weave(mat, scale=170.0, strength=0.14):
    """Cloth-grain bump on a Principled material — the linen 'a bit of
    texture' pass. Idempotent: nodes are found by name on re-run."""
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if bsdf is None:
        return mat
    noise = nt.nodes.get("WeaveNoise")
    if noise is None:
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.name = "WeaveNoise"
        noise.location = (-500, -200)
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 3.0
    noise.inputs["Roughness"].default_value = 0.55
    bump = nt.nodes.get("WeaveBump")
    if bump is None:
        bump = nt.nodes.new("ShaderNodeBump")
        bump.name = "WeaveBump"
        bump.location = (-250, -200)
    bump.inputs["Strength"].default_value = strength
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


# Structure-cube oak: the committed direction (session 132 cube call) —
# continuous planking, "slightly darker than mid", grain along the plank.
OAK_A = (0.235, 0.130, 0.056, 1.0)
OAK_B = (0.200, 0.110, 0.047, 1.0)


def _grain(mat, tone, axis='X'):
    """Directional wood grain: stretched noise drives a two-stop ramp
    around the base tone plus a gentle bump. Idempotent by node name.
    axis = the plank's long axis (grain runs along it)."""
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if bsdf is None:
        return mat

    def node(name, kind, loc):
        n = nt.nodes.get(name)
        if n is None:
            n = nt.nodes.new(kind)
            n.name = name
            n.location = loc
        return n

    co = node("GrainCo", "ShaderNodeTexCoord", (-900, -100))
    mp = node("GrainMap", "ShaderNodeMapping", (-720, -100))
    mp.inputs["Scale"].default_value = \
        (3.0, 26.0, 26.0) if axis == 'X' else (26.0, 3.0, 26.0)
    noise = node("GrainNoise", "ShaderNodeTexNoise", (-540, -100))
    noise.inputs["Scale"].default_value = 1.0
    noise.inputs["Detail"].default_value = 2.5
    noise.inputs["Roughness"].default_value = 0.5
    ramp = node("GrainRamp", "ShaderNodeValToRGB", (-360, -100))
    ramp.color_ramp.elements[0].position = 0.25
    ramp.color_ramp.elements[0].color = \
        (tone[0] * 1.06, tone[1] * 1.06, tone[2] * 1.06, 1.0)
    ramp.color_ramp.elements[1].position = 0.75
    ramp.color_ramp.elements[1].color = \
        (tone[0] * 0.72, tone[1] * 0.70, tone[2] * 0.68, 1.0)
    bump = node("GrainBump", "ShaderNodeBump", (-360, -380))
    bump.inputs["Strength"].default_value = 0.05
    nt.links.new(co.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], noise.inputs["Vector"])
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def oak_grain(axis='X'):
    """Two-tone grained plank pair for the given plank direction."""
    a = _grain(pl.get_material(f"InvOakA{axis}", OAK_A, roughness=0.78),
               OAK_A, axis)
    b = _grain(pl.get_material(f"InvOakB{axis}", OAK_B, roughness=0.78),
               OAK_B, axis)
    return a, b


def materials():
    m = pl.materials()  # white, kraft, brass, channel, gray
    m.update({
        "wood": pl.get_material("InvWood", WOOD, roughness=0.75),
        "wood_dark": pl.get_material("InvWoodDark", WOOD_DARK, roughness=0.8),
        "linen": _weave(pl.get_material("InvLinen", LINEN, roughness=0.95)),
        "cord": pl.get_material("InvCord", CORD, roughness=0.95),
        "iron": pl.get_material("InvIron", IRON, roughness=0.55),
        "ink": pl.get_material("InvInk", INK, roughness=0.6),
    })
    return m


def root_empty(name, loc):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.2
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    return e


def box(name, center, size, mats, parent=None):
    cx, cy, cz = center
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
    lo = [(cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
          (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz)]
    hi = [(x, y, cz + sz) for x, y, _ in lo]
    pv, pf = pl.loft([lo, hi])
    return pl.make_object(name, pv, pf, mats, parent=parent)


def rod(name, p0, p1, r, mats, sides=8, r1=None, parent=None):
    """Straight dowel between two points; r1 tapers the far end."""
    p0, p1 = Vector(p0), Vector(p1)
    t = (p1 - p0).normalized()
    up = Vector((0, 0, 1)) if abs(t.dot(Vector((0, 0, 1)))) < 0.95 \
        else Vector((1, 0, 0))
    x = t.cross(up).normalized()
    y = t.cross(x).normalized()
    rb = r if r1 is None else r1
    ring0 = [tuple(p0 + (x * cos(k * tau / sides) + y * sin(k * tau / sides)) * r)
             for k in range(sides)]
    ring1 = [tuple(p1 + (x * cos(k * tau / sides) + y * sin(k * tau / sides)) * rb)
             for k in range(sides)]
    pv, pf = pl.loft([ring0, ring1])
    return pl.make_object(name, pv, pf, mats, parent=parent)


def lathe(name, profile, mats, segs=24, axis='Z', center=(0, 0, 0),
          close=True, parent=None):
    """Revolve a (radius, height) profile around an axis through center."""
    cx, cy, cz = center
    rings = []
    for r, h in profile:
        rr = max(r, 0.003)
        if axis == 'Z':
            ring = [(cx + rr * cos(k * tau / segs), cy + rr * sin(k * tau / segs),
                     cz + h) for k in range(segs)]
        elif axis == 'Y':
            ring = [(cx + rr * cos(k * tau / segs), cy + h,
                     cz + rr * sin(k * tau / segs)) for k in range(segs)]
        else:
            ring = [(cx + h, cy + rr * cos(k * tau / segs),
                     cz + rr * sin(k * tau / segs)) for k in range(segs)]
        rings.append(ring)
    pv, pf = pl.loft(rings, cap_start=close, cap_end=close)
    return pl.make_object(name, pv, pf, mats, parent=parent)


def torus(name, R, r, mats, center=(0, 0, 0), axis='X', segs=28, sides=8,
          parent=None):
    """Hoop around an axis — brass bands, rope windings, collar rings."""
    c0 = Vector(center)
    rings = []
    for i in range(segs + 1):
        a = (i % segs) * tau / segs
        if axis == 'X':
            c = c0 + Vector((0, R * cos(a), R * sin(a)))
            radial = Vector((0, cos(a), sin(a)))
            axial = Vector((1, 0, 0))
        elif axis == 'Y':
            c = c0 + Vector((R * cos(a), 0, R * sin(a)))
            radial = Vector((cos(a), 0, sin(a)))
            axial = Vector((0, 1, 0))
        else:
            c = c0 + Vector((R * cos(a), R * sin(a), 0))
            radial = Vector((cos(a), sin(a), 0))
            axial = Vector((0, 0, 1))
        rings.append([tuple(c + radial * cos(k * tau / sides) * r
                            + axial * sin(k * tau / sides) * r)
                      for k in range(sides)])
    pv, pf = pl.loft(rings, cap_start=False, cap_end=False)
    return pl.make_object(name, pv, pf, mats, parent=parent)


def sweep(name, path, r, mats, sides=6, parent=None):
    """Constant-radius sweep along a 3D polyline — spiral battens, curved
    grips. Frames from tangent + global-Z fallback."""
    pts = [Vector(p) for p in path]
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, n - 1)]
        t = (b - a).normalized()
        up = Vector((0, 0, 1)) if abs(t.dot(Vector((0, 0, 1)))) < 0.95 \
            else Vector((1, 0, 0))
        x = t.cross(up).normalized()
        y = t.cross(x).normalized()
        rings.append([tuple(p + (x * cos(k * tau / sides)
                                 + y * sin(k * tau / sides)) * r)
                      for k in range(sides)])
    pv, pf = pl.loft(rings)
    return pl.make_object(name, pv, pf, mats, parent=parent)


def ribbon(name, rows, thickness, mats, parent=None):
    """Thin stretched sheet from a grid of rows (equal-length 3D point
    lists). Each row becomes a closed ring by appending the reversed row
    dropped by `thickness` — membrane with visible edge thickness."""
    rings = []
    for row in rows:
        bottom = [(x, y, z - thickness) for (x, y, z) in reversed(row)]
        rings.append([tuple(p) for p in row] + bottom)
    pv, pf = pl.loft(rings)
    return pl.make_object(name, pv, pf, mats, parent=parent)


def arc_seg_x(name, r_out, wall, x0, x1, a0_deg, a1_deg, segs, mats,
              center=(0.0, 0.0), parent=None):
    """paperlib.arc_shell rotated to sweep along X; profile in (y, z).
    Wheel felloes, drum staves."""
    a0, a1 = radians(a0_deg), radians(a1_deg)
    angs = [a0 + (a1 - a0) * i / segs for i in range(segs + 1)]
    cy, cz = center
    prof = [(cy + cos(a) * r_out, cz + sin(a) * r_out) for a in angs]
    prof += [(cy + cos(a) * (r_out - wall), cz + sin(a) * (r_out - wall))
             for a in reversed(angs)]
    rings = [[(x0, y, z) for y, z in prof], [(x1, y, z) for y, z in prof]]
    pv, pf = pl.loft(rings)
    return pl.make_object(name, pv, pf, mats, parent=parent)


def frame_view(center, distance, shading='MATERIAL'):
    """Point every 3D viewport at a study and switch shading so
    screenshots are material-lit and framed."""
    from mathutils import Euler
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type != 'VIEW_3D':
                continue
            space = area.spaces.active
            space.shading.type = shading
            r3d = space.region_3d
            r3d.view_perspective = 'PERSP'
            r3d.view_location = Vector(center)
            r3d.view_rotation = Euler(
                (radians(65), 0.0, radians(35)), 'XYZ').to_quaternion()
            r3d.view_distance = distance
