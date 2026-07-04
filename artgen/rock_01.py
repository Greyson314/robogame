# artgen/rock_01.py — faceted low-poly boulder, proof-of-loop for the Blender pipeline.
# Run inside Blender (via blender-mcp execute_code or Scripting tab).
# Deterministic: same params -> same mesh. Re-running replaces the previous Rock_01.
import bpy
import os

EXPORT_PATH = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Art\Models\Props\Rock_01.fbx"

# idempotent: remove any previous run's output
prev = bpy.data.objects.get("Rock_01")
if prev:
    bpy.data.objects.remove(prev, do_unlink=True)

bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=1.0, location=(3.0, 0.0, 0.0))
rock = bpy.context.active_object
rock.name = "Rock_01"

# boulder proportions: wide, squat
rock.scale = (1.15, 1.0, 0.72)

tex = bpy.data.textures.get("RockNoise")
if tex is None:
    tex = bpy.data.textures.new("RockNoise", type="CLOUDS")
tex.noise_scale = 0.55

disp = rock.modifiers.new("Displace", "DISPLACE")
disp.texture = tex
disp.strength = 0.6
disp.texture_coords = "LOCAL"

# decimate to chunky facets — the flat-shaded look
dec = rock.modifiers.new("Decimate", "DECIMATE")
dec.ratio = 0.06

# export with pivot at origin, modifiers applied by the exporter
os.makedirs(os.path.dirname(EXPORT_PATH), exist_ok=True)
saved_loc = rock.location.copy()
rock.location = (0.0, 0.0, 0.0)
bpy.ops.object.select_all(action="DESELECT")
rock.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=EXPORT_PATH,
    use_selection=True,
    object_types={"MESH"},
    use_mesh_modifiers=True,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    add_leaf_bones=False,
)
rock.location = saved_loc

eval_mesh = rock.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
print(f"Rock_01 exported: {len(eval_mesh.polygons)} tris-ish faces -> {EXPORT_PATH}")
