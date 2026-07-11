# artgen/inv_anims.py — object-level animation studies (session 139).
# Unlike the wing swim (armature + skinning, inv_wing_anim.py), these
# three animate whole OBJECTS — doors on hinges, a bellows squash, a
# breathing capybara — so they need no rigs and port to Unity as
# procedural transform drivers on named FBX nodes, not skinned meshes.
# Each build_*() renames its study to an Anim* prefix (source builds
# clear their own prefix and would otherwise eat the copy on rebuild).
# Loops are 48 f @ 24 fps with CYCLES modifiers except the capybara
# idle (240 f). build_all() lines them up on the y=+2.5 anim row.

from math import cos, radians, tau

import bpy

import paperlib as pl
import inventorlib as il


def _rename(old_pfx, new_pfx):
    for ob in bpy.data.objects:
        if ob.name.startswith(old_pfx):
            ob.name = new_pfx + ob.name[len(old_pfx):]


def _reparent(child, parent):
    # Explicit local-matrix math: assigning matrix_world right after a
    # parent swap reads a stale depsgraph for freshly created empties
    # and silently leaves the child's local at identity (ears on the
    # moon). Update the view layer, then compute the local ourselves.
    bpy.context.view_layer.update()   # freshly built objects evaluate late
    mw = child.matrix_world.copy()
    child.parent = parent
    bpy.context.view_layer.update()
    child.matrix_parent_inverse.identity()
    child.matrix_basis = parent.matrix_world.inverted() @ mw


def _fcurves(act):
    """Action fcurves across the 4.x -> 5.x layered-action API change."""
    if hasattr(act, "fcurves"):
        return list(act.fcurves)
    return [fc for layer in act.layers for strip in layer.strips
            for cb in strip.channelbags for fc in cb.fcurves]


def _cycles(*obs):
    for ob in obs:
        if ob.animation_data and ob.animation_data.action:
            for fc in _fcurves(ob.animation_data.action):
                if not any(m.type == 'CYCLES' for m in fc.modifiers):
                    fc.modifiers.new('CYCLES')


def _keys(ob, path, index, frame_values):
    for f, v in frame_values:
        if index < 0:
            setattr(ob, path, v)
        else:
            getattr(ob, path)[index] = v
        ob.keyframe_insert(path, index=index, frame=f)


# ---- thruster: bellows pump + crank rev (48 f loop) --------------------

def build_thruster(loc=(2.6, 2.5, 0.5)):
    pl.clear_objects(prefixes=("AnimThr_",))
    import inv_thruster
    inv_thruster.build(loc)
    _rename("InvThr_", "AnimThr_")
    root = bpy.data.objects["AnimThr_Root"]

    # Squeeze profile: quick compression, slower re-inflation, rest.
    SQUEEZE = [(1, 1.0), (11, 0.72), (31, 1.0), (49, 1.0)]
    AFT = -0.14      # bellows aft edge (fixed against the aft board)
    FORE = 0.404     # bellows fore edge at rest

    # NOTE: children sit at identity local transforms under the root
    # empty, so all location keys below are root-LOCAL (no loc offset).
    bel = bpy.data.objects["AnimThr_Bellows"]
    _keys(bel, "scale", 1, [(f, s) for f, s in SQUEEZE])
    # keep the aft edge pinned: p' = s*p + t, want AFT fixed
    _keys(bel, "location", 1, [(f, AFT * (1 - s)) for f, s in SQUEEZE])

    # Fore group rides the fore edge of the accordion (staves are the
    # piston guides, fixed to the fore board, sliding past the aft one).
    for part in ("BoardFore", "IntakeRing", "IntakeHole",
                 "Stave0", "Stave1", "Stave2", "Stave3"):
        ob = bpy.data.objects["AnimThr_" + part]
        _keys(ob, "location", 1, [(f, (FORE - AFT) * (s - 1))
                                  for f, s in SQUEEZE])

    # Crank: one revolution per pump cycle. Pivot empty on the axle.
    piv = il.root_empty("AnimThr_CrankPivot", (0, 0, 0))
    piv.parent = root
    piv.location = (0.0, 0.10, 0.0)
    for part in ("CrankArm", "CrankGrip"):
        _reparent(bpy.data.objects["AnimThr_" + part], piv)
    _keys(piv, "rotation_euler", 0,
          [(1, 0.0), (13, tau * 0.25), (25, tau * 0.5),
           (37, tau * 0.75), (49, tau)])
    for fc in _fcurves(piv.animation_data.action):
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'

    _cycles(bel, piv, *[bpy.data.objects["AnimThr_" + p] for p in
                        ("BoardFore", "IntakeRing", "IntakeHole",
                         "Stave0", "Stave1", "Stave2", "Stave3")])
    return root


