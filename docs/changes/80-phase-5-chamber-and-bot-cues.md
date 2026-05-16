# 80 — Phase 5 chamber + bot audio/VFX cues

> Status: **shipped, machine gates green.** Layered onto session 79's
> VoxelChaserBot: the bot now spawns inside a pre-carved underground
> chamber (stand-in for the Phase 5 `.dig` baker pipeline), and the
> bot's gameplay events declare AudioCue + VfxKind call sites per
> CLAUDE.md's "every feature ships with VFX + audio" invariant.

## What changed

### 1) `DigZone._initialBrushes` — runtime POI authoring stand-in

**[`DigZone.cs`](../../Assets/_Project/Scripts/Voxel/DigZone.cs)**
gains a serialised `List<InitialBrushSpec>` and an
`AddInitialBrush(InitialBrushSpec)` public API for tests /
scaffolders. `EnsureInitialised` applies the list against each
chunk's SDF AFTER the half-space / snapshot seed but BEFORE the
occupancy grid is built — so the occupancy grid classifies cells
based on the post-brush SDF.

This is the Phase 5 POI-chamber pre-carve, minus the full Phase 2d
`.dig` baker pipeline. Brushes live as data in the scene, regenerate
each load, run before any gameplay can see the zone. The actual
`.dig` baker authors brushes into a `.dig` asset whose payload is the
authoritative initial SDF; that's still its own session. Both
mechanisms route through the same brush-op verb at runtime, so
swapping in the `.dig` path later is a one-line change in
`EnsureInitialised`.

**Gotcha** — brush position vs occupancy classification. The
occupancy-cell classifier counts 5×5×5 SDF samples and requires
> 50% interior to mark Solid. A brush centered on a cell *corner*
only overlaps ~1/8 of the cell and fails to flip it Open. Center
brushes on cell *centers* (occupancy cell N at world `origin + (N +
0.5) * cellSize`) — or up the radius. Both the chamber test and
the in-arena scaffolder were rewritten to use cell-center anchors.

### 2) VoxelChaserBot audio + VFX

**[`VoxelChaserBot.cs`](../../Assets/_Project/Scripts/Gameplay/VoxelChaserBot.cs)**
declares two new audio cues + a VFX hook:

- `AudioCue.BotDetected` — fires on the no-path → path edge in
  `RefreshPath`. The gameplay moment: the bot just acquired a route
  to its target (e.g., because the player drilled a connecting
  tunnel). One-shot per path-acquisition cycle, not per refresh.
- `AudioCue.BotStep` + a low-scale `VfxKind.DebrisDust` puff —
  emitted on every *other* waypoint advance via the new
  `AdvanceWaypoint` helper. Throttled to keep long paths from
  spamming the audio mixer.

Both cues are declared in `AudioCue.cs` per AUDIO_PLAN's
declared-then-authored pipeline. Until the library lands a clip,
calls are no-ops (the missing-cue logger reports them once for the
audio pass to pick up).

### 3) EnvironmentBuilder wires it all together

**[`EnvironmentBuilder.cs`](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)**:

- Configures the in-arena dig zone's `_initialBrushes` list via
  `SerializedObject`: one `SphereSubtract` at world (77, -3, 77)
  radius 2.5m — cell-center-aligned, ~3m below the half-space
  surface plane, mid-zone in XZ.
- The `BuildArenaDigZoneChaser` helper now takes the chamber
  center as a parameter and spawns the bot at `chamberCenter +
  (0, 0.5, 0)` so it lands inside the chamber's OpenWithFloor cell.

## Test

`DigZone_InitialBrush_CarvesChamberBeforeOccupancyBuild` — builds a
zone with a single cell-center-anchored SphereSubtract initial brush
and asserts the corresponding occupancy cell classifies as
non-Solid (Open) after init. Pins the order-of-operations
guarantee in `EnsureInitialised` against future refactors that
might accidentally move occupancy build before brush apply.

## Validation

- `.claude/scripts/run-tests.sh PlayMode`: 62/64 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
- `.claude/scripts/run-tests.sh EditMode`: 193/194 passed, 0
  failed, 1 inconclusive (pre-existing `PresetBlueprintTests`
  unscaffolded preset).

## What's deferred

- **`.dig` baker pipeline** (TERRAFORMING_PLAN § 9 `Bake POIs`
  menu item). The serialized brush list is a stand-in; the real
  thing voxelises authored POI prefabs into a `.dig` asset's
  payload. Big editor tooling — own session.
- **Bot combat behaviour.** Bot walks toward player; doesn't
  attack. Phase 5's playtest gate is "AI notices and paths," the
  next session can layer in damage / weapon mounting.
- **Bot audio clip authoring.** `BotDetected` and `BotStep` cues
  are declared but unmapped in the AudioCueLibrary asset. Audio
  pass picks them up.
- **Chamber visualisation.** The chamber is invisible from above
  (it's hollow inside the solid terrain). Could add a thin
  wireframe overlay or beam indicator so the player has a hint
  where to drill — defer to playtest feedback.

## Playtest brief

Things to verify on return:

1. **Bot is somewhere underground.** After running `Robogame >
   Build Everything` to regenerate the arena, the chaser bot
   sphere should be invisible from above (it's at world
   (77, -2.5, 77), 2.5m below the half-space surface inside the
   pre-carved chamber). You won't see it until you drill down.

2. **Bot pursues when you tunnel down.** Drive the DrillBot to
   roughly world (77, 0, 77) on the dig zone surface. Hold
   left-click + drive forward to start a trench. As the trench
   deepens past world Y ≈ -1, the bot's A* should find a path
   (chamber → trench → your chassis) and the bot should start
   moving toward you. If you see the bot emerge from the hole
   you dug, that's the Phase 5 visual playtest gate green.

3. **Bot doesn't pursue when you stay on the surface.** If you
   just drive over the surface without drilling, the chamber stays
   sealed, no path from chamber to surface exists, the bot stays
   put. (Bot's `Zone` and `Grid` fields should still resolve in
   the Inspector — RefreshPath returns false silently.)

4. **Bomb crater opens the chamber too.** A bomb dropped near the
   chamber XZ position should be able to break through. With the
   bomb's 0.3× terrain scale and ~5m radius (session 77), one
   well-placed bomb might just barely reach the chamber from the
   surface. Mostly: confirm the bomb still works; nothing in this
   session touches projectiles.

5. **Drill tunneling actually works** (session 79's fix). 3m-wide
   trench appears as you drive + drill. If it still makes one
   hole and stops, the Δ-tip-pos-per-frame is zero — let me know.

## Files

- New: `docs/changes/80-phase-5-chamber-and-bot-cues.md`.
- Modified:
  `Assets/_Project/Scripts/Voxel/DigZone.cs` (initial brushes + apply path),
  `Assets/_Project/Scripts/Core/AudioCue.cs` (2 new cues),
  `Assets/_Project/Scripts/Gameplay/VoxelChaserBot.cs` (audio/VFX call sites + AdvanceWaypoint helper),
  `Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs` (chamber brush config + chamber-spawn the bot),
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs` (chamber test + MakeZoneWithChamber helper).
