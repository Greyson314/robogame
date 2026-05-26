# 98 — Dev-only aircraft tuning overrides

> Status: **Shipped.** The pitch/roll/thrust/damping sliders are back in
> the in-game Settings panel — as compile-stripped dev-only overrides
> sitting on top of the server-authoritative blueprint. Zero risk to
> invariant #1: the override code path doesn't exist in shipping
> builds.

## Why this exists

Session 85 migrated every gameplay-observable chassis tuning knob
(`Plane.PitchPower`, `Plane.RollPower`, `Ground.MaxSpeed`,
`Chassis.LinearDamping`, etc.) off `Tweakables` and onto
`ChassisBlueprint` to satisfy invariant #1 — Tweakables are per-machine
JSON and would desync the moment MP lands. The migration removed
the familiar in-game tuning UX.

User wants the sliders back for dev iteration. The straightforward
restoration would silently break invariant #1; instead this session
takes the dev-build-only override path, which preserves both the
invariant and the workflow.

## What landed

**`DevTuningOverride`** ([Assets/_Project/Scripts/Block/DevTuningOverride.cs](Assets/_Project/Scripts/Block/DevTuningOverride.cs)).
Static helper with four `Apply*(ref TConfig)` methods (Plane, Ground,
ChassisDamping, Thruster). Every method is wrapped in
`#if UNITY_EDITOR || DEVELOPMENT_BUILD`; in shipping builds the methods
compile to a bare return so the call site has zero cost and zero
behavioral effect. When the master `Dev.OverrideChassisTuning` toggle
is on, the matching `Tweakables.Get(...)` values are written into the
config struct, overwriting the blueprint values.

**Tweakables registration**
([Assets/_Project/Scripts/Core/Tweakables.cs](Assets/_Project/Scripts/Core/Tweakables.cs)).
13 new entries inside a `#if UNITY_EDITOR || DEVELOPMENT_BUILD` block,
grouped under `"Dev (Override Chassis Tuning)"`:

- Master enable toggle
- Plane: PitchPower, RollPower, YawFromBank, PitchDamping, RollDamping, YawDamping
- Ground: Acceleration, MaxSpeed, TurnRate
- Chassis: LinearDamping, AngularDamping
- Thruster: IdleThrottle, ThrottleResponse

Defaults match the historical pre-session-85 Tweakable defaults, so
enabling the master toggle with sliders untouched preserves the old
per-machine feel for a sanity-check moment.

**Consumer wiring.** All four subsystems that read these configs now
do two things in OnEnable: (a) call `ResolveTuning()` which reads the
blueprint then applies the override, and (b) subscribe to
`Tweakables.Changed` so the override sliders take effect live without
a chassis respawn. Mirror unsubscribe in OnDisable.

- [PlaneControlSubsystem.cs](Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs) — extracted `ResolveTuning()`
- [GroundDriveSubsystem.cs](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs) — extracted `ResolveTuning()`
- [ThrusterBlock.cs](Assets/_Project/Scripts/Movement/ThrusterBlock.cs) — extracted `ResolveTuning()`
- [RobotDrive.cs](Assets/_Project/Scripts/Movement/RobotDrive.cs) — extracted `PushChassisDamping()` (writes directly to `_rb.linearDamping/angularDamping` on every change)

## Invariant compliance

Invariant #1 ("no Tweakable affects gameplay outcomes") still holds in
the shipped binary because the entire override surface is compile-
stripped. The `Dev.*` keys aren't registered, the `DevTuningOverride.Apply*`
methods compile to no-op `return`s, the consumers' `ResolveTuning()` /
`PushChassisDamping()` calls become straight blueprint reads.

In dev builds (editor + Development Player), the master toggle is the
single gate — when off (the default), every consumer reads the
blueprint exactly as before. Flipping it on is an explicit, visible
opt-in; the slider group is named `"Dev (Override Chassis Tuning)"`
in the settings panel so there's no ambiguity about what's being
overridden.

## Tests

`EditMode: 252/253 passed, 0 failed, 1 inconclusive.`
`PlayMode: 92/93 passed, 0 failed, 0 inconclusive.`

No new tests. The override path is dev-only and would need a separate
DEV_BUILD test target to exercise; the consumers' refactor was a pure
extract-method (same behavior as before when override is off), which
the existing PlayMode coverage on PlaneControl / GroundDrive /
ThrusterBlock catches.

## Files

New:
- `Assets/_Project/Scripts/Block/DevTuningOverride.cs` (lives in `Robogame.Block` because the tuning config types it mutates do — `Robogame.Core` can't reference `Robogame.Block`)

Edited:
- `Assets/_Project/Scripts/Core/Tweakables.cs` (13 new entries inside dev guard)
- `Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs`
- `Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs`
- `Assets/_Project/Scripts/Movement/ThrusterBlock.cs`
- `Assets/_Project/Scripts/Movement/RobotDrive.cs`
- `docs/changes/README.md` (session index)
