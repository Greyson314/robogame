# 58 — Scrap Loop v1 (6-phase end-to-end pass)

> Status: **shipped, untested in-engine.** Implements every phase of
> [SCRAP_LOOP_PLAN.md](../SCRAP_LOOP_PLAN.md) in one session. Friendly
> AI tank + carry-weight penalty + depot rework (instant-bank → score
> tick) + grinder hazard + per-weapon-type ammo + reload.

## Why this session

User: *"Please help me fully implement SCRAP_LOOP_PLAN.md."* The plan
itself was authored 2026-05-08 with six dependency-ordered phases.
This session lands all six in implementation order.

## What changed

### Phase 1 — Team allegiance + friendly AI tank

- New [`TeamId`](../../Assets/_Project/Scripts/Robot/TeamId.cs) enum in
  `Robogame.Robots`. Carries `None / Player / Enemy`; numerically
  matches `MatchSide` so `ArenaController` casts between them with a
  byte cast, no translation table. Lives in the Robots asmdef so
  `ProjectileWorld` (Combat tier) can damage-filter on it without
  pulling Gameplay-tier types down. `Robot.Team` + `ConfigureTeam(...)`
  exposed.
- [`ArenaController`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)
  pushes `TeamId` on registration. New `_friendlyTank*` SerializeFields
  spawn one Player-team AI tank alongside the player. Existing bot-
  target binding rewritten as `ResolveTargetFor(side)` + `RebindBotTargets()`
  — friendly bots hunt enemies, enemy bots hunt the player, and
  rebinding fires on every kill so a dead target doesn't strand a
  bot in Patrol.
- [`ProjectileWorld`](../../Assets/_Project/Scripts/Combat/ProjectileWorld.cs)
  drops damage on same-team hits in all three resolve paths
  (`ApplyDirect`, `ApplyRingSplashOnHit`, `ApplyAreaSplash`). Neutral
  chassis (`TeamId.None`) remain damageable by everyone so training
  dummies still bleed. V1 limitation: bullets *stop* on a teammate
  rather than passing through, but no damage applies. Acceptable for
  arcade arenas.

### Phase 2 — Scrap carry-weight penalty

Stepped curve per the plan: 0–2 = 1.00, 3–5 = 0.95, 6–9 = 0.85, 10+ = 0.70.

- `Robot.CarryWeightMoveMultiplier` + `Robot.ComputeMoveMultiplier(int)`
  static curve.
- `DriveControl.SpeedMultiplier` scalar; `RobotDrive.CarrySpeedMultiplier`
  public setter populates it each tick.
- `GroundDriveSubsystem` and `ThrusterBlock` scale their force output
  (and `GroundDriveSubsystem`'s top speed) by the multiplier. Torques
  and directional channels are untouched.
- New
  [`ScrapCarryMovementPenalty`](../../Assets/_Project/Scripts/Gameplay/ScrapCarryMovementPenalty.cs)
  bridge component: subscribes to `Robot.ScrapAwarded` and pushes the
  multiplier onto `RobotDrive.CarrySpeedMultiplier`. Lives in
  Gameplay tier because `Robogame.Movement → Robogame.Robots` would be
  a circular asmdef reference. `ChassisAssembler` adds it on every
  combat-armed chassis.

### Phase 3 — Deposit AOE + instant-transfer + score tick

[`ScrapDepot`](../../Assets/_Project/Scripts/Gameplay/ScrapDepot.cs)
fully rewritten:

- Trigger radius bumped 3.5 m → 5.5 m (`_triggerRadius`) so the depot
  reads as an AOE volume the player drives through, not a contact pad.
- Two-stage transfer. Touching the volume *instantly* drains
  `Robot.ScrapHeld` into a per-depot `_bankedScrap` pool. The depot
  then ticks `_scoreTickAmount` (default 1) scrap from that pool into
  `MatchController.DepositScrap` every `_scoreTickInterval` (default
  1.0 s). `BankedScrap` is a public read for HUDs / tests.
- Raid window: while the pool drains, an enemy raiding the depot can
  delay the bank by killing the depot's defenders (the scrap goes to
  the depot, not the chassis, so the kill doesn't undo banked scrap —
  but the bot can no longer carry new scrap to extend the lead).
- New audio cue
  [`AudioCue.ScrapTick`](../../Assets/_Project/Scripts/Core/AudioCue.cs)
  fires on every increment. Quiet metronome pulse; library entry left
  blank per AUDIO_PLAN.

### Phase 4 — Depot AOE damage + double-scrap-on-kill

Same `ScrapDepot` rewrite handles the grinder:

- `OnTriggerEnter` / `Exit` track enemy chassis inside the volume.
  Friendly chassis are excluded by the side check.
