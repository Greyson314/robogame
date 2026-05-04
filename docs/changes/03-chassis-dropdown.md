# Session — Chassis dropdown (Tank / Plane / Buggy)

**Intent.** Let the player swap pre-built chassis from the garage HUD.

**What landed.**

- New buggy blueprint generator in [GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
  `BuildBuggyEntries`: 2-wide × 3-long ground bot, steering wheels
  front, drive wheels rear, weapon mounted on a roll cage. Stored at
  `Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultBuggy.asset`.
  Renamed existing blueprints' display names to "Tank", "Plane",
  "Buggy".
- [GameStateController.cs](../../Assets/_Project/Scripts/Gameplay/GameStateController.cs)
  gained `ChassisBlueprint[] _presetBlueprints`, `IReadOnlyList<ChassisBlueprint> PresetBlueprints`,
  `int CurrentPresetIndex`, `void SelectPreset(int)`, `event Action<int> PresetChanged`.
  `Awake` aligns `CurrentPresetIndex` with `_defaultBlueprint` so the
  HUD dropdown shows the right starting selection.
- [GarageController.cs](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)
  subscribes to `PresetChanged` in `OnEnable`, calls a new
  `Respawn()` that destroys the current chassis and rebuilds via
  `ChassisFactory`. Follow camera rebinds.
- [SceneTransitionHud.cs](../../Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
  procedurally builds a `Dropdown` (template + viewport + content + item)
  in the bottom-left. Visible only in `GameState.Garage`. Selection
  calls `GameStateController.SelectPreset(i)`.
- Scaffolder wires the `_presetBlueprints` array (Tank/Plane/Buggy) onto
  `GameStateController` in BuildBootstrapPassA via SerializedObject array writes.

**Notes.** Selection state lives on the controller, not the UI — so
future code (save/load, AI bot variants, networked sync) drives
`SelectPreset(i)` without touching UI. Adding a 4th preset is just one
line in `BuildBootstrapPassA` (extending `_presetBlueprints` to
`arraySize = 4`).
