# 62 — Project-health sweep (dead-code deletion + comment hygiene)

> Status: **shipped, untested in-engine** (no runtime behaviour
> changed; pure deletion + comment-scrub). Conservative cleanup
> pass: delete only what grep proves is dead, refuse the bait of
> speculative "modularization," and keep the diff surgical.

## Why this session

User: *"Let's do a 'project health'-style code cleanup session.
Please go through the entire code base to remove legacy code,
modularize existing code, minimize code overhead, maximize code
readability, maintain understanding of project direction."*

Plus the autonomy hand-off: *"i need to step out for a while, so
please execute this autonomously."*

The codebase had explicit no-speculation rules in
[CLAUDE.md](../../CLAUDE.md) Rules 2 + 3 ("Simplicity First",
"Surgical Changes") that I read as the binding interpretation of
"modularize" — don't introduce abstractions for code that has one
caller, don't blend the user's "cleanup" request into a refactor.
So the bias was hard toward *deletion*, soft against *restructure*.

## What changed

### Five files deleted

Each had zero callsites in code, scenes, or prefabs. Auditing
process per file was: `grep -rn ClassName Assets/_Project/` →
filter out the file's own self-reference + comment mentions →
confirm no `.unity` / `.prefab` references.

| File | Lines | Why dead |
|---|---|---|
| `Scripts/Tools/Editor/ArenaBuilder.cs` | 267 | Long-retired Kenney-asset experiment. Only self-references; the runtime arena builder is `EnvironmentBuilder.cs`. |
| `Scripts/Tools/Editor/KenneyKit.cs` | (full file) | Loader for Kenney FBX kits. Only caller was `ArenaBuilder` + the also-dead `ScrapPrefabScaffolder`. |
| `Scripts/Tools/Editor/RobotLayouts.cs` | 235 | Pre-`ScriptedChassisBuilder` chassis authoring path. Replaced session 57; nothing has called it since. |
| `Scripts/Gameplay/DummyAiInputSource.cs` | 28 | Already `[Obsolete]`-marked. No `.unity` / `.prefab` references; superseded by `GroundBotInputSource`. |
| `Scripts/Tools/Editor/ScrapPrefabScaffolder.cs` | 129 | Built a Kenney-coin-based scrap pickup prefab that was never actually saved to `Resources/Prefabs/ScrapPickup` — the live path is `ScrapPickup.BuildProcedural`. The scaffolder's only output never shipped, so the tool was dead-by-design. |

Plus their `.meta` siblings (10 files total).

### `ScaffoldHelpers.cs` — slimmed 156 → 41 lines

With `RobotLayouts` gone, eight of the ten methods in
`ScaffoldHelpers` lost their only caller. Audited per-method:

| Method | Was called by |
|---|---|
| `EnsureComponent<T>` | only `RobotLayouts` (ChassisAssembler has its own private version) — deleted |
| `AssignTuning` | only `RobotLayouts` — deleted |
| `WirePlayerInput` | only `RobotLayouts` — deleted |
| `EnsureWeaponMountAndBinder` | only `RobotLayouts` (ChassisAssembler has its own) — deleted |
| `EnsureWheelBinder` | only `RobotLayouts` — deleted |
| `EnsureAeroBinder` | only `RobotLayouts` — deleted |
| `BindFollowCameraTo` | only `RobotLayouts` — deleted |
| `RemoveLegacyRootGun` | only `RobotLayouts` — deleted |
| `EnsureDevHud` | zero callers in code — deleted |
| `ClearPlayerChassis` | `GameplayScaffolder` (still live) — **kept** |

### Dead Tweakables retired

[`Tweakables.cs`](../../Assets/_Project/Scripts/Core/Tweakables.cs) —
two unused keys deleted along with their `Register` calls:

- `RopeAngularLimit` — joint-bend-limit setting from the old
  `ConfigurableJoint`-chain rope. Verlet rope derives bending from
  positional constraints (`VerletRopeChain.BendingStiffness`), not
  from a per-joint angle limit.
- `RopeAngularDamping` — per-joint angular damping for the same
  retired joint-chain. Verlet sim has no per-particle angular
  state to damp.

Audited every Tweakables key by greppting `Tweakables.X` across
the codebase; these were the only two with zero consumers.

### `BlockDefinitionWizard.LoadById` deleted

Explicit "legacy alias kept for source compatibility" with zero
remaining callers per the Explore-agent audit.

### Comment hygiene

Comments that pointed at deleted symbols were updated rather than
left to rot:

- `Tweakables.cs` Tank-dummy comment now points at `GroundBotInputSource`
  (was `DummyAiInputSource`).
- `EnvironmentBuilder.cs` removed the "see ArenaBuilder.cs /
  KenneyKit.cs" Kenney-experiment paragraph and reworded the
  `ScrubKenneyInstances` doc.
- `SceneScaffolder.cs` class doc no longer references the deleted
  `RobotLayouts`; points at `GameplayScaffolder`'s `ScriptedChassisBuilder`-driven
  preset plans instead.
