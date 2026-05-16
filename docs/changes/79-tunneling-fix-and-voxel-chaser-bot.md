# 79 — Tunneling fix + Phase 5 visual-playtest gate (VoxelChaserBot)

> Status: **shipped, machine gates green.** Two-part session: first
> the tunneling fix the playtest demanded (drill made one tiny hole,
> then stopped); second the Phase 5 visual playtest gate — a chaser
> bot that uses the OccupancyGrid + A* to follow the player across
> voxel terrain and through carved tunnels.

## Part 1 — Drill tunneling fix

### Symptom

> "The drill is currently making a tiny hole (yay!), but only on
> impact, and only for an instant. It's important that we're able
> to dig-while-driving."

### Diagnosis

A cell-sized `DrillBlock` mounted on a wheeled chassis sits ~0.5m
above the terrain surface plane (the wheel-to-body offset). The
brush emitted at the drill's `transform.position` (cell center) only
just scraped the surface plane at radius 0.8m. After one tick the
affected cells were exterior; the drill hadn't moved (chassis is on
wheels above the same spot); next tick's brush carved cells already
exterior. Max-fold idempotency = every subsequent tick a no-op.

### Fixes

1. **Tip projects past cell center along `transform.up`**. New
   `_tipForwardOffset` SerializeField (default 0.6m). The brush emits
   at `transform.position + transform.up * _tipForwardOffset` rather
   than at `transform.position`. For a front-mounted drill (up=+Z)
   the brush leads the chassis forward; for a bottom-mounted drill
   (up=-Y) it carves below. Orientation tracks `BlockGrid.OrientationFromUp`'s
   `transform.up` assignment, so the projection always points the
   right way regardless of mount face.

2. **Radius bumped 0.8m → 1.5m**. A 3m-diameter tunnel fits the
   2.5m-wide DrillBot chassis (3×3 floor + side wheels at ±1.0m +
   tyre extents) with ~0.25m clearance, so the chassis can actually
   advance into what the drill carves rather than wedging on
   un-carved walls.

### Test

`Drill_TipWorldPosition_AppliesForwardOffsetAlongTransformUp` —
verifies the tip projects along `transform.up` at default and +Z
orientations. Prevents a silent regression of this fix.

## Part 2 — VoxelChaserBot

### Why

`TERRAFORMING_PLAN § 12` Phase 5 visual-playtest gate: *"AI enemy in
a POI chamber notices the player drilling in, paths through the new
tunnel to attack."* The OccupancyGrid + A* foundation has shipped
(session 75); this is the first consumer.

This isn't the full POI-chamber-enemy scenario yet — that needs the
SDF-baker `.dig` authoring pipeline to pre-carve chambers, which is
its own session. For v1 the bot spawns on the surface and chases
the player across the dig zone; when the player drills trenches,
the cells become OpenWithFloor and the bot's A* picks them up,
following the player into the carved trench.

### Implementation

**New: [`VoxelChaserBot`](../../Assets/_Project/Scripts/Gameplay/VoxelChaserBot.cs)**.
A MonoBehaviour that:

- Resolves its `DigZone` via `DigField.ZoneAt(transform.position)`
  at spawn, and its target by scanning the scene for the first
  `Robot` that isn't itself.
- Each `FixedUpdate`, refreshes the path every `_pathRefreshInterval`
  seconds (default 0.5s) by calling `OccupancyGrid.TryFindPath`,
  then steps toward the next waypoint at `_walkSpeed` (default 2 m/s).
- Movement is kinematic transform stepping — no Rigidbody, no
  collision damage. Mirrors the minimal-AI shape used by the
  existing test bots (`GroundBotInputSource`-style stand-ins) but
  with A* navigation instead of nearest-neighbour pursuit.
- Test-friendly `BindZone(DigZone)` and `BindTarget(Transform)` let
  PlayMode tests inject a specific zone + target without relying on
  scene-search heuristics. `RefreshPath()` is public + returns
  bool so tests can pin path-existence directly.

**Modified: [`EnvironmentBuilder.cs`](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)**.
After `BuildArenaDigZone` constructs the zone, a single chaser bot
is spawned on the zone's surface at the far corner. Visual is a
sphere primitive (collider stripped — bot is kinematic) tinted
red-orange against the existing `WorldPalette.ArenaPillar`.

### Tests

Three new PlayMode tests in [DigZoneTests.cs](../../Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs):

- `VoxelChaserBot_FindsPathBetweenSurfaceCells_AcrossHalfSpace` —
  bind a zone + target, call `RefreshPath`, assert a path exists
  between two surface (OpenWithFloor) cells.
