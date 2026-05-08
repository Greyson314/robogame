# Architecture snapshot (current state)

## Modules

```
Robogame.Core         — Tweakables, IDamageable, GameBootstrap, PerfMarkers,
                         RuntimePalette, VfxKind, VfxSpawner,
                         AudioBus, AudioCue, AudioCueLibrary, AudioRouter
Robogame.Block        — BlockDefinition, BlockGrid, BlockBinder, BlockIds, BlockVisuals,
                         ChassisBlueprint, BlockDefinitionLibrary,
                         BlueprintBuilder, BlueprintValidator, BlueprintAsciiDump
Robogame.Movement     — RobotDrive, GroundDriveSubsystem, PlaneControlSubsystem,
                         ThrusterBlock, AeroSurfaceBlock, WheelBlock, RudderBlock,
                         RopeBlock, RopeTip, RotorBlock,
                         VerletRopeSimulator, VerletRopeChain,
                         TipBlock, HookBlock, MaceBlock,
                         ChassisWindAudio,
                         RobotAeroBinder, RobotWheelBinder, RobotRopeBinder,
                         RobotRotorBinder, RobotTipBlockBinder, tuning SOs
Robogame.Combat       — ProjectileWorld + ProjectileSpec + ProjectileVisual + ProjectileKind
                         (single custom-stepped integrator, session 32 — drives SMG / bomb / cannon),
                         WeaponDefinition, BombDefinition, CannonDefinition,
                         WeaponBlock + ProjectileGun (SMG turret + fire),
                         BombBayBlock (gravity bomb spawner),
                         CannonBlock (pirate cannon turret + fire),
                         WeaponMount, RobotWeaponBinder,
                         CombatVfxLibrary, MomentumImpactHandler
Robogame.Input        — PlayerInputHandler (Input System), IInputSource
Robogame.Player       — PlayerController, FollowCamera, AimReticle, OrbitCamera,
                         BuildFreeCam, DeathOverlay, FloatingDamageOverlay,
                         HitMarkerOverlay, VehicleStatsHud
Robogame.Robot/Robots — Robot, aggregates
Robogame.Gameplay     — GameStateController, ChassisFactory,
                         GarageController, ArenaController,
                         PlanetArenaController, WaterArenaController, MainMenuController,
                         SceneTransitionHud, SettingsHud,
                         BuildModeController, BlockEditor, BuildHotbar, BlockGhostFactory,
                         VariantConfigPanel,
                         BuoyancyController, WaterMeshAnimator, WaterSurface, WaterVolume,
                         PlanetBody, PlanetGravity, PlanetGravityBody,
                         MatchController, MatchConfig + types,
                         GroundBotInputSource, AirBotInputSource, DummyAiInputSource (deprecated),
                         ObjectiveHud, MatchEndOverlay, StartMatchHud, KillAnnouncer
Robogame.Tools.Editor — scaffolders, EnvironmentBuilder, WorldPalette, BlockDefinitionWizard,
                         ScaffoldHelpers, TuningAssets, RobotLayouts, FluffGround,
                         HillsGround, PerformanceMenu
Robogame.UI           — DevHud, FpsCounter, PerformanceHud
Robogame.Network      — empty placeholder for the netcode roadmap (no source yet)
```

## Runtime flow