- `TickGrinder()` applies splash damage every `_grinderDamageInterval`
  (default 0.25 s) at `_grinderDamagePerSecond` (default 50). Three-ring
  splash centred on the enemy block closest to the depot centre — the
  cheap shape called out in `SCRAP_LOOP_PLAN § 4` (per-block in-volume
  queries would be expensive).
- Static `ScrapDepot.FindDepotContaining(Robot)` lets `ScrapDropper`
  consult whether the dying chassis was inside a depot. If so, the
  drop's total value is multiplied by `GrinderKillBonus` (default 2×)
  before being split into pickups.
- The depot pulse / visual scales with the new trigger radius (puck
  diameter = `_triggerRadius × 2`).

### Phase 5 — Max ammo + per-weapon-type clip system

- `WeaponDefinition`, `BombDefinition`, `CannonDefinition` gained
  `ClipSize`, `ReloadDuration`, `AutoReloadDelay`. Defaults: SMG
  30 rds / 1.5 s, Cannon 6 rds / 3.0 s, Bomb 4 rds / 4.0 s, auto-delay
  0.3 s for all. Per the plan, stats live on the SO — never on
  `Tweakables`.
- New
  [`WeaponAmmoState`](../../Assets/_Project/Scripts/Combat/WeaponAmmoState.cs)
  per-chassis tracker. Per-weapon-type pools keyed by `BlockId`. Max
  capacity = `clipSize × instances`. Init walks the grid at OnEnable;
  subscribes to `BlockGrid.BlockPlaced` / `BlockRemoving` so a chassis
  losing weapon blocks mid-fight shrinks the pool and clamps current
  ammo. `ChassisAssembler` adds it to every combat-armed chassis.
- Fire paths in
  [`ProjectileGun`](../../Assets/_Project/Scripts/Combat/ProjectileGun.cs),
  [`CannonBlock`](../../Assets/_Project/Scripts/Combat/CannonBlock.cs),
  and [`BombBayBlock`](../../Assets/_Project/Scripts/Combat/BombBayBlock.cs)
  consult `WeaponAmmoState.CanFire` before firing and call `Consume`
  on each shot. Throttled `AudioCue.WeaponEmpty` plays on a held
  trigger over an empty pool so the player gets feedback.
- New cues: `WeaponEmpty`, `ReloadStart`, `ReloadComplete`. Library
  entries blank.

### Phase 6 — Auto-reload-on-empty + manual R