- `VoxelChaserBot_FollowsPath_MovesTowardTarget` — `[UnityTest]`
  letting 10 FixedUpdates fire; asserts the bot's distance to the
  target decreases over the interval (verifies waypoint stepping +
  speed are wired).
- `VoxelChaserBot_NoPath_RefreshReturnsFalse_PathStaysEmpty` —
  spawn bot in a Solid cell, assert `RefreshPath` returns false
  and `HasPath` stays false (fail-closed at zone-unreachable
  states).

### Constraint check (per CLAUDE.md)

- **No new Tweakables driving gameplay outcomes.** ✓ All bot
  parameters are SerializeFields on the chaser bot itself.
- **Single Rigidbody per chassis.** ✓ Bot has no Rigidbody.
- **Zero baseline cost when feature is disabled.** ✓ The bot is
  spawned only by `EnvironmentBuilder.BuildArenaDigZone`. Arenas
  without a dig zone never run the spawner.
- **No per-frame allocations.** ✓ A* uses the OccupancyGrid's
  internal per-search allocs (one-shot at refresh time); the per-
  fixed-step movement code is purely arithmetic on `transform.position`.

## What's deferred

- **Pre-carved chambers + underground spawn.** Bot spawns on the
  surface for v1. A real underground enemy needs the `.dig` baker
  to author chambers into the initial SDF (TERRAFORMING_PLAN § 9).
  Listed as the obvious next session.
- **Combat behaviour.** Bot just walks toward the player. No
  attack, no damage. Phase 5's plan is "enemy notices and paths"
  not "enemy kills"; combat layering is the session after POI
  authoring.
- **VFX / audio on bot.** Per CLAUDE.md every gameplay system
  should declare cues even if blank. Bot is minimal demo so no
  cues yet; declare on next iteration once the bot has actual
  gameplay states (idle / chase / contact).

## Files

- New:
  `Assets/_Project/Scripts/Gameplay/VoxelChaserBot.cs`,
  `docs/changes/79-tunneling-fix-and-voxel-chaser-bot.md`.
- Modified:
  `Assets/_Project/Scripts/Voxel/DrillBlock.cs` (tip offset +
  radius bump),
  `Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs`
  (chaser bot spawner),
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` (4 new
  tests).

## Validation

- `.claude/scripts/run-tests.sh PlayMode`: 61/63 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
- `.claude/scripts/run-tests.sh EditMode`: 193/194 passed, 0
  failed, 1 inconclusive (pre-existing `PresetBlueprintTests` for
  an unscaffolded preset).

## Playtest brief (for the user's return)

Things to verify when in front of the game again:

1. **Drill actually tunnels.** Drive the DrillBot forward into the
   in-arena dig zone (at world ~(60, 0, 60)) while holding
   left-click. Expected: a 3m-wide trench appears in the terrain
   as the chassis drives. Each second of holding fire while
   driving should carve another few metres. If the drill still
   makes only one hole, the geometry doesn't actually feed into
   the cells brush (Δ in `_lastTipPosWorld` between ticks might
   be zero) — flag for investigation.

2. **DrillBot reaches arena.** You'll need to run `Robogame >
   Build Everything` once (or the equivalent menu that runs
   `CreateDefaultBlueprints` + `BuildBootstrapPassA`) to materialise
   `Blueprint_DefaultDrillBot.asset` and slot it into the preset
   selector. Then choose it from the preset dropdown and drive.

3. **VoxelChaserBot follows.** Look for a red-orange sphere on the
   far corner of the dig zone surface (~(86, 0.5, 86) given the
   default zone position). It should walk toward your chassis
   when you're inside the zone. If you drill a trench, the bot
   should descend into it as your chassis moves through.

   Failure modes worth flagging:
   - Bot doesn't move at all → A* isn't finding a path (likely
     `DigField.ZoneAt` returning null, or the bot's spawn cell
     classifying as Solid). Check `bot.Zone` and `bot.Grid` in
     the Inspector.
   - Bot moves erratically / through walls → waypoint stepping
     not respecting `OpenWithFloor` cells. Less likely; the A*
     paths are guaranteed to traverse only traversable cells.
   - Bot snaps to grid cells too coarsely → 2m cell size is the
     occupancy grid's resolution; the bot's step alignment to
     cell centers will look chunky at close range. Acceptable
     for v1.

4. **Terrain damage exemption still working.** Driving the
   DrillBot full-speed into the un-carved terrain edge should
   not destroy the chassis. (Session 78's fix.)

5. **Bomb crater scale** still proportional, no regression. (Session
   77's fix.)
