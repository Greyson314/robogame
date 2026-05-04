# Session — Pass A + garage/arena visual identity

**Intent.** Get a real state machine + scene flow up, then make the
two scenes feel like different *places*.

**Pass A scaffolding** (state machine + scenes + serialised chassis):

- New [GameStateController.cs](../../Assets/_Project/Scripts/Gameplay/GameStateController.cs)
  — singleton on the persistent Bootstrap GameObject. Owns
  `BlockDefinitionLibrary`, `CurrentBlueprint`, `InputActionAsset`. State
  machine: Bootstrap → Garage ⇄ Arena. `EnterGarage()`, `EnterArena()`
  are scene-load wrappers; `StateChanged` event fires on
  `sceneLoaded`.
- New [ChassisBlueprint.cs](../../Assets/_Project/Scripts/Block/ChassisBlueprint.cs)
  ScriptableObject. List of `(BlockId, Vector3Int Position)` entries +
  `DisplayName`, `Kind`. `GameStateController.CloneBlueprint` deep-clones
  into a runtime instance so edits don't write back to the asset.
- New [BlockDefinitionLibrary.cs](../../Assets/_Project/Scripts/Block/BlockDefinitionLibrary.cs)
  — id → `BlockDefinition` map.
- New [ChassisFactory.cs](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
  — runtime equivalent of the editor's `RobotLayouts`. Adds always-on
  components (Rigidbody, BlockGrid, Robot, RobotDrive,
  PlayerInputHandler, PlayerController), then optional components
  implied by blueprint contents (wheels → GroundDrive + WheelBinder;
  aero → PlaneControl + AeroBinder; weapon → WeaponMount + WeaponBinder),
  then places blocks. Recalculates aggregates.
- New [GarageController.cs](../../Assets/_Project/Scripts/Gameplay/GarageController.cs),
  [ArenaController.cs](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)
  — per-scene controllers. Garage spawns the player chassis from
  `CurrentBlueprint`. Arena spawns player + a combat dummy from a
  separate blueprint.
- New [GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
  editor menu: `Robogame → Scaffold → Gameplay → Build All Pass A`.
  Idempotent: creates `BlockDefinitionLibrary`, three blueprints (now
  four with Buggy), then rebuilds Bootstrap / Garage / Arena scenes
  wired to the runtime state machine. Leaves Bootstrap.unity open so
  pressing Play exercises the flow.
- New [SceneTransitionHud.cs](../../Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
  — single button, label switches based on `GameState`.

**Visual identity pass:**

- New [WorldPalette.cs](../../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  — cached URP/Lit material assets (GarageFloor/Wall/Accent/Podium,
  ArenaGround/Wall/Ramp/Bump/Stair/Pillar) + `GarageClear`/`ArenaClear`
  colors.
- New [EnvironmentBuilder.cs](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
  — `BuildGarageEnvironment()` (small enclosed bay with hazard
  stripes, podium, dark clear), `BuildArenaEnvironment()` (220m grass
  field + recolored obstacle course, sky-blue clear).
- New `_tintColor` field on
  [BlockDefinition.cs](../../Assets/_Project/Scripts/Block/BlockDefinition.cs).
  [BlockGrid.cs](../../Assets/_Project/Scripts/Block/BlockGrid.cs)'s
  `ApplyTint` uses `mr.material` (per-instance copy) so primitive
  shared mats don't get recolored project-wide.
- [BlockDefinitionWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
  rewrote `CreateOrSkip` → `CreateOrUpdate` with a `Color tint` param.
  Cube=light gray, CPU=cyan, Wheel=dark gray, WheelSteer=medium gray,
  Thruster=orange, Aero=off-white, Weapon=red.

**Edge case fixed during scaffolding.** `AssetDatabase.Refresh()` (which
`BlockDefinitionWizard` calls and `OpenScene` may trigger) invalidates
in-memory C# refs to assets — they go "fake null" and `SerializedObject`
won't accept them. Fix: load by path **immediately before** the
`SerializedObject.FindProperty(...).objectReferenceValue = ...` write,
not earlier. This is now the pattern used in
`BuildBootstrapPassA`.
