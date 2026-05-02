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

### Session — Polish: foam wake on chassis + connectivity flood-fill at placement

**Intent.** Two follow-ups from the water-visuals session, picked off the
roadmap: *"yep, let's do 1 and 3"* — connectivity flood-fill at placement
time, and foam-on-collision wake where chassis cut through the surface.

**Shipped.**

- **Foam wake.** [BuoyancyController.cs](Assets/_Project/Scripts/Gameplay/BuoyancyController.cs)
  now keeps a static `Active` registry (HashSet, OnEnable add / OnDisable
  remove) and a per-instance `SurfaceContacts : IReadOnlyList<Vector2>`.
  Each `FixedUpdate` clears the list and re-appends the world XZ of every
  block whose submerged fraction lies in (0.05, 0.95) — i.e. blocks
  straddling the waterline, the natural hull-meets-surface points.
  [WaterMeshAnimator.cs](Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)
  reads the registry once per Update and, in the per-vert loop, computes a
  smooth-falloff foam halo around each contact (`_wakeFoamRadius=2.5 m`,
  `_wakeFoamStrength=0.85`). Max-blended with perimeter and crest foam so
  the result stays in [0,1] and the shader never saturates back to white
  the way it did pre-explicit-vertex-colour.
- **Connectivity flood-fill at placement.** [BlockEditor.cs](Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  gained `BuildCpuReachableSet()` — same BFS pattern as
  `WouldOrphanIfRemoved`, but rooted at the CPU and returning the full
  reachable set. `IsValidPlacement` now requires the new cell to be
  adjacent to a CPU-reachable block, not merely *any* block. In normal
  play this is identical to the old "any neighbour" rule (every existing
  block is CPU-reachable by induction); the change defends against
  loading a hand-edited or corrupted blueprint that came in with a
  disconnected island — you can no longer extend the orphaned cluster,
  only the CPU's component. Empty-grid case still allows the very first
  block.

**Cost notes.** Wake is 4 225 verts × ~1 chassis × 5–40 contact points =
~30–170 k distance tests/frame, well under budget. Connectivity BFS runs
once per `UpdateTarget` (≈ once per frame while build mode is active) over
≤ 100 blocks — sub-microsecond.

### Session — Water visuals: Bitgem shader + Gerstner mesh + DevHud waves slider

**Intent.** Two threads merged into one push. Earlier in the session we
locked in the Fluff grass tuning that had been drifting for several
iterations; then user pivoted: *"let's get water texture/shading into
the water arena before i go too far in other directions."* Goal:
replace the flat translucent-teal placeholder with a stylised water
shader, keep our CPU-driven Gerstner waves authoritative for buoyancy,
and add live tuning so sea state can be dialled without editing code.

**Shipped.**

- New [BitgemWaterMaterial.cs](Assets/_Project/Scripts/Tools/Editor/BitgemWaterMaterial.cs).
  Editor-only factory that clones `Assets/Bitgem/StylisedWater/URP/Materials/example-water-01.mat`
  → `Assets/_Project/Materials/Mat_Water.mat`, preserving every tuned
  Vector1_* float the demo ships (foam width/noise, depth scale/power,
  refraction strength, glossiness, etc.) and overriding only the bits
  we own:
  - `Color_F01C36BF` (`_ShallowColor`) ← `WorldPalette.WaterSurface` α=0.30
  - `Color_7D9A58EC` (`_DeepColor`)    ← `WorldPalette.WaterDeep`
  - `_WaveScale` / `_WaveSpeed` / `_WaveFrequency` → **0** (kills the
    shader's GPU vertex displacement so it doesn't fight our CPU mesh)
  - `_ScrollSpeed = 0.15`, `_DetailStrength = 0.12`, `_BumpStrength = 0.20`
    (demo ships at 1.2 / 0.25 / 0.35 — too zippy against a 15 s swell;
    these calm the normal-map current to a lazy drift).
  Returns `null` if the Bitgem package is missing, so
  [WorldPalette.WaterMat](Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  falls back to its old translucent-URP/Lit factory and headless
  builds keep working.

- Patched [WaterMeshAnimator.cs](Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs).
  Now writes an all-black `Color[]` to the procedural mesh's vertex
  colours. Bitgem's shader graph reads vertex colour as a foam mask
  (red = foam edge, black = open water) — Unity's default `Color.white`
  was the smoking gun behind the *"water is straight up 100% white"*
  bug. URP/Lit fallback ignores the channel, so it's harmless either
  way.

- Re-tuned wave defaults in [Tweakables.cs](Assets/_Project/Scripts/Core/Tweakables.cs).
  Old defaults (amp 0.30, λ 12, speed 1.5, steepness 0.30) read as
  fizzy ripples on a 220 m arena. New defaults: **amp 1.20 m, λ 30 m,
  speed 2.0 m/s, steepness 0.45** — gives a slow ~15 s dominant period
  with crests that actually peak. Amplitude max bumped 2 → 4, length
  max 40 → 80 to allow stormier sea state if we ever want it.

- Added a **▼ Waves** collapsible section to [DevHud.cs](Assets/_Project/Scripts/UI/DevHud.cs)
  with live `HorizontalSlider` controls for Wave Amplitude / Length /
  Speed / Steepness, plus a **Reset Waves** button. Generic
  `DrawTweakSlider(key)` helper reads min/max/label straight from the
  Tweakables spec, so adding more sections later (Buoyancy, Plane,
  Ground) is one line per slider. HUD is now wider (260 px) and
  scrollable when the section expands.

**Grass lock-in (same session).** Final values baked into
[FluffGround.cs](Assets/_Project/Scripts/Tools/Editor/FluffGround.cs)
after several rounds of camera-frame and tile-repeat fights:
shell count 16, max height 1.2 m, world scale 65, world-space sampling
(`_TextureSamplingMethod = 1`), shape noise scale 2.7 / strength 0.15,
detail noise scale 1.0 / strength 0.65. New `AssignNoiseTexture()`
helper pins `grass-noise-23` (shape) and `grass-noise-14` (detail) by
name from the package's `Runtime/Textures/Noise/` folder so re-running
Build Arena always re-stamps the user-locked picks. Camera also
lowered for grass framing in [FollowCamera.cs](Assets/_Project/Scripts/Player/FollowCamera.cs)
(`_distance` 12 → 9, `_height` 2 → 1).

**Architecture notes.**

- *Two animations, one surface.* The Bitgem shader and our
  `WaterMeshAnimator` are now strictly separated by responsibility:
  geometry comes from the CPU mesh (so buoyancy and visuals can never
  drift), shading + normal-map scroll come from Bitgem (so we get
  depth fade, foam edges, fresnel, refraction without authoring a
  graph). The vertex-wave properties are zeroed in
  `BitgemWaterMaterial` — that's the seam.
- *Why clone-and-override instead of authoring our own material.*
  The demo ships ~15 unnamed `Vector1_*` floats tuning foam, depth,
  detail noise, etc. Cloning preserves all of them for free; we only
  override the four properties we have an opinion about. Same pattern
  as `FluffGround` — the script is the source of truth, inspector
  edits get clobbered on next Build Arena.
- *Bitgem's WaterVolume\* MonoBehaviours are intentionally unused.*
  We keep `Robogame.Gameplay.WaterVolume` as the data marker because
  `BuoyancyController` already binds to it and the in-engine arena
  is a flat plane, not a tile-volume.
- *Why vertex-colour foam mask matters.* Bitgem's shader graph treats
  `vertexColor.r` as foam intensity. A flat plane authored without
  vertex colours inherits Unity's `Color.white` default → "100% foam
  everywhere" → pure white surface. The fix is one `_mesh.colors`
  assignment in `WaterMeshAnimator.BuildMesh`. Future: write
  `Color.red` near the arena walls if we ever want lapping foam.

**Known follow-ups (deferred).**

- Saved values in `tweakables.json` win over new defaults. After
  bumping defaults you have to **Reset Waves** in the HUD (or delete
  `%LocalLow%/<co>/<prod>/tweakables.json`) before they take effect.
  Acceptable; we deliberately want player tuning to persist.
- Bitgem `_ScrollSpeed` is hard-coded in `BitgemWaterMaterial`. If
  we want it tied to wind heading later, promote it to a Tweakable
  and read it via `MaterialPropertyBlock` in a runtime updater.
- No foam edges anywhere — vertex colours are uniformly black.
  Authoring red bands at the arena perimeter (or where blocks
  intersect the surface) is a Phase 3 polish task.
- Analytic normals via `WaterSurface.SampleNormal` are still TODO;
  current mesh runs `RecalculateNormals` every frame, which is fine
  at 64×64 tessellation but the cheaper path is one-cross-product
  per vertex from the same Gerstner derivatives the height sampler
  uses.

**Files touched.**

- Added: [BitgemWaterMaterial.cs](Assets/_Project/Scripts/Tools/Editor/BitgemWaterMaterial.cs).
- Modified: [WorldPalette.cs](Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  (`WaterMat` getter now prefers Bitgem),
  [WaterMeshAnimator.cs](Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)
  (vertex-colour foam mask),
  [Tweakables.cs](Assets/_Project/Scripts/Core/Tweakables.cs) (wave defaults + maxes),
  [DevHud.cs](Assets/_Project/Scripts/UI/DevHud.cs) (Waves section + slider helper),
  [FluffGround.cs](Assets/_Project/Scripts/Tools/Editor/FluffGround.cs) (locked grass values + noise pinning),
  [FollowCamera.cs](Assets/_Project/Scripts/Player/FollowCamera.cs) (lowered framing).
- Asset: `Assets/_Project/Materials/Mat_Water.mat` regenerated by the
  factory on next Build Arena Pass A.

---

### Session — Build mode: in-garage block editor (Pass B Phase 3a)

**Intent.** *"Let's focus on how to add a block to a new bot."* After a
short design memo, user picked: **(A) modal toggle** (not always-on),
hotbar OK, and — overriding the default proposal — **block CPU removal
entirely** rather than warn-and-allow. Also delivered as a side quest:
[docs/BEST_PRACTICES.md](docs/BEST_PRACTICES.md), a 16-section
Robocraft-clone playbook (architecture, block-grid pitfalls, vehicle
physics, URP, GC, pooling, save/load, MP-readiness, profiling, named
pitfalls, perf budgets).

**Shipped — four new components:**
- [OrbitCamera](Assets/_Project/Scripts/Player/OrbitCamera.cs) —
  RMB-drag rotate, MMB-drag pan (clamped 4 m radius), scroll-zoom
  (3–20 m). Sibling to FollowCamera; only one enabled at a time. UI
  blocking via `EventSystem.IsPointerOverGameObject`. No cursor lock so
  the HUD stays clickable.
- [BuildModeController](Assets/_Project/Scripts/Gameplay/BuildModeController.cs) —
  modal owner. `Enter()` zeros velocity, sets the chassis Rigidbody
  kinematic + FreezeAll, disables `PlayerInputHandler` and
  `FollowCamera`, enables/creates `OrbitCamera`. `Exit()` reverses and
  calls `GarageController.Respawn()` so subsystems reattach to the
  edited blueprint. Public `IsActive`, `Entered`/`Exited` events,
  `Toggle()`.
- [BlockEditor](Assets/_Project/Scripts/Gameplay/BlockEditor.cs) —
  Camera.main raycast → BlockBehaviour ancestor → face-normal in
  chassis-local space → integer cell. Ghost preview (lazy unit cube
  with translucent URP/Unlit, green/red MaterialPropertyBlock).
  Validation: cell empty + ≥1 occupied 6-axis neighbour + only one
  CPU. LMB place, RMB remove. **CPU cannot be removed** (per user).
  After every mutation: `RecalculateAggregates` + `SyncBlueprintFromGrid`
  regenerates `state.CurrentBlueprint` entries from the live
  `BlockGrid.Blocks` dict — Save Robot is now trivially correct.
- [BuildHotbar](Assets/_Project/Scripts/Gameplay/BuildHotbar.cs) —
  procedural Canvas, 7 slots, keys **1–7** map to BlockIds: Cube, CPU,
  Wheel, Steer, Thrust, Aero, Gun. Selected slot tinted hazard orange.
  Visible only while build mode active.

**Wiring.**
- [GarageController](Assets/_Project/Scripts/Gameplay/GarageController.cs)
  now lazily attaches the build-mode trio in `EnsureBuildModeWired()`
  and rebinds `SetChassis` after every Respawn. New
  `ToggleBuildMode()` entry point + `BuildMode` accessor.
- [SceneTransitionHud](Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
  gains a third stacked garage-only button. Label flips between
  "Build Mode" ↔ "Drive Mode" by subscribing to
  `BuildModeController.Entered/Exited`.

**Why modal.** Always-on edit competes with driving for the same mouse
buttons and same camera. Modal also gives a clean place to freeze the
Rigidbody and swap to an orbit camera that's actually good for
inspection — both of which would be jarring if they happened
implicitly mid-drive.

**Why blueprint sync via "rebuild Entries from grid".** The grid is
already the source of truth at runtime (subsystem auto-binders react
to `BlockPlaced`/`BlockRemoving`). Mirroring back to the blueprint on
each mutation is O(blocks) and keeps Save Robot correct without a
separate diff pipeline. Cheap, idempotent, no edge cases.

**Why CPU-cannot-be-removed (override).** User's call. Removes one
class of surprise — an empty grid, or a CPU-less chassis that fails to
respawn cleanly — without needing a confirm dialog yet. Trivial to
relax later: delete the early-return in `BlockEditor.TryRemove`.

**Known follow-ups (deferred).**
- Hotbar palette is fixed 7 slots; a categorised picker (per
  [BlockCategory](Assets/_Project/Scripts/Block/BlockDefinition.cs))
  is the next step once we have more block defs.
- No CPU-budget enforcement yet — `CpuCost` is summed but not gated
  (Robocraft-style CPU cap is a Pass B Phase 3b task).
- Connectivity flood-fill from the CPU is not enforced; current rule
  is "must touch ≥1 existing block", which can produce floating
  islands if the player removes a bridge block. Acceptable for now;
  proper connectivity check goes alongside the CPU cap.
- Garage geometry expansion (bigger walls, grid floor decal,
  back-wall headroom for the orbit camera) is still on deck.

---

### Session — Save/load foundations + "+ New Robot" button (Pass B kickoff)

**Intent.** User pivoted from art polish back to gameplay: *"Let's begin
work on 1) expanding the garage, 2) adding a 'New Custom Robot' button,
and roadmapping out how we're going to load and save new robots that
we create."* This session lands the **save/load foundation** and the
**"+ New Robot" / "Save Robot"** HUD buttons. Garage geometry expansion
and the in-garage block-placement editor are deferred to follow-on
sessions (see roadmap below).

**What shipped.**

- New [BlueprintSerializer.cs](Assets/_Project/Scripts/Block/BlueprintSerializer.cs).
  Pure (no I/O) JSON round-trip for `ChassisBlueprint`. Explicit DTO
  with a `schemaVersion` field so we can migrate the on-disk format
  without breaking older saves. v1 schema:
  `{ schemaVersion, displayName, kind, createdUtc, entries:[{id,x,y,z}] }`.
  Serializes block IDs (stable strings) rather than asset references —
  saves stay valid across asset moves and are netcode-friendly.

- New [UserBlueprintLibrary.cs](Assets/_Project/Scripts/Block/UserBlueprintLibrary.cs).
  Disk-backed registry under `Application.persistentDataPath/blueprints/`
  (survives game updates, untouched by reinstalls). `LoadAll()`,
  `Save()`, `Delete()`, `Changed` event. Generates collision-safe
  slugified filenames (`my-robot.robot.json`, `my-robot-2.robot.json`,
  ...). Pure runtime — does not touch `AssetDatabase`, so player
  builds Just Work.

- New [StarterBlueprints.cs](Assets/_Project/Scripts/Block/StarterBlueprints.cs).
  `CreateGroundStarter()` mints a fresh runtime blueprint mirroring
  the proven default rover layout (3×3 cube floor, CPU at origin,
  hitscan weapon on top, 4 corner wheels + 2 mid-side wheels with
  steering at the front). The "blank canvas" the **+ New Robot**
  button drops onto the podium.

- Extended [GameStateController.cs](Assets/_Project/Scripts/Gameplay/GameStateController.cs).
  Now owns a merged catalog of **presets first, user blueprints
  second**. New API: `UserBlueprints` list, `CreateNewBlueprint()`,
  `SaveCurrentBlueprint()` (overwrite-or-create, repoints
  `CurrentUserFileName` after save), `DeleteCurrentUserBlueprint()`,
  `RefreshUserBlueprints()`, and a `BlueprintCatalogChanged` event.
  `SelectPreset(int)` is now merged-index-aware — `[0..presetCount)`
  are presets, `[presetCount..total)` are user records. Hydrates the
  user catalog on `Awake()`.

- Extended [SceneTransitionHud.cs](Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs).
  Two new bottom-left buttons stacked above the existing chassis
  dropdown (garage-only): **+ New Robot** (calls `CreateNewBlueprint`
  → `GarageController.Respawn` via the existing `PresetChanged`
  pipeline) and **Save Robot** (calls `SaveCurrentBlueprint` and logs
  the resulting filename). Dropdown now shows presets followed by
  user blueprints (suffixed with a ◆ glyph). Subscribes to
  `BlueprintCatalogChanged` so the picker refreshes the moment a save
  or delete completes.

**Architecture.** The `GarageController.PresetChanged → Respawn`
contract did all the heavy lifting — every blueprint mutation
(preset swap, user load, "+ New", save-then-overwrite) flows through
`GameStateController.SetCurrentBlueprint` / `SelectPreset` /
`CreateNewBlueprint`, all of which fire `PresetChanged`. The HUD
never has to talk to `GarageController` directly to refresh the
chassis on the podium.

**Save location.** `%USERPROFILE%/AppData/LocalLow/<company>/<product>/blueprints/`
on Windows. Each robot is one human-readable JSON file; users can
hand-edit, share over Discord, or paste into the future cloud-sync
flow.

**Roadmap (remaining Pass B work).**

- *Phase 2 — UX polish on save flow.* Rename inline (currently uses
  `DisplayName` from the SO; no rename UI yet). Confirm-overwrite
  dialog. Delete button next to dropdown for user blueprints. Save
  toast / status line.
- *Phase 3 — In-garage editor.* Raycast block placement tool that
  edits `CurrentBlueprint.Entries` live, validation overlay (CPU
  count, structural connectivity), part palette UI.
- *Phase 4 — Cross-cutting.* Optional cloud sync, Base64-zip
  share-via-clipboard, schema v2 if we add per-block paint colors or
  rotation.
- *Garage geometry expansion.* Awaiting user choice between
  (1) bigger physical bay + turntable, (2) multiple build pads,
  (3) editor-grid overlay, or (4) all of the above.

**Files touched.**

- Added: [BlueprintSerializer.cs](Assets/_Project/Scripts/Block/BlueprintSerializer.cs),
  [UserBlueprintLibrary.cs](Assets/_Project/Scripts/Block/UserBlueprintLibrary.cs),
  [StarterBlueprints.cs](Assets/_Project/Scripts/Block/StarterBlueprints.cs).
- Modified: [GameStateController.cs](Assets/_Project/Scripts/Gameplay/GameStateController.cs),
  [SceneTransitionHud.cs](Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs).
- Untouched (deferred): [EnvironmentBuilder.cs](Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
  garage geometry — pending user's expansion-scope answer.

---

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