# ---- bomb bay: trapdoor drop (48 f one-shot, cycled for review) --------

def build_bombbay(loc=(5.4, 2.5, 0.5)):
    pl.clear_objects(prefixes=("AnimBay_",))
    import inv_bombbay
    inv_bombbay.build(loc, doors=(0.0, 180.0), with_bomb=False)
    _rename("InvBay_", "AnimBay_")
    root = bpy.data.objects["AnimBay_Root"]

    # Snap open with overshoot, hold, slam shut with a rebound.
    def door_keys(closed, sign):
        o = lambda deg: radians(closed + sign * deg)
        return [(1, o(0)), (4, o(100)), (7, o(88)), (10, o(93)),
                (26, o(93)), (31, o(0)), (34, o(16)), (38, o(0)),
                (49, o(0))]

    for name, closed, sign in (("AnimBay_Door1", 0.0, -1),
                               ("AnimBay_Door-1", 180.0, 1)):
        door = bpy.data.objects[name]
        _keys(door, "rotation_euler", 0, door_keys(closed, sign))
        _cycles(door)
    return root


# ---- capybara: idle breath + ear twitches (240 f loop) -----------------

def build_capy(loc=(8.2, 2.5, 0.5)):
    pl.clear_objects(prefixes=("AnimCapy_",))
    import inv_capycube
    inv_capycube.build(loc)
    _rename("InvCapyCube_", "AnimCapy_")
    root = bpy.data.objects["AnimCapy_Root"]
    body = bpy.data.objects["AnimCapy_CapyBody"]
    head = bpy.data.objects["AnimCapy_CapyHead"]

    # Breath: 80 f period, 3 per loop. Body swells, head bobs with lag.
    for idx, hi in ((0, 1.012), (1, 1.012), (2, 1.025)):
        _keys(body, "scale", idx, [(1, 1.0), (41, hi), (81, 1.0)])
    _keys(head, "location", 2, [(11, 0.0), (51, 0.007), (91, 0.0)])
    # slow head wander (full 240 f period)
    _keys(head, "rotation_euler", 2,
          [(1, 0.0), (61, 0.030), (121, 0.0), (181, -0.024), (241, 0.0)])

    # Ears ride the head via pivot empties at their bases, then twitch.
    twitches = {0: [(60, 61), (150, 151)],      # right ear
                1: [(104, 105), (152, 153)]}    # left ear
    for i, s in ((0, 1), (1, -1)):
        piv = il.root_empty(f"AnimCapy_EarPiv{i}", (0, 0, 0))
        piv.parent = head
        piv.location = (s * 0.160, -0.130, 1.115)
        _reparent(bpy.data.objects[f"AnimCapy_CapyEar{i}"], piv)
        kv = [(1, 0.0)]
        for f0, _ in twitches[i]:
            kv += [(f0, 0.0), (f0 + 2, s * -0.38),
                   (f0 + 5, s * 0.07), (f0 + 8, 0.0)]
        kv.append((241, 0.0))
        _keys(piv, "rotation_euler", 1, kv)
        _cycles(piv)
    _cycles(body, head)
    return root


def build_all():
    scn = bpy.context.scene
    scn.render.fps = 24
    scn.frame_start, scn.frame_end = 1, 240
    build_thruster()
    build_bombbay()
    build_capy()
