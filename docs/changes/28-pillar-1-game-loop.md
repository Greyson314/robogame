# Session 28 — Pillar 1: Singleplayer game loop

> Status: **shipped, untested in-engine**. Whole feature surface compiles
> against the test contract drafted up-front. A user-facing in-engine
> playthrough is the next gate (one MatchConfig field needs an inspector
> wire-up — see "Action required" below).

## Why this session

Robogame had every load-bearing piece of a sandbox — chassis, weapons,
damage, destruction — but no actual *game*: no win condition, no round
timer, no score, no return-to-garage flow, and the only "AI" was a
Tweakable-gated debug bot. This session turns that sandbox into a
playable singleplayer match.

## What landed

### New types and state machine — [`MatchTypes.cs`](../../Assets/_Project/Scripts/Gameplay/MatchTypes.cs)

- `MatchSide` enum: `None / Player / Enemy`. Two-sided today; the
  `RegisterChassis(robot, side)` API extends naturally to teams later.
- `MatchState` enum: `WarmingUp / InProgress / RoundEnded`. Terminal on
  `RoundEnded`.
- `MatchEndReason` enum: `FragLimitReached / TimeExpired / PlayerEliminated / Draw`.
- `MatchEndedArgs` `readonly struct`: winner + reason + final score, fired
  exactly once per match.
- `MatchConfig` plain `[Serializable]` class: warmup duration, round
  duration, frag limit, player lives, bot respawn delay, player respawn
  delay, ground-bot roster, air-bot roster.

### Match controller — [`MatchController.cs`](../../Assets/_Project/Scripts/Gameplay/MatchController.cs)

Plain C# class — **not a MonoBehaviour**. Constructible from EditMode
tests via `new MatchController(config)`; ticked from the host
MonoBehaviour via `Tick(deltaTime)`. This shape kills three birds:

1. **Testable.** All 11 EditMode tests in [`MatchControllerTests.cs`](../../Assets/_Project/Tests/EditMode/Gameplay/MatchControllerTests.cs)
   drive the controller without spinning up a scene.
2. **Network-ready.** When NGO lands, `MatchController` becomes the
   inner state of a `NetworkBehaviour` wrapper; events become RPCs;
   no logic moves.
3. **Hostable.** Any MonoBehaviour can own a controller and feed it
   events. Today `ArenaController` does it; an in-game lobby /
   replay-scrubber would do it the same way.

State transitions:

```
WarmingUp  --(timer ≥ WarmupDuration)-->  InProgress
InProgress --(score hits TargetFragCount)-->  RoundEnded   (FragLimitReached)
InProgress --(round timer expires)-->         RoundEnded   (TimeExpired or Draw)
InProgress --(NotifyPlayerLivesExhausted)-->  RoundEnded   (PlayerEliminated)
```