```
Bootstrap.unity (persistent)
├─ GameBootstrap       — first-scene loader
├─ GameStateController — singleton, owns CurrentBlueprint + presets
└─ SettingsHud         — Esc-toggled tweak panel

Auto-bootstrapped (RuntimeInitializeOnLoadMethod, present in every scene)
├─ FpsCounter          — top-left fps readout
├─ PerformanceHud      — F3-toggleable diagnostics overlay
├─ VerletRopeSimulator — scene-root singleton ticking every rope chain
├─ VfxSpawner          — pooled procedural one-shot VFX dispatcher
├─ AudioRouter         — Tweakables-driven mix routing
└─ ProjectileWorld     — single integrator for every projectile in the game
                         (SMG / bomb / cannon — see session 32)

Garage.unity / Arena.unity / WaterArena.unity / PlanetArena.unity
└─ {Garage,Arena,…}Controller
   ├─ spawns chassis via ChassisFactory.Build(go, blueprint, library, inputActions)
   ├─ binds FollowCamera + AimReticle + per-camera HUDs
   └─ SceneTransitionHud (Launch / Garage button + chassis dropdown in garage)

Arena round flow (Pillar 1)
└─ ArenaController owns a MatchController (plain C#) ticked from Update
   ├─ WarmingUp:  bots spawned passive (Target=null), cursor locked, free-flight
   │             StartMatchHud renders "Press [`] to begin combat"
   ├─ → StartMatch (`` ` `` backtick by default; configurable via _startMatchKey) → InProgress
   ├─ InProgress: ArenaController binds bot Target + FireAtTarget=true
   │             ObjectiveHud shows HP / score / round timer
   │             Robot.Destroyed events route to MatchController.RegisterKill
   ├─ → MatchEnded (frag limit / lives out / time up / draw)
   └─ RoundEnded: MatchEndOverlay (modal IMGUI + Return-to-Garage button)
```

## Where to change what

| Want to… | Edit |
|---|---|
| Adjust pitch/roll/thrust feel live | Press Esc in-game; values persist to JSON |
| Add a new tweak slider | `Register(...)` in [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs) + `Tweakables.Get(key)` at consumer |
| Add a new chassis preset | Author via [`BlueprintBuilder`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs) inside `GameplayScaffolder` (`Block`, `Row`, `Box`, `MirrorX/Z`, `RotorWithFoils`, `RopeWithHook`, `RopeWithMace`); call `BuildValidated(library)` to fail fast on no-CPU / orphans / duplicates |
| Inspect a preset's shape without launching | EditMode → run `PresetBlueprintTests.DumpAllPresets_WritesAsciiSnapshot`; reads on disk and writes [docs/blueprint-snapshots/presets.md](../blueprint-snapshots/presets.md) with one ASCII layer per Y |
| Add a new block type | New `BlockDefinition` (via wizard or asset menu); add `BlockIds.X` const; if it has behaviour, a new `MonoBehaviour` + `BlockBinder` subclass; for a tip-on-rope block, derive from [`TipBlock`](../../Assets/_Project/Scripts/Movement/TipBlock.cs) |
| Restyle Garage or Arena | [EnvironmentBuilder.cs](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs) + [WorldPalette.cs](../../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs) |
| Tune the plane's aerodynamics shape | Change the lift formula in [AeroSurfaceBlock.cs](../../Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs) (not exposed to Tweakables yet) |
| Re-run scaffolding | `Robogame → Scaffold → Gameplay → Build All Pass A` |
| Add an AI opponent to the match | Edit `_matchConfig.GroundBots` or `_matchConfig.AirBots` on Arena scene's `ArenaController`. Each entry is a `BotEntry { Blueprint, SpawnPositionOverride, PatrolCentreOverride }` |
| Tune AI behaviour | Inspector fields on [`GroundBotInputSource`](../../Assets/_Project/Scripts/Gameplay/GroundBotInputSource.cs) / [`AirBotInputSource`](../../Assets/_Project/Scripts/Gameplay/AirBotInputSource.cs) — `OptimalRange`, `EngageBuffer`, `ChaseRange`, `RetreatHealthFraction`, `EngageFacingDotThreshold`, `PursueThrottle` (ground only) |
| Tune match shape | `_matchConfig` SerializeField on `ArenaController` — `WarmupDuration`, `RoundDuration`, `TargetFragCount`, `PlayerLives`, `BotRespawnDelay`, `PlayerRespawnDelay`, `RequireManualStart` |
| Spawn a debug enemy from in-game | F1 (DevHud) → toggle `Stress.TankDummy` / `Stress.AirDummy` to spawn passively; toggle `…Fire` to bind target + enable fire |
| Profile / diagnose perf | F3 (PerformanceHud) for live frame-time / GC / Rb-count; `Robogame > Perf > {Toggle Perf HUD, Log Render Stats, Toggle V-Sync, Capture Profiler Frame}`; add `using PerfMarkers.X.Auto()` scopes for new hot paths via [`PerfMarkers.cs`](../../Assets/_Project/Scripts/Core/PerfMarkers.cs) |
| Add a one-shot VFX burst at a gameplay event | `Robogame.Core.VfxSpawner.Spawn(VfxKind.X, position, rotationOrForward[, scale])`. New kinds add a `VfxKind` enum value and a `Configure*` recipe in [`VfxSpawner.cs`](../../Assets/_Project/Scripts/Core/VfxSpawner.cs); palette-locked tints come from `RuntimePalette` |
| Trigger an audio cue from gameplay | `Robogame.Core.AudioRouter.PlayOneShot(AudioCue.X, worldPos)` (3D) or `PlayUI(cue)` (2D). Cues live in [`AudioCue.cs`](../../Assets/_Project/Scripts/Core/AudioCue.cs); clip routing in `Resources/AudioCueLibrary.asset` (auto-built by `AudioCueWizard`). See [docs/AUDIO_PLAN.md](../AUDIO_PLAN.md) for the contract. |
| Add a new projectile weapon | Add a `ProjectileKind` enum value, add a switch arm in [`ProjectileWorld.DispatchImpactFx`](../../Assets/_Project/Scripts/Combat/ProjectileWorld.cs), build a `ProjectileSpec` in your weapon block's fire path, call `ProjectileWorld.Spawn(in spec)`. No new MonoBehaviour, no new Rigidbody, no new self-collision plumbing. See [session 32](32-projectile-unification.md) for the rationale and runner-up architectures. |
| Modify the Fluff grass shader | Modify the package shader directly; document the change in [docs/PACKAGE_MODIFICATIONS.md](../PACKAGE_MODIFICATIONS.md) so it can be re-applied after a Fluff upgrade |
| Lock / unlock the cursor from outside FollowCamera | Call `FollowCamera.ReleaseCursor()` / `FollowCamera.ApplyCursorLock()`. Bare `Cursor.lockState = …` is reverted on the next frame by FollowCamera's relock-recovery path |

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
- **IMGUI buttons are invisible to the EventSystem.** They don't show
  up in `EventSystem.current.IsPointerOverGameObject()`. That's why
  `FollowCamera`'s click-to-recapture path treats clicks on IMGUI
  buttons as "regrab the cursor" and centres the cursor before IMGUI
  can process the click. **Don't put gameplay-critical buttons in
  IMGUI when the cursor is locked.** Use a hotkey (`StartMatchHud` is
  the example) or unlock the cursor first.
- **Build All Pass A doesn't recompile.** Saving a .cs *and* focusing
  Unity does. The Tweakables registry obsoletes most reasons to care,
  but blueprint shape changes (block layouts) still need a re-scaffold.
- **Foil colliders + chassis colliders.** When `RotorBlock` adopts an
  `AeroSurfaceBlock` as a rotor blade, the foil's host cube collider
  is reparented under the kinematic hub — its effective Rigidbody
  becomes the hub, not the chassis. Foils and chassis now own
  different Rigidbodies, so PhysX considers them collidable. As the
  hub spins, foil colliders sweep through the chassis's swept volume
  (notably the mechanism cube placed at the same y-level by
  `RotorWithFoils`). `RotorBlock.IgnoreFoilChassisContacts` walks
  every chassis collider once at adoption and ignore-pairs each
  against the foil's collider. Foil-vs-arena and foil-vs-other-chassis
  contacts are preserved (future flail-weapon).
- **Tweakables defaults vs persisted JSON.** Bumping a registered
  default in code does NOT take effect for users who already have a
  saved value in `tweakables.json` — `Load()` clamps the saved value
  into the new range but doesn't overwrite it. Either drag the slider
  in-game or wipe the JSON to pick up a new default.
- **No Tweakable affects gameplay outcomes.** PHYSICS_PLAN § 1.5.
  Match shape (frag limit, round duration, lives, AI fire range)
  lives on the `MatchConfig` SerializeField on `ArenaController`,
  not in `Tweakables`. The `Stress.*` Tweakables that gate dev-only
  bot spawn / fire toggles still satisfy the rule because they're
  developer affordances, not server-canonical match data.
- **`MatchController` is plain C# (not a MonoBehaviour).** Constructed
  by `ArenaController` and ticked from `Update`. Plumbed this way so
  EditMode tests can drive the state machine without a scene and a
  future `NetworkBehaviour` wrapper can host it without restructuring.
  Tests live at [`Tests/EditMode/Gameplay/MatchControllerTests.cs`](../../Assets/_Project/Tests/EditMode/Gameplay/MatchControllerTests.cs).
- **Bot AI passive during `MatchState.WarmingUp`.** Bots are spawned
  with `Target = null` and `FireAtTarget = false`. They Patrol around
  their patrol point harmlessly. `ArenaController.HandleMatchStarted`
  binds the player chassis as Target + enables fire when the round
  goes hot. The legacy tweakable-spawned dummies follow the same
  rule — toggling `Tank/Air Bot Fires` off keeps them passive.
- **`FollowCamera` re-locks cursor every frame.** Once you've called
  `Cursor.lockState = Locked`, FollowCamera's `_cursorWasLocked` flag
  goes true and a per-frame guard re-applies the lock if Unity drops
  it (alt-tab recovery). This means a bare `Cursor.lockState = None`
  from outside FollowCamera lasts exactly one frame. Use the public
  `FollowCamera.ReleaseCursor()` / `FollowCamera.ApplyCursorLock()`
  helpers — they manage `_cursorWasLocked` correctly.
- **MatchEndOverlay vs DeathOverlay collision.** `DeathOverlay`
  shows whenever `Robot.BlockCount == 0`; `MatchEndOverlay` shows on
  `MatchState.RoundEnded`. Both can be true simultaneously. The
  overlap is resolved by `ArenaController.HandleMatchEnded` setting
  `DeathOverlay.enabled = false` so the Match overlay is the only
  visible one. Re-enabled implicitly when the next scene reloads.
- **Bot-target rebinding after respawn.** When the player chassis
  is destroyed and respawned, every live bot's `Target` field
  points at the destroyed (Unity-fake-null) old transform. Bots
  fall back to Patrol forever unless rebound. `ArenaController.RebindBotTargets`
  iterates `_matchBots` after `RespawnPlayer` and pushes the new
  chassis transform onto each one.
- **Fluff package is modified locally.** The arena's grass shader
  has a Robogame-authored UV-warp tweak. Tracked in
  [docs/PACKAGE_MODIFICATIONS.md](../PACKAGE_MODIFICATIONS.md);
  re-apply after any Fluff package upgrade.
