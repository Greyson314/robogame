# artgen — generated-asset manifest (provenance, charter I6)

The scripts in this folder are the source of truth for every FBX under
`Assets/_Project/Art/Models/`; the FBX is a build artifact (docs/changes/130).
They are procedural Blender scripts written in-repo, not AI-generated meshes: the
"prompt" charter I6 asks for is the script itself, and its history is `git log`.
Run inside Blender (the FBX headers read `Blender (stable FBX IO) - 5.1.2`; the latest
export is Wing_Inv, 2026-07-11; the pipeline's own log is docs/changes/130-131)
through the blender-mcp bridge or Blender's script editor. `paperlib.export_tree`
does the Unity frame conversion (Y-up, identity node rotations on root/Turret) for
everything; `inventorlib.py` holds the inventor-study primitives.

The table is machine-checked: `ArtgenManifestTests` (EditMode) fails when an FBX
under Art/Models has no row here, when a row names a script that no longer exists,
or when a row's FBX is gone.
Add a row in the same commit as a new FBX. Paths are relative to `Assets/_Project/Art/Models/`.

| FBX | Generator script (study) | Notes |
|---|---|---|
| `Blocks/Inv/CapyCube_Inv.fbx` | `inv_export.py` (study: `inv_capycube.py`) | `export_capycube`; 1x1x2 cockpit cube + capybara head |
| `Blocks/Inv/Cpu_Inv.fbx` | `inv_export.py` (study: `inv_cpu.py`) | STATICS table |
| `Blocks/Inv/Cube_Inv.fbx` | `inv_export.py` (study: `inv_cube.py`) | STATICS table |
| `Blocks/Inv/Drill_Inv.fbx` | `inv_export.py` (study: `inv_drill.py`) | `export_drill`; Rx-90 bake so the auger runs along the mount axis |
| `Blocks/Inv/Fin_Inv.fbx` | `inv_export.py` (study: `inv_aerofin.py`) | `export_fin`; Wing-frame bake |
| `Blocks/Inv/Foil_Inv.fbx` | `inv_export.py` (study: `inv_foil.py`) | `export_foil` |
| `Blocks/Inv/Grapple_Inv.fbx` | `inv_export.py` (study: `inv_grapple.py`) | STATICS table |
| `Blocks/Inv/Hover_Inv.fbx` | `inv_export.py` (study: `inv_hoverblade.py`) | STATICS table |
| `Blocks/Inv/ModuleBlink_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_blink` |
| `Blocks/Inv/ModuleEmp_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_emp` |
| `Blocks/Inv/ModuleInvis_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_invis` |
| `Blocks/Inv/ModuleMines_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_mines` |
| `Blocks/Inv/ModuleRepair_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_repair` |
| `Blocks/Inv/ModuleShield_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_shield` |
| `Blocks/Inv/ModuleSmoke_Inv.fbx` | `inv_export.py` (study: `inv_modules.py`) | STATICS table, `build_smoke` |
| `Blocks/Inv/Rope_Inv.fbx` | `inv_export.py` (study: `inv_rope.py`) | STATICS table |
| `Blocks/Inv/Rotor_Inv.fbx` | `inv_export.py` (study: `inv_rotor.py`) | STATICS table |
| `Blocks/Inv/Rudder_Inv.fbx` | `inv_export.py` (study: `inv_rudder.py`) | STATICS table |
| `Blocks/Inv/Spring_Inv.fbx` | `inv_export.py` (study: `inv_spring.py`) | STATICS table |
| `Blocks/Inv/Thruster_Inv.fbx` | `inv_export.py` (study: `inv_thruster.py`) | `export_thruster`; 180° spin so the nozzle exhausts aft |
| `Blocks/Inv/TipHook_Inv.fbx` | `inv_export.py` (study: `inv_tips.py`) | STATICS table, `build_hook` |
| `Blocks/Inv/TipMace_Inv.fbx` | `inv_export.py` (study: `inv_tips.py`) | STATICS table, `build_mace` |
| `Blocks/Inv/TipMagnet_Inv.fbx` | `inv_export.py` (study: `inv_tips.py`) | STATICS table, `build_magnet` |
| `Blocks/Inv/Wheel_Inv.fbx` | `inv_export.py` (study: `inv_wheel.py`) | `export_wheel`; 1 m outer diameter, WheelBlock scales the model by 2 × wheel radius (WheelBlock.cs:344) |
| `Blocks/Inv/Wing_Inv.fbx` | `inv_export.py` (study: `inv_wing.py`, `inv_wing_anim.py`) | `export_wing_anim`; rigged + baked flap action |
| `Props/Rock_01.fbx` | `rock_01.py` | first asset through the pipeline (docs/changes/130); 76 faces |
| `Weapons/BombBay_Inv.fbx` | `inv_export.py` (study: `inv_bombbay.py`) | `export_bombbay`; doors authored nearly closed, no bomb |
| `Weapons/Cannon_Inv.fbx` | `inv_export.py` (study: `inv_cannon.py`) | `export_cannon`; yaw gear normalized to a 1 m ring, yoke rake zeroed |
| `Weapons/Cannon_Paper.fbx` | `cannon_paperpunk.py` (lib: `paperlib.py`) | paper-punk family (docs/changes/131) |
| `Weapons/Mortar_Inv.fbx` | `inv_export.py` (study: `inv_mortar.py`) | `export_mortar`; tub tilt baked into meshes under an identity yoke |
| `Weapons/Mortar_Paper.fbx` | `mortar_paperpunk.py` (lib: `paperlib.py`) | paper-punk family (docs/changes/131) |
| `Weapons/SMG_Inv.fbx` | `inv_export.py` (study: `inv_smg.py`) | `export_smg`; yaw gear normalized to a 1 m ring |
| `Weapons/SMG_Paper.fbx` | `smg_paperpunk.py` (lib: `paperlib.py`) | paper-punk family (docs/changes/131) |

Scripts with no FBX of their own: `inventorlib.py`, `paperlib.py` (shared libraries);
`inv_anims.py`, `inv_wheelsteer.py` (studies whose output is folded into the rows
above or not exported); `gen_garage_theme.py`, `gen_music_assets.py` (audio, not
models: see docs/subsystems/audio.md).

Manifest first written 2026-09-17 by the Robogame Factory (CHG-001, FINDINGS F-006).
