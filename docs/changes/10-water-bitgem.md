# Session — Water visuals: Bitgem shader + Gerstner mesh + DevHud waves slider

**Intent.** Two threads merged into one push. Earlier in the session we
locked in the Fluff grass tuning that had been drifting for several
iterations; then user pivoted: *"let's get water texture/shading into
the water arena before i go too far in other directions."* Goal:
replace the flat translucent-teal placeholder with a stylised water
shader, keep our CPU-driven Gerstner waves authoritative for buoyancy,
and add live tuning so sea state can be dialled without editing code.

**Shipped.**

- New [BitgemWaterMaterial.cs](../../Assets/_Project/Scripts/Tools/Editor/BitgemWaterMaterial.cs).
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
  [WorldPalette.WaterMat](../../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  falls back to its old translucent-URP/Lit factory and headless
  builds keep working.

- Patched [WaterMeshAnimator.cs](../../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs).
  Now writes an all-black `Color[]` to the procedural mesh's vertex
  colours. Bitgem's shader graph reads vertex colour as a foam mask
  (red = foam edge, black = open water) — Unity's default `Color.white`
  was the smoking gun behind the *"water is straight up 100% white"*
  bug. URP/Lit fallback ignores the channel, so it's harmless either
  way.

- Re-tuned wave defaults in [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs).
  Old defaults (amp 0.30, λ 12, speed 1.5, steepness 0.30) read as
  fizzy ripples on a 220 m arena. New defaults: **amp 1.20 m, λ 30 m,
  speed 2.0 m/s, steepness 0.45** — gives a slow ~15 s dominant period
  with crests that actually peak. Amplitude max bumped 2 → 4, length
  max 40 → 80 to allow stormier sea state if we ever want it.

- Added a **▼ Waves** collapsible section to [DevHud.cs](../../Assets/_Project/Scripts/UI/DevHud.cs)
  with live `HorizontalSlider` controls for Wave Amplitude / Length /
  Speed / Steepness, plus a **Reset Waves** button. Generic
  `DrawTweakSlider(key)` helper reads min/max/label straight from the
  Tweakables spec, so adding more sections later (Buoyancy, Plane,
  Ground) is one line per slider. HUD is now wider (260 px) and
  scrollable when the section expands.

**Grass lock-in (same session).** Final values baked into
[FluffGround.cs](../../Assets/_Project/Scripts/Tools/Editor/FluffGround.cs)
after several rounds of camera-frame and tile-repeat fights:
shell count 16, max height 1.2 m, world scale 65, world-space sampling
(`_TextureSamplingMethod = 1`), shape noise scale 2.7 / strength 0.15,
detail noise scale 1.0 / strength 0.65. New `AssignNoiseTexture()`
helper pins `grass-noise-23` (shape) and `grass-noise-14` (detail) by
name from the package's `Runtime/Textures/Noise/` folder so re-running
Build Arena always re-stamps the user-locked picks. Camera also
lowered for grass framing in [FollowCamera.cs](../../Assets/_Project/Scripts/Player/FollowCamera.cs)
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

- Added: [BitgemWaterMaterial.cs](../../Assets/_Project/Scripts/Tools/Editor/BitgemWaterMaterial.cs).
- Modified: [WorldPalette.cs](../../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs)
  (`WaterMat` getter now prefers Bitgem),
  [WaterMeshAnimator.cs](../../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)
  (vertex-colour foam mask),
  [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs) (wave defaults + maxes),
  [DevHud.cs](../../Assets/_Project/Scripts/UI/DevHud.cs) (Waves section + slider helper),
  [FluffGround.cs](../../Assets/_Project/Scripts/Tools/Editor/FluffGround.cs) (locked grass values + noise pinning),
  [FollowCamera.cs](../../Assets/_Project/Scripts/Player/FollowCamera.cs) (lowered framing).
- Asset: `Assets/_Project/Materials/Mat_Water.mat` regenerated by the
  factory on next Build Arena Pass A.
