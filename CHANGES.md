# Robogame — dev log

This file is the catch-up brief for any future contributor (human or AI)
landing on the project mid-stream. Read top-down: the **Architecture
snapshot** at the bottom is what's true *right now*; the **Session log**
above it is how we got here, newest first. Skim a session entry for
*why* a thing is shaped the way it is.

Style: dev log, not changelog. Each session entry covers user intent,
what shipped, what we learned. File links use repo-relative paths.

---

## Session log (newest first)

### Session — Phase 1 art pass: cel-shading, post-FX, ambient, skybox

**Intent.** Stand up the visual identity sketched in
[docs/ART_DIRECTION.md](docs/ART_DIRECTION.md). User had just imported MK
Toon (paid), Polyverse Skies (free), and Cartoon FX Remaster Free. Goal:
make every Pass A re-scaffold produce a scene that already *looks* like
the doc — palette, lighting, post-stack, skybox — without anyone
hand-tuning a Volume in the Inspector.

**What shipped.**

- New [PostProcessingBuilder.cs](Assets/_Project/Scripts/Tools/Editor/PostProcessingBuilder.cs).
  Authors two `VolumeProfile` assets at
  `Assets/_Project/Rendering/PostProcessing/PostProfile_{Garage,Arena}.asset`
  with Bloom + ACES Tonemapping + ColorAdjustments + (garage only)
  Vignette. Numbers come straight from ART_DIRECTION.md §
  "Post-Processing Rules" so the doc and the asset can never drift
  silently — re-running rebuilds the profile in place.

- New [SkyboxBuilder.cs](Assets/_Project/Scripts/Tools/Editor/SkyboxBuilder.cs).
  Authors `Assets/_Project/Rendering/Skyboxes/Skybox_Arena.mat` using
  the `BOXOPHOBIC/Polyverse Skies/Standard` shader, tinted from
  `WorldPalette` tokens (`SkyDay` / `SkyEquator` / `Grass`). Falls
  back to `Skybox/Procedural` if the Polyverse package vanishes — the
  rest of the scaffold keeps running.