Idempotent: `MatchEnded` fires exactly once even when multiple end
conditions trigger on the same `Tick`. Kills during `WarmingUp` are
silently dropped (a stray pre-fight projectile shouldn't count).
Kills after `RoundEnded` are also dropped (the corpse pile keeps
falling but the score is locked).

### AI input sources

Both implement the existing [`IInputSource`](../../Assets/_Project/Scripts/Input/IInputSource.cs)
interface and feed the same `PlayerController` → `RobotDrive`
pipeline the human player uses — so bots and player share the entire
drive / weapon / damage path. **MP-readiness rule satisfied: AI
sends inputs, not state.**

- **[`GroundBotInputSource.cs`](../../Assets/_Project/Scripts/Gameplay/GroundBotInputSource.cs)**.
  Four states: `Patrol / Engage / Retreat / Dead`. The existing
  patrol-circle steering math from `DummyAiInputSource` survives
  verbatim (extracted as `public static ComputeSteer` for testability).
  Engage adds chase-range + fire-arc gating; Retreat reverses throttle
  and steers away from the target; Dead zeroes outputs.

- **[`AirBotInputSource.cs`](../../Assets/_Project/Scripts/Gameplay/AirBotInputSource.cs)**.
  Four states: `Cruise / Engage / LowHealth / Dead`. Cruise holds
  altitude via a P-controller on `targetAlt - currentAlt`. Engage
  computes a first-order **lead-the-target** point from the target's
  Rigidbody linear velocity × estimated projectile travel time —
  necessary for a fast plane chassis to land hits. LowHealth bleeds
  altitude and disengages.

- **[`DummyAiInputSource.cs`](../../Assets/_Project/Scripts/Gameplay/DummyAiInputSource.cs)**
  is now a thin `[Obsolete]` subclass of `GroundBotInputSource`,
  preserving the meta GUID so any prefab / asset that references the
  old class keeps loading. **Delete in a follow-up session** once we
  confirm no external references remain.

### HUDs (in `Robogame.Gameplay`, not `Robogame.Player`)

These read `MatchController` state so they have to live at the
`Gameplay` asmdef tier — `Player` sits below `Gameplay` in the
dependency direction (BEST_PRACTICES § 2.2) and can't reference up.

- **[`ObjectiveHud.cs`](../../Assets/_Project/Scripts/Gameplay/ObjectiveHud.cs)**.
  Top-centre IMGUI panel: HP bar (player block-count fraction), kill
  counter (`P — E`), round timer (`mm:ss`). HP tint flips healthy →
  hurt → danger; timer flips alert-red in the final 30 s.
  Allocation-free hot path: `StringBuilder` reused, label strings
  rebuilt only when the displayed value changes.

- **[`MatchEndOverlay.cs`](../../Assets/_Project/Scripts/Gameplay/MatchEndOverlay.cs)**.
  Full-screen IMGUI overlay shown on `MatchController.MatchEnded`.
  VICTORY / DEFEAT / DRAW headline tinted from the existing palette
  (Hazard / Alert / neutral grey). Score line, reason copy, and a
  "Return to Garage" button that calls `GameStateController.EnterGarage()`.
  IMGUI on purpose — the in-arena cursor is locked, and IMGUI
  buttons fire on raw mouse events without the EventSystem dance.
  Cursor unlocks automatically when the match ends.

### Wiring — [`ArenaController.cs`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)

- Creates a `MatchController` in `Start` after spawning the player
  chassis. Order is deliberate: controller → BindFollowCamera (which
  calls `BindMatch` on both HUDs) → register player chassis → spawn
  bots from `MatchConfig`.
- Subscribes to every `Robot.Destroyed` for chassis it spawned and
  routes the event through `MatchController.RegisterKill`. Killer
  side is inferred from the victim's side (singleplayer 1-vs-N
  invariant; multi-bot would need a real damage-source tracker on
  `Robot`, flagged as MP debt).
- Schedules respawn coroutines: player on `PlayerRespawnDelay`, bots
  on `BotRespawnDelay`. Both bail cleanly if the match ends before
  the delay elapses.
- Disables `DeathOverlay` when `MatchEnded` fires so the per-chassis
  "DESTROYED" overlay doesn't stack behind the match-end overlay.

### Profiler markers — [`PerfMarkers.cs`](../../Assets/_Project/Scripts/Core/PerfMarkers.cs)

- `Robogame.Match.Update` — wraps the controller `Tick`.
- `Robogame.Bot.InputUpdate` — wraps every AI brain tick.

Cost is trivial (markers are no-ops with profiler detached), but
they show up named in captures so a future "AI is slow" investigation
has a target.

## Open questions answered (planner Q1–Q5)

- **Q1 (bot mix):** default `MatchConfig` ships with empty `GroundBots` /
  `AirBots` arrays. The user wires their preferred chassis into the
  scene's ArenaController inspector. Saves a session of tuning and
  doesn't force air-bot quality before it's ready.
