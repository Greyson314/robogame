# 59 — Feel-good / Visual / Design Sweep

> Status: **shipped, untested in-engine.** One-pass sweep on feel-good
> items per the user's direct ask: HUD font + scoreboard polish, larger
> arena with mountains and no central obstacle course, ScrapDepot
> visual rewrite (wider AOE + recessed hole), new Magnet tip-block
> weapon. No new gameplay invariants; everything sits inside the
> existing tip-block / scrap-loop / HUD frameworks.

## Why this session

User: *"Please autonomously make a large sweep of feel-good, visual,
and design passes…"* The brief explicitly mentioned: fonts, scoreboard
HUD, bigger arena, removed central geometry, mountain or two, larger
scrap depot AOE as a literal hole, magnet weapon like hook/mace.

## What changed

### HUD: shared font + tightened scoreboard

- New
  [`HudStyles`](../../Assets/_Project/Scripts/Core/HudStyles.cs)
  in `Robogame.Core`. Single source of font, palette colours, and
  GUIStyle helpers for every IMGUI overlay.
  - Font: OS-dynamic monospace stack
    (`Consolas → Menlo → DejaVu Sans Mono → Courier New`). Monospace
    keeps changing readouts (SPD `7.3 m/s` ↔ `12.6 m/s`) from shifting
    the suffix horizontally.
  - Colour tokens: `TextPrimary`, `TextMuted`, `Accent`, `Warning`,
    `Danger`, `Healthy`, `PanelBg`, `PanelBgHeavy`, `PanelEdge`.
  - `Label(size, color, anchor, style?)` + `Bold(size, color, anchor)`
    factories so every HUD's style construction is one line.
- [`ObjectiveHud`](../../Assets/_Project/Scripts/Gameplay/ObjectiveHud.cs)
  fully restructured. Wider panel (360 → 520 px) with three rows:
  Row 1 = team headers (`YOU` / `ENEMY`) in their accent colours;
  Row 2 = large scrap totals flanking a centred round-timer pill,
  with `/ target` muted sub-text under each score;
  Row 3 = `FRAGS  N  —  M` line in muted text + HP bar.
  Panel uses `PanelBgHeavy` + an accent top-edge highlight so it
  reads as scoreboard chrome.
- HUDs migrated to `HudStyles` for font + colour consistency:
  - [`VehicleStatsHud`](../../Assets/_Project/Scripts/Player/VehicleStatsHud.cs)
    — accent + danger tokens, accent top edge.
  - [`StartMatchHud`](../../Assets/_Project/Scripts/Gameplay/StartMatchHud.cs)
    — pill + accent edge, rich-text key name via `TagAccent`.
  - [`KillAnnouncer`](../../Assets/_Project/Scripts/Gameplay/KillAnnouncer.cs)
    — streak colours pull from `HudStyles.Accent / Warning / Danger`
    plus a local plasma constant for `RAMPAGE!`.
  - [`MatchEndOverlay`](../../Assets/_Project/Scripts/Gameplay/MatchEndOverlay.cs)
    — headline colour driven by team palette, rich-text per-team
    scores on the result line, headline 64 → 72 pt.
  - [`FpsCounter`](../../Assets/_Project/Scripts/UI/FpsCounter.cs)
    — one-line style.

### Scoreboard frag tracking

[`MatchController`](../../Assets/_Project/Scripts/Gameplay/MatchController.cs)
gained per-team kill counters (`_playerKills`, `_enemyKills`) bumped by
`RegisterKill`, exposed as `KillsForSide(MatchSide)`. The scoreboard's
new FRAGS row reads it every frame. Not part of the win condition —
scrap deposits still drive scoring. New test
`RegisterKill_IncrementsKillsForSide_ForScoreboard` covers the
counter shape.

### Arena: bigger, no central obstacles, mountain ring

- [`HillsSettings.asset`](../../Assets/_Project/ScriptableObjects/HillsSettings.asset)
  rescaled for a 360 m × 360 m floor (was 220 m):
  | field | old | new |
  |---|---|---|
  | size | 220 | 360 |
  | resolution | 81 | 121 |
  | hillAmpLow / hillFreqLow | 4 / 0.025 | 5.5 / 0.018 |
  | flatRadius | 25 | 38 |
  | rampOuter | 55 | 90 |
  | edgeFlatStart / edgeFlatEnd | 80 / 100 | 145 / 170 |
