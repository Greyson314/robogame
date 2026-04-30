# Refactor pass — change log & rationale

This pass executes the seven highest-impact items from the in-repo code
review. The goal was **modularity, brevity, and removing duplication**
without changing in-game behaviour.

Every change preserves the existing public API surface (component names,
serialised field names, MenuItem paths) so saved scenes and prefabs keep
their references.

---

## 1. Centralised block IDs

**Problem.** The seven canonical block-asset IDs (`block.cube`,
`block.cpu`, `block.wheel`, `block.wheel.steer`, `block.thruster`,
`block.aero`, `block.weapon`) were duplicated as string literals across
[BlockDefinitionWizard.cs](Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs),
[RobotAeroBinder.cs](Assets/_Project/Scripts/Movement/RobotAeroBinder.cs),
[RobotWheelBinder.cs](Assets/_Project/Scripts/Movement/RobotWheelBinder.cs),
and several scaffolder paths. Renaming any one of them required a
manual cross-file sweep with a high chance of missed callers.

**Fix.** New file [BlockIds.cs](Assets/_Project/Scripts/Block/BlockIds.cs)
holds every canonical ID as a `public const string`. All callers were
switched from string literals to `BlockIds.Wheel` / `BlockIds.Thruster`
/ etc. The wizard now also exposes `LoadByAssetName(name)` that mirrors
the new convention; the old `LoadById` survives as a thin alias for
back-compat.

---

## 2. Shared `BlockBinder` base class

**Problem.** Three components — `RobotWheelBinder`, `RobotAeroBinder`,
`RobotWeaponBinder` — were 95% identical: each subscribed to
`BlockGrid.BlockPlaced`, filtered by a hard-coded definition ID, and
attached one component to the placed block. About 55 lines of
boilerplate per binder, with three independent subscribe/unsubscribe
implementations to keep in sync.

**Fix.** New abstract [BlockBinder.cs](Assets/_Project/Scripts/Block/BlockBinder.cs)
handles the lifecycle once. Subclasses override only:

```csharp
protected abstract bool ShouldBind(BlockBehaviour block);
protected abstract void Bind(BlockBehaviour block);
```

The three concrete binders shrunk from ~55 LOC each to ~25 LOC and now
share identical event-subscription semantics — fixing one bug fixes all
three.

---

## 3. `BlockVisuals` rig helpers

**Problem.** Four block components (`ThrusterBlock`, `AeroSurfaceBlock`,
`WheelBlock`, `WeaponBlock`) each open-coded the same three steps when
building their visual rig: hide the host MeshRenderer, find-or-create a
named child Transform, find-or-create a primitive child with its
collider stripped. ~30 lines of nearly-identical Transform plumbing per
component.

**Fix.** New static [BlockVisuals.cs](Assets/_Project/Scripts/Block/BlockVisuals.cs)
exposes:

- `HideHostMesh(GameObject)` — disables the host's MeshRenderer.
- `GetOrCreateChild(parent, name)` — idempotent named child lookup.
- `GetOrCreatePrimitiveChild(parent, name, primitiveType, stripCollider = true)`.

Each rig method in the four components collapsed by 60–80%. As a bonus,
[ThrusterBlock.cs](Assets/_Project/Scripts/Movement/ThrusterBlock.cs)
now caches a single `static Material s_nozzleMaterial` instead of
allocating one Material per thruster — fixing a small per-instance
material leak.

---

## 4. SceneScaffolder split

**Problem.** [SceneScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs)
was 735 lines doing three jobs: (a) menu-command orchestration, (b)
chassis layout authoring, (c) component wiring with raw
`SerializedObject` writes. Most of (b) and (c) was unreadable because
the per-block tuning numbers were buried inside `so.FindProperty(...)
.floatValue = 26.25f;` triplets. This was the same code smell that hid
the plane forward-thrust regression in the previous session.

**Fix.** Three new editor files plus a slim rewrite:

| File | Role |
|---|---|
| [TuningAssets.cs](Assets/_Project/Scripts/Tools/Editor/TuningAssets.cs) | `LoadOrCreate<T>(assetName, initializer)` — get-or-make a ScriptableObject under `Assets/_Project/ScriptableObjects/Tuning/`. |
| [ScaffoldHelpers.cs](Assets/_Project/Scripts/Tools/Editor/ScaffoldHelpers.cs) | Shared editor utilities: `EnsureComponent<T>`, `AssignTuning`, `WirePlayerInput`, `EnsureWeaponMountAndBinder`, `EnsureWheelBinder`, `EnsureAeroBinder`, `RemoveLegacyRootGun`, `BindFollowCameraTo`, `ClearPlayerChassis`, `EnsureDevHud`. |
| [RobotLayouts.cs](Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs) | Block layouts: `PopulateTestRobot`, `PopulateTestPlane`, `PopulateCombatDummy`. |
| [SceneScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs) | Now ~250 lines of MenuItems + scene-element helpers (`EnsureGround`, `EnsureCamera`, `EnsureLight`, `PopulateTestTerrain`). |

