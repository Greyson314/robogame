# 45 — Building/Garage architecture review implementation (steps 1–8)

> Implements every refactor step from `docs/BUILDING_ARCHITECTURE_REVIEW.md`
> §4 (Steps 1–8). The review's "what should be left" goal in §5 is the
> structure that's now in place.

## What changed

### Step 1 — Canonical block-entry order (netcode contract)

- New [`Block/BlockEntries.cs`](../../Assets/_Project/Scripts/Block/BlockEntries.cs)
  with `Compare`, `SortCanonical`, `IsCanonical` helpers.
- [`ChassisBlueprint.SetEntries`](../../Assets/_Project/Scripts/Block/ChassisBlueprint.cs)
  now canonical-sorts in place. Every mutation path
  (`BlueprintBuilder.Build → ToBlueprint`, `BlueprintSerializer.TryFromJson`,
  `BlockEditor.SyncBlueprintFromGrid`, `GameStateController.CloneBlueprint`)
  flows through this chokepoint, so on-disk JSON, edit-mid-flight grid syncs,
  and runtime spawns all see entries in the same `(z, y, x)` lex order.
- New tests pin the contract: order is canonical after authoring, idempotent
  across two builds with different statement orders, and survives
  serializer round-trips.
- [`RepairPad`](../../Assets/_Project/Scripts/Gameplay/RepairPad.cs:50)'s
  prior "block-index ordering note" updated to reflect the resolved sort.

### Step 2 — `BlockGraph` BFS primitive

- New [`Block/BlockGraph.cs`](../../Assets/_Project/Scripts/Block/BlockGraph.cs)
  with reusable `Buffers` and `BfsFrom` overloads for live `BlockGrid`
  + position-set inputs. Adds `FindCpuCell` and `WouldOrphanIfRemoved`
  helpers.
