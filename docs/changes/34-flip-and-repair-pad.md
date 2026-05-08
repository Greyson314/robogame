# Session 34 — Flip hotkey + repair-pad gradual rebuild

> Status: **shipped, untested in-engine.** New: snap-rotate flip on
> `H`, and a procedural healing AoE pad auto-spawned in flat + water
> arenas that gradually rebuilds the player chassis from its frozen
> blueprint over up to 10 s. Also: `GravityField` and `IGravitySource`
> promoted from `Robogame.Gameplay` to `Robogame.Core` so chassis-tier
> systems can sample gravity-up.

## Why this session

User: *"two fundamental robocraft mechanics that need to be implemented:
the ability to flip your bot at the press of a button in case you get
trapped upside-down, and self-repair of damaged bots — maybe just a
magic healing AoE circle in the corner of the arena for now."*

Decisions, captured up-front:

- Flip = **snap rotate** (instant), not impulse. Key = `H`.
- Repair = **full rebuild** (re-place destroyed blocks, not just heal).
- Repair = **gradual over 10 s**, not instant.
- Blueprint-index sort (CLAUDE.md aspirational claim, not enforced by
  the serializer): left as pre-existing debt — repair uses the same
  blueprint reference at build and rebuild, so iteration order is
  stable for free.

## What changed

### New components