Net result: the 735-line god-file is gone. Adding a new chassis layout
is now an isolated change in [RobotLayouts.cs](Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs);
adding a new MenuItem only touches [SceneScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs).

---

## 5. Tuning ScriptableObjects

**Problem.** Tuning numbers (acceleration, max speed, jump impulse,
thruster max-thrust, plane center-of-mass offset, …) lived as inline
`[SerializeField]` floats. The scaffolder force-wrote them via
`SerializedObject.FindProperty(...).floatValue = …` on **every** rebuild,
which is what caused the plane forward-thrust regression — saved scene
state was overwriting design-time tuning.

**Fix.** New ScriptableObject profiles under
[Movement/Tuning/](Assets/_Project/Scripts/Movement/Tuning/):

- [GroundDriveTuning](Assets/_Project/Scripts/Movement/Tuning/GroundDriveTuning.cs)
- [PlaneControlTuning](Assets/_Project/Scripts/Movement/Tuning/PlaneControlTuning.cs)
- [ThrusterTuning](Assets/_Project/Scripts/Movement/Tuning/ThrusterTuning.cs)
- [ChassisTuning](Assets/_Project/Scripts/Movement/Tuning/ChassisTuning.cs)

Each consumer gained an optional `[SerializeField] private XxxTuning
_tuning;` field plus a private resolved-property pattern:

```csharp
private float Acceleration => _tuning != null ? _tuning.Acceleration : _acceleration;
```

All `Tick`/`Awake` bodies now read the resolved property. The scaffolder
calls `TuningAssets.LoadOrCreate<>` to get-or-create one shared asset
per chassis class and wires it via `ScaffoldHelpers.AssignTuning`.
Inline fields remain as the design-time fallback so existing prefabs
keep working untouched.

This eliminates the force-write code path completely — the scaffolder
now sets a *reference* to a SO asset, which is idempotent and
non-destructive.

Files touched:
[GroundDriveSubsystem.cs](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs),
[PlaneControlSubsystem.cs](Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs),
[ThrusterBlock.cs](Assets/_Project/Scripts/Movement/ThrusterBlock.cs),
[RobotDrive.cs](Assets/_Project/Scripts/Movement/RobotDrive.cs).

---

## 6. Pooled tracers + RaycastNonAlloc

**Problem.** Two hot-path allocations per frame:

1. [HitscanGun.cs](Assets/_Project/Scripts/Combat/HitscanGun.cs) created a
   fresh `GameObject + LineRenderer + Material` per shot and destroyed
   them after `_tracerLifetime`. With sustained fire that's 6–10
   GameObjects + Materials/second per gun.
2. Four callsites used `Physics.RaycastAll(...)` which allocates a
   fresh `RaycastHit[]` per call:
   [HitscanGun.RaycastIgnoringSelf](Assets/_Project/Scripts/Combat/HitscanGun.cs),
   [WheelBlock.RaycastIgnoringSelf](Assets/_Project/Scripts/Movement/WheelBlock.cs)
   (called per wheel per FixedUpdate),
   [RobotDrive.ComputeAimPoint](Assets/_Project/Scripts/Movement/RobotDrive.cs)
   (called every frame), and
   [WeaponMount.ComputeFallbackAim](Assets/_Project/Scripts/Combat/WeaponMount.cs).

**Fix.**
- HitscanGun now uses a `static Stack<LineRenderer>` pool, a `static
  Material s_tracerMaterial` cached once, and a `static List<PendingTracer>`
  drained each `Update`. Active tracer count grows as needed but never
  reallocates per shot.
- All four raycast callsites switched to `Physics.RaycastNonAlloc` with
  a class-private `static readonly RaycastHit[]` buffer
  (sized 8 for wheels, 16 for aim/hitscan).

Net: zero per-shot, per-wheel-tick, per-frame heap allocations on the
combat / driving hot paths.

---

## 7. Event-driven wheel cache

**Problem.** [GroundDriveSubsystem.cs](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs)
maintained its `_wheels` array by polling
`GetComponentsInChildren<WheelBlock>` every 0.5 seconds — a
GC-allocating call running on every player chassis forever, just to
catch the rare case where a wheel was placed or removed.

