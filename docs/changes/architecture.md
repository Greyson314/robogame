# Architecture snapshot (current state)

## Modules

```
Robogame.Core         — Tweakables, IDamageable, GameBootstrap
Robogame.Block        — BlockDefinition, BlockGrid, BlockBinder, BlockIds, BlockVisuals, ChassisBlueprint, BlockDefinitionLibrary
Robogame.Movement     — RobotDrive, GroundDriveSubsystem, PlaneControlSubsystem, ThrusterBlock, AeroSurfaceBlock, WheelBlock, tuning SOs
Robogame.Combat       — HitscanGun, WeaponMount, WeaponBlock, RobotWeaponBinder, Bomb, BombBayBlock, CombatVfxLibrary
Robogame.Input        — PlayerInputHandler (Input System), IInputSource
Robogame.Player       — PlayerController, FollowCamera, AimReticle, OrbitCamera
Robogame.Robot/Robots — Robot, aggregates
Robogame.Gameplay     — GameStateController, ChassisFactory, GarageController, ArenaController, SceneTransitionHud, SettingsHud, BuildModeController, BlockEditor, BuildHotbar, BuoyancyController, WaterMeshAnimator
Robogame.Tools.Editor — scaffolders, EnvironmentBuilder, WorldPalette, BlockDefinitionWizard, ScaffoldHelpers, TuningAssets, RobotLayouts
Robogame.UI           — DevHud (placeholder)
Robogame.Network      — empty placeholder for the netcode roadmap (no source yet)
```

## Runtime flow

```
Bootstrap.unity (persistent)
├─ GameBootstrap       — first-scene loader
├─ GameStateController — singleton, owns CurrentBlueprint + presets
└─ SettingsHud         — Esc-toggled tweak panel

Garage.unity / Arena.unity
└─ {Garage,Arena}Controller
   ├─ spawns chassis via ChassisFactory.Build(go, blueprint, library, inputActions)
   ├─ binds FollowCamera + AimReticle
   └─ SceneTransitionHud (Launch / Garage button + chassis dropdown in garage)
```

## Where to change what

| Want to… | Edit |
|---|---|
| Adjust pitch/roll/thrust feel live | Press Esc in-game; values persist to JSON |
| Add a new tweak slider | `Register(...)` in [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs) + `Tweakables.Get(key)` at consumer |
| Add a new chassis preset | New `BuildXEntries()` in [GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs); extend `_presetBlueprints` array |
| Add a new block type | New `BlockDefinition` (via wizard or asset menu); add `BlockIds.X` const; if it has behaviour, a new `MonoBehaviour` + `BlockBinder` subclass |
| Restyle Garage or Arena | [EnvironmentBuilder.cs](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs) + [WorldPalette.cs](../../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs) |
| Tune the plane's aerodynamics shape | Change the lift formula in [AeroSurfaceBlock.cs](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs) (not exposed to Tweakables yet) |
| Re-run scaffolding | `Robogame → Scaffold → Gameplay → Build All Pass A` |

## Patterns / gotchas

- **Statics survive domain reload, GameObjects don't.** Any static
  cache of Unity objects must `[RuntimeInitializeOnLoadMethod]` reset.
- **`AddComponent<T>` runs `OnEnable` synchronously.** Reflection-based
  field assignment must happen with the root deactivated. See
  `ChassisFactory.Build`.
- **`AssetDatabase.Refresh` invalidates C# refs.** Re-load by path right
  before `SerializedObject.FindProperty(...).objectReferenceValue = ...`
  if anything between the load and the write touched the asset DB.
- **Input System UI doesn't gate over UI for free.** Use
  `EventSystem.current.IsPointerOverGameObject()` to suppress fire /
  camera-capture / etc. when the cursor's on the HUD.
- **Build All Pass A doesn't recompile.** Saving a .cs *and* focusing
  Unity does. The Tweakables registry obsoletes most reasons to care,
  but blueprint shape changes (block layouts) still need a re-scaffold.
