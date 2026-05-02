# 🎨 Robogame Art Direction

> Single source of truth for how Robogame **looks**. Read top-down before authoring or generating any art-adjacent code or assets. Mirrors the structure of [ROBOCRAFT_REFERENCE.md](ROBOCRAFT_REFERENCE.md) — decisions first, rationale next, open questions at the bottom.

---

## 📋 Table of Contents

- [Headline Direction](#headline-direction)
- [Style Pillars](#style-pillars)
- [Reference Touchstones](#reference-touchstones)
- [Palette](#palette)
- [Material Vocabulary](#material-vocabulary)
- [Lighting Rules](#lighting-rules)
- [Silhouette & Form Rules](#silhouette--form-rules)
- [Post-Processing Rules](#post-processing-rules)
- [Authoring Pipeline](#authoring-pipeline)
- [Imported Assets](#imported-assets)
- [Forbidden List](#forbidden-list)
- [Phasing](#phasing)
- [Open Questions](#open-questions)

---

## Headline Direction

**Robogame is stylized, graphic, and saturated.** Not realistic, not cel-shaded-anime, not low-poly-cute. Closer to **clean industrial sci-fi with hazard-stripe energy** — readable from across an arena, comfortable on a programmer's GPU, and forgiving of solo-dev rough edges.

Three sentences that should govern every visual decision:

1. **The block is the hero.** Every painterly choice serves making block placement, damage, and category instantly readable.
2. **Silhouette before surface.** A black-and-white screenshot should communicate the gameplay; surface detail is icing.
3. **Stylized means cheap.** Every shortcut a stylized look offers — flat shading, no normal maps, zero PBR drama — is a shortcut we **take**, not one we feel guilty about.

---

## Style Pillars

### 1. Graphic, not realistic
Materials read as colored shapes with edge definition. Light bounces are implied, not simulated. We use one custom block shader long-term; until then, URP/Lit + post-processing carries us.

### 2. Saturated, limited palette
A locked 12-color palette (see [Palette](#palette)). Anything that doesn't map to a palette token gets pushed back to one. No drift, no ad-hoc tints.

### 3. Hazard-industrial mood
Orange hazard stripes, slate-blue structure, exposed cyan tech, warning red. The vibe is *workshop where dangerous things get built*, not *hand-crafted artisan future*.

### 4. Damage as drama
Damaged blocks are a story beat, not a stat readout. The cyan CPU beacon already does this well — **escalate** that vocabulary, don't dilute it.

### 5. Solo-dev honest
We never ship art that requires a 40-hour authored mesh. If we can't generate it procedurally, scaffold it from primitives, or grab it CC0, we don't ship it.

---

## Reference Touchstones

In order of relevance. None of these are templates — they're tone setters.

| Reference | What we're stealing | What we're not |
|-----------|---------------------|----------------|
| **Robocraft (2014–2025)** | Voxel chassis, hazard-orange UI accents, energy-cyan tech bits, "vehicle as loadout" | Realistic-ish lighting, bloom-heavy combat, MOBA-grade FX |
| **Risk of Rain 2** | Saturated palette on stylized geometry, generous post-processing, readable enemy silhouettes | Hand-painted textures, organic creature forms |
| **Astroneer** | Clean color blocking, minimal-detail surfaces, soft-but-graphic lighting | Round-everything aesthetic |
| **Lethal Company** | Industrial mood, hard shadows, deliberately limited palette | Horror dimness |
| **Mirror's Edge (1)** | "One color does the work of a hundred props" — strict palette discipline | First-person, clean realism |
| **Tron / Tron Legacy** | Emissive accents on otherwise-dark surfaces (CPU beacon DNA) | Full-blackbox-with-neon look |

---

## Palette

The palette is **the** tightest design constraint in the project. Every authored color comes from this list. Code lives in [WorldPalette.cs](../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs); when we expand it, we expand the table here first, then mirror it in code.

### Structure & environment

| Token | Hex | Use |
|-------|-----|-----|
| `Slate` | `#2A323C` | Garage walls, neutral structural cubes |
| `SlateLight` | `#525B66` | Garage upper walls, secondary structure |
| `Concrete` | `#3F4348` | Floors, podium top |
| `Grass` | `#4D7340` | Arena ground |
| `SkyDay` | `#8CB7E0` | Arena sky / camera clear |

### Action accents

| Token | Hex | Use |
|-------|-----|-----|
| `Hazard` | `#F28C1A` | Stripes, ramps, warning markers, **player-side accents** |
| `Caution` | `#E6CC33` | Bumps, secondary hazard, low-priority warnings |
| `Alert` | `#BF333F` | Pillars, **enemy-side accents**, weapon blocks |

### Tech / energy

| Token | Hex | Use |
|-------|-----|-----|
| `Cyan` | `#33D9F2` | CPU, energy, friendly tech, beacons |
| `Cyan_Emit` | `Cyan × 4` (HDR) | CPU beacon emissive; never used as base color |
| `Plasma` | `#A155F2` | Future module abilities (shield, blink, EMP) |
| `Mint` | `#34A659` | Repair / regen / healing UI |

### UI / chrome

| Token | Hex | Use |
|-------|-----|-----|
| `UIBg` | `#0F1219` | Settings panel, HUD backdrops |
| `UIText` | `#FFFFFF` | Body copy |
| `UIDim` | `#FFFFFF @ 55%` | Modal scrim |

> Twelve tokens. If a sprite, material, light, or particle isn't one of these, **it's wrong**. The compiler can't enforce this — discipline does.

---

## Material Vocabulary

Each block category gets one canonical material identity. The shader stays URP/Lit for now (Phase 1); the *property values* are what carry style.

| Category | Base | Metallic | Smoothness | Emission | Notes |
|----------|------|----------|------------|----------|-------|
| **Structure** | `Slate` / `SlateLight` | 0.0 | 0.15 | none | Matte, slightly varied per block |
| **CPU** | `Cyan` | 0.6 | 0.7 | `Cyan_Emit` (low) | Beacon adds the loud emission |
| **Wheel** | `#202428` | 0.0 | 0.05 | none | Near-black rubber, dead matte |
| **Wheel hub** | `Slate` | 0.7 | 0.6 | none | Metallic accent |
| **Thruster** | `Hazard` | 0.4 | 0.5 | `Hazard × 2` (nozzle only) | Heat glow on nozzle child |
| **Aero / wing** | `SlateLight` | 0.0 | 0.3 | none | Slight sheen, no metal |
| **Weapon** | `Alert` | 0.5 | 0.6 | none baseline, `Alert × 3` while firing | Future: muzzle emission spike |
| **Debris** | inherits source | inherits | -0.2 from source | none | Damaged → less smooth |

> Damaged blocks already darken via `BlockBehaviour.UpdateDamageVisual`. Phase 2 adds an emissive crack pattern at low HP. Both effects are owned by the **block shader**, not added geometry — so they cost zero per-frame Update logic.

---

## Lighting Rules

One rig per scene. Tuned per location, not per object.

### Garage
- **Sun**: dim, warm, low-angle. `(45°, 30°, 0°)` Euler, intensity 1.0, color `#FFE0B0`.
- **Ambient**: `#2A2027` warm shadow color. Garage is enclosed, so ambient does most of the lifting.
- **Reflection probe**: small, baked, centered on podium.
- **Mood**: workshop at dusk. Player's robot is the brightest thing on screen.

### Arena
- **Sun**: bright, slightly cool, raked. `(50°, -30°, 0°)` Euler, intensity 1.3, color `#FFF8E0`.
- **Ambient**: `#5A6E80` cool sky tint.
- **Reflection probe**: large, baked, centered on origin.
- **Mood**: bright, exposed, no place to hide. Combat is legible at distance.

### Bootstrap
- N/A — pure black, no rendering work happens here.

### Future scenes
- **Night arena** (post-launch idea): same arena, sun off, cyan/red emissives doing all the work.

---

## Silhouette & Form Rules

These rules let new contributors (or future-me) make blocks that **fit** without asking.

1. **Voxel-aligned everything.** Every block bounding box is exactly `1m × 1m × 1m` in world units. No 1.5m hero parts.
2. **No organic curves on chassis blocks.** Cylinders, cubes, beveled cubes only. Thrusters can have a chamfered nozzle.
3. **One protruding feature per block, max.** A wheel has its hub. The CPU has its antenna. A weapon has its barrel. Don't stack features.
4. **Blocks must read at 30m.** Test: take a screenshot, scale to 256px wide, can you still tell what each block does? If not, the silhouette is too busy.
5. **Visual hierarchy via emission, not size.** CPU is small but glowy. Weapons are matte but red. Don't oversize "important" blocks.
6. **Antenna / beacon as recurring motif.** The CPU beacon vocabulary (mast + tip + light) is reused for any "hero point" — future radar block, command beacon, capture point flag.

---

## Post-Processing Rules

URP Volume profiles, one per scene, committed as assets.

| Effect | Garage | Arena |
|--------|--------|-------|
| **Bloom** | Threshold 1.1, Intensity 0.5 | Threshold 1.0, Intensity 0.7 |
| **Color Adjustments** | Contrast +10, Saturation +15, slight warm hue shift | Contrast +5, Saturation +10, neutral |
| **Tonemapping** | ACES | ACES |
| **Vignette** | Intensity 0.25 | Off |
| **Film Grain** | Off | Off |
| **Chromatic Aberration** | Off | Off (toggleable for "low HP" UI later) |
| **Motion Blur** | Off | Off (Phase 3+) |

> Bloom is the single most important effect. The cyan CPU beacon does not work without it. Tune carefully — too much and the whole screen smears.

---

## Authoring Pipeline

### Default: procedural primitives + tint

This is what the project does today via [BlockDefinitionWizard.cs](../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs) and is what we keep doing for as long as it scales. Adding a block:

1. New entry in `BlockIds.cs`.
2. New `CreateOrUpdate(...)` row in `BlockDefinitionWizard`.
3. Optional: rig children in code via `BlockVisuals` helpers.
4. Optional: marker component (like `CpuBlockMarker`) for hero blocks.

No mesh authoring, no UV unwrap, no texture export.

### Tier 2: hand-authored prefab

When a block needs more than primitives can express (curved barrel, complex thruster nozzle), the rule is:

- Author the **mesh** in Blender, exported as `.fbx` to `Assets/_Project/Art/Models/Blocks/`.
- The mesh **must** snap to the 1m voxel bounds.
- It uses the **same** URP/Lit material from `WorldPalette` — no per-mesh material.
- A new `BlockDefinition.Prefab` reference replaces the primitive.

### Tier 3: third-party CC0 mesh kits

When we hit "we need 30 environment props this week," we grab Kenney / Quaternius / Poly Pizza voxel kits. Workflow:

- Asset goes under `Assets/_Project/Art/ThirdParty/<Vendor>/<PackName>/`.
- The original **license file** is committed alongside it.
- Materials are **re-pointed** to `WorldPalette` — we never ship a third-party material on a third-party mesh, because palette drift starts there.
- A `*.import.md` next to each pack records which pieces we actually use; everything else is excluded from the build via `.gitignore` patterns.

### Tier 4: paid asset store assets

Off the table for v1 **except** the cel-shading dependency: see [Imported Assets](#imported-assets) below for what we did purchase and why. Anything else paid stays off the table — cost discipline + license-rot risk.

### Scene scaffolder conventions

Every scene-builder (`BuildGarageEnvironment`, `BuildArenaEnvironment`, future `BuildHangar`, …) follows the same idempotent shape so they don't rot into divergent code paths:

1. **One canonical clean-slate primitive.** [EnvironmentBuilder.ResetEnvRoot()](../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs) is *the* place that decides what gets nuked at the start of a scene rebuild. Every scaffolder calls it; nobody open-codes their own "find this loose GameObject and destroy it" inside a scene-specific method. When a new cleanup case shows up (legacy parent name, third-party instance kind, etc.), it gets added to `ResetEnvRoot` and every scene inherits it for free.
2. **One parent for static decor.** Everything a scaffolder spawns (ground, walls, props, terrain, accents, lights it manages) lives under the `Environment` GameObject that `ResetEnvRoot` returns. Loose GameObjects at scene root are reserved for things the builder *doesn't* own — `ArenaController`, the player chassis, the camera rig, the dummy. That split is what makes the next cleanup pass trivial: `DestroyImmediate(env)` and you're done with decor; gameplay objects are untouched.
3. **Reparent, don't open-code.** Helpers that pre-existed this convention (e.g. `SceneScaffolder.PopulateTestTerrain` creates a loose `Terrain` root) get reparented under `Environment` immediately after they run, not rewritten. Keeps the helper reusable and the scaffolder honest.
4. **Bombproof scrubs by source, not by name.** When stale instances need to be hunted down across a scene (e.g. leftover Kenney FBX trees from a removed feature), the scrub identifies them by the asset path their `sharedMesh` was imported from — `/kenney_*` etc. — never by a parent name or transform position, both of which the user can change in the editor without us noticing. See `ScrubKenneyInstances` for the pattern.
5. **Idempotent or it doesn't ship.** Re-running `Build All Pass A` ten times must produce the same scene as running it once. If a builder stamps duplicates, doubles a tint, or drops orphans, treat it as a bug.

---

## Imported Assets

Living inventory. Future-AI: when you add or remove a third-party pack, update this table in the same commit. Each entry should record where it lives, what we use it for, what license it ships under, and which scaffolder code touches it (so a missing pack fails loudly, not silently).

| Pack | Location | Used for | License | Wired by |
|------|----------|----------|---------|----------|
| **MK Toon (paid)** | `Assets/MK/MKToon/` | Cel-shaded surface look on every world material. Shader name: `MK/Toon/URP/Standard/Physically Based`. Outline variant `MK/Toon/URP/Standard/Physically Based + Outline` reserved for Phase 2 hero blocks. | Asset Store EULA (single seat) | [WorldPalette.cs](../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs) prefers it via `Shader.Find(...)`, falls back to URP/Lit if missing |
| **Polyverse Skies (free)** | `Assets/BOXOPHOBIC/Polyverse Skies/` | Arena skybox material `Skybox_Arena.mat`. Shader: `BOXOPHOBIC/Polyverse Skies/Standard`. Tinted to palette tokens (`SkyDay`, `SkyEquator`, `Grass`). | BOXOPHOBIC EULA (free) | [SkyboxBuilder.cs](../Assets/_Project/Scripts/Tools/Editor/SkyboxBuilder.cs) authors `Assets/_Project/Rendering/Skyboxes/Skybox_Arena.mat`; [EnvironmentBuilder.cs](../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs) assigns it to `RenderSettings.skybox` for the Arena scene only |
| **Cartoon FX Remaster Free** | `Assets/JMO Assets/Cartoon FX Remaster/` | Phase 3 source material (muzzle flashes, hit sparks, debris). **Not yet wired.** Will be palette-recoloured before use; never ship a third-party effect at its stock colours. | Asset Store free EULA | _none yet — Phase 3_ |
| **Fluff (paid, OccaSoftware)** | `Packages/com.occasoftware.fluff/` (UPM, embedded) | Shell-based stylized grass on the Arena ground plane. Material cloned from the shipped `Samples/Demo/Materials/Grass.mat`, then `_TopColor` and `_BaseColor` overridden to `WorldPalette.Grass` (and a darker derivative). Shader: `OccaSoftware/Fluff/Grass`. Unlike per-blade systems, Fluff is just a material swap on the ground plane — no extra renderer component, chunk grid, or blade meshes. | OccaSoftware EULA (single seat) | [FluffGround.cs](../Assets/_Project/Scripts/Tools/Editor/FluffGround.cs) clones the sample material into `Mat_ArenaFluff.mat`, palette-tints the two colour properties, and assigns it via `MeshRenderer.sharedMaterial`. Falls back to [GroundMaterial.cs](../Assets/_Project/Scripts/Tools/Editor/GroundMaterial.cs)'s procedural tile texture if `Shader.Find` returns null. Called from [EnvironmentBuilder.cs](../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs) `BuildArenaEnvironment`. The package's optional `RenderInteractiveGrass` component (player-driven grass deflection) is not yet wired — Phase 2+ task. |
| **Bitgem Stylised Water URP (paid)** | `Assets/Bitgem/StylisedWater/` | Stylised water shading on the WaterArena surface (depth fade, foam, fresnel, refraction, scrolling normal map). Shader Graph: `WaterVolume-URP.shadergraph`. Material cloned from the shipped `example-water-01.mat` and only the colours, vertex-wave knobs (zeroed — see below), and surface-detail scroll values are overridden. Bitgem's bundled `WaterVolume*` MonoBehaviours and `WateverVolumeFloater.cs` are intentionally NOT used — we keep our own [WaterVolume](../Assets/_Project/Scripts/Gameplay/WaterVolume.cs) data marker because [BuoyancyController](../Assets/_Project/Scripts/Gameplay/BuoyancyController.cs) already binds to it. **Critical wiring:** the procedural mesh must write `Color.black` to its vertex colours (the shader graph reads `vertexColor.r` as a foam mask, so the default `Color.white` produces a pure white surface — see [WaterMeshAnimator.BuildMesh](../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)). The shader's GPU vertex displacement (`_WaveScale`/`_WaveSpeed`/`_WaveFrequency`) is zeroed because [WaterMeshAnimator](../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs) owns geometry on the CPU; otherwise the visible surface drifts away from where buoyancy thinks it is. | Asset Store EULA (single seat) | [BitgemWaterMaterial.cs](../Assets/_Project/Scripts/Tools/Editor/BitgemWaterMaterial.cs) clones the demo material into `Mat_Water.mat` and stamps the overrides; [WorldPalette.WaterMat](../Assets/_Project/Scripts/Tools/Editor/WorldPalette.cs) calls it with a translucent-URP/Lit fallback if the Bitgem package is missing |
| ~~GrassFlow 2 (paid, $38)~~ | ~~`Assets/GrassFlow/`~~ | **Disconnected.** Per-blade mesh aesthetic read as obvious polygons at gameplay camera distance, even after density and shape tuning. Replaced with Fluff (shell-based) above. The package files remain on disk in case we want to revisit; only the integration code (`GrassflowGround.cs`, `Mat_ArenaGrassflow.mat`) was removed. | Asset Store EULA (single seat) | _none — superseded by FluffGround_ |
| ~~Kenney Voxel Pack~~ | ~~`Assets/_Project/Art/ThirdParty/kenney_voxel-pack/`~~ | **Wrong pack** — this is the 2D sprite voxel pack, not a 3D mesh kit. Ignored. Safe to delete. | CC0 | _n/a — orphan_ |

### Adding a new third-party pack — checklist

1. Asset goes under `Assets/_Project/Art/ThirdParty/<Vendor>/<PackName>/` *unless* the importer hardcodes a top-level folder (MK, BOXOPHOBIC, JMO Assets all do — leave them where the package put them).
2. Commit the original license file alongside.
3. Re-point any third-party material to a `WorldPalette` token before it hits a render path. **Never ship a third-party material at its stock colour.**
4. Add a row to the table above. Record the shader name(s) you'll be `Shader.Find`-ing and which builder script wires it up.
5. If the pack ships an asmdef, the consuming asmdef (usually [Robogame.Tools.Editor.asmdef](../Assets/_Project/Scripts/Tools/Editor/Robogame.Tools.Editor.asmdef)) must reference it explicitly.

### Verifying authored size before scaling — do this once per pack

Future-AI: if you find yourself guessing scale multipliers ("looks like 50× too big, let me try 0.02"), **stop and measure first.** Eyeballing scale is how we ended up with a horizon city floating around the periphery looking like ant farm props. The kits are almost always authored at the right scene scale; the bug is usually in the importer settings.

**The honest pipeline:**

1. **Find the actual authored size.** Most Kenney / itch-style kits ship `.obj` siblings next to the `.fbx` files (e.g. `kenney_platformer-kit/Models/OBJ format/`). OBJ is plain text — read the `v x y z` lines and compute bounding boxes. A ~10-line Python snippet does this. Do **not** open the FBX and trust Unity's reported size before you've enforced importer settings, because the same FBX-unit-scale bug you're trying to fix is what makes that number lie.

2. **Diagnose the importer, not the asset.** When props import giant or tiny, the cause is almost always one of:
   - **`useFileScale = true`** on the `ModelImporter`. The FBX header declares its internal unit (Kenney FBXs declare cm), and Unity multiplies every vertex by that unit's scale factor (cm → ×100). A 0.5 m crate becomes 50 m. **Fix:** `useFileScale = false`.
   - **`globalScale ≠ 1`** layered on top of the file-scale bug, "correcting" it with a fudge factor (the 0.02 I guessed). Compounds badly the moment you toggle `useFileScale`. **Fix:** `globalScale = 1.0` once `useFileScale = false`.
   - **Unit mismatch in your scaffolder** — multiplying a metre-scale prop by a centimetre-scale assumption. Check the per-prop `scale` you pass to `KenneyKit.Spawn`. A 1.5 m building × 8 = 12 m skyline silhouette. A 1.5 m building × 0.16 = ant farm.

3. **Pin the importer settings in code, idempotently.** All third-party model imports go through [KenneyKit.cs](../Assets/_Project/Scripts/Tools/Editor/KenneyKit.cs)'s `EnsureImportSettings()` (or an equivalent for non-Kenney packs). It walks the kit, sets `globalScale = 1`, `useFileScale = false`, `animationType = None`, `materialImportMode = ImportStandard`, and `SaveAndReimport`s only when something actually changed. **Never** rely on the inspector — Unity has been known to silently revert importer settings on package upgrade.

4. **Pin texture import settings the same way.** For palette/colormap textures (Kenney's shared `colormap.png`), force `FilterMode.Point`, `WrapMode.Clamp`, and uncompressed. Bilinear filtering muddies the cel look; compression introduces palette ghosting. Same idempotent re-import dance as above.

5. **Tile, don't stretch.** When a texture is meant to repeat (Kenney pattern pack tiles, brick patterns, etc.), set its tiling on the *material*, not by scaling the mesh UVs. `Material.SetTextureScale` keeps geometry sane and lets you tune density per category — see [BlockTextures.cs](../Assets/_Project/Scripts/Tools/Editor/BlockTextures.cs)'s `DefaultTiling` knob.

6. **Verify with a known reference.** Spawn one prop next to the player chassis (≈1.6 m tall) before scaffolding the whole arena. If a "factory" reads as knee-high to the player, something's wrong; don't fix it by inflating the per-prop scale to compensate, fix the importer.

**Forbidden cargo-cult:** any constant of the form `0.02f` / `50f` / `100f` floating in the codebase as a "scale fudge" without a comment that traces it to either an importer setting or an explicit measured size. If we can't justify the number from the OBJ bounds + importer config, it's a bug we haven't found yet.

---

## Forbidden List

Things we will **not** ship:

- ❌ **Asset-store shaders.** Stock URP/Lit or one custom shader we own. Nothing in between. (See README discussion of codebase integrity.)
- ❌ **Realistic textures.** No 4K wood-grain, no concrete-with-cracks, no rust-and-grime PBR sets. Stylized only.
- ❌ **Normal-mapped surface detail.** If a surface needs visual interest, it gets a chamfer or a decal, not a normal map.
- ❌ **Soft particles** for combat FX. Hard-edged, alpha-clipped, palette-locked.
- ❌ **Per-block unique material instances.** Always go through `MaterialPropertyBlock` for variation, never `Renderer.material`.
- ❌ **Hand-painted SDF / hand-tweaked vertex colors per block.** Procedural-only color authoring.
- ❌ **Random-color blocks.** Every color is a palette token.
- ❌ **Off-palette emission.** Cyan, hazard, alert, plasma, mint. That's it.

---

## Phasing

Roughly aligned with the engineering phases in [README.md](../README.md).

### Phase 1 — Look layer (this art pass)
Everything in this doc that's procedural / shader / volume.

- [x] Per-scene URP Volume profiles (`PostProfile_Garage.asset`, `PostProfile_Arena.asset`) — authored by [PostProcessingBuilder.cs](../Assets/_Project/Scripts/Tools/Editor/PostProcessingBuilder.cs)
- [x] Skybox material wired into `EnvironmentBuilder` — Polyverse Skies via [SkyboxBuilder.cs](../Assets/_Project/Scripts/Tools/Editor/SkyboxBuilder.cs) (Arena only; Garage stays dark)
- [x] Lighting rig refactored to match this doc's [Lighting Rules](#lighting-rules) — warm sun in garage, raked cool sun in arena, trilight ambient
- [x] `WorldPalette.cs` expanded to the full 12-token table
- [x] All existing materials migrated to palette tokens, default shader switched to MK Toon (URP/Lit fallback)
- [ ] Tone-pass on the existing scaffolder logs / debug HUDs to use palette UI tokens — deferred to a UI-focused session

### Phase 2 — Block shader
**Path chosen: MK Toon's `Physically Based + Outline` variant for hero blocks**, `Physically Based` for everything else. We don't author a custom shader unless MK Toon hits a wall.

- [x] Per-category materials authored under `Assets/_Project/Materials/Blocks/` by [BlockMaterials.cs](../Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs)
- [x] Hero blocks (CPU, Weapon, Thruster) on the `+ Outline` variant; Structure / Wheel / Aero on the unlit-edge `Physically Based` variant
- [x] [BlockDefinition.cs](../Assets/_Project/Scripts/Block/BlockDefinition.cs) gained a `Material _material` slot; [BlockGrid.cs](../Assets/_Project/Scripts/Block/BlockGrid.cs) `PlaceBlock` swaps `sharedMaterial` (no per-renderer instances → batching survives at 10k+ blocks)
- [x] Damage darkening migrated to `MaterialPropertyBlock` writing `_AlbedoColor` (MK Toon) + `_BaseColor` (URP/Lit fallback). No more `Renderer.material` churn.
- [ ] MK Toon Per Object Outlines renderer feature added to `Assets/Settings/PC_Renderer.asset` (silences the import warning *and* enables the outline pass)
- [ ] Damage 0–1 → MK Toon `_Hue`/`_Saturation`/`_Brightness` for hue-shift cracks (Phase 2.1)
- [ ] Reserved keyword for hero blocks (CPU, weapon-firing) to enable extra emission band

### Phase 3 — VFX
- [ ] Muzzle flash, hit sparks, debris dust, thruster plume
- [ ] All `ParticleSystem` (no VFX Graph dep yet)
- [ ] Palette-locked

### Phase 4 — Cinemachine + cameras
- [ ] Garage turntable cam
- [ ] Arena combat cam (subtle look-at-impact)
- [ ] Death cam (arc-around debris)

### Phase 5 — Optional asset integration
- [ ] Kenney Voxel Pack as environment prop kit (you fetch, I integrate)
- [ ] Optional: stylized skybox cubemap from Poly Haven if procedural ceiling is hit

### Phase 6 — Audio (out of scope here, but unblocks "feel")

---

## Open Questions

1. ~~**Cel-shaded or just stylized URP/Lit?**~~ **Resolved (Apr 30 2026): cel-shaded via MK Toon.** PBS shader gives us a soft cel diffuse out of the box; URP/Lit kept as the fallback for environments where MK Toon isn't appropriate (UI quads, debug helpers). See [Imported Assets](#imported-assets).
2. **Emission policy for damaged blocks.** Crack pattern emissive (Tron-leaning) vs. matte cracks (Astroneer-leaning). Lean Tron — fits the CPU motif. Decide at Phase 2.
3. ~~**Outline rendering.**~~ **Resolved: MK Toon's `+ Outline` shader variants** for hero blocks (CPU, weapons, selected-in-garage). Per-object inverted-hull is fine at our block count once we cap which categories opt in. Screen-space outline (Robin Seibold's MIT impl.) stays as a fallback if we ever want a uniform outline on every block.
4. **Skybox: procedural gradient or 2-color "painted" gradient shader?** Procedural is one line; painted gradient gives more art control. Try procedural first.
5. **Day/night arenas.** Cool aesthetic, but doubles the lighting tuning surface area. Park until post-launch.
6. **Dynamic palette swaps for team colors** (multiplayer phase). Faction A is hazard-orange, Faction B is plasma-purple? Easy if we're rigorous about palette tokens now, painful if we drifted.
7. **HUD tone.** Currently functional / placeholder. Settings panel uses dark slate + hazard. UI direction probably deserves its own doc once it grows past "Esc panel + scene transition button."
8. **Decals.** Worth using URP's decal projector for things like scorch marks and team logos? Probably yes; defer until Phase 3.

---

*Last updated: April 30, 2026 — Phase 1 art pass shipped (MK Toon + Polyverse Skies + URP volumes).*