**Fix.** Subscribe to `BlockGrid.BlockPlaced` / `BlockRemoving` once at
`OnEnable` and maintain a `HashSet<WheelBlock>` incrementally. `OnEnable`
also seeds the set from the existing hierarchy so the scaffolder's
build-then-Awake order works. No more periodic
`GetComponentsInChildren`, no more allocations, and the set updates the
exact instant a wheel block is placed or destroyed.

---

## Summary of files

| Status | Path |
|---|---|
| **NEW** | [Assets/_Project/Scripts/Block/BlockIds.cs](Assets/_Project/Scripts/Block/BlockIds.cs) |
| **NEW** | [Assets/_Project/Scripts/Block/BlockBinder.cs](Assets/_Project/Scripts/Block/BlockBinder.cs) |
| **NEW** | [Assets/_Project/Scripts/Block/BlockVisuals.cs](Assets/_Project/Scripts/Block/BlockVisuals.cs) |
| **NEW** | [Assets/_Project/Scripts/Movement/Tuning/ChassisTuning.cs](Assets/_Project/Scripts/Movement/Tuning/ChassisTuning.cs) |
| **NEW** | [Assets/_Project/Scripts/Movement/Tuning/GroundDriveTuning.cs](Assets/_Project/Scripts/Movement/Tuning/GroundDriveTuning.cs) |
| **NEW** | [Assets/_Project/Scripts/Movement/Tuning/PlaneControlTuning.cs](Assets/_Project/Scripts/Movement/Tuning/PlaneControlTuning.cs) |
| **NEW** | [Assets/_Project/Scripts/Movement/Tuning/ThrusterTuning.cs](Assets/_Project/Scripts/Movement/Tuning/ThrusterTuning.cs) |
| **NEW** | [Assets/_Project/Scripts/Tools/Editor/TuningAssets.cs](Assets/_Project/Scripts/Tools/Editor/TuningAssets.cs) |
| **NEW** | [Assets/_Project/Scripts/Tools/Editor/ScaffoldHelpers.cs](Assets/_Project/Scripts/Tools/Editor/ScaffoldHelpers.cs) |
| **NEW** | [Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs](Assets/_Project/Scripts/Tools/Editor/RobotLayouts.cs) |
| Rewrite | [Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs) (735 → ~250 LOC) |
| Edit | [Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs](Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs) |
| Edit | [Assets/_Project/Scripts/Movement/RobotWheelBinder.cs](Assets/_Project/Scripts/Movement/RobotWheelBinder.cs) (subclass of `BlockBinder`) |
| Edit | [Assets/_Project/Scripts/Movement/RobotAeroBinder.cs](Assets/_Project/Scripts/Movement/RobotAeroBinder.cs) (subclass of `BlockBinder`) |
| Edit | [Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs](Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs) (subclass of `BlockBinder`) |
| Edit | [Assets/_Project/Scripts/Movement/ThrusterBlock.cs](Assets/_Project/Scripts/Movement/ThrusterBlock.cs) (BlockVisuals, tuning, cached material) |
| Edit | [Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs](Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs) (BlockVisuals) |
| Edit | [Assets/_Project/Scripts/Movement/WheelBlock.cs](Assets/_Project/Scripts/Movement/WheelBlock.cs) (BlockVisuals, RaycastNonAlloc) |
| Edit | [Assets/_Project/Scripts/Combat/WeaponBlock.cs](Assets/_Project/Scripts/Combat/WeaponBlock.cs) (BlockVisuals) |
| Edit | [Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs) (tuning, event-driven wheels) |
| Edit | [Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs](Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs) (tuning) |
| Edit | [Assets/_Project/Scripts/Movement/RobotDrive.cs](Assets/_Project/Scripts/Movement/RobotDrive.cs) (tuning, RaycastNonAlloc) |
| Edit | [Assets/_Project/Scripts/Combat/HitscanGun.cs](Assets/_Project/Scripts/Combat/HitscanGun.cs) (tracer pool, RaycastNonAlloc) |
| Edit | [Assets/_Project/Scripts/Combat/WeaponMount.cs](Assets/_Project/Scripts/Combat/WeaponMount.cs) (RaycastNonAlloc) |

## What this didn't fix

Items 8–23 from the original review (e.g. redundant `WeaponMount`,
silent `HitscanGun` auto-add, `Robot.cs` mixing template registry with
production logic, splitting the input layer) are intentionally **not**
in this pass. They are lower-impact and several of them require API
changes that I'd want a separate review on before touching.