- [`BlockEditor.cs`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  drops two per-frame `new HashSet<>` / `new Queue<>` allocations from
  `IsValidPlacement` and `WouldOrphanIfRemoved`. Buffers are now instance
  fields. CLAUDE.md invariant 6 is back to compliance.
- [`BlueprintValidator.Validate`](../../Assets/_Project/Scripts/Block/BlueprintValidator.cs)
  step 3 BFS replaced with a `BlockGraph.BfsFrom` call.
- [`BlockGrid.GetReachableFrom`](../../Assets/_Project/Scripts/Block/BlockGrid.cs)
  / `FindDisconnectedFrom` now route through `BlockGraph` with a
  reusable-buffer field.

### Step 3 — `IBlueprintEntryTransform` interface

- New [`Block/IBlueprintEntryTransform.cs`](../../Assets/_Project/Scripts/Block/IBlueprintEntryTransform.cs)
  with per-field methods (`TransformBlockId`, `TransformPosition`,
  `TransformUp`, `TransformDims`, `TransformPitch`) +
  `BlueprintEntryTransform.Apply(t, source)` aggregator.
- [`BlockMirror.cs`](../../Assets/_Project/Scripts/Block/BlockMirror.cs)
  gains `MirrorPitch(pitch, axis)` and a `MirrorTransform : IBlueprintEntryTransform`
  class.
- [`BlueprintBuilder.MirrorX/Z`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)
  now compose over `MirrorTransform` via `BlueprintEntryTransform.Apply`,
  collapsing the prior two inline reflection bodies into one call site.
  Adding a new `Entry` field becomes a compile error in every implementer.
- §3a Bug 1 spot fix lands here: mirror reads `EffectiveUp` and routes
  pitch through `MirrorPitch` (identity today, centralised so a future
  axis can revisit the rule in one place).
- §3a Bug 2 spot fix: [`BlockGhostFactory.Build`](../../Assets/_Project/Scripts/Gameplay/BlockGhostFactory.cs)
  now takes `pitchDeg`; foil ghost rotates around chord axis to match
  what `AeroSurfaceBlock.ApplyOrientationToVisual` does on the placed
  block. `BlockEditor.EnsureGhost` threads pitch through from the
  variant panel and rebuilds when it changes.

### Step 4 — `PlacementRules` engine

- New [`Block/PlacementRules.cs`](../../Assets/_Project/Scripts/Block/PlacementRules.cs)
  with a `Candidate` struct, `PlacementError` enum, per-rule pure
  functions (`CheckCellOccupied`, `CheckHostExists`,
  `CheckHostIsConnective`, `CheckHostIsCpuReachable`, `CheckMountFace`,
  `CheckSecondCpu`, `CheckSweptOverlap`), and an `EvaluatePlacement`
  short-circuiting aggregator.
- [`BlockEditor.IsValidPlacement`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  shrinks to a thin wrapper over `EvaluatePlacement`. The previous
  ~70-line composition lives in one library now.
- [`BlueprintValidator`](../../Assets/_Project/Scripts/Block/BlueprintValidator.cs)
  gets a new per-entry rule pass for leaf-host + side-mount-face
  violations (when a `BlockDefinitionLibrary` is supplied), so a
  blueprint loaded from disk that violates the editor's placement rules
  fails validation rather than silently slipping through.

### Step 5 — Schema-side specialization

- [`BlockDefinition`](../../Assets/_Project/Scripts/Block/BlockDefinition.cs)
  gains `_hasVariantConfig` field + `HasVariantConfigRaw` accessor.
- New [`Block/BlockVariants.cs`](../../Assets/_Project/Scripts/Block/BlockVariants.cs)
  static helper mirrors the `BlockConnectivity` pattern: SO flag wins,
  hardcoded id list is the migration fallback for shipped assets.
- [`VariantConfigPanel.IsVariableBlock`](../../Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs)
  delegates to `BlockVariants`; [`BuildHotbar`](../../Assets/_Project/Scripts/Gameplay/BuildHotbar.cs)
  's "VAR" badge reads `BlockVariants.HasVariantConfig(def)` directly.
- New [`Block/FoilDefaults.cs`](../../Assets/_Project/Scripts/Block/FoilDefaults.cs)
  is the single source of truth for foil shape constants.
  `BlockOccupancy.FoilDefault*` and `AeroSurfaceBlock.Default*` are now
  aliases of `FoilDefaults` — the prior comment about
  "duplicated because Block can't see Movement" is resolved.
- The `BlockGhostFactory` and `BlockOccupancy` per-id switches are
  unchanged in this pass; promoting them to dispatch-table-keyed-by-SO
  is queued for when a second scalable shape lands (per
  `SCALABLE_PARTS_PLAN.md` Phase 2).

### Step 6 — `BuildSession` plain-C# model

- New [`Gameplay/BuildSession.cs`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs)
  owns: live grid + blueprint + library bindings, selected block id,
  per-block dims/pitch caches, mirror enabled/axis state, and a reusable
  `BlockGraph.Buffers` + CPU-reachable snapshot. Exposes verbs `TryPlace`,
  `TryRemove`, `EvaluatePlacement`, `EvaluateRemoval`, plus `SyncBlueprint`.
- [`GarageController.EnsureBuildModeWired`](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)
  creates and binds the session; passes it to `BlockEditor`,
  `VariantConfigPanel`, and `BuildMirrorMode`.
- [`VariantConfigPanel`](../../Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs)
  drops its own `Dictionary<string, Vector3>` / `<string, float>` caches;
  reads/writes through the session.
- [`BuildMirrorMode`](../../Assets/_Project/Scripts/Gameplay/BuildMirrorMode.cs)
  becomes a thin hotkey + HUD adapter — Enabled/Axis are properties that
  delegate to the session, Toggle/SetAxis call session methods, the
  Changed event proxies `MirrorChanged`.
- [`BlockEditor.SyncBlueprintFromGrid`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  routes through `BuildSession.SyncBlueprint` (which canonical-sorts via
  Step 1's chokepoint).
- BlockGhostRenderer extraction (the doc-mentioned full "150-line
  driver" split) is queued — `BlockEditor` retains its ghost-rendering
  responsibilities for now since those don't impact the model layer.

### Step 7 — `ChassisAssembler` + `ChassisHandle`

- New [`Gameplay/ChassisAssembler.cs`](../../Assets/_Project/Scripts/Gameplay/ChassisAssembler.cs)
  with one `Assemble(root, blueprint, library, AssemblyOptions)` entry
  point and an explicit `ChassisHandle { Root, Robot, Grid, Blueprint, Library }`
  return.
- `AssemblyOptions.Player(inputActions)`, `AssemblyOptions.Bot()`,
  `AssemblyOptions.Target(freezeRotation)` factory methods replace the
  prior split between `ChassisFactory.Build` and `BuildTarget`.
- The prior "ORDER MATTERS" comment is replaced by explicit phase
  comments (substrate → drive/aero/weapon → rig binders → block
  placement → post-activation). Tip-binder before rope-binder is now a
  Phase 3 ordering contract.
- [`ChassisFactory`](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
  shrinks to a back-compat facade (Build/BuildTarget thin shims over
  Assemble) so every existing call site (arena controllers, garage,
  scaffolders) keeps working without code changes.
- `Robot.Blueprint` / `Robot.Library` sidechannels are still set for
  RepairPad backwards-compat; future migration to the handle is queued.

### Step 8 — `BuildModeController` ↔ `GarageController` decouple

- [`BuildModeController.Exit`](../../Assets/_Project/Scripts/Gameplay/BuildModeController.cs)
  no longer calls `FindAnyObjectByType<GarageController>`. Sets
  `ExitRequestedRespawn` and fires `Exited`.
- [`GarageController`](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)
  subscribes to `Exited` in `EnsureBuildModeWired` and re-spawns from
  the saved blueprint on the event.
- The build-mode lifecycle is now decoupled from the garage scene
  specifically — future "spectator builder" / "view-a-friend's-loadout"
  features can drive the controller from a non-garage scene without
  redoing the lifecycle contract.

## New EditMode tests

- `BlockEntriesSortTests` — Compare/IsCanonical/SetEntries, serializer
  round-trip, builder-to-blueprint determinism.
- `BlockGraphTests` — BFS over a position set, ignore-cell isolation,
  buffer reuse.
- `BlueprintEntryTransformTests` — MirrorTransform per-axis behaviour,
  zero-up legacy normalisation, MirrorPitch identity.
- `BlockVariantsTests` — schema flag + hardcoded fallback list.
- `BuildSessionTests` — variant cache round-trip, mirror toggle/axis
  events, selected-block change idempotency.

## Design choices worth flagging

1. **`SetEntries` sorts in place.** Existing call sites all pass
   freshly-constructed arrays so this is safe; documented in the
   method's remarks for any future caller.

2. **BlockOccupancy / BlockGhostFactory still use per-id switches.**
   Promoting them to schema-side dispatch tables is the right move per
   §3.5, but it requires either authoring a `FoilBlockData` SO sidecar
   per shape or a delegate-keyed registry. The current single-shape
   case (foils only) doesn't justify either; second-shape time is the
   right moment.

3. **`PlacementRules` is composed, not declarative.** The doc proposes
   a "Rules are pure; the engine is allocation-free given a reused
   result buffer" library. That's what we have — the per-rule functions
   are pure, the aggregator short-circuits, the buffers are caller-owned.
   The runtime editor is the only sequential consumer today; the
   validator runs the rules independently because its data shape is
   different (entry list, no live grid).

4. **`BuildSession` doesn't fully replace `BlockEditor`.** The model
   state (variant cache, mirror, selected block) migrated to the
   session, and the placement evaluation goes through it. The mouse
   targeting + ghost rendering still live on `BlockEditor` — the doc's
   ~780→~150-line split would require also extracting `BlockGhostRenderer`,
   which is a separate scoped piece of work. Filed as follow-up.

5. **`ChassisFactory` survives as a facade.** The doc proposes a rename
   to `ChassisAssembler`. The new class exists; the old name stays as a
   back-compat layer because every arena/garage/water/planet controller
   would need touching. The runtime cost is two extra method dispatches
   per spawn — irrelevant at chassis-spawn frequency.

## Follow-ups queued

- BlockGhostRenderer extraction from BlockEditor (Step 6's "couple of
  days" part).
- BlockGhostFactory + BlockOccupancy dispatch table (Step 5's per-id
  switch consolidation), waiting on a second scalable shape.
- `Robot.Blueprint` sidechannel removal (Step 7's RepairPad migration
  to ChassisHandle); cosmetic, doesn't change behaviour.
- §3a Bug 3 instrumentation ("FOIL VISUAL FAILED" log) and Bug 4 HUD
  overlay — both small spot fixes mentioned in the doc as follow-ons
  to the structural refactor.
