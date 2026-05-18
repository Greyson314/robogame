# 85 — Pre-netcode cleanup + the gameplay-Tweakable migration

> Status: **shipped in 12 commits, build-green at every step, untested
> in-engine.** `dotnet build` clean throughout; EditMode/PlayMode tests
> and an in-engine playtest are the user-side gate (no headless Unity).
> Session-84 perf WIP (4 scenes + `Mat_ArenaFluff.mat` + harness logs)
> deliberately left unstaged the entire session — never touched.

## Why this session

User: *"the deep-dive code cleanup pass, so we have the cleanest slate
possible before netcode … critically analyze … minimize overhead,
remove unused/legacy code, make code and gameplay concepts dynamic,
flag perf layups."* Sign-off captured up front (full Tweakable
migration, Option A, Phases 1–4 then checkpoint, fill test stubs, delete
Kenney).

Four parallel research agents mapped the codebase. Headline finding:
the runtime layer was already disciplined (session 62 held) — there is
no dead-code pile. The real gap was netcode-readiness: ~22
gameplay-observable knobs still read per-machine `Tweakables` (hard
invariant #1 violation, catalogued in PHYSICS_PLAN §5 as known debt).

## What shipped

**Cleanup tier.** Deleted grep-proven dead code (`RopeTip`,
`Robot.RebuildFromSnapshot`, `SceneScaffolder.BuildBootstrap`,
`Robot.AwardScrap`). Removed ~27 MB of two dead Kenney kits — but
**kept `kenney_pattern-pack`**: a GUID-level reference check (not the
filename grep session 62 relied on) proved its PNGs are wired into
every `BlockMat_*` material. The session-62 "fully unreferenced" note
was wrong and acting on it would have corrupted the block materials.
Fixed the orphaned `.cs.meta` fresh-clone hazard. Killed steady-state
OnGUI GC in `VehicleStatsHud` + `FloatingDamageOverlay` (FpsCounter
dirty-string pattern). Real assertions for `MatchFlowTests` (the old
stubs asserted a `TargetFragCount` win condition **that never
existed**), a real grapple-joint test, new `GrappleMagnetBlock`
coverage, and fixed a session-22 test silently broken by session-60's
`SpringJoint` switch.

**Migration tier (invariant #1 fully cleared).** Every
gameplay-observable Tweakable moved to server-authoritative config:

- P1: ramming damage → `ImpactConfig` SO (world-canonical, Resources,
  defaults == old, missing-asset fallback == identical behaviour).
- P2: `BlueprintSerializer` v4 + `BlueprintMovementConfig` (classes,
  not structs — `default(struct)` is all-zeros and would silently
  change every save; class field initializers == old Tweakable
  defaults, so v1–v3 loads are behaviour-identical). Per-block
  `Entry.BlockConfig`. Canonical block-index sort untouched.
- P3: Plane/Ground/Chassis tuning → blueprint, carried on `RobotDrive`
  (not `Robot` — Movement↛Robots asmdef forbids it; the plan's
  assumption was unbuildable). Per-FixedUpdate Tweakables lookup
  dropped (perf win).
- P4: Thruster/Rudder/Rotor → per-block `BlockBehaviour.ConfigValue`
  (Option A). Thruster fallback is 310 N — the value the Tweakable
  actually shipped at, not the vestigial 155 SerializeField.
- P5a: `BuildSession` variant cache + `TryPlace` + `SyncBlueprint`
  persist `BlockConfig` so it round-trips build/save — config is now
  dynamic end-to-end via blueprint JSON / ScriptedChassisBuilder.

Two plan corrections made rather than blind-followed: SO couldn't be
named `MatchConfig` (collision) → `ImpactConfig`; subsystems can't
reach `Robot.Blueprint` (asmdef cycle) → `RobotDrive` carries it.

## Deliberately NOT done

- **P5b in-garage slider UI for the new config.** A multi-section UGUI
  build I cannot visually verify headless; CLAUDE.md requires UI to be
  editor-tested before "done". Deferred as a playtest-gated follow-up
  rather than blind-landed. Config is already author-editable via
  blueprint/ScriptedChassisBuilder — the slider is UX only.
- **Preset `.asset` re-save (P5 tail).** Needs Unity; presets already
  load behaviour-identical via the v4 back-compat path (defaults ==
  old, `BlockConfig 0` == block default). No code change needed.
- **god-class splits** (`RopeBlock`, `ArenaController`) — session-62
  precedent, speculative.

## Files

Cleanup: deleted RopeTip.cs/.meta, 2 Kenney kits; edited Robot.cs,
SceneScaffolder.cs, VehicleStatsHud.cs, FloatingDamageOverlay.cs,
MatchFlowTests.cs, HookGrappleTests.cs; new GrappleMagnetTests.cs.
Migration: new ImpactConfig.cs, BlueprintMovementConfig.cs,
Resources/ImpactConfig.asset; edited MomentumImpactHandler,
ChassisBlueprint, BlueprintSerializer, BlockBehaviour, ChassisAssembler,
RobotDrive, PlaneControlSubsystem, GroundDriveSubsystem, ThrusterBlock,
RudderBlock, RotorBlock, BuildSession, Tweakables, BlueprintSerializerTests.

## Hard-invariant check

- **#1 (no gameplay Tweakable):** satisfied — grep-confirmed zero
  gameplay knobs on `Tweakables` (only dev-only `Stress.*` remains).
- **#2 (block-index sort = netcode contract):** untouched —
  `BlockConfig` rides existing records, not the sort key.
- Back-compat is guaranteed *by construction* (defaults == old,
  0-fallback == old); needs Test Runner + playtest to confirm in-engine.

## Follow-ups

- Run EditMode (`BlueprintSerializerTests` v4/v3) + PlayMode
  (`MatchFlowTests`, `HookGrappleTests`, `GrappleMagnetTests`) and a
  flight/drive/ram playtest to confirm the migration in-engine.
- P5b garage slider UI + preset re-save (bundled, playtest-gated).
- `[FormerlySerializedAs]` on `ArenaController` (carry from 62).
- Hand-authored `.meta`/`.asset` GUIDs validate on next Unity import.