- [WorldPalette.cs](Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  rewritten. Old: 10 ad-hoc material accessors with inline `Color`
  literals. New: full **12-token palette** (Slate, SlateLight,
  Concrete, Grass, SkyDay, Hazard, Caution, Alert, Cyan, Plasma, Mint,
  UIBg, plus `CyanEmit` HDR + `SkyEquator` derivative + UI alphas)
  exposed as `public static readonly Color`, mirroring the doc 1:1.
  Material factory now prefers `MK/Toon/URP/Standard/Physically Based`
  with URP/Lit fallback, and probes `_AlbedoColor` / `_BaseColor` /
  `_Color` so the same factory works on MK Toon, URP/Lit, and
  Standard. Per-material smoothness/metallic numbers come from
  ART_DIRECTION § Material Vocabulary.

- [EnvironmentBuilder.cs](Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
  lighting rig refactored. `EnsureCameraAndLight` now takes
  `lightColor` + `lightIntensity` so per-scene values aren't buried in
  one helper. Garage = warm `#FFE0B0` sun at 1.0 intensity, Arena =
  cool `#FFF8E0` sun at 1.3 intensity, both with soft shadows. New
  `ConfigureAmbient` switches `RenderSettings.ambientMode` to
  Trilight (cheapest stylized fake-bounce) and pushes per-scene sky/
  equator/ground tints. New `EnsureSceneVolume` drops a global
  `Volume` GameObject into the scene wired to the matching profile —
  loads the profile by path right before assignment, same fake-null
  avoidance pattern. Arena calls `SkyboxBuilder` and assigns the
  result to `RenderSettings.skybox`; Garage stays dark on purpose.

- [Robogame.Tools.Editor.asmdef](Assets/_Project/Scripts/Tools/Editor/Robogame.Tools.Editor.asmdef)
  gained `Unity.RenderPipelines.Core.Runtime` +
  `Unity.RenderPipelines.Universal.Runtime` references so the new
  Volume / Bloom / Tonemapping / etc. types resolve. Without these,
  every URP override type is invisible to the editor assembly.

- [GameplayScaffolder.BuildAllPassA](Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
  now calls `PostProcessingBuilder.BuildAll()` and
  `SkyboxBuilder.BuildArenaSkybox()` *before* the per-scene builds so
  the `EnsureSceneVolume` / `RenderSettings.skybox` writes have live
  assets to point at.

- [CpuBlockMarker.cs](Assets/_Project/Scripts/Block/CpuBlockMarker.cs)
  also writes `_AlbedoColor` (MK Toon's main colour slot) alongside
  `_BaseColor` / `_Color`. Without this the cyan beacon would render
  as MK Toon's default white when a CPU material got swapped to MK
  Toon.

- [docs/ART_DIRECTION.md](docs/ART_DIRECTION.md) updated with a new
  **Imported Assets** section (current entries: MK Toon, Polyverse
  Skies, Cartoon FX Remaster Free, plus the wrong-Kenney-pack as a
  strikethrough orphan), Phase 1 checklist marked complete, Phase 2
  pivoted from "author a custom shader" to "use MK Toon's `+ Outline`
  variant", and Open Questions #1 (cel decision) and #3 (outline
  approach) marked resolved with rationale.

**Imported assets — cheatsheet.** See
[ART_DIRECTION.md § Imported Assets](docs/ART_DIRECTION.md#imported-assets)
for the canonical table; short version:

- `Assets/MK/MKToon/` — MK Toon paid. Shader: `MK/Toon/URP/Standard/Physically Based` (+ outline variant for hero blocks).
- `Assets/BOXOPHOBIC/Polyverse Skies/` — free skybox. Shader: `BOXOPHOBIC/Polyverse Skies/Standard`.
- `Assets/JMO Assets/Cartoon FX Remaster/` — free VFX. Not yet wired (Phase 3).
- `Assets/_Project/Art/ThirdParty/kenney_voxel-pack/` — wrong pack (2D sprites). Orphan, safe to delete.

**Notes for future tabs.**

- *Pattern.* The "third-party shader fallback" pattern is now used in
  three places (`WorldPalette`, `SkyboxBuilder`, implicitly the Volume
  loader). Always `Shader.Find` the preferred name first, fall back,
  log a warning. Re-importing a pack should never break the scaffold.
- *Pattern.* Asset-DB writes (volume profiles, skyboxes) **always**
  load by path right before the consumer assignment. Same fake-null
  avoidance documented for the Pass A serialised refs.
- *Inventory discipline.* The Imported Assets table in
  ART_DIRECTION.md is the single source of truth for what
  third-party packs we have, what they're wired to, and which
  builder script touches them. **Update it in the same commit you
  add or remove a pack** — drift here turns into "why is this
  shader missing" mystery later.
- *MK Toon outline warning.* Adding the "MK Toon Per Object Outlines"
  renderer feature to `Assets/Settings/PC_Renderer.asset` silences the
  import warning *and* enables outlines for materials using the
  `+ Outline` shader variants. Currently nothing uses those variants
  so the warning is benign — Phase 2 is when we start opting hero
  blocks in.
- *URP package versions.* If Unity bumps the URP package and the
  referenced asmdef names change (`Unity.RenderPipelines.Core.Runtime`
  / `Unity.RenderPipelines.Universal.Runtime`), every Volume / Bloom
  / Tonemapping / etc. type in the editor assembly will go red. Easy
  fix: update the asmdef references; no code change needed.

---

### Session — Settings panel + Tweakables registry

**Intent.** "Tuning by recompile" was killing iteration speed. The user
wanted a runtime settings UI that owns the physics knobs — no more
edit-save-wait-recompile-Play loops.

**What shipped.**

- New [Assets/_Project/Scripts/Core/Tweakables.cs](Assets/_Project/Scripts/Core/Tweakables.cs).
  Static registry of named float specs (key, group, label, default, min,
  max). Persisted as JSON to `Application.persistentDataPath/tweakables.json`
  so values survive runs *and* editor restarts. Public API:
  `Get(key)`, `Set(key, v)`, `Reset(key)`, `ResetAll()`, `event Changed`.
  Lazy-initialised, range-clamped on every set.

- 14 specs registered today, grouped by category:
  - **Plane** — pitch / roll / yaw-from-bank power, pitch / roll / yaw damping
  - **Thruster** — max thrust, idle throttle, throttle response
  - **Ground** — acceleration, max speed, turn rate
  - **Chassis** — linear damping, angular damping

- Subsystems migrated to read through the registry:
  - [PlaneControlSubsystem.cs](Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs)
    reads every FixedUpdate — changes are live with zero plumbing.
  - [ThrusterBlock.cs](Assets/_Project/Scripts/Movement/ThrusterBlock.cs)
    same pattern.
  - [GroundDriveSubsystem.cs](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs)
    only the three exposed knobs (accel/max/turn) routed through;
    jump/upright/grip stayed on the SO/inline path because they're not
    in the UI yet.
  - [RobotDrive.cs](Assets/_Project/Scripts/Movement/RobotDrive.cs)
    reads damping through registry. Subscribes to `Tweakables.Changed`
    and re-pushes `_rb.linearDamping`/`angularDamping` because rigidbody
    damping is cached on the body, not read each frame.

- New [Assets/_Project/Scripts/Gameplay/SettingsHud.cs](Assets/_Project/Scripts/Gameplay/SettingsHud.cs).
  Esc toggles a procedurally-built UGUI panel. Scrollable body builds
  one row per `Tweakables.Spec` with label + slider + live value text +
  per-row reset (↺) button. Header: title, "Tweaks" tab pill,
  Reset-All, ✕ close. Sits on the persistent Bootstrap GameObject so
  one instance covers Garage and Arena. Wired in by
  [GameplayScaffolder.BuildBootstrapPassA](Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs).

**Workflow now.** Press Esc → drag slider → effect is immediate. Values
persist across sessions (JSON in `persistentDataPath`). Adding a new
tweak is two lines: a `Register(...)` call in `Tweakables.EnsureInitialized`
and a `Tweakables.Get(key)` at the consumer. The UI rebuilds itself
from `Tweakables.All`.

**Notes for future tabs.** The "Tweaks" tab pill in the HUD is a stub —
adding Audio / Graphics / Bindings tabs means making the body content
swappable per active tab. Right now there's only one tab so the body
is always tweaks. The slider, button, and tab-pill helpers in
[SettingsHud.cs](Assets/_Project/Scripts/Gameplay/SettingsHud.cs)
(`AddButton`, `AddTabPill`, `BuildSlider`, `NewChild`, `FillParent`)
are designed to be reused.

**Esc collision.** [FollowCamera.cs](Assets/_Project/Scripts/Player/FollowCamera.cs)
also reacts to Esc to release the cursor. Both want the cursor freed,
so this is fine. The settings HUD doesn't try to re-capture; left-click
in the world re-locks via FollowCamera as before.

---

### Session — Plane "feel" pass

**Intent.** Plane top speed felt too high, pitch was sluggish, then
later: a constant "buoyancy" pushing the tail up.

**What landed.**

- Bumped pitch/roll authority and tightened damping in
  [PlaneControlSubsystem.cs](Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs):
  pitch 3.2 → 7.5, roll 4.5 → 9.0, yaw-from-bank 1.4 → 2.0; damping
  pitch 2.6 → 3.5, roll 2.6 → 2.8, yaw 1.4 → 1.6.
- Lowered top speed in [ThrusterBlock.cs](Assets/_Project/Scripts/Movement/ThrusterBlock.cs):
  max thrust 220 → 155 N, idle throttle 0.5 → 0.4.
- **Aerodynamics rewrite** in [AeroSurfaceBlock.cs](Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs).
  Was: `lift = speed² × coef × sign(forward)`. Every wing produced
  upward force unconditionally, so wings far from COM caused a
  permanent pitching moment — that was the "buoyancy on the tail" feel.
  Now: angle-of-attack-driven lift, `lift = speed² × coef × (clamp(aoa, ±stallAoA) + zeroLiftBias) × sign(forward)`,
  with soft stall past `stallAoA`. At level flight every surface
  produces only `zeroLiftBias × speed²` of lift evenly distributed →
  pitching moments cancel → plane self-trims. Pitching up raises AoA
  uniformly across the plane → all wings produce more lift → real
  elevator authority through aerodynamics, not just torque.

**Lesson learned.** First-pass attempt at fixing the "buoyancy" was to
*remove* the rear lifting surfaces (replace tailplane Aero with Cube).
That made the plane lift-deficient and dive. The AoA model alone fixes
the moment problem without removing surfaces. Reverted blueprint to
all-Aero tail.

**Tuning hooks.** `_zeroLiftBias` controls "how much free lift at zero
AoA." 0 = pure symmetric airfoil (must constantly pitch up to stay
level). 0.12 ≈ current — plane cruises level on its own at the right
speed. `_liftCoef` scales total lift; bump if it dives, drop if it
climbs unprompted.

**Tuning-iteration trap (resolved by the Tweakables session above).**
User reported "saving + Build All Pass A + Play, settings don't
change." Root cause: there are two parallel sets of defaults — inline
`[SerializeField]` on the component, and `*.Tuning.cs` SO defaults. The
runtime path through [ChassisFactory.cs](Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
never assigns a tuning SO, so SO edits are dead code there. Build All
Pass A doesn't trigger recompile — only saving a .cs file *and*
focusing Unity does. Added `OnEnable` debug logs that print the
actually-effective values + their source. The Tweakables session
obsoletes both paths for the exposed knobs.

---

### Session — HitscanGun MissingReferenceException on Stop

**Intent.** User reported error on stopping play after a fire burst:
`MissingReferenceException: LineRenderer has been destroyed`.

**Fix in [HitscanGun.cs](Assets/_Project/Scripts/Combat/HitscanGun.cs).**
Two changes:

1. `SpawnTracer` now drains destroyed entries on pop. Loops popping
   until it finds a live `LineRenderer` (Unity's overloaded `==` returns
   true for destroyed objects) before falling through to fresh
   construction.
2. Added `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`
   `ResetStatics` that clears `s_tracerPool`, `s_activeTracers`,
   `s_tracerMaterial` at every Play session start.

**Root cause.** Statics survive domain reload (or "Enter Play Mode
without reload" in modern Unity); the GameObjects they referenced do
not. The pool from session N held references that became fake-null in
session N+1. Same pattern will bite any other static pool we add —
template:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStatics() { s_pool.Clear(); s_active.Clear(); s_material = null; }
```

---

### Session — Chassis dropdown (Tank / Plane / Buggy)

**Intent.** Let the player swap pre-built chassis from the garage HUD.

**What landed.**

- New buggy blueprint generator in [GameplayScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
  `BuildBuggyEntries`: 2-wide × 3-long ground bot, steering wheels
  front, drive wheels rear, weapon mounted on a roll cage. Stored at
  `Assets/_Project/ScriptableObjects/Blueprints/Blueprint_DefaultBuggy.asset`.
  Renamed existing blueprints' display names to "Tank", "Plane",
  "Buggy".
- [GameStateController.cs](Assets/_Project/Scripts/Gameplay/GameStateController.cs)
  gained `ChassisBlueprint[] _presetBlueprints`, `IReadOnlyList<ChassisBlueprint> PresetBlueprints`,
  `int CurrentPresetIndex`, `void SelectPreset(int)`, `event Action<int> PresetChanged`.
  `Awake` aligns `CurrentPresetIndex` with `_defaultBlueprint` so the
  HUD dropdown shows the right starting selection.
- [GarageController.cs](Assets/_Project/Scripts/Gameplay/GarageController.cs)
  subscribes to `PresetChanged` in `OnEnable`, calls a new
  `Respawn()` that destroys the current chassis and rebuilds via
  `ChassisFactory`. Follow camera rebinds.
- [SceneTransitionHud.cs](Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
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

---

### Session — Launch button, three rounds of debugging

**Intent.** "Click Launch button → goes to Arena" wasn't working. Bug
required three orthogonal fixes, none obvious in isolation.

**Round 1: PlayerInputHandler null-deref on respawn.**
`AddComponent<T>` runs `OnEnable` immediately. `PlayerInputHandler.OnEnable`
was looking up the action map and bailing because `_actions` hadn't
been set via reflection yet. Fix in [ChassisFactory.cs](Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
`Build()`: `bool wasActive = root.activeSelf; root.SetActive(false); try { ...add components, reflect refs in... } finally { root.SetActive(wasActive); }`.
Now `OnEnable` runs once, after wiring. **General lesson: any reflection-based serialised-field assignment must happen with the root deactivated.**

**Round 2: Gun fired through UI buttons.**
Input System actions don't auto-block over UI like legacy input does.
Fix in [PlayerInputHandler.cs](Assets/_Project/Scripts/Input/PlayerInputHandler.cs):
`FireHeld` getter returns false when `EventSystem.current.IsPointerOverGameObject()`
is true. Same trick is reusable for any other "is mouse on the world?"
gate.

**Round 3: Cursor lock ate UI clicks.**
[FollowCamera.cs](Assets/_Project/Scripts/Player/FollowCamera.cs)
locked the cursor on the same left-click frame that the UI raycaster
needed. Once locked, cursor is at screen center invisible — UI sees
nothing. Fix: same pattern, the click-to-capture condition now also
requires `!EventSystem.current.IsPointerOverGameObject()`.

**Side fix on the way: SceneTransitionHud rebuilt as UGUI.**
Was OnGUI/IMGUI. Input System UI module doesn't route to IMGUI. Full
rewrite to procedural UGUI: `EventSystem` (with
`InputSystemUIInputModule`, deletes legacy `StandaloneInputModule` if
present), `Canvas` (ScreenSpaceOverlay, sortingOrder 100),
`CanvasScaler`, `GraphicRaycaster`, `Button` with `Image` background +
child `Text` label. Uses `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`.

---

### Session — Pass A + garage/arena visual identity

**Intent.** Get a real state machine + scene flow up, then make the
two scenes feel like different *places*.

**Pass A scaffolding** (state machine + scenes + serialised chassis):

- New [GameStateController.cs](Assets/_Project/Scripts/Gameplay/GameStateController.cs)
  — singleton on the persistent Bootstrap GameObject. Owns
  `BlockDefinitionLibrary`, `CurrentBlueprint`, `InputActionAsset`. State
  machine: Bootstrap → Garage ⇄ Arena. `EnterGarage()`, `EnterArena()`
  are scene-load wrappers; `StateChanged` event fires on
  `sceneLoaded`.
- New [ChassisBlueprint.cs](Assets/_Project/Scripts/Block/ChassisBlueprint.cs)
  ScriptableObject. List of `(BlockId, Vector3Int Position)` entries +
  `DisplayName`, `Kind`. `GameStateController.CloneBlueprint` deep-clones
  into a runtime instance so edits don't write back to the asset.
- New [BlockDefinitionLibrary.cs](Assets/_Project/Scripts/Block/BlockDefinitionLibrary.cs)
  — id → `BlockDefinition` map.
- New [ChassisFactory.cs](Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
  — runtime equivalent of the editor's `RobotLayouts`. Adds always-on
  components (Rigidbody, BlockGrid, Robot, RobotDrive,
  PlayerInputHandler, PlayerController), then optional components
  implied by blueprint contents (wheels → GroundDrive + WheelBinder;
  aero → PlaneControl + AeroBinder; weapon → WeaponMount + WeaponBinder),
  then places blocks. Recalculates aggregates.
- New [GarageController.cs](Assets/_Project/Scripts/Gameplay/GarageController.cs),
  [ArenaController.cs](Assets/_Project/Scripts/Gameplay/ArenaController.cs)
  — per-scene controllers. Garage spawns the player chassis from
  `CurrentBlueprint`. Arena spawns player + a combat dummy from a
  separate blueprint.
- New [GameplayScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
  editor menu: `Robogame → Scaffold → Gameplay → Build All Pass A`.
  Idempotent: creates `BlockDefinitionLibrary`, three blueprints (now
  four with Buggy), then rebuilds Bootstrap / Garage / Arena scenes
  wired to the runtime state machine. Leaves Bootstrap.unity open so
  pressing Play exercises the flow.
- New [SceneTransitionHud.cs](Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
  — single button, label switches based on `GameState`.

**Visual identity pass:**

- New [WorldPalette.cs](Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  — cached URP/Lit material assets (GarageFloor/Wall/Accent/Podium,
  ArenaGround/Wall/Ramp/Bump/Stair/Pillar) + `GarageClear`/`ArenaClear`
  colors.
- New [EnvironmentBuilder.cs](Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
  — `BuildGarageEnvironment()` (small enclosed bay with hazard
  stripes, podium, dark clear), `BuildArenaEnvironment()` (220m grass
  field + recolored obstacle course, sky-blue clear).
- New `_tintColor` field on
  [BlockDefinition.cs](Assets/_Project/Scripts/Block/BlockDefinition.cs).
  [BlockGrid.cs](Assets/_Project/Scripts/Block/BlockGrid.cs)'s
  `ApplyTint` uses `mr.material` (per-instance copy) so primitive
  shared mats don't get recolored project-wide.
- [BlockDefinitionWizard.cs](Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
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

---

### Background — initial refactor pass (pre-log)

The repo had already gone through one large hygiene pass before this
log started. Highlights, in case future contributors trip over the
patterns:

1. **Centralised block IDs.** All canonical IDs live as
   `public const string` in [BlockIds.cs](Assets/_Project/Scripts/Block/BlockIds.cs).
   Don't use string literals; they break on rename.
2. **`BlockBinder` base class** at
   [BlockBinder.cs](Assets/_Project/Scripts/Block/BlockBinder.cs).
   Subclasses (Wheel/Aero/Weapon) override only `ShouldBind` + `Bind`.
3. **`BlockVisuals` rig helpers** at
   [BlockVisuals.cs](Assets/_Project/Scripts/Block/BlockVisuals.cs):
   `HideHostMesh`, `GetOrCreateChild`, `GetOrCreatePrimitiveChild`.
   Used by every block type with a mesh rig.
4. **Editor scaffolder split.** [SceneScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs)
   is just menu commands now. Tuning SO load-or-create lives in
   [TuningAssets.cs](Assets/_Project/Scripts/Tools/Editor/TuningAssets.cs).
   Editor utilities live in
   [ScaffoldHelpers.cs](Assets/_Project/Scripts/Tools/Editor/ScaffoldHelpers.cs).
   Block layouts live in
   [RobotLayouts.cs](Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs).
5. **Tuning ScriptableObjects** under
   [Movement/Tuning/](Assets/_Project/Scripts/Movement/Tuning/).
   Pattern: optional `[SerializeField] private XxxTuning _tuning`;
   resolved-property reads `_tuning != null ? _tuning.X : _x`.
   Note: superseded for the *exposed* knobs by the new Tweakables
   registry. Inline + SO defaults still resolve when a Tweakables key
   isn't registered.
6. **Pooled tracers + `Physics.RaycastNonAlloc`** everywhere on hot
   paths. See [HitscanGun.cs](Assets/_Project/Scripts/Combat/HitscanGun.cs),
   [WheelBlock.cs](Assets/_Project/Scripts/Movement/WheelBlock.cs),
   [RobotDrive.cs](Assets/_Project/Scripts/Movement/RobotDrive.cs),
   [WeaponMount.cs](Assets/_Project/Scripts/Combat/WeaponMount.cs).
7. **Event-driven wheel cache** in [GroundDriveSubsystem.cs](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs):
   `BlockGrid.BlockPlaced`/`BlockRemoving` rather than periodic
   `GetComponentsInChildren`.

---

## Architecture snapshot (current state)

### Modules

```
Robogame.Core         — Tweakables, IDamageable, GameBootstrap
Robogame.Block        — BlockDefinition, BlockGrid, BlockBinder, BlockIds, BlockVisuals, ChassisBlueprint, BlockDefinitionLibrary
Robogame.Movement     — RobotDrive, GroundDriveSubsystem, PlaneControlSubsystem, ThrusterBlock, AeroSurfaceBlock, WheelBlock, tuning SOs
Robogame.Combat       — HitscanGun, WeaponMount, WeaponBlock, RobotWeaponBinder
Robogame.Input        — PlayerInputHandler (Input System), IInputSource
Robogame.Player       — PlayerController, FollowCamera, AimReticle
Robogame.Robot/Robots — Robot, aggregates
Robogame.Gameplay     — GameStateController, ChassisFactory, GarageController, ArenaController, SceneTransitionHud, SettingsHud
Robogame.Tools.Editor — scaffolders, EnvironmentBuilder, WorldPalette, BlockDefinitionWizard, ScaffoldHelpers, TuningAssets, RobotLayouts
Robogame.UI           — (placeholder)
```

### Runtime flow

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

### Where to change what

| Want to… | Edit |
|---|---|
| Adjust pitch/roll/thrust feel live | Press Esc in-game; values persist to JSON |
| Add a new tweak slider | `Register(...)` in [Tweakables.cs](Assets/_Project/Scripts/Core/Tweakables.cs) + `Tweakables.Get(key)` at consumer |
| Add a new chassis preset | New `BuildXEntries()` in [GameplayScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs); extend `_presetBlueprints` array |
| Add a new block type | New `BlockDefinition` (via wizard or asset menu); add `BlockIds.X` const; if it has behaviour, a new `MonoBehaviour` + `BlockBinder` subclass |
| Restyle Garage or Arena | [EnvironmentBuilder.cs](Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs) + [WorldPalette.cs](Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs) |
| Tune the plane's aerodynamics shape | Change the lift formula in [AeroSurfaceBlock.cs](Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs) (not exposed to Tweakables yet) |
| Re-run scaffolding | `Robogame → Scaffold → Gameplay → Build All Pass A` |

### Patterns / gotchas

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
