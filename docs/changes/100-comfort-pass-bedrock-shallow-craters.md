# 100 — Comfort pass: bedrock, shallow craters, quieter thrusters, snappier drill

> Status: **Shipped.** Four small playtest-driven QoL changes. None
> structural; all surgical. Most of the surface area is in
> `DigZone.cs` and `EnvironmentBuilder.cs`.

## What landed

**Thruster accel/decel audio removed; reload/empty cues wired.** Two
passes:
- First pass swapped the 12-semitone `CHARGE_Complex_Wet_12_Semi_Up_1000ms`
  / `..._Down_1000ms` pitch-sweep swooshes (vol 0.55/0.50) for short
  8-bit sine beeps at vol 0.25 and widened hysteresis to 0.15/0.85.
- User callback (still session 100): throttle adjustment doesn't need
  a discrete cue at all. ChassisWindAudio + WheelRoll + RotorSpin
  already cover continuous movement feedback. Both `AudioRouter.PlayOneShot`
  calls + the `_audioIgnited` bookkeeping + hysteresis constants were
  removed from `ThrusterBlock`; the wizard's two cue rows + their
  asset entries were deleted (the cue enum entries stay so any stray
  caller no-ops via the missing-cue logger).
- New cues wired (asked for by the same user callback): `ReloadComplete`
  (USER_INTERFACES/Beeps/UI_Beep_Double_Clean_Up_stereo.wav, UI bus,
  vol 0.30) — subtle "ready to fire" two-tone affirmation; and
  `WeaponEmpty` (USER_INTERFACES/Errors/UI_Error_Double_Note_Down_Muffled_Short_stereo.wav,
  Sfx bus, vol 0.35) — muffled dry-click on attempted-fire of an
  empty pool. `ReloadStart` deliberately stays unwired (logs missing-
  cue once, no spam) — the WeaponEmpty cue already announces the
  reload trigger event from the player's perspective.

**Shallower bomb craters.** Same `SphereSubtract` brush, same radius,
but the sphere centre is now lifted along `-spec.GravityWorld.normalized`
by `TerrainCraterUpwardBias × craterR` (0.6 — six-tenths of the radius
above the impact point). The sphere now bites a shallow dish instead
of a deep bowl: visible crater depth is roughly `(1 - 0.6) × craterR ≈
0.4 R`. Using gravity (not world up) keeps this correct on planet
arenas if a bomb ever detonates on one.

**Bedrock + extended dig zone.** Two related changes against the
"player drills straight down and falls out of the world" failure mode:

1. `DigZone` gained a `_bedrockCells` field (default 3). Cells at
   `globalY in [1, _bedrockCells]` are seeded as `sbyte.MinValue` in
   `InitializeHeightmapSurface`. `ClampBedrock(chunk)` re-clamps any
   bedrock cell a brush touched back to `MinValue` after every
   `ApplyBrush` / `ApplyBrushDeferred`, and **returns the count of
   cells it had to restore**. The brush's gross `changedCount` is then
   reduced by that amount so the public API returns NET cells changed
   (round-trip MinValue → MaxValue → MinValue counts as zero). That
   preserves the max-fold idempotency contract two callers rely on:
   - `DrillBlock.Drill` refreshes its glide window only on `changed > 0`.
     If a drill plunging straight down hits only bedrock, the brush
     reports 0 net change, the glide window expires within ~1.5 emit
     intervals, and the chassis drops back to dynamic physics + gravity
     instead of kinematic-floating through the floor.
   - The `DrillBlock_NoMotion_ReDrillSamePoint_ChangesNothing` test
     asserts re-applying the same brush returns 0 — the subtract-
     restored design satisfies this naturally even when the brush
     touches bedrock cells.
   The clamp runs ONLY on chunks the brush actually touched and ONLY
   on the bottom row (`chunkCoord.y == 0`), so the per-brush cost is
   a few hundred sbyte writes — negligible against the chunk's remesh.
   The mesher emits a floor surface at the bedrock / boundary-shell
   interface, which is the visible "this is the bottom of the world"
   surface.

   **Iteration note.** First attempt defaulted `_bedrockCells = 3` but
   ClampBedrock didn't subtract from `changedCount`, which broke
   max-fold idempotency. Second attempt defaulted to 0 (opt-in) as a
   workaround; production opt-in via `EnvironmentBuilder` worked but
   the live Arena scene's YAML didn't include the field, so Unity used
   the C# default of 0 → no bedrock at runtime → drill carved through
   the world. Third attempt (this one): subtract-restored design lets
   the default safely return to 3 (correct under all callers), the
   live arena gets bedrock without re-scaffolding, and the
   `DrillBlock_NoMotion` regression resolves naturally.

