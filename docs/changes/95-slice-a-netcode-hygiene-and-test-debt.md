# 95 — Slice A: netcode hygiene & test debt

> Status: **Slice A shipped.** 6 baseline test failures (2 EditMode + 4
> PlayMode) cleared; Phase 4 late-join replay wired into the scene
> lifecycle. EditMode 252/253, PlayMode 92/93 — both green (the remaining
> 1 each is a documented `[Ignore]` / `Inconclusive`, not a failure).

## Scope

First slice of the polish + netcode QoL phase plan
(`~/.claude/plans/let-s-deep-run-on-the-bright-swan.md`). Goal: clear the
standing test-debt baseline and connect the dangling Phase 4 late-join
machinery so subsequent slices land on a green test runner.

## What landed

**EditMode test fixes (2).**

- `BlueprintBlobTests.ContentHash_StableAcrossReserialize` — removed the
  obsolete `Assert.AreNotEqual(j1, j2)` sanity check at the tail. The
  assertion presumed `BlueprintSerializer.ToJson` would always vary
  per-call via `DateTime.UtcNow`'s sub-second precision; on Windows the
  system clock resolution (~15 ms) means two adjacent calls can produce
  identical `"o"` strings, tripping the meta-assertion. The primary
  invariant (`h1 == h2`) is what the test name promises and it works
  correctly — `BlueprintBlob.Encode` deliberately excludes `createdUtc`.
- `NetworkContextTests.RegisterNull_IsIgnored_StaysOffline` — wrapped
  the `Register(null)` call with `LogAssert.Expect(LogType.Error, ...)`.
  The `Debug.LogError` is the contract (it surfaces the misuse), but
  NUnit treats unexpected log errors as test failure. Surgical fix.

**PlayMode test fixes (4).**

- `RotorBlockTests.RotorBlock_BuildLiftRig_AdoptsFourLateralAerofoils_PlacedAtSpinPlaneCells` —
  relaxed the world-position tolerance from 5 cm to 20 cm and fixed the
  broken `{epsilon}` string interpolation. `RotorBlock.AdoptAdjacentAerofoils`
  places blades on a ring whose radius is slightly larger than one cell
  (blade geometry needs the clearance), so foils settle 12 cm outside
  their placed-cell centre in steady state. The test's original intent
  per its comment was "the foil is reparented but NOT displaced by a
  full block" — 20 cm is well under that bar.
- `DigZoneTests.OnEnable_RegistersWithDigField` +
  `…DrillBlock_InsideZone_FireHeld_AutoPollsViaFixedUpdate_CarvesSdf` +
  `…TerrainCratering_BombInsideZone_CarvesSphereCrater` — all three use
  `DigField.ZoneAt(...)` to look up the test's zone, and were
  intermittently picking up a stale entry from a prior test in the
  PlayMode session. Added `DigField.ResetForTesting()` (a deliberately
  named public test-seam) and called it from `[SetUp]` + `[TearDown]`
  in `DigZoneTests`. Production code is unchanged.

**Phase 4 late-join wiring.** `NetworkSceneFlow.HandleSceneEvent` now
also handles `SceneEventType.SynchronizeComplete` on the server side.
On non-host client sync, walks every `NetworkBlockGrid` in the scene
and calls `ServerSendDestructionLogTo(clientId)`. Each grid no-ops if
its log is empty, so it's cheap on a fresh round. A new `ServerClientSynced`
event is also surfaced for future use. v1 still locks lobbies at round
start (§10) — wiring this now means v2 mid-match join is a lobby-config
flip, not a fresh integration.

## What's deferred (deliberate scope cut)

`Assets/_Project/Tests/Scenes/MinimalArena.unity` scaffold — the plan
called for creating this scene to unblock `MatchFlowTests.SpawnBot_…`.
That test is already `[Ignore]`'d with a documented blocker, so it
doesn't pollute the test-runner output. Hand-crafting a valid `.unity`
asset without the editor is brittle, and a runtime-bootstrap approach
would need significant test-suite rework. The slice's actual goal
(clear baseline failures → 0 failures) is achieved; the MinimalArena
scaffold is a separate concern flagged for a future session if the
SpawnBot path becomes load-bearing.

## Tests

`EditMode: 252/253 passed, 0 failed, 1 inconclusive.`
`PlayMode: 92/93 passed, 0 failed, 0 inconclusive.`

The remaining 1 inconclusive (EditMode) + 1 ignored (PlayMode
`MatchFlowTests.SpawnBot`) are pre-existing documented gaps, not
regressions.

## Files

Edited: `Assets/_Project/Tests/EditMode/Blueprints/BlueprintBlobTests.cs`,
`Assets/_Project/Tests/EditMode/Gameplay/NetworkContextTests.cs`,
`Assets/_Project/Tests/PlayMode/Movement/RotorBlockTests.cs`,
`Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`,
`Assets/_Project/Scripts/Core/DigField.cs` (added `ResetForTesting`),
`Assets/_Project/Scripts/Network/Bootstrap/NetworkSceneFlow.cs`
(SynchronizeComplete handler + `ReplayDestructionLogTo` + new
`ServerClientSynced` event), `docs/subsystems/netcode.md` (Phase 4 entry
ticked to "wired"), this log + the README index.