- `ScrapPickup.cs` removed the dead `ScrapPrefabScaffolder`
  reference; the procedural-cube fallback is now the documented
  primary path.
- `docs/changes/architecture.md` module list trimmed: removed
  `DummyAiInputSource (deprecated)` from Gameplay, removed
  `RobotLayouts` from Tools.Editor.
- `docs/ART_DIRECTION.md` retired the Kenney-Kit-specific
  importer-helper guidance; kept the general "pin importer
  settings in code" rule (still applicable to any future kit).

## What I deliberately did NOT do

Per "Simplicity First" + "Surgical Changes":

1. **No file splits.** `ArenaController.cs` (1325 lines) and
   `GameplayScaffolder.cs` (1116 lines) are large but tightly
   internally-coherent — each method is its own concern; splitting
   into `ArenaSpawner` / `ArenaMatch` / etc. would introduce
   cross-file coupling worse than the current monolith.
   `GameplayScaffolder` is editor-only, runs once per Build
   Everything, so size doesn't affect runtime.

2. **No new abstractions.** The codebase already factors well:
   per-asmdef separation, `BlockBinder` base class,
   `ChassisAssembler`/`ChassisFactory` pattern, `VerletRopeSimulator`
   singleton with chain registration, `IInputSource` /
   `IDriveSubsystem` interfaces. Adding a second-tier abstraction
   over any of these would be speculation, not cleanup.

3. **No per-frame alloc fixes.** Audited `new (List|Dictionary|HashSet|…)`
   in every `FixedUpdate`-bearing file under
   `Scripts/Movement/` and `Scripts/Combat/`; all container
   allocations are in field initializers (one-time at construction).
   Hot path is clean.

4. **Did NOT delete the Kenney FBX art assets** under
   `Assets/_Project/Art/ThirdParty/kenney_*`. The code that
   referenced them is gone; the art is unreferenced. Left in place
   because deleting a 100+-file third-party folder is invasive and
   reversibility is one-way. Surfaced as a follow-up below.

5. **Did NOT scrub the `[FormerlySerializedAs]` attributes** on
   `ArenaController` (`_dumbbellBlueprint`, `_barbellBlueprint`,
   `_dumbbellPosition`, `_barbellPosition`, `_dumbbellName`,
   `_barbellName` → `_arch*`). They cost nothing at runtime and
   protect scene-file deserialisation across the rename. Removable
   once we're confident no checked-in scene references the old
   names; not today.

## Files

- **Deleted (5 × .cs + 5 × .meta):**
  - `Scripts/Tools/Editor/ArenaBuilder.cs`
  - `Scripts/Tools/Editor/KenneyKit.cs`
  - `Scripts/Tools/Editor/RobotLayouts.cs`
  - `Scripts/Tools/Editor/ScrapPrefabScaffolder.cs`
  - `Scripts/Gameplay/DummyAiInputSource.cs`
- **Slimmed:**
  - `Scripts/Tools/Editor/ScaffoldHelpers.cs` — 156 → 41 lines, 1 surviving method.
  - `Scripts/Core/Tweakables.cs` — 2 dead keys + 2 dead `Register` calls.
  - `Scripts/Tools/Editor/BlockDefinitionWizard.cs` — 1 dead alias.
- **Comment-scrub:**
  - `Scripts/Gameplay/ScrapPickup.cs`
  - `Scripts/Tools/Editor/EnvironmentBuilder.cs`
  - `Scripts/Tools/Editor/SceneScaffolder.cs`
  - `docs/changes/architecture.md`
  - `docs/ART_DIRECTION.md`

## Hard-invariant check

- **No runtime behaviour changes.** Every deletion was code with
  zero callsites. Every comment edit is descriptive only.
- **No physics, no networking-sensitive code touched.**
- **Tests untouched.** No test file modified.
- **`BlockDefinitionLibrary.asset` untouched.** The deleted scripts
  didn't have associated BlockDefinitions.

## Known follow-ups

- **Kenney art folder.** `Assets/_Project/Art/ThirdParty/kenney_city-kit-industrial_1.0/`
  is now fully unreferenced (the deleted ArenaBuilder + KenneyKit
  were its only consumers). Safe to delete; held off because it's
  third-party content (~hundreds of files) and the user might want
  to re-introduce it on a future pass.
- **`[FormerlySerializedAs]` on ArenaController** — see above.
- **`ScrapPickup` Resources.Load fallback path** — kept the
  `Resources.Load<GameObject>("Prefabs/ScrapPickup")` lookup +
  `ResourcePath` const even though no asset is currently authored
  at that path. Leaving the contract in place means a future
  authored prefab will be picked up automatically; cost is 5 lines
  of code and one stat call on first spawn.
- **Periodic re-sweep.** This kind of pass is most useful run every
  10-ish sessions, with the same "delete only what grep proves
  dead" discipline. Next natural checkpoint: after a multiplayer
  session that's likely to introduce its own dead-code lobby.