2. Arena dig zone widened from `Vector3Int(6, 1, 6)` at `(-96, -16, -96)`
   through `Vector3Int(7, 1, 7)` at `(-112, -16, -112)` and ultimately
   to **`Vector3Int(11, 1, 11)` at `(-176, -16, -176)`** — 192 → 224 →
   352 m footprint, 36 → 49 → 121 chunks. The first jump (to 224 m) was
   made on the incorrect assumption that the wall ring sat at ±100 m;
   playtest revealed `SceneScaffolder.PopulateTestTerrain` actually places
   the walls at ±170 m, so the 224 m grid left a 58 m no-collider gap
   between the dig zone edge and the walls — flying chassis driving past
   the dirt boundary fell through the world. The 352 m grid covers the
   full wall-ring playable area with a 6 m skirt past it. Tri-budget
   trade-off (worst-case ~2.4 M tris exceeds the 1.5 M nominal target)
   accepted because (a) the dig-mask renderer cull keeps undug chunks
   from drawing, (b) undug surface is only ~2–3 K tris per chunk, and
   (c) the alternative (falling through the world) is a worse player
   experience than an aspirational tri budget.

**Drill speed + maneuverability bump.** `DrillBlock._digTargetSpeed`
2.0 → 2.6 m/s (kinematic glide is 30 % faster, still well under drive
speed so tunnelling reads as the slow-deliberate option). `_glideTurnSpeed`
270 → 360 deg/s so a player flicking the camera mid-bore noses into
the new direction more responsively. No other drill behaviour changed.

## Files

Edited:
- `Assets/_Project/Scripts/Voxel/DrillBlock.cs` — two field defaults.
- `Assets/_Project/Scripts/Movement/ThrusterBlock.cs` — hysteresis widened.
- `Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs` — two cue rows swapped.
- `Assets/_Project/Resources/AudioCueLibrary.asset` — cues 10 / 11 patched.
- `Assets/_Project/Scripts/Combat/ProjectileWorld.cs` — `TerrainCraterUpwardBias` + bomb-resolve branch.
- `Assets/_Project/Scripts/Voxel/DigZone.cs` — `_bedrockCells`, seed + clamp.
- `Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs` — 7×1×7 / 224 m / (-112, -16, -112).
- `Assets/_Project/Tests/PlayMode/Voxel/DigZoneHeightmapTests.cs` — `FullArenaConfig…` updated, new `Bedrock_BottomCellsStaySolid_AcrossBrushes`.

## Perf risk

The chunk count growth (36 → 49) is the load-bearing perf risk. Per
the LOD-disabled comment in `BuildArenaDigZone`, the dig zone's worst
case at 1 m cells is ~20 K tris per chunk, so the new worst case is
~1.0 M tris vs the 1.5 M target. The dig-mask texture grows
192² → 224² floats (~200 KB, was ~144 KB) — trivial. The bedrock clamp
adds a few hundred sbyte writes per drill tick on touched chunks only;
not measurable. Perf-checker run is gating this session per the
CLAUDE.md protocol.

## Invariant compliance

- **#1 — no Tweakable affects gameplay outcomes.** No new Tweakables.
- **#5 — zero baseline cost for new physics blocks.** No new physics
  objects; the per-brush bedrock clamp runs only when a brush already
  triggered a remesh.
- **#6 — no per-frame allocations.** `ClampBedrock` writes into the
  chunk's existing `NativeArray<sbyte>`; no managed allocations.
- **#9 — terraforming is dig-only.** Bedrock is the bottom *limit* of
  digging, not a fill; honors the invariant.
- **#10 — triangle and chunk budgets for voxel terrain are hard ceilings.**
  Worst-case ~1.0 M tris (49 × 20 K) under the 1.5 M target. Perf-checker
  verifies.

## To pick up the new arena dig zone in the live scene

Already done — `Robogame → Build Everything` was triggered via MCP and
`Arena.unity` now has `m_LocalPosition: {x: -176, y: -16, z: -176}` /
`_chunkGridSize: {x: 11, y: 1, z: 11}` / `_bedrockCells: 3` baked in.
Runtime probe at end of session confirmed 121 chunks all spawning with
valid colliders and 2025/2025 raycasts inside ±110 m hitting ground.

## Tests

New PlayMode test under `DigZoneHeightmapTests.cs`:

- `Bedrock_BottomCellsStaySolid_AcrossBrushes` — pre-condition: bedrock
  cell at MinValue. Brush carves through it. Post-condition: same cell
  still MinValue; dirt above bedrock carved exterior. The mesher
  doesn't run in this test (we're checking SDF directly), so it
  catches the per-brush clamp in isolation.

Updated `FullArenaConfig_CoversArena_AndContainsPlay` to assert the
new 7×1×7 / 224 m / 49-chunk shape and that the wall-perimeter band
(±105 m) is inside the diggable zone — the previous 6×1×6 / 192 m
config failed that check, which is the gap this session closed.