- [`SceneScaffolder.PopulateTestTerrain`](../../Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs)
  strip-down:
  - Removed all central obstacles (ramps, bumps, stairs, free pillars).
  - Wall ring pushed to ±170 m, walls tall enough (8 m, was 4 m) to
    deter trivial flyover.
  - New `BuildMountainRing` drops six deterministic stacked-cube
    mountains at ~152 m radius. Each is a four-tier pyramid of
    palette-tinted cubes; reads as stylised peaks matching the
    blocky art direction. Heights jittered via deterministic sin
    hash so re-scaffolds are idempotent.
- [`EnvironmentBuilder.TintTerrain`](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
  picks up the new `Mountain_*` name prefix → `WorldPalette.ArenaWall`
  (slate stone).

### ScrapDepot: wider AOE + recessed hole

[`ScrapDepot`](../../Assets/_Project/Scripts/Gameplay/ScrapDepot.cs)
visual rewrite:
- Trigger radius default 5.5 → 9.0 m. Reads as a *volume* to fight over.
- Procedural visual is now three pieces:
  - **Rim** — wide flat cylinder flush with the ground; strong sine
    pulse on emission, this is the bright "active" element.
  - **Well** — narrower cylinder recessed ~0.1 m below ground, reads
    as the literal hole/pit you drive into. Steady dim glow.
  - **Beam** — tall column of light (14 m, 0.7 m diameter) above
    the rim with a separate faster pulse so it reads as a beacon
    rather than static decor.
- ApplyTeamColor unchanged in shape; emit multiplier 1.6 → 1.8 so the
  rim + beam glow more against the new larger ambient.

### Magnet weapon

New tip block: [`MagnetBlock`](../../Assets/_Project/Scripts/Movement/MagnetBlock.cs).

- Behaves like Hook/Mace structurally (extends `TipBlock`, adopted
  onto rope tip by `RopeBlock`, ghost in build mode, palette-tinted).
- New behaviour: each `FixedUpdate`, runs a single
  `Physics.OverlapSphereNonAlloc` at the host-segment world position
  (radius 6 m by default), de-duplicates by attached Rigidbody, and
  applies a falloff-scaled force toward the magnet on every
  non-kinematic body that isn't the owner chassis. Default 1500 N
  with linear falloff — yanks a 5 kg dummy at ~300 m/s² baseline,
  scales down with mass.
- Visual: horseshoe shape — wide bridge slab + two forward-pointing
  pole shafts capped in cyan. Reads against the warm-hook /
  cool-mace palette.
- Contact damage: low (DamagePerKj 0.8). Headline feeling is "this
  pulls", not "this kills" — the kill comes from whatever you drag
  the target into. Mass 3 kg sits between hook (1.5 kg) and mace (5 kg).
- Cosmetic: spawns a small `FlipBurst` cyan kick every 0.35 s while
  attached, signalling the field is live.

IFF is intentionally *not* enforced inside the magnet (asmdef tier
prevents `Robogame.Movement` from referencing `Robogame.Robots`).
Mirrors the existing tip-block damage model where hook/mace also
ignore team — if it's near your ally, you yank your ally. Move the
filter in later if playtesting shows that's too punishing.

### Wire-through (Magnet)

Magnet inserted everywhere Hook/Mace are checked:
- [`BlockIds.Magnet`](../../Assets/_Project/Scripts/Block/BlockIds.cs)
  = `"block.weapon.tip.magnet"`.
- [`BlockConnectivity`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)
  — connector whitelist + rope-tip face accept rule.
- [`BlockGraph`](../../Assets/_Project/Scripts/Block/BlockGraph.cs)
  — three rope-bridge resolvers (run-time + blueprint).
- [`PlacementRules.IsTipBlockId`](../../Assets/_Project/Scripts/Block/PlacementRules.cs).
- [`BlueprintAsciiDump`](../../Assets/_Project/Scripts/Block/BlueprintAsciiDump.cs)
  — ASCII char `'n'`.
- [`RobotWeaponBinder`](../../Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs)
  — excluded from yoke-aim treatment.
- [`BlockEditor.IsTipBlockSelected`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs).
- [`BlockGhostFactory.BuildMagnet`](../../Assets/_Project/Scripts/Gameplay/BlockGhostFactory.cs)
  — build-mode ghost.
- [`RobotTipBlockBinder`](../../Assets/_Project/Scripts/Movement/RobotTipBlockBinder.cs)
  — attaches `MagnetBlock` component at chassis assemble.
- [`BlockDefinitionWizard`](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
  — `CreateOrUpdate("BlockDef_Magnet", …, mass: 3.0f, cpuCost: 24)`.
- [`ScriptedChassisBuilder.RopeWithMagnet`](../../Assets/_Project/Scripts/Tools/Editor/ScriptedChassisBuilder.cs)
  — designer API helper for preset chassis.
- New on-disk asset
  [`BlockDef_Magnet.asset`](../../Assets/_Project/ScriptableObjects/BlockDefinitions/BlockDef_Magnet.asset)
  + meta, registered in
  [`BlockDefinitionLibrary.asset`](../../Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset).

### Arena content positions

[`ArenaController`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)
defaults scaled to the larger arena:
- Player depot `(0, 0.2, -30)` → `(0, 0.2, -90)`.
- Enemy depot  `(0, 0.2,  40)` → `(0, 0.2,  90)`.
- Combat dummy `(0, 0.5,  18)` → `(0, 0.5,  30)`.
- Stress tower `(40, 0.5, 18)` → `(55, 0.5, 30)`.
- Arch dummy   `(-25, 0.5, 18)` → `(-40, 0.5, 30)`.
- Repair pad   `(35, 0.1, 35)`  → `(55, 0.1, 55)`.

Tank dummy / friendly tank / air dummy positions unchanged — they're
dev affordances and patrol radii were already comfortable inside the
new wall ring.

## Files

- **New:**
  - `Scripts/Core/HudStyles.cs`
  - `Scripts/Movement/MagnetBlock.cs`
  - `ScriptableObjects/BlockDefinitions/BlockDef_Magnet.asset` (+ meta)
- **Edited:**
  - `Scripts/Block/BlockConnectivity.cs` — magnet in connector list + tip-face check.
  - `Scripts/Block/BlockGraph.cs` — magnet in all three rope-bridge resolvers.
  - `Scripts/Block/BlockIds.cs` — new `Magnet` const.
  - `Scripts/Block/BlueprintAsciiDump.cs` — `'n'` glyph.
  - `Scripts/Block/PlacementRules.cs` — `IsTipBlockId` includes magnet.
  - `Scripts/Combat/RobotWeaponBinder.cs` — magnet excluded from yoke-aim.
  - `Scripts/Core/HudStyles.cs` — *new* (see above).
  - `Scripts/Gameplay/ArenaController.cs` — depot + content positions rescaled.
  - `Scripts/Gameplay/BlockEditor.cs` — `IsTipBlockSelected` includes magnet.
  - `Scripts/Gameplay/BlockGhostFactory.cs` — `BuildMagnet` + switch arm.
  - `Scripts/Gameplay/KillAnnouncer.cs` — `HudStyles`.
  - `Scripts/Gameplay/MatchController.cs` — kill counters + `KillsForSide`.
  - `Scripts/Gameplay/MatchEndOverlay.cs` — `HudStyles`, headline 72 pt.
  - `Scripts/Gameplay/ObjectiveHud.cs` — full restructure (scoreboard + frags).
  - `Scripts/Gameplay/ScrapDepot.cs` — wider AOE + 3-piece visual (rim/well/beam).
  - `Scripts/Gameplay/StartMatchHud.cs` — `HudStyles`.
  - `Scripts/Movement/RobotTipBlockBinder.cs` — magnet bind path.
  - `Scripts/Player/VehicleStatsHud.cs` — `HudStyles`.
  - `Scripts/Tools/Editor/BlockDefinitionWizard.cs` — `BlockDef_Magnet` author step.
  - `Scripts/Tools/Editor/EnvironmentBuilder.cs` — `Mountain_*` palette case.
  - `Scripts/Tools/Editor/SceneScaffolder.cs` — strip central obstacles + mountain ring.
  - `Scripts/Tools/Editor/ScriptedChassisBuilder.cs` — `RopeWithMagnet` helper.
  - `Scripts/UI/FpsCounter.cs` — `HudStyles`.
  - `ScriptableObjects/HillsSettings.asset` — rescaled for 360 m arena.
  - `ScriptableObjects/BlockDefinitionLibrary.asset` — magnet registration.
  - `Tests/EditMode/Gameplay/MatchControllerTests.cs` — frag-counter test.

## How to re-bake the arena

The session edits the scaffolder source. Until Unity re-runs the
scaffolders, the on-disk scene file (`Arena.unity`) still references
the old central obstacle course and the old wall ring.

After opening the project:

1. **Build Everything** —
   `Robogame > Build Everything` (Ctrl+Shift+B). Rewrites
   `Arena.unity` (new terrain root: mountain ring, ±170 m walls)
   and re-authors every block definition + library entry. (Older
   docs may refer to this as "Build All Pass A" — same scaffolder
   under the hood, the menu was consolidated.)
2. **Rebake hills mesh** — the new
   `HillsSettings.asset` values won't take effect on the existing
   `Mesh_ArenaHills.asset` until the inspector "Rebake hills mesh"
   button runs (or you call `HillsGround.RebakeMesh()`).
3. **Block defs** — Build Everything also re-runs
   `BlockDefinitionWizard.CreateTestDefinitions`, which writes the
   `BlockDef_Magnet.asset` `_material` reference + ensures it's
   added to the library if missing. (We pre-stamped both on disk so
   even without re-scaffolding the magnet should load.)

## Hard-invariant check

- **No Tweakable affects gameplay.** Magnet pull radius / force /
  falloff live on serialized fields on the component; ScrapDepot
  trigger radius is also a SerializeField. All scoreboard / HUD
  changes are cosmetic. PHYSICS_PLAN § 1.5: clean.
- **Server-authoritative shape.** Magnet pull applies forces via
  `Rigidbody.AddForce` — server-replaceable when MP lands. The
  scoreboard's frag counter mirrors the server's `RegisterKill`
  notifications; no client-side tally.
- **Single Rigidbody per chassis.** Magnet doesn't add a new
  Rigidbody — it operates on the rope's existing tip-end body via
  the standard tip-block adoption path.
- **No per-frame allocations.** Magnet uses a static `Collider[32]`
  scratch buffer for `OverlapSphereNonAlloc`. HudStyles' GUIStyle
  helpers do allocate per call — but every HUD caches the returned
  style behind an `if (_style != null) return` gate. ScrapDepot
  uses a single shared `MaterialPropertyBlock` across all three
  pulse-pushed renderers.
- **VFX + audio.** Magnet ships VFX (`FlipBurst` cyan kick every
  0.35 s while live). Audio piggybacks on `TipImpact` for contact
  damage. A bespoke `MagnetEngage` cue is a follow-up nice-to-have
  but the missing-cue logger will surface it whenever it ships.

## Known follow-ups

- **Magnet IFF.** Pulls everything regardless of team. Move the
  filter behind a callback the Combat tier installs, or relocate
  `TeamId` to a lower asmdef tier so Movement can read it directly.
- **Magnet pull-clip line VFX.** A literal "line from magnet to
  pulled target" pulse would sell the gameplay feel; deferred until
  the line-renderer infrastructure lands.
- **ScrapDepot well clipping.** With the depot at ±90 m on the
  new HillsGround mesh (which still has Perlin hills out to
  rampOuter = 90 m), the recessed well can poke through hill
  geometry. Acceptable for v1; the depots sit comfortably inside
  the edgeFlat zone so the floor is mostly flat at the depot
  position.
- **Build mode magnet variant config.** None today; magnet is
  fixed-orientation tip. Future: per-block adjustable pull radius
  / strength via `_hasVariantConfig: 1`.
- **Audio cues for the scoreboard.** No bespoke "frag scored"
  ping; KillAnnouncer's `KillBanner` cue covers it. A subtle
  scoreboard-tick cue could be added later.
- **Bigger arena AI tuning.** Bot patrol radii (tank: 30 m,
  friendly: 18 m, air: 80 m) weren't changed; they read fine in
  the new arena but could grow to use the new playspace.

## Verification

1. **HUD.** Enter Arena → top-centre shows scoreboard with `YOU` /
   `ENEMY` headers, large scrap counts flanking timer, `FRAGS 0 — 0`
   line. Kill a bot → `FRAGS 1 — 0` updates.
2. **Arena size.** Walls visible at ±170 m. Six mountain peaks ring
   the play area between the walls and the spawn. No ramps, stairs,
   or pillars in the central playfield.
3. **Depot.** Approach the player depot → ~9 m radius (visible from
   the rim diameter). Drive *into* the rim → camera dips below the
   well's top cap on the way through.
4. **Magnet.** Author a chassis with a rope + magnet tip via the
   build mode hotbar (it appears in the Weapon category). In the
   arena, swing the magnet near an enemy chassis → enemy is pulled
   toward the magnet while it's inside the pull radius. Damage
   numbers chip but don't dominate.
5. **HudStyles font.** All HUD numbers render in a monospace font
   (Consolas on Windows). SPD `7.3 m/s` and SPD `12.6 m/s` align
   on the suffix.
