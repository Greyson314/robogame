# 56 — Scrap-based scoring + rope aim-sphere persistence fix

> Two changes shipped in one session. (1) A rope aim-sphere regression:
> the generous-aim sphere that lets the player place a hook at a chain's
> free end was only spawned when the tip cell was empty, but
> `RopeBlock` didn't rebuild when a placed tip was right-click-removed,
> so subsequent re-placements went back to threading through the
> cylinder's 16 cm cap. (2) Battle scoring switched from kill count to
> team scrap deposited at depots.

## Rope fix: rebuild static visual on tip removal

[`RopeBlock`](../../Assets/_Project/Scripts/Movement/RopeBlock.cs)
subscribes to `BlockGrid.BlockRemoving` and queues a static-visual
rebuild via a `_pendingStaticRebuild` flag drained in `Update()`. The
event fires BEFORE the grid removes the cell — querying the grid in
the handler would still see the tip and re-suppress the sphere. The
one-frame defer reads the grid in its post-removal state and
re-spawns the aim sphere correctly.

## Scrap scoring rework

### Model — team scrap as the score

[`MatchController`](../../Assets/_Project/Scripts/Gameplay/MatchController.cs)
no longer counts kills. Score is the team-scrap total deposited at
each team's `ScrapDepot`. New API:

- `DepositScrap(MatchSide side, int amount)` → returns the side's
  new running total. Triggers `MatchEnded(ScrapLimitReached)` when
  the side hits `MatchConfig.TargetTeamScrap` (default 20).
- `TeamScrapChanged` event — fired on every deposit; HUDs read this
  instead of polling.
- `ScoreForSide(side)` semantics flipped from "kill count" to
  "team-scrap total". Same return type, same diagnostic accessors
  on `ObjectiveHud` work unchanged.
- `RegisterKill` is **informational only** now — fires
  `KillRegistered` for the `KillAnnouncer` streak banner but does
  not move the score.

[`MatchConfig`](../../Assets/_Project/Scripts/Gameplay/MatchTypes.cs)
replaced `TargetFragCount = 5` with `TargetTeamScrap = 20`, and
added two new tuning fields: `BaseDeathDrop = 3` (flat scrap dropped
on every death) and `ScrapCarryCapacity = 8` (max scrap a single
robot can carry). [`MatchEndReason.FragLimitReached`](../../Assets/_Project/Scripts/Gameplay/MatchTypes.cs)
renamed to `ScrapLimitReached`.

### Per-robot carry mechanic

[`Robot`](../../Assets/_Project/Scripts/Robot/Robot.cs):

- `ScrapHeld` and `ScrapAwarded` event already existed (session 35);
  added `ScrapCarryCapacity`, `IsScrapFull`, and `ConfigureScrap`
  (called by `ArenaController` to push `MatchConfig`'s cap onto
  every spawned chassis).
- New `TryAwardScrap(int)` returns the amount actually awarded —
  respects the cap and partial-fits a pickup into the remaining
  slot. The legacy `AwardScrap` is now a thin wrapper for non-
  pickup callers.
- New `DepositScrap()` drains carried scrap and returns the amount
  banked. Fires `ScrapAwarded` with a negative delta so HUDs update
  without polling.

[`ScrapPickup`](../../Assets/_Project/Scripts/Gameplay/ScrapPickup.cs):

- `Collect` → `TryCollect`. Now calls `TryAwardScrap`; if the
  collector is full, the pickup stays in the world. If a partial fit
  is possible, the pickup banks what fits and reduces its own value
  to the remainder so a second robot can finish it off.
- Added `OnTriggerStay` so a chassis that's full on entry, then
  deposits at a depot WITHOUT leaving the pickup's trigger volume,
  banks the pickup on the next tick.

[`ScrapDropper`](../../Assets/_Project/Scripts/Gameplay/ScrapDropper.cs)
replaced the `scrapPerBlock × startingBlocks` formula with the simpler
`BaseDeathDrop + victim.ScrapHeld`. A respawn-fresh enemy is a 3-scrap
kill; an enemy carrying 7 scrap is a 10-scrap kill. Encourages
target prioritisation.

### Scrap depots — touch to bank

New [`ScrapDepot`](../../Assets/_Project/Scripts/Gameplay/ScrapDepot.cs)
component. A team-aligned trigger volume that drains
`Robot.ScrapHeld` and credits the matching team total when a
matching-side robot enters. Procedural visuals: a wide flat puck +
a tall beam of team-coloured light (orange for the player team, red
for enemy) so depots are findable from across the arena. Subtle
emission pulse marks them as "active / collecting".

[`ArenaController`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)
spawns both depots procedurally after the match controller exists +
bots are registered. Default placement: player depot at
`(0, 0.2, -30)`, enemy depot at `(0, 0.2, 40)` — opposite sides of
the existing combat dummy area. Inspector fields override.