- **Q2 (respawn vs single-life):** limited lives. Player gets
  `PlayerLives = 3` by default; bots respawn until frag limit. Player
  out of lives → `PlayerEliminated`.
- **Q3 (draw on tie):** real `Draw` state, real overlay copy.
- **Q4 (`DummyAiInputSource` fate):** kept as `[Obsolete]` thin
  subclass for one session of compatibility, then delete.
- **Q5 (`ObjectiveHud` vs extending `VehicleStatsHud`):** new component.
  Vehicle telemetry stays in `VehicleStatsHud`; match objectives go
  in `ObjectiveHud`. Each will map to one panel when we move to UGUI.

## Tests

The test-drafter wrote 27 tests up-front against the planned API
surface. The implementation matches that contract — none of the
EditMode tests needed assertion changes.

- **EditMode** ([`MatchControllerTests.cs`](../../Assets/_Project/Tests/EditMode/Gameplay/MatchControllerTests.cs),
  [`DummyAiInputSourceTests.cs`](../../Assets/_Project/Tests/EditMode/Gameplay/DummyAiInputSourceTests.cs)):
  state machine transitions, score routing, frag-limit win, timer-expiry
  draw, idempotency, kills-ignored-during-warmup, kills-ignored-after-end,
  patrol-steer math edge cases.
- **PlayMode** ([`MatchFlowTests.cs`](../../Assets/_Project/Tests/PlayMode/Gameplay/MatchFlowTests.cs)):
  all stubs (`Assert.Pass`) — full-scene tests deferred until the
  scene is wired (see Action required).

## Action required after pulling

The `_matchConfig` field on `ArenaController` is a brand-new
SerializeField. Existing `Arena.unity` will get a fresh
`new MatchConfig()` instance with default values (3 lives, 5-frag
limit, 5-minute round, empty bot rosters). To play a real match:

1. Open `Assets/_Project/Scenes/Arena.unity`.
2. Select the `ArenaController` GameObject in the hierarchy.
3. In the inspector, expand **Match → Match Config → Ground Bots**,
   set size to 1, and assign the Tank blueprint asset to Element 0's
   `Blueprint` field.
4. (Optional) Set an explicit `Spawn Position Override` if you want
   the bot somewhere other than `_groundBotSpawnDefault` (28, 2, 0).
5. Save the scene.
6. Press Play from `Bootstrap.unity`. Match begins after the
   3-second warmup; first to 5 frags wins.

The legacy `Stress.TankDummySpawn` Tweakable still works as a debug
shortcut — toggling it spawns a tank dummy that's also registered
with the match controller, so killing it scores. Both spawn paths
coexist; remove the Tweakable-driven dummy from the inspector by
clearing its blueprint when you're ready.

## Ancillary features deferred

The planner flagged several "could ship now" extras the implementation
does not yet include. Each is a half-day at most when prioritised:

- **Kill feed** — short scrolling list of recent kills above the
  ObjectiveHud. The `KillRegistered` event already exposes the data;
  consumer is a small IMGUI overlay.
- **First-blood / multi-kill announcements** — toast tied to a
  per-side score-delta detector on `KillRegistered`.
- **PlanetArena air-bot gravity fix** — `AirBotInputSource` reads
  `transform.position.y` for altitude, which is wrong on the
  spherical planet arena. Flagged in the file's `<remarks>` block.
- **In-game scoreboard panel** (Tab to peek) — separate from the
  always-on ObjectiveHud.
- **Damage-source tracking on `Robot`** — drops the `victimSide ==
  Player ? Enemy : Player` heuristic in favour of a real
  per-block-damage-attribution path. Required for multi-bot teams,
  not for 1-v-N.

## Risks / known gotchas

- **Killer attribution is heuristic.** With one player and N bots
  it's correct by construction. If we add multiple players or
  team-vs-team, the heuristic produces nonsense — flagged above.
