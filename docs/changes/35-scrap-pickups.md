# Session 35 — Scrap pickups (drop-on-death + collect-by-overlap)

> Status: **shipped, untested in-engine.** New: every destroyed
> chassis scatters a small ring of `ScrapPickup` instances. Driving
> through one collects it onto the chassis's new `Robot.ScrapHeld`
> counter (with magnetic pull within ~4 m). HUD gains a fourth row.
> Foundation only — `ScrapHeld` is the hook for future systems
> (match score, build-mode purchasing, persistent currency).

## Why this session

User: *"When a robot is destroyed, it should leave behind something
called Scrap. If another robot drives through that scrap, they should
collect it. If there are any further features or ideas you can think
to add as a strong foundation for future ideas, please feel free to
implement them. Please use an asset, if we have any, to represent the
scrap on the ground."*

Beyond the literal request, the foundation pieces that landed:

- A counter on the Robot (`ScrapHeld`) and an event (`ScrapAwarded`)
  so HUD / score / future systems read off the chassis directly.
- A drop-on-death component (`ScrapDropper`) separate from `Robot`
  itself, so future drop-rule features (faction filters, perks, mode
  multipliers) have a natural home.
- Total scrap value scales with the chassis's at-spawn block count —
  big bots are worth more than scout chassis.
- Magnetic pickup radius — scrap drifts toward a nearby chassis so
  collection isn't precision-driving.
- Despawn timer (30 s default) so scrap doesn't accumulate across a
  long match.
- HUD readout (SCR row in `VehicleStatsHud`).

## What changed

### New components

**[`Gameplay/ScrapPickup.cs`](../../Assets/_Project/Scripts/Gameplay/ScrapPickup.cs)**
— world-floating collectible. Trigger sphere on the root; on overlap
with a `Robot` (via `GetComponentInParent`), calls
`Robot.AwardScrap(value)` and self-destructs. Visual loaded from
`Resources/Prefabs/ScrapPickup` when available, falls back to a
palette-tinted procedural cube. Bobs vertically and spins on Y for
readability. Magnetic pull toward the nearest chassis Rigidbody once
inside `_magneticRadius`. Arm delay (~0.35 s) prevents a kill from
auto-vacuuming its own scrap before it's left the killer's collider.

**[`Gameplay/ScrapDropper.cs`](../../Assets/_Project/Scripts/Gameplay/ScrapDropper.cs)**
— sibling component on the chassis that subscribes to `Robot.Destroyed`
and scatters pickups around the chassis centre on death. Tunable per
component: `_scrapPerBlock` (default 1.0), `_minTotalValue` (2),
`_minPickupCount` / `_maxPickupCount` (2 / 6), and an unused-for-now
`_ownerCannotCollect` hook for future faction filtering. Lives in
Gameplay, not Robots, so the drop logic can reach `ScrapPickup`
without inverting the asmdef tier.

### Modified

**[`Robot/Robot.cs`](../../Assets/_Project/Scripts/Robot/Robot.cs)**
— new `ScrapHeld` int property, `AwardScrap(int)` method, and
`ScrapAwarded` event. Negative awards are rejected (a future
`SpendScrap` will be the explicit decrement path).

**[`Gameplay/ChassisFactory.cs`](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)**
— adds `ScrapDropper` on both the player path (`Build`) and the
target path (`BuildTarget`), so combat dummies and bots both yield
scrap when killed.

**[`Player/VehicleStatsHud.cs`](../../Assets/_Project/Scripts/Player/VehicleStatsHud.cs)**
— fourth row: `SCR  N`. Panel height default bumped from 92 → 122 so
the rows aren't cramped. Existing scenes with the old serialised
value will still work but the rows will be tight; widen the panel in
the inspector if needed.

**[`Core/AudioCue.cs`](../../Assets/_Project/Scripts/Core/AudioCue.cs)**
— two new cues: `ScrapDrop` (spawn) and `ScrapCollect` (pickup).
Library entries left blank per AUDIO_PLAN — missing-cue logger
surfaces them next audio pass.

**[`Core/VfxKind.cs`](../../Assets/_Project/Scripts/Core/VfxKind.cs) +
[`Core/VfxSpawner.cs`](../../Assets/_Project/Scripts/Core/VfxSpawner.cs)**
— new `ScrapBurst` kind: warm hazard-orange particle pop, fired at
both drop (small scale) and collect (full scale). Same recipe both
times — scale alone differentiates the two events.

### Editor

**[`Tools/Editor/ScrapPrefabScaffolder.cs`](../../Assets/_Project/Scripts/Tools/Editor/ScrapPrefabScaffolder.cs)**
— menu item `Robogame > Scaffold > Build Scrap Prefab`. Wraps the
Kenney `coin-bronze.fbx` (with `coin-silver` / `coin-gold` fallbacks)
in a prefab at `Assets/_Project/Resources/Prefabs/ScrapPickup.prefab`
with the trigger collider and `ScrapPickup` component pre-attached.
Idempotent. Run once after pulling the session-35 changes to upgrade
the visual from procedural cube to coin model.

## Hard-invariant check

- **No Tweakable affects gameplay.** All tuning lives on
  `[SerializeField]` fields on `ScrapDropper` + `ScrapPickup`.
  PHYSICS_PLAN § 1.5: clean.
- **Single Rigidbody per chassis.** Pickups are at scene root with
  no Rigidbody (kinematic float). Don't add a Rigidbody to the
  chassis or spawn under it.
- **No per-frame allocations.** `ScrapPickup.Update` uses a static
  `OverlapSphereNonAlloc` buffer (size 16) for the magnetic pull —
  zero per-step allocs.
- **Server-authoritative shape.** `Robot.AwardScrap` is the single
  mutation path, called only from `ScrapPickup.Collect`. When MP
  lands, this becomes a server-side RPC and the client renders the
  HUD off the replicated `ScrapHeld`. The drop side is similarly
  pinned to a single event handler.
- **VFX + audio shipped.** `VfxKind.ScrapBurst` + `AudioCue.ScrapDrop`
  / `ScrapCollect` declared and called at the right gameplay events.

## Known follow-ups

- **Run the editor scaffolder.** Until `Robogame > Scaffold > Build
  Scrap Prefab` is run, drops use the procedural orange cube. The
  Kenney coin model is the intended visual.
- **Audio cues blank.** `ScrapDrop` and `ScrapCollect` need clips
  authored on the AudioCueLibrary asset.
- **HUD panel height in pre-existing scenes.** Existing scenes
  serialise the old `_panelHeight = 92`, which makes the new 4-row
  layout tight. Widen via inspector when polishing.
- **No HUD popup on collect.** A "+1 SCRAP" floating text would feel
  good. Deferred.
- **No `SpendScrap` yet.** The dual to `AwardScrap` lands when the
  first spending feature does (build-mode purchases, repair-pad cost,
  upgrade unlocks).
- **No faction filter on collect.** `ScrapDropper.OwnerCannotCollect`
  is wired up but not yet honoured by `ScrapPickup` — left as a hook
  for the inevitable team-mode work.
- **Despawn is hard delete, no fade.** A short alpha fade in the last
  ~1.5 s would read more polished.