**[`Movement/FlipController.cs`](../../Assets/_Project/Scripts/Movement/FlipController.cs)**
— player-only chassis MonoBehaviour added by `ChassisFactory.Build`
when `addPlayerInputs:true`. Polls `Keyboard.current[Key.H]` (mirrors
`RobotHookReleaseInput`'s pattern, no `IInputSource` extension). On
press past cooldown: computes local-up via `GravityField.SampleAt`
(world up on flat arenas, planet-radial on spherical), builds a
shortest-arc rotation that maps the chassis's current `transform.up`
onto local-up, applies it via `Rigidbody.MoveRotation`, and zeroes
`angularVelocity`. Linear velocity preserved so a mid-air flip keeps
its airspeed. Default cooldown 7 s. Fires `VfxKind.FlipBurst` and
`AudioCue.FlipActivate`.

**[`Gameplay/RepairPad.cs`](../../Assets/_Project/Scripts/Gameplay/RepairPad.cs)**
— trigger-volume MonoBehaviour. On player-chassis entry (filtered via
`PlayerInputHandler`), builds a work list by walking
`robot.Blueprint.Entries`: each entry whose grid position lacks a
block becomes a `Place` step; each surviving block below full HP
becomes a `Heal` step. Step interval = `max(0.1 s, 10 s /
robot.InitialBlockCount)` so a fully-destroyed chassis takes ~10 s and
a chip-damaged one finishes in a second or two. Each step spawns
`VfxKind.BlockRespawn` + `AudioCue.RepairBlockRespawn`; an ambient
`VfxKind.RepairGlow` column re-emits every 0.6 s while the pad is
active. Cancellation by leaving the trigger; re-entering rebuilds the
work list and starts fresh. After a successful repair calls
`Robot.ResetInitialAggregates()` so the mass-loss destroy threshold
re-baselines against the rebuilt chassis. Includes a
`CreateProcedural(pos, parent)` static helper so arena controllers
can spawn a default visual + collider pad without an authored prefab.

### Modified

**[`Block/BlockBehaviour.cs`](../../Assets/_Project/Scripts/Block/BlockBehaviour.cs)**
— added `Heal(float amount)` symmetric with `TakeDamage`. No-op on a
zero-HP block (those are already removed from the grid).

**[`Robot/Robot.cs`](../../Assets/_Project/Scripts/Robot/Robot.cs)** —
added `Blueprint` and `Library` properties (set by
`ChassisFactory.Build`) so repair systems can re-resolve block
definitions without re-walking `GameStateController`. Added
`ResetInitialAggregates()` for post-repair re-baselining.

**[`Gameplay/ChassisFactory.cs`](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)**
— stashes `blueprint` + `library` on the spawned `Robot`, and adds
`FlipController` alongside `RobotHookReleaseInput` on the
`addPlayerInputs:true` path.

**[`Gameplay/ArenaController.cs`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)**
and **[`Gameplay/WaterArenaController.cs`](../../Assets/_Project/Scripts/Gameplay/WaterArenaController.cs)**
— new `_spawnRepairPad` (default true) + `_repairPadPosition`
SerializeFields, and a one-line call to `RepairPad.CreateProcedural`
in `Start`. Default position is the arena's NE corner (35, 0.1, 35);
water arena uses (35, 1.5, 35) so the trigger sits above the surface.

**[`Core/GravityField.cs`](../../Assets/_Project/Scripts/Core/GravityField.cs)**
— new file. Hosts `IGravitySource` and the `GravityField` registry,
**moved from `Robogame.Gameplay`**. The doc
([SPHERICAL_ARENAS.md §6](../SPHERICAL_ARENAS.md)) explicitly flagged
this promotion as the eventual destination once non-Gameplay tiers
needed to sample gravity. The `FlipController` (in Movement) was that
trigger. Old `Gameplay/PlanetGravity.cs` deleted; `using
Robogame.Core;` added to `PlanetBody.cs`, `PlanetGravityBody.cs`, and
`PlanetArenaController.cs`. Also added a `[RuntimeInitializeOnLoadMethod]`
that clears the static source list on subsystem registration —
`Robot.cs` taught the project that "statics survive domain reload but
the GameObjects they reference don't", and the original v1 sketch
hadn't applied that lesson here.

**[`Core/AudioCue.cs`](../../Assets/_Project/Scripts/Core/AudioCue.cs)**
— five new cues: `FlipActivate`, `RepairPadEnter`, `RepairBlockRespawn`,
`RepairComplete`, `RepairCancel`. Library entries left blank — the
missing-cue logger surfaces them for the next audio pass per
[AUDIO_PLAN.md](../AUDIO_PLAN.md).

**[`Core/VfxKind.cs`](../../Assets/_Project/Scripts/Core/VfxKind.cs)
+ [`Core/VfxSpawner.cs`](../../Assets/_Project/Scripts/Core/VfxSpawner.cs)**
— three new kinds with palette-locked recipes: `FlipBurst` (cyan +
cream sphere kick at chassis COM), `RepairGlow` (cyan/mint upward
column, long lifetime, re-emitted on a timer for sustained presence),
`BlockRespawn` (tight bright pop at a single block).

**[`Core/PerfMarkers.cs`](../../Assets/_Project/Scripts/Core/PerfMarkers.cs)**
— `Robogame.RepairPad.Step` marker around per-tick rebuild work.

## Hard-invariant check

- **No Tweakable affects gameplay.** Both features use
  `[SerializeField]` only. PHYSICS_PLAN §1.5: clean.
- **Single Rigidbody per chassis.** Flip operates on the existing
  chassis Rigidbody; repair uses `BlockGrid.PlaceBlock` which already
  parents new blocks under the chassis Rigidbody's transform.
- **Zero baseline cost.** `FlipController` adds one keyboard read per
  Update per chassis (same shape as `RobotHookReleaseInput`). Repair
  pad is arena-placed; if you set `_spawnRepairPad = false` it
  doesn't exist.
- **No per-frame allocations.** `FlipController.Update` and
  `RepairPad.OnTriggerEnter/Exit` allocate nothing in the steady
  state. The work-list `List<WorkItem>` is pre-sized at 64; grows
  once on first repair on an extra-large chassis. The repair coroutine
  uses cached `WaitForSeconds` instances per step and per glow tick.
- **Server-authoritative shape.** `RepairPad.RepairRequested` is the
  netcode seam (becomes a `ServerRpc` later). Flip currently polls
  keyboard locally; the migration target is to add a `FlipRequested`
  bit to the per-tick input command and have the server validate
  cooldown — `FlipController.ApplyFlip()` is already isolated for that
  swap.
- **VFX + audio shipped.** Both features call `VfxSpawner.Spawn` and
  `AudioRouter.PlayOneShot` at every gameplay event (flip activate,
  pad entry/exit/complete, per-block respawn). Cues without authored
  clips log once via the missing-cue path.

## Known follow-ups / things left for the next session

- **Spherical arena.** `PlanetArenaController` does not auto-spawn a
  repair pad. "Corner of the arena" doesn't apply on a sphere; needs
  a planet-relative offset that's deferred to a later session. Same
  one-line `RepairPad.CreateProcedural` call works once the position
  is decided.
- **Audio cues blank.** `FlipActivate`, `RepairPadEnter`,
  `RepairBlockRespawn`, `RepairComplete`, `RepairCancel` need clips
  authored on the AudioCueLibrary asset. Until then the
  missing-cue logger surfaces them once each.
- **Repair on bots.** Pad is player-only — bots crossing the trigger
  are filtered out by the `PlayerInputHandler` check. Easy to relax
  later if AI healing becomes a feature.
- **Heal-only on a fully healthy chassis.** The pad's work list comes
  back empty if the chassis is already at full mass + full HP. The
  coroutine completes immediately and plays only the entry + complete
  cues. Acceptable, but feels abrupt; could add a small ignore guard
  if it surfaces as awkward.
- **Damage during a rebuild.** If the player takes hits while the
  rebuild is mid-tick, the work list (frozen at entry time) keeps
  ticking and may try to "place" a block whose position got
  re-occupied between work-list build and apply. The `Place` branch
  early-returns on an occupied cell rather than warning. Mid-rebuild
  destruction beyond the original work list isn't repaired in the
  current pass — leaving the pad and re-entering picks up the new
  damage.
- **Pre-existing block-index sort debt.** CLAUDE.md claims entries
  are "sorted by `Vector3Int`" as a netcode contract; the serializer
  doesn't enforce it. Repair sidesteps the issue by reusing the same
  blueprint reference, but the upstream divergence between doc and
  code remains. Out of scope this session.