Faction filter: depots take a `Func<Robot, MatchSide>` callback at
spawn (the ArenaController's `LookupSide` method) so each
`OnTriggerStay` tick is O(1) without scanning the registration map.

### HUD changes

[`ObjectiveHud`](../../Assets/_Project/Scripts/Gameplay/ObjectiveHud.cs)
top-centre panel:

- Score line changed from `"3  —  2"` (kills) to
  `"3 / 20      2 / 20"` (team scrap, team-coloured).
- Added a `TEAM SCRAP` label row above the score.
- Panel height bumped 72→96 px to accommodate the label.

[`VehicleStatsHud`](../../Assets/_Project/Scripts/Player/VehicleStatsHud.cs)
bottom-right panel: `SCR` row now shows `held / capacity` and flips
to alert-red with a `FULL` suffix when the chassis can't carry more.

[`MatchEndOverlay`](../../Assets/_Project/Scripts/Gameplay/MatchEndOverlay.cs)
end-screen: score line reads `SCRAP   N — M` and the reason copy
maps `ScrapLimitReached → "Scrap quota reached"`.

## Bonus features

Three QoL / dev-speed extensions called out in the spec:

### 1. Worldspace scrap-carried indicator

[`ScrapCarriedIndicator`](../../Assets/_Project/Scripts/Gameplay/ScrapCarriedIndicator.cs)
parented to the main camera. Scans live `Robot` instances every
0.5 s, projects each carrying robot's world position to screen
space, draws a team-tinted `⛁ N` label above their chassis. Lets
the player target high-load enemies (juicy kill) vs respawn-fresh
ones (3-scrap kill only). Hidden within ~4.5 m of the camera so the
local player's own indicator doesn't clutter their viewport.

### 2. Capacity-aware pickup + visible "FULL" cue

The `ScrapCarryCapacity` cap forces commitment to the deposit loop.
A robot can't infinitely hoard — they have to bank to keep killing
profitable. The HUD's `SCR  7 / 8  FULL` red label tells the player
exactly when the next pickup will refuse. Pair this with capacity-
aware partial pickups (a 3-value pickup walked through at 6/8 banks
2 and leaves a 1-value pickup behind — never wastes loot) and the
loop self-balances.

### 3. Dev cheat keys for fast iteration

[`ScrapDevCheats`](../../Assets/_Project/Scripts/Gameplay/ScrapDevCheats.cs)
compile-guarded behind `UNITY_EDITOR || DEVELOPMENT_BUILD`. Saves
the test-loop time of "kill three bots, walk to depot, deposit,
repeat" every iteration:

- **F2**: grant the local player 5 scrap.
- **F3**: teleport the local player to their team depot.
- **F4**: push player team score to `target - 1`. Walk a 1-scrap
  deposit onto the pad to trigger the victory overlay.

All gated on `Cursor.lockState == Locked` so they don't fire while
a settings panel is up.

## Files

- **Edited:**
  - `RopeBlock.cs` — `BlockRemoving` subscription + deferred rebuild.
  - `MatchController.cs`, `MatchTypes.cs` — score-by-scrap rewrite.
  - `Robot.cs` — capacity + deposit + partial-fit award.
  - `ScrapDropper.cs` — drop formula = `BaseDeathDrop + ScrapHeld`.
  - `ScrapPickup.cs` — capacity-aware collection + partial fit.
  - `ArenaController.cs` — depot spawn, side lookup, scrap config push,
    dev-cheat + indicator binding.
  - `ObjectiveHud.cs`, `VehicleStatsHud.cs`, `MatchEndOverlay.cs` —
    HUD copy + capacity readout.
  - `Arena.unity` — MatchConfig fields updated; depot positions added.
- **New:**
  - `ScrapDepot.cs` — touch-to-bank trigger.
  - `ScrapCarriedIndicator.cs` — worldspace label above carrying robots.
  - `ScrapDevCheats.cs` — F2/F3/F4 cheat keys (editor / dev only).
- **Tests:**
  - `MatchControllerTests.cs` — full rewrite for scrap-deposit semantics
    (kill = no score, deposit = score, capacity-aware end conditions).

## Open + tested by hand

- Bots don't yet seek depots on their own. Enemy bots will collect
  scrap (their AI passes through pickups during normal patrol +
  pursue) but won't actively path to their depot, so player progress
  is the dominant scoring driver in current playtests. Bot-side
  depot pathfinding is the natural next step.
- "Drop on damage" / partial-scrap-loss-on-hit is *not* implemented.
  The cap + deposit-or-lose-everything-on-death pressure already
  pushes engagement; an additional drop-on-damage system can come
  later if hoarding-by-survival becomes a problem.
- The bonus indicator scans `FindObjectsByType<Robot>` every 0.5 s.
  If bot count explodes (>20 chassis on screen), revisit with a
  static `Robot` registry.

## Verification

1. **Rope fix.** Build chassis with rope + hook → remove the hook
   (right-click) → re-place via the generous aim sphere. Should be
   as easy as the first placement.
2. **Score flow.** Spawn into arena → kill the bot → walk over the
   scrap drop → see `SCR 0 → 3` on the bottom-right HUD → drive to
   the orange depot at z=-30 → see `TEAM SCRAP 3 / 20` top-centre.
3. **Win.** F4 to push to 19/20, drive to depot with 1+ scrap →
   victory overlay fires with `SCRAP 20 — 0` and `Scrap quota reached`.
4. **Capacity.** Pick up enough scrap to fill the cap → SCR row turns
   red with `FULL` → next pickup refuses (pickup stays in world) →
   deposit → return → pickup is collected.
5. **Worldspace indicator.** Look at an enemy bot carrying scrap (or
   F2 your own bot via console fudge) → see `⛁ N` floating over them.