- `WeaponAmmoState` schedules a reload when the last round is consumed
  (after the SO's `AutoReloadDelay`). Two-stage timer:
  `_reloadStartsAt` (post-delay) → `_reloadEndsAt` (pool refills at
  completion).
- `IInputSource` gained `bool ReloadPressed`. `PlayerInputHandler`
  reads `Keyboard.current.rKey.wasPressedThisFrame` directly — the
  project `InputActionAsset` doesn't yet carry a Reload binding, so
  this keeps the change out of the asset. Bot input sources
  (`GroundBotInputSource`, `AirBotInputSource`) return `false` —
  bots ride on auto-reload only.
- `WeaponAmmoState.Update` reads the bound input source's
  `ReloadPressed` and calls `RequestReloadAll()` on the player frame.
  Bots get the same auto-reload-on-empty behaviour without any
  manual override.
- HUD: new AMO row in
  [`VehicleStatsHud`](../../Assets/_Project/Scripts/Player/VehicleStatsHud.cs)
  collapses every pool into one compact line — e.g.
  `AMO  SMG 27/30 · CAN 4/6 · BMB R`. `R` = reload in progress.
  Empty pools flip the line to alert-red. Panel height bumped
  122 → 152 to fit the new row.

## Files

- **New:**
  - `Scripts/Robot/TeamId.cs`
  - `Scripts/Gameplay/ScrapCarryMovementPenalty.cs`
  - `Scripts/Combat/WeaponAmmoState.cs`
- **Edited:**
  - `Scripts/Robot/Robot.cs` — `TeamId` + `ConfigureTeam` +
    `CarryWeightMoveMultiplier` + `ComputeMoveMultiplier`.
  - `Scripts/Combat/ProjectileWorld.cs` — friendly-fire filter on
    all three resolve paths.
  - `Scripts/Movement/DriveControl.cs` — `SpeedMultiplier` field.
  - `Scripts/Movement/RobotDrive.cs` — `CarrySpeedMultiplier` push.
  - `Scripts/Movement/GroundDriveSubsystem.cs` — scale accel + cap by carry-mul.
  - `Scripts/Movement/ThrusterBlock.cs` — scale thrust by carry-mul.
  - `Scripts/Gameplay/ArenaController.cs` — friendly tank wiring,
    team-aware target rebind, push `TeamId` on registration.
  - `Scripts/Gameplay/ScrapDepot.cs` — full rewrite (AOE, instant-
    bank, score tick, grinder, in-volume tracking).
  - `Scripts/Gameplay/ScrapDropper.cs` — grinder-kill bonus.
  - `Scripts/Gameplay/ChassisAssembler.cs` — register the two new
    Gameplay-tier bridge components.
  - `Scripts/Combat/WeaponDefinition.cs` — ammo + reload fields.
  - `Scripts/Combat/BombDefinition.cs` — ammo + reload fields.
  - `Scripts/Combat/CannonDefinition.cs` — ammo + reload fields.
  - `Scripts/Combat/ProjectileGun.cs` — ammo gate + consume.
  - `Scripts/Combat/CannonBlock.cs` — ammo gate + consume.
  - `Scripts/Combat/BombBayBlock.cs` — ammo gate + consume.
  - `Scripts/Input/IInputSource.cs` — `ReloadPressed`.
  - `Scripts/Input/PlayerInputHandler.cs` — implement R via direct keyboard read.
  - `Scripts/Gameplay/GroundBotInputSource.cs` / `AirBotInputSource.cs` — stub `ReloadPressed`.
  - `Scripts/Player/VehicleStatsHud.cs` — AMO row, panel height 152.
  - `Scripts/Core/AudioCue.cs` — `ScrapTick`, `WeaponEmpty`,
    `ReloadStart`, `ReloadComplete`.

## Hard-invariant check

- **No Tweakable affects gameplay.** Carry-weight curve, depot tick
  rate, grinder damage, ammo / reload tuning all live on Robot (curve
  is static), `ScrapDepot` SerializeFields, or per-weapon Definition
  SOs. PHYSICS_PLAN § 1.5: clean.
- **Server-authoritative shape.** Damage filter, ammo consume,
  deposit, score-tick are all single-mutation paths. When MP lands
  these become server-side RPCs; clients render off replicated state.
- **No per-frame allocations.** `TickReloads` collects mutated pool
  keys into a per-frame list that bails when no pool changes;
  `TickGrinder` has a per-tick prune scratch list reused across calls.
  Ammo HUD uses a `StringBuilder` reset per frame, no new strings.
- **VFX + audio.** New cues: `ScrapTick`, `WeaponEmpty`,
  `ReloadStart`, `ReloadComplete`. Library entries are blank for now;
  missing-cue logger will surface what the audio pass owes.

## Known follow-ups

- **Audio cues blank.** Four new cues need clips on the library asset.
- **Friendly-fire bullet pass-through.** Bullets currently stop on
  teammate hits but deal no damage. Cleaner shape: skip teammate
  colliders in the swept cast. Defer until it's measurably annoying.
- **Per-bot target proximity.** `ResolveTargetFor` anchors at the
  player chassis for proximity sort. Once multiple friendly bots are
  in play this should resolve per-bot.
- **Depot-only reload for heavies.** Per the plan's open question,
  V1 ships "reload anywhere". Depot-only reload for cannons / bombs
  is a Phase 6.5 once the depot proves out as a destination.
- **Friendly scrap drop ownership.** Currently any team can collect
  any pickup. The plan flagged this as Phase 3.5; left as-is for V1.
- **Bot ammo tuning.** Bots respect the same pool / reload rules as
  the player. May read as a feel issue if a cannon bot stops shooting
  every 6 rounds. Watch in playtest; if bad, give bots a separate
  larger clip via per-team configuration.

## Verification

1. **IFF.** Spawn into Arena → friendly tank exists at `_friendlyTankSpawn`
   on the player team → shooting it produces no damage (block count
   doesn't drop). Enemy tank shooting friendly produces no damage.
2. **Carry weight.** Hold zero scrap → tank speed normal. F2 cheat to
   10 scrap → tank visibly slower (~70% top speed). Deposit at depot →
   speed restored over the next few seconds as the depot drains.
3. **Depot tick.** Drive over player depot with 5 scrap → SCR drops to
   0 immediately, team score climbs 1/sec over 5 seconds. Visible on
   the top-centre `TEAM SCRAP` HUD.
4. **Grinder.** Drive an enemy bot into the player depot → enemy
   takes damage every ~250 ms, dies in ~3 seconds. The death drop is
   doubled — scrap pile is visibly fatter than a non-grinder kill.
5. **Ammo.** Hold fire on the SMG → AMO row decrements from 30 to 0
   over 2.5 s → fire stops → AMO shows `R` for 1.5 s → AMO refills
   to 30 → fire resumes.
6. **Manual reload.** Fire 10 rounds → press R → reload starts
   immediately on every non-full pool.
