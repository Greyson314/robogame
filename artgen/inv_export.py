# artgen/inv_export.py — export the inventor studies to Unity FBX.
# Run inside Blender. Uses paperlib.export_tree (session-131 frame
# conversion: Y-up, identity node rotations on root/Turret).
#
# Rig rules encoded here:
# - Turret weapons (SMG/cannon/mortar) scale-normalize the yaw gear to
#   a 1 m ring (SCALE = 0.5 / 0.32 where the study didn't already).
# - TurretYoke forces IDENTITY on the Turret node at runtime, so any
#   authored rotation on the yoke empty must be zeroed (cannon rake)
#   or baked into child geometry (mortar tub: authored mouth-up under
#   a -90°X tilt; we bake that rotation into the meshes so the tub
#   points forward under an identity yoke and MortarBlock lobs it).
# - Wheel: WheelBlock spins its model about local +Y; Blender +Z maps
#   to Unity +Y, so the wheel (axle along X) is parented under a
#   -90°-about-Y empty. Exported at exactly 1 m outer diameter;
#   runtime scales by 2 * wheel radius.
# - Everything else exports as-is (static visuals for the sweep).
# Display builds are restored after every export.

import importlib
import os
import sys

import bpy
from math import radians, pi
from mathutils import Matrix, Vector

ARTGEN = r"C:\Users\Grey\Desktop\mutedtuple\robogame\artgen"
if ARTGEN not in sys.path:
    sys.path.insert(0, ARTGEN)

import paperlib as pl
import inventorlib as il

WEAPONS_DIR = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Weapons"
BLOCKS_DIR = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Blocks\Inv"

SCALE = 0.5 / 0.32   # yaw-gear ring -> 1 m diameter


def bake_rotation(parent_empty, rot):
    """Bake `rot` into the direct children of an empty so the empty
    itself can stay identity (TurretYoke wipes its rotation)."""
    for o in parent_empty.children:
        if o.type == 'MESH':
            o.data.transform(rot)
        o.location = rot @ o.location
    parent_empty.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()


def export_smg():
    import inv_smg
    importlib.reload(inv_smg)
    root = inv_smg.build()
    yoke = bpy.data.objects["InvSMG_Yoke"]
    muzzle = bpy.data.objects["InvSMG_Muzzle"]
    pl.export_tree(root, os.path.join(WEAPONS_DIR, "SMG_Inv.fbx"),
                   yoke=yoke, muzzle=muzzle)
    inv_smg.build()


def export_cannon():
    import inv_cannon
    importlib.reload(inv_cannon)
    root = inv_cannon.build()
    pitch = bpy.data.objects["InvCannon_Pitch"]
    pitch.rotation_euler = (0, 0, 0)      # display rake; rig owns pitch
    muzzle = bpy.data.objects["InvCannon_Muzzle"]
    pl.scale_tree(root, SCALE)
    pl.export_tree(root, os.path.join(WEAPONS_DIR, "Cannon_Inv.fbx"),
                   yoke=pitch, muzzle=muzzle)
    inv_cannon.build()


def export_mortar():
    import inv_mortar
    importlib.reload(inv_mortar)
    root = inv_mortar.build()
    tilt = bpy.data.objects["InvMortar_Tilt"]
    # Tub is authored mouth-up (+Z) under a display tilt. Bake a +90°X
    # rotation so the mouth lands on -Y (weapon forward): R_x(+90)
    # maps +Z -> -Y. (First export used -90 and the tub aimed backward
    # — caught by the in-editor ShootPoint offset check.)
    bake_rotation(tilt, Matrix.Rotation(pi / 2, 4, 'X'))
    muzzle = bpy.data.objects["InvMortar_Muzzle"]
    pl.scale_tree(root, SCALE)
    pl.export_tree(root, os.path.join(WEAPONS_DIR, "Mortar_Inv.fbx"),
                   yoke=tilt, muzzle=muzzle)
    inv_mortar.build()


def export_bombbay():
    import inv_bombbay
    importlib.reload(inv_bombbay)
    root = inv_bombbay.build(doors=(-12.0, 192.0), with_bomb=False)
    pl.export_tree(root, os.path.join(WEAPONS_DIR, "BombBay_Inv.fbx"))
    inv_bombbay.build()


def export_wheel():
    import inv_wheel
    importlib.reload(inv_wheel)
    pl.clear_objects(prefixes=("WheelExp_",))
    m = il.materials()
    root = il.root_empty("WheelExp_Root", (0, 0, 0))
    axle = il.root_empty("WheelExp_Axle", (0, 0, 0))
    axle.parent = root
    axle.location = (0, 0, 0)
    axle.rotation_euler = (0, radians(-90), 0)   # Blender X-axle -> +Z
    # Authored outer diameter 0.928 -> exactly 1 m.
    inv_wheel.make_wheel("WheelExp_", axle, m, s=1.0 / 0.928, axle=False)
    pl.export_tree(root, os.path.join(BLOCKS_DIR, "Wheel_Inv.fbx"))
    pl.clear_objects(prefixes=("WheelExp_",))
    inv_wheel.build()


STATICS = [
    # (module, build attr, root object name, out name)
    ("inv_cube", "build", "InvCube_Root", "Cube_Inv"),
    ("inv_rotor", "build", "InvRotor_Root", "Rotor_Inv"),
    ("inv_wing", "build", "InvWing_Root", "Wing_Inv"),
    ("inv_aerofin", "build", "InvFin_Root", "Fin_Inv"),
    ("inv_rudder", "build", "InvRudder_Root", "Rudder_Inv"),
    ("inv_thruster", "build", "InvThr_Root", "Thruster_Inv"),
    ("inv_hoverblade", "build", "InvHover_Root", "Hover_Inv"),
    ("inv_spring", "build", "InvSpring_Root", "Spring_Inv"),
    ("inv_drill", "build", "InvDrill_Root", "Drill_Inv"),
    ("inv_rope", "build", "InvRope_Root", "Rope_Inv"),
    ("inv_cpu", "build", "InvCpu_Root", "Cpu_Inv"),
    ("inv_grapple", "build", "InvGrap_Root", "Grapple_Inv"),
    ("inv_tips", "build_hook", "InvHook_Root", "TipHook_Inv"),
    ("inv_tips", "build_mace", "InvMace_Root", "TipMace_Inv"),
    ("inv_tips", "build_magnet", "InvMagnet_Root", "TipMagnet_Inv"),
    ("inv_modules", "build_emp", "InvModEmp_Root", "ModuleEmp_Inv"),
    ("inv_modules", "build_repair", "InvModRep_Root", "ModuleRepair_Inv"),
]


def export_statics():
    for mod_name, fn, root_name, out in STATICS:
        mod = importlib.import_module(mod_name)
        importlib.reload(mod)
        getattr(mod, fn)()
        root = bpy.data.objects[root_name]
        saved_rot = tuple(root.rotation_euler)
        root.rotation_euler = (0, 0, 0)   # display spins don't export
        pl.export_tree(root, os.path.join(BLOCKS_DIR, out + ".fbx"))
        root.rotation_euler = saved_rot
        print("static exported:", out)


def export_all():
    os.makedirs(WEAPONS_DIR, exist_ok=True)
    os.makedirs(BLOCKS_DIR, exist_ok=True)
    export_smg()
    export_cannon()
    export_mortar()
    export_bombbay()
    export_wheel()
    export_statics()
    print("inv export complete")