- **Air-bot spin-out under hard pitch.** Session 21's helicopter
  rotor-lift instability still applies; pitch is implicitly clamped
  by the `_verticalGain` field. If air bots wobble under engagement,
  drop `_verticalGain` from 0.1 to 0.05.
- **Cursor stays unlocked after Return to Garage.** The garage
  scaffolder's button HUD calls `Cursor.lockState = None` itself, so
  this works; but if a future scene assumes the cursor is locked on
  load, it'll need to relock.
- **Respawn inside the round-end window.** A bot dies the same
  frame the timer expires → `RegisterKill` fires inside `Tick`'s
  round-end branch but `EndMatch` has already been entered. The
  state-machine guard `if (State != MatchState.InProgress) return`
  on both sides handles this — the test
  `MatchEnded_FiresExactlyOnce_WhenMultipleEndConditionsTriggerSameTick`
  exercises it.

# Addendum (post-session bug fixes)

After the initial Pillar-1 ship, three issues surfaced in playtest:

### 1. Bots wouldn't fire

**Symptom.** A bot spawned via `MatchConfig` would patrol but never pull
the trigger.

**Cause.** The original brain in Patrol/Engage state used the same
patrol-circle steering — orbit the configured `_circleCentre`. With
that point at the player's spawn, the bot's forward direction was
always tangential to the player, so the `facingDot > 0.5` fire-arc
gate was almost never satisfied. Compounded by the fact that "go in
circles forever" isn't a behaviour anyone wanted.

**Fix.** Redesigned both bot brains around a richer state machine —
`Patrol → Pursue → Engage → Retreat` for ground,
`Cruise → Pursue → Engage → LowHealth` for air. The behavioural
intent for each:

- **Patrol/Cruise**: no target, drive in a wide circle around the
  configured patrol point. Holds fire.
- **Pursue**: target spotted but too far. Drive *directly at* the
  target at high throttle. Holds fire.
- **Engage**: target inside `OptimalRange + EngageBuffer`. Orbit the
  *target* (not the patrol point) at OptimalRange — the
  tank-skirmish / dogfight pattern. Fires when target is within
  fire range AND within the fire arc.
- **Retreat (ground) / LowHealth (air)**: HP below threshold. Reverse
  and steer away (ground) or bleed altitude with throttle reduced
  (air). Holds fire.

The Engage-orbits-the-target trick reuses the existing
`ComputeSteer` math with the player's position as the centre, so
no new vector code is needed. Hysteresis on the
Pursue ↔ Engage boundary (`OptimalRange + buffer*0.5` to enter,
`+ buffer*1.5` to leave) prevents state flicker.

The fire-arc threshold for ground bots is now `-0.3` (≈250° arc) —
the WeaponMount's turret yaw isn't hard-clamped, so any direction
where the chassis isn't pointing *away* from the target is fair
game. Air bots keep a stricter `0.6` (≈100°) because plane chassis
typically have fixed-forward guns.

### 2. "Return to Garage" button didn't work

**Symptom.** Clicking the button on `MatchEndOverlay` did nothing.

**Cause.** [`FollowCamera.cs:302–307`](../../Assets/_Project/Scripts/Player/FollowCamera.cs:302)
has a per-frame guard that re-applies cursor lock when `_cursorWasLocked
== true` and the cursor isn't currently locked — meant to recover from
an alt-tab focus drop. `ArenaController.HandleMatchEnded` was setting
`Cursor.lockState = None` directly, but the next frame's
`FollowCamera.LateUpdate` re-locked it before IMGUI could process the
mouse-up event. The cursor was visible for one frame then snapped back
to centre-locked.

**Fix.** Made
[`FollowCamera.ReleaseCursor()`](../../Assets/_Project/Scripts/Player/FollowCamera.cs)
public. It clears `_cursorWasLocked` (which breaks the relock loop)
in addition to setting the cursor state. `ArenaController.HandleMatchEnded`
now calls `follow.ReleaseCursor()` instead of poking the cursor
directly. One-line conceptual change; reinstates the click path.

