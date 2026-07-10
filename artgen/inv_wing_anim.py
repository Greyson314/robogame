# artgen/inv_wing_anim.py — animated study: bat-wing aerofoil "swimming".
# Builds a fresh copy of the inv_wing bat-wing study, joins it into one
# mesh, rigs a 3-bone chain from the root boss out along the mid-fan
# direction, and keys a looping traveling-wave flap: root leads, tip
# lags, with a quarter-cycle feathering twist so the membrane sculls
# instead of just hinging. Reads mechanical because the chain is three
# rigid panels with narrow hinge blends, not a smooth spline.
# The root boss / mount / whipping are pinned to a static Base bone —
# the attach point never moves.
# NOTE: prefix is InvSwim_ (not InvWing*_) because inv_wing.build()
# clears every InvWing_* object and would otherwise eat this copy.
# Loop: 48 frames @ 24 fps (frame 49 key == frame 1). Bakes clean to
# FBX if this ever graduates from study to shipped animation.

from math import cos, hypot, radians, sin, tau

import bpy
from mathutils import Vector

import paperlib as pl
import inv_wing

PFX = "InvSwim_"

PIVOT_2D = inv_wing.ORIGIN          # fan origin = attach point (root-local)
CHAIN_ANG = radians(-46.0)          # mid-fan direction the chain runs along
JOINTS = [0.0, 0.5, 1.05, 1.75]     # hinge radii from the pivot
BLEND = 0.09                        # half-width of each hinge's weight blend
PINNED = ("Boss", "Mount", "Whip")  # root hardware: full Base weight

CYCLE = 48
# per chain bone: flap amplitude (rad), flap phase lag (rad), twist amp
WAVE = {"Flap0": (0.26, 0.0, 0.05),
        "Flap1": (0.20, 0.9, 0.10),
        "Flap2": (0.26, 1.8, 0.16)}
ASYM = 0.22          # 2nd harmonic: quick power stroke, slower recovery
TW_LAG = tau / 4     # twist trails the flap — sculling, not screwing


def _chain_weights(r):
    """Hat weights over the 3 chain segments, narrow blend at hinges."""
    w = []
    for k in range(3):
        lo, hi = JOINTS[k], JOINTS[k + 1]
        up = 1.0 if k == 0 else \
            min(1.0, max(0.0, (r - (lo - BLEND)) / (2 * BLEND)))
        dn = 1.0 if k == 2 else \
            min(1.0, max(0.0, ((hi + BLEND) - r) / (2 * BLEND)))
        w.append(min(up, dn))
    s = sum(w)
    return [x / s for x in w] if s > 0 else [0.0, 0.0, 1.0]


def build(loc=(0.0, -7.5, 0.5)):
    pl.clear_objects(prefixes=(PFX,))
    root = inv_wing.build(loc)      # clears InvWing_* and rebuilds at loc
    root.name = PFX + "Root"
    ox, oy = PIVOT_2D

    # Tag pinned root hardware before the join (groups merge by name).
    children = list(root.children)
    for ch in children:
        if ch.name[len(inv_wing.PFX):] in PINNED:
            vg = ch.vertex_groups.new(name="Base")
            vg.add(list(range(len(ch.data.vertices))), 1.0, 'REPLACE')

    # Join everything into one deformable mesh (membrane holds the join;
    # every part sits at identity local transform under the root empty,
    # so joined vertex coords stay root-local).
    bpy.ops.object.select_all(action='DESELECT')
    holder = bpy.data.objects[inv_wing.PFX + "Membrane"]
    for ch in children:
        ch.select_set(True)
    bpy.context.view_layer.objects.active = holder
    bpy.ops.object.join()
    mesh_ob = holder
    mesh_ob.name = PFX + "Mesh"

    # Armature: static Base stub + 3-bone chain out along the mid-fan.
    dirv = Vector((cos(CHAIN_ANG), sin(CHAIN_ANG), 0.0))
    pivot = Vector((ox, oy, 0.0))
    arm = bpy.data.armatures.new(PFX + "Arm")
    rig = bpy.data.objects.new(PFX + "Rig", arm)
    bpy.context.collection.objects.link(rig)
    rig.parent = root               # rig local == mesh local == root-local
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    prev = arm.edit_bones.new("Base")
    prev.head = pivot + Vector((0, 0, -0.15))
    prev.tail = pivot
    for k in range(3):
        eb = arm.edit_bones.new(f"Flap{k}")
        eb.head = pivot + dirv * JOINTS[k]
        eb.tail = pivot + dirv * JOINTS[k + 1]
        eb.roll = 0.0               # local X horizontal -> X-rot flaps
        eb.parent = prev
        eb.use_connect = k > 0
        prev = eb
    bpy.ops.object.mode_set(mode='OBJECT')
    arm.display_type = 'STICK'

    # Radial skinning: distance from the pivot in the fan plane.
    base_idx = mesh_ob.vertex_groups["Base"].index
    flap_g = [mesh_ob.vertex_groups.new(name=f"Flap{k}") for k in range(3)]
    for v in mesh_ob.data.vertices:
        if any(g.group == base_idx and g.weight > 0.5 for g in v.groups):
            continue
        r = hypot(v.co.x - ox, v.co.y - oy)
        for k, wk in enumerate(_chain_weights(r)):
            if wk > 0.001:
                flap_g[k].add([v.index], wk, 'REPLACE')
    mesh_ob.modifiers.new("Rig", 'ARMATURE').object = rig

    # Traveling-wave keys, every 2 frames, cyclic (frame 49 == frame 1).
    scn = bpy.context.scene
    scn.frame_start, scn.frame_end = 1, CYCLE
    scn.render.fps = 24
    for name, (amp, lag, tw) in WAVE.items():
        pb = rig.pose.bones[name]
        pb.rotation_mode = 'XYZ'
        for f in range(1, CYCLE + 2, 2):
            th = tau * (f - 1) / CYCLE - lag
            pb.rotation_euler.x = amp * (sin(th) + ASYM * sin(2 * th))
            pb.rotation_euler.y = tw * sin(th - TW_LAG)
            pb.keyframe_insert("rotation_euler", index=0, frame=f)
            pb.keyframe_insert("rotation_euler", index=1, frame=f)
    return root
