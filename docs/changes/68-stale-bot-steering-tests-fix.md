# 68 — Stale bot-steering tests (session 62 follow-up)

> Status: **code written, dotnet build clean, EditMode test run pending
> on user's Unity side.** Pure cleanup — no production code changed,
> tests migrate from a deleted type to the live one with the same math.

## Why this session

Session 62 deleted `DummyAiInputSource.cs` (28 lines, `[Obsolete]`-marked,
superseded by `GroundBotInputSource`). Its log explicitly noted "Tests
untouched" — and that was the bug. `DummyAiInputSourceTests.cs` survived
because it never imported the deleted type: it inlined a copy of the
patrol-circle math as a private static helper and asserted against
that. The tests *compiled* but two of them now failed against an
incorrect coordinate-frame assumption baked into the scenarios.

Failures the user reported:

- `PatrolSteer_BotFacingTangent_AtRadius_ProducesZeroSteer` — expected 0,
  got 1.
- `PatrolSteer_BotFacingAwayFromTangent_ProducesHardTurn` — expected ±1,
  got 0.

Both turned out to encode the same mistake (see "Root cause" below).
`PatrolSteer_FullSteer_ReducesThrottle` had the same mis-set `forward`
and asserted `Assert.Less(0.7, 0.7)` — also a latent failure even if
the user's report only listed the first two.

## Root cause

`Vector3.Cross(Vector3.up, radial)` with `radial = (+1, 0, 0)` returns
`(0, 0, -1)` in Unity's coordinate system. The patrol-circle tangent at
the east point is therefore **−Z**, not +Z. Each failing test's
`forward` had been authored as if the tangent were +Z (the "CCW when
viewed from above" intuition), so:

- "Facing the tangent" with `forward = (0, 0, 1)` was actually facing
  the *opposite* of the tangent → `dot < -0.5` branch → `steer = ±1`.
- "Facing away from the tangent" with `forward = (0, 0, -1)` was
  actually aligned with the tangent → `dot = 1`, `cross = 0` → `steer = 0`.

The tests were testing the right invariants with the wrong setups.

## Path taken

**Path B** — migrate, not delete. The steering invariants the tests
encode (anti-parallel hard-turn fallback, on-tangent zero-steer,
clamp to ±1, anti-spin-out throttle reduction, zero-forward fallback,
singularity guard) are all still load-bearing for `GroundBotInputSource`,
which inherits the identical math from the deleted source (production
`GroundBotInputSource.ComputeSteer` is the exact same formula).

## What changed

- **Renamed** `Tests/EditMode/Gameplay/DummyAiInputSourceTests.cs` →
  `GroundBotInputSourceTests.cs`. `.meta` GUID preserved across the
  rename so any (unlikely) scene/prefab GUID references survive.
- **Dropped the inline `ComputePatrolSteer` helper.** The real
  `GroundBotInputSource.ComputeSteer` is public + static + pure; the
  tests now call it directly. Tiny test-side wrapper supplies the
  serialized-field defaults so per-test call sites stay terse.
- **Fixed three test setups** so `forward` is chosen against the
  *actual* −Z tangent at the +X point of the circle:
  - `PatrolSteer_BotFacingTangent_AtRadius_ProducesZeroSteer`:
    `forward = (0, 0, -1)`.
  - `PatrolSteer_BotFacingAwayFromTangent_ProducesHardTurn`:
    `forward = (0, 0, 1)`.
  - `PatrolSteer_FullSteer_ReducesThrottle`: `forward = (0, 0, 1)`.
- **Tightened `PatrolSteer_SteerOutput_IsAlwaysClampedToMinusOneToOne`.**
  Previous setup (`forward = (0.01, 0, -1)`) put the chassis nearly
  *aligned* with the tangent, so the unclamped value never exceeded ±1
  and the clamp was never actually exercised. New setup uses
  `forward = (1, 0, 0)` (perpendicular to tangent), which forces
  `cross.y ≈ 1, gain 4 → unclamped 4` so the clamp is the load-bearing
  step. Added a positive saturation assertion.
- **Dropped three `Assert.Pass`-only stubs** (`GroundBotInputSource_ChaseMode_*`,
  `GroundBotInputSource_RetreatMode_*`, `AirBotInputSource_MoveVertical_*`).
  Each was a no-op placeholder for "API in flight"; the APIs have all
  landed (chase + retreat states in `GroundBotInputSource`,
  `AirBotInputSource` exists). Per CLAUDE.md Rule 12 a test that can
  never fail is worse than no test — leaving them as future work for a
  real bot-behaviour suite.
- **Updated the file's header doc-block** to point at
  `GroundBotInputSource` and document the −Z-tangent convention.
- **Updated csproj** entry to track the rename
  (`Robogame.Tests.EditMode.csproj` — Unity regenerates this on next
  Editor open; manual edit is the one-shot build patch).
- **Comment scrub:** `MatchFlowTests.cs:256` mentioned
  "DummyAiInputSource" parenthetically — dropped the dead alias.

## Files

- **Renamed:** `Tests/EditMode/Gameplay/DummyAiInputSourceTests.cs` →
  `GroundBotInputSourceTests.cs` (+ `.cs.meta`).
- **Edited:** `Robogame.Tests.EditMode.csproj`,
  `Tests/PlayMode/Gameplay/MatchFlowTests.cs`.
- **New:** this session log.

## Verification

- `dotnet build Robogame.Tests.EditMode.csproj` — **clean**
  (0 errors, 27 pre-existing warnings unrelated to this change).
- EditMode test run: not executed in this session — requires Unity
  Test Runner. Math verified by manual trace of
  `GroundBotInputSource.ComputeSteer` against each scenario:
  - Test 1: `dot=1, cross=0 → steer=0, t=0.7` ✓
  - Test 2: `dot=-1, cross=0 → steer=Sign(1)=1, |steer|=1` ✓
  - Test 7 (clamp): `dot=0, cross.y=1, gain=4 → clamp(4,−1,1)=1` ✓
  - Test 8 (throttle reduction): `|steer|=1, t=0.7·Lerp(1,0.55,1)=0.385 < 0.7` ✓

## Hard-invariant check

- No production code touched — only tests + csproj + doc.
- No physics, no netcode-sensitive surface.
- `BlockDefinitionLibrary.asset` untouched.
- No new abstractions; the test-side `ComputeSteer` wrapper is a
  three-line forwarder to keep call sites terse, not an indirection
  layer.