### 3. Bots stuck on stale player reference after respawn

**Discovered while testing #1.** When the player died and respawned,
existing bots kept their `Target` pointing at the destroyed
chassis's transform. Unity's fake-null check returned "null" so the
bots fell back to Patrol/Cruise forever.

**Fix.** Added
[`RebindBotTargets()`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)
called from `RespawnPlayer`. Iterates the
`_matchBots` list, finds every `GroundBotInputSource` /
`AirBotInputSource`, and pushes the new chassis transform onto each
one's `Target` property. Cheap (called only on respawn, not per
frame).

## Addendum 2 — Air dummy + round-start gate

User asked for two follow-ups: a tweakable-spawnable air bot mirroring
the existing tank dummy, and a "don't start the fight until I press a
button" gate so the player can compose themselves before bots aggro.

### Air dummy bot

Two new Tweakables in [`Tweakables.cs`](../../Assets/_Project/Scripts/Core/Tweakables.cs):

- `Stress.AirDummy` (toggle) — spawn/despawn the air bot.
- `Stress.AirDummyFire` (toggle) — bind the player as target + enable
  fire. With this off the bot Cruises peacefully; with it on the bot
  switches to Pursue/Engage and fires.

ArenaController gained a parallel block to the tank-dummy path:
`SpawnAirDummy` / `DespawnAirDummy` / `ApplyAirDummyState` /
`ApplyAirDummyFire`. The blueprint resolver picks the first preset
whose name contains `Heli` or `Plane`, falling back to any Plane-kind
preset, so the existing helicopter blueprint is the default
out-of-the-box without any inspector wiring. Spawn position, cruise
centre, cruise radius, and altitude are SerializeFields on
ArenaController for per-arena tuning.

The DevHud auto-shows both new sliders since it iterates
`Tweakables.All` — no UI changes required.

### Round-start gate

The user wanted the enemy bot to spawn but stay non-aggressive until
the player explicitly starts the fight. Implemented via:

1. **`MatchConfig.RequireManualStart`** — new bool, default `true`.
   When set, `MatchController.Tick` no longer auto-transitions out of
   WarmingUp on `WarmupDuration` expiry. Only an explicit
   `MatchController.StartMatch()` call ends warmup.

2. **`MatchController.StartMatch()`** — new public method that
   fast-forwards the state machine from WarmingUp → InProgress and
   raises `MatchStarted`. Idempotent on terminal states.

3. **[`StartMatchHud.cs`](../../Assets/_Project/Scripts/Gameplay/StartMatchHud.cs)**
   — IMGUI overlay shown only during `MatchState.WarmingUp`. Shows a
   "STAND BY" headline, instructional copy, and a big **FIGHT!**
   button. Clicking calls `match.StartMatch()`. Self-suppresses once
   the round is in progress. Same IMGUI pattern as `MatchEndOverlay`,
   bound by `ArenaController.BindFollowCamera`.

4. **Passive bot spawning.** `SpawnGroundBot` and `SpawnAirBot` now
   spawn with `Target = null` and `FireAtTarget = false`. Bots
   Patrol around their circle centre without engaging. When
   `MatchStarted` fires, ArenaController's `HandleMatchStarted`
   iterates `_matchBots` and binds the player chassis as `Target` +
   `FireAtTarget = true`. Bots flip into Pursue/Engage and the
   round goes hot.

5. **Cursor lifecycle.** During warmup the cursor is unlocked
   (`ReleaseCursorForUI` → `FollowCamera.ReleaseCursor`) so the
   FIGHT button can be clicked. On `MatchStarted` the cursor relocks
   (`ReclaimCursorForGameplay` → `FollowCamera.ApplyCursorLock`).

   To make this round-trip work cleanly, `FollowCamera.ApplyCursorLock`
   was made public — same trick as `ReleaseCursor` last addendum, so
   external systems can hand cursor control back to the
   FollowCamera's relock-recovery state machine without poking
   `Cursor.lockState` directly.

