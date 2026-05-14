# 57 — Default presets re-authored through the player's verb

> User flagged a long-standing divergence: shipped default blueprints
> were authored by a different code path than user-built bots, so a
> bug in either path could land without the other catching it. This
> session unifies the two — defaults are now produced by running
> `BuildSession.TryPlace` against a temp `BlockGrid`, the same verb the
> garage uses, with hard-fail validation. If a player can't build it,
> the default can't ship it.

## What was wrong

Two parallel authoring pipelines, each with quiet drift:

- **Defaults** (`GameplayScaffolder.Build*Entries`) appended raw
  `Entry[]` arrays via [`BlueprintBuilder`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)'s
  `RotorWithFoils` / `RopeWithHook` shortcuts. The shortcuts
  hand-authored the rotor mechanism cube and the foil ring. Validation
  errors degraded to `Debug.LogWarning` — invalid presets shipped.
- **User bots** went through [`BlockEditor`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  → [`PlacementRules`](../../Assets/_Project/Scripts/Block/PlacementRules.cs)
  → `BlockGrid.PlaceBlock`, with the auto-companion cube cascade
  living on `BlockEditor.AutoPlaceCompanionsOf`. `BuildSession.TryPlace`
  existed but **nothing actually called it** — session 45's "single
  placement verb" wasn't adopted.

Concrete divergences caught:

- The shipped helicopter side cubes at `(±1, 0, ±1..2)` were authored
  with default Up = (0, 1, 0) but no host at `(±1, -1, *)`. The rules
  engine would reject those placements. A player who removed and
  re-placed any of those cubes would be unable to re-mount them with
  the same orientation.
- The auto-companion cascade in `BlockEditor` was a runtime-only
  behaviour. Tests that exercised `BuildSession.TryPlace` directly
  missed the cube and saw a different topology than the player did.

## What changed

### 1. `BuildSession` is now the single placement verb

[`BuildSession.TryPlace`](../../Assets/_Project/Scripts/Gameplay/BuildSession.cs):

- Gained an explicit-dims/pitch overload so scripted callers don't
  have to thread the variant cache.
- Auto-places structural companions (rotor → mechanism cube on the
  spin-axis face) on every successful placement, including the
  mirrored side.
- `TryRemove` enforces "CPU is sacred" and runs the rotor →
  mechanism-cube cascade in one place — the orphan check sees the
  post-cascade graph, not the post-rotor-only graph.
- New `ResolveRotorCascadeCell` exposes the cascade target for any
  consumer that needs to reason about it.

### 2. `BlockEditor` now delegates

[`BlockEditor.TryPlace`](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
and `TryRemove` push mirror state onto the session and call
`_session.TryPlace` / `_session.TryRemove`. The duplicate
`AutoPlaceCompanionsOf`, `TryMirrorPlace`, `TryMirrorRemove`,
`ResolveRotorCascadeCell`, `HasAnyCpu`, `SyncBlueprintFromGrid`
helpers are gone (~150 lines deleted). The editor is now the
input/UI driver the session-45 refactor described.

### 3. `ScriptedChassisBuilder` — Editor-time scripted authoring

New [`ScriptedChassisBuilder`](../../Assets/_Project/Scripts/Tools/Editor/ScriptedChassisBuilder.cs)
runs the same `BuildSession.TryPlace` verb against a temp
`BlockGrid` in an inactive scene-graph root (so prefab Awake /
OnEnable don't fire during scaffolding). Public API mirrors the
player's mental model: `Place`, `Row`, `Box`, `BoxSkip`,
`MirrorX/Z`, plus high-level helpers `RotorWithFoils`, `RotorBare`,
`RopeWithHook`, `RopeWithMace` — each helper bottoms out in
`Place`, so the rules engine runs on every step. Disposes the
temp hierarchy when `Build()` returns the `BlueprintPlan`.

Every step that the rules engine rejects throws — a scripted
build that can't pass the player's gate is a bug in the script,
not a warning the scaffolder swallows.

### 4. All eleven blueprints re-authored

`GameplayScaffolder.CreateDefaultBlueprints` now builds every preset
through `ScriptedChassisBuilder`, with each cube's Up direction
pointing back toward a placed neighbor:

- Tank, Plane, Buggy, Boat, Bomber, Prop Plane, Helicopter
- Combat Dummy, Arch Dummy, Stress Rotor Tower, Stress Rope Tower

Each is written as a CPU-outward growth pattern. The helicopter
side cubes (the divergent case) now mount with Up = `(1, 0, 0)`
back toward the central spine — a layout the player can reproduce
by clicking the central spine's +X face. The stress towers no
longer hand-author the mechanism cube — `BuildSession`'s auto-
companion drops it on each rotor placement.

### 5. Hard-fail validation in the scaffolder

`CreateOrUpdateBlueprint` now throws on any validation error.
Library-aware validation runs (catches host-face rejections the
positions-only path missed). The asset isn't written when the plan
is invalid. Warnings still log but don't block.

### 6. `BlueprintBuilder` shortcuts removed

[`RotorWithFoils`, `RotorBare`, `RopeWithHook`, `RopeWithMace`](../../Assets/_Project/Scripts/Block/BlueprintBuilder.cs)
deleted — they duplicated runtime cascades the scripted path now
gets for free. The remaining `BlueprintBuilder` is reframed as the
pure-data shape builder for validator unit tests; defaults use
`ScriptedChassisBuilder`. Tests that referenced the deleted helpers
moved to raw `Entry[]` construction or to the new
`ScriptedChassisBuilderTests`.

## Why this matters

The contract: **a default blueprint is what a player would produce
clicking the equivalent sequence in the garage**. The same rules
engine, the same cascades, the same validator. The next time
someone adds a placement rule (per-face exception, new connectivity
check), there's no second authoring path to update — defaults pick
it up automatically.

Test surface that backs the contract:

- `ScriptedChassisBuilderTests.EveryShippedPreset_PassesLibraryAwareValidation`
  asserts every on-disk preset passes the library-aware validator
  (including host-face checks the prior position-only validator
  skipped).
- `Place_ThrowsWhenHostMissing` and `Place_ThrowsOnSecondCpu` pin
  the rules-engine enforcement to test fixtures.
- `PlaceRotor_AutoPlacesMechanismCubeOnSpinAxisFace` proves the
  cascade fires through the scripted path the same way it does in
  the editor.

## Files

- **New:**
  - `Assets/_Project/Scripts/Tools/Editor/ScriptedChassisBuilder.cs`
  - `Assets/_Project/Tests/EditMode/Blueprints/ScriptedChassisBuilderTests.cs`
- **Edited:**
  - `BuildSession.cs` — new TryPlace overload, AutoPlaceCompanionsOf,
    ResolveRotorCascadeCell, TryRemove cascade + CPU-sacred policy.
  - `BlockEditor.cs` — delegates to session verbs; ~150 lines removed.
  - `PlacementRules.cs` — two-cell `EvaluateRemoval` overload.
  - `BlueprintBuilder.cs` — shortcuts deleted, docs reframed as
    pure-data builder.
  - `GameplayScaffolder.cs` — all 11 presets re-authored as scripted
    builds; `CreateOrUpdateBlueprint` hard-fails validation.
  - `BlueprintBuilderTests.cs`, `BlueprintValidatorTests.cs`,
    `PresetBlueprintTests.cs` — refactored off the deleted helpers;
    library-aware validation everywhere.
  - `Robogame.Tests.EditMode.asmdef` — added Tools.Editor ref so the
    new test class can reach `ScriptedChassisBuilder`.
  - `BlockMirror.cs`, `RotorBlockTests.cs` — stale-doc cleanup.
  - `docs/changes/architecture.md` — pointer updates.

## Verification

1. **Run `Robogame → Scaffold → Gameplay → Build All Pass A`.** Every
   preset script must pass without exception (the scaffolder throws
   loud on a rules-engine rejection or a validator failure).
2. **EditMode tests:** `PresetBlueprintTests`,
   `ScriptedChassisBuilderTests`, `BlueprintBuilderTests`,
   `BlueprintValidatorTests` all green.
3. **In-game:** select each preset from the garage picker, fly each
   one, confirm rotors / ropes / hooks behave identically to the
   pre-refactor build.
4. **Build mode round-trip:** in the garage, remove one block from
   the helicopter's side cubes via right-click, then re-place it
   from the hotbar. The rules engine should accept the placement
   (it did NOT before this refactor — that was the divergent bug).

## Open

- The `BlockGrid.PlaceBlock` path that `ChassisAssembler` uses at
  spawn-time still bypasses rules. That's correct (re-spawning from
  a validated blueprint isn't placement, it's hydration), but the
  bypass is implicit. A future rename — `BlockGrid.SpawnFromBlueprint`
  vs `BlockGrid.PlacePlayerBlock` — would make the contract
  visible at call sites.
- `BlueprintBuilder` is now a single-purpose data builder for tests.
  Could be inlined into the test files if no other consumer
  surfaces.
- The scripted-build helpers are Editor-only (Tools.Editor asmdef
  has `includePlatforms: Editor`). Moving them to a runtime
  assembly would let PlayMode tests script chassis layouts too;
  not needed today but worth noting.