6. **Legacy tank/air dummies follow the same passive/aggressive
   split.** `ApplyTankDummyFire` / `ApplyAirDummyFire` now bind
   Target only when the fire toggle is on. With the toggle off the
   bot has no target → stays in Patrol. Toggle on → bot binds the
   player and goes hot. Matches the round-start gate's "passive
   until I press FIGHT" feel.

### Test impact

The pre-existing `MatchControllerTests.MakeConfig` helper was
updated to set `RequireManualStart = false` so the warmup-timer
auto-transition tests still pass. Production default remains
`true` (manual start required) — the test path is the deviation.

### Correction — start gate is a hotkey, not a button

The first cut of the round-start gate used a modal IMGUI overlay
with a clickable FIGHT! button. Two issues surfaced in playtest:

1. **The button didn't register.** `FollowCamera.Update` has a
   click-to-recapture path (line 282–290) that locks the cursor
   on LMB-down whenever the click isn't on a UGUI element. IMGUI
   buttons don't register with the EventSystem, so the path saw
   the click as a "regrab cursor" event and locked the cursor to
   centre BEFORE IMGUI could process the click. Net effect: cursor
   centres, IMGUI sees a click at centre-of-screen, button doesn't
   fire.
2. **A modal overlay broke free-flight.** Forcing the cursor
   unlocked just to click a button means the player can't mouse-look,
   which is exactly the experience the user wanted preserved during
   warmup.

Replaced with a non-blocking corner prompt + hotkey:

- **`StartMatchHud`** now draws a small "Press [SPACE] to begin
  combat" pill at the bottom-centre of the screen during WarmingUp.
  Non-modal — the player flies / drives freely.
- **`ArenaController._startMatchKey`** (default `Space`,
  configurable in the inspector) is polled in `Update`. When
  pressed during WarmingUp with the cursor locked, calls
  `_match.StartMatch()`. Same pattern as the existing
  `_respawnKey` (K).
- The cursor stays **locked** the entire warmup. No special
  `ReleaseCursorForUI` / `ReclaimCursorForGameplay` round-trip.
  The end-of-match overlay still uses the unlock helper for its
  Return-to-Garage button — that remains modal because the
  player is dead/match over and there's nothing to fly.

Bot passivity, target-binding-on-MatchStarted, and the
`RequireManualStart` config flag are unchanged.

`FollowCamera.ApplyCursorLock` was made public during the first
cut to support the relock-on-MatchStarted step. With cursor lock
unbroken through warmup it's no longer called externally, but
the public symmetry with `ReleaseCursor` is worth keeping —
some future system (settings-panel dismiss, build-mode exit)
will want it.

### New tunables on bot input sources

These ship at sensible defaults; mention here so the inspector
fields are discoverable:

- `OptimalRange` — engagement orbit radius. Ground default 25 m,
  air default 150 m.
- `EngageBuffer` — hysteresis gap for Pursue ↔ Engage. Ground
  default 8 m, air default 30 m.
- `PursueThrottle` (ground only) — throttle while closing distance.
  Default 0.95 (committed approach).
- `EngageFacingDotThreshold` — fire-arc cosine threshold. Ground
  default −0.3 (~250°), air default 0.6 (~100°).

## Future-session starter

1. Read this file (latest in `docs/changes/`).
2. `docs/changes/architecture.md` — modules table.
3. `docs/PERFORMANCE.md` — perf rules (the new HUDs and AI bots
   should pass the steady-state-zero-alloc bar; spot-check with the
   F3 perf HUD if anything feels off).
4. Run the EditMode tests (`Window > General > Test Runner > EditMode > Run All`)
   to confirm the state machine still passes after any future
   refactor.
