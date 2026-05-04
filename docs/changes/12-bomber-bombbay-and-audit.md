# Session — Bomber preset + Bomb Bay block + health check / docs split

**Intent.** Two-part push. First: *"add a bomb bay block that can drop a
bomb that explodes on the ground… create a new default bot called
'Bomber' that has the bomb bay as its weapon."* Then, after confirming
bombs drop and detonate end-to-end: *"we're at a good spot and need to
do a health check… also separate the changes.md file into multiple md
files, each about one session."* This entry covers both.

## Bomber feature

**New block.** `block.weapon.bombbay` registered in
[BlockIds.cs](../../Assets/_Project/Scripts/Block/BlockIds.cs).
[BlockDefinitionWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
authors `BlockDef_BombBay` (Weapon category, 110 HP, mass 3.0,
CpuCost 40). [BlockMaterials.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs)
adds `BlockMat_BombBay` (dark grey w/ outline) and routes it through
the Weapon branch in `Resolve(blockId)`.

**Runtime behaviour — two new components:**

- [BombBayBlock.cs](../../Assets/_Project/Scripts/Combat/BombBayBlock.cs).
  Rigidbody-aware dispenser. Subscribes to `RobotWeaponBinder.Fire`
  while held; spawns a `Bomb` prefab beneath the bay with a
  configurable cooldown and inherited chassis velocity (so a diving
  Bomber's bombs lead the target naturally). Spawn point sits below
  the bay's collider so the bomb clears its own host on the way down.
- [Bomb.cs](../../Assets/_Project/Scripts/Combat/Bomb.cs).
  Gravity-driven projectile. Explicit `SphereCollider` on the root,
  `CollisionDetectionMode.ContinuousSpeculative`, and
  `OnCollisionEnter` triggers `ApplyAreaDamage(point, radius)` plus
  the cartoon explosion VFX. Owner-filtered via `Robot` reference so
  the bomber doesn't damage itself; otherwise hits any
  `IDamageable` in the blast sphere via
  `Physics.OverlapSphereNonAlloc` (rented buffer, no per-shot alloc
  except the de-dupe sets noted below).

**Weapon-binder dispatch.** [RobotWeaponBinder.cs](../../Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs)
already auto-attached to `BlockIds.Weapon`; extended to also bind
`BlockIds.BombBay` so the existing Fire event re-broadcasts to
whichever weapon-style behaviour the chassis owns.

**Bomber preset.**
[GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
gained `BuildBomberEntries()` (a Plane-kind chassis with the bomb bay
on the underbelly at `y=-1`), a 5th preset slot in
`BuildBootstrapPassA` (`presets.arraySize = 5`), and a new
`Blueprint_DefaultBomber.asset` written next to the existing four
presets. The HUD dropdown picks it up automatically through the
existing preset-list pipeline.

**VFX wiring.** [CombatVfxLibrary.cs](../../Assets/_Project/Scripts/Combat/CombatVfxLibrary.cs)
+ [CombatVfxWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/CombatVfxWizard.cs)
load Cartoon FX Remaster's explosion prefab by GUID/path and pin a
single library asset that `Bomb` reads on detonation. Library is also
referenced by `BuildAllPassA` so re-running the scaffold rewires the
prefab if it ever moves.

## Bug fixes

**1. Bombs not dropping on left-click.** Root cause: weapon
auto-attach in
[ChassisFactory.cs](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
only checked `BlockIds.Weapon`, so a Bomber chassis (which has only a
bomb bay, no hitscan gun) never got `RobotWeaponBinder` attached and
the Fire event was never broadcast. Fix:

```csharp
// future weapon variants (BombBay, future rocket pods, …) trigger the
// same binder; switch to a category-aware check if the list grows.
if (e.BlockId == BlockIds.Weapon || e.BlockId == BlockIds.BombBay)
    hasWeapon = true;
```

at [ChassisFactory.cs#L104](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs#L104).

**2. Bombs falling but not exploding.** Root cause: the bomb's
SphereCollider had been authored on the visual `Body` child while
[Bomb.cs](../../Assets/_Project/Scripts/Combat/Bomb.cs) declared
`[RequireComponent(typeof(Collider))]` on the root — Unity's collision
callbacks don't always reliably surface to a non-collider parent
Rigidbody when the matched collider is a child primitive of mismatched
geometry. Fix: stripped the primitive collider off the body child,
added an explicit `SphereCollider` on the root, switched the rb to
`CollisionDetectionMode.ContinuousSpeculative` so high-velocity
contacts can't tunnel.

## Polytope welcome window

Deleted `Assets/Polytope Studio/Welcome_Screen/` and its `.meta` to
silence the recurring `PT_PackageWelcomeWindow` cctor error
(`EditorPrefs.GetBool` on a yet-uninitialised prefs key during
domain reload).

## Health check (audit only — no source edits)

Findings, ordered by severity:

**Dead / legacy code (worth a follow-up sweep):**

- [Robogame.Network](../../Assets/_Project/Scripts/Network/) — only
  `.gitkeep` + asmdef referencing `Robogame.Core`. No source files,
  no consumers. Empty placeholder for the netcode roadmap; safe
  to keep but nothing imports it today.
- [SceneScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs)
  still exposes `BuildTestGarage` / `BuildTestRobot` /
  `BuildTestPlane` / `BuildCombatDummy` menu items. These predate
  Pass A and overlap with `GameplayScaffolder.BuildAllPassA`. Note
  that `SceneScaffolder.PopulateTestTerrain()` is *still called* from
  [EnvironmentBuilder.cs#L126](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs#L126),
  so deletion needs care — extract `PopulateTestTerrain` to a
  separate helper before deleting the legacy menu entries.
- [DamageTestTools.cs](../../Assets/_Project/Scripts/Tools/Editor/DamageTestTools.cs)
  menu items (Damage Random / Destroy CPU / Rebuild Test Robot /
  Rebuild Combat Dummy) duplicate functionality already exposed by
  the in-play `DevHud`. Also the only file in the project that
  imports `System.Linq`.

**Deprecated API surface:**

- Project is on Unity 6000.4.4f1, but
  [DevHud.cs#L268](../../Assets/_Project/Scripts/UI/DevHud.cs#L268)
  and
  [DamageTestTools.cs#L113](../../Assets/_Project/Scripts/Tools/Editor/DamageTestTools.cs#L113)
  guard `FindObjectOfType` behind `#if UNITY_2023_1_OR_NEWER` —
  the `#else` branches are dead code. Both can be simplified to an
  unconditional `Object.FindAnyObjectByType<Robot>()`.
- No deprecated `velocity` / `drag` / `angularDrag` reads anywhere
  (all sites use `linearVelocity` / `linearDamping` /
  `angularDamping`). No legacy `Input.Get*` calls. Clean.

**Diagnostic noise:**

- [Bomb.cs#L110](../../Assets/_Project/Scripts/Combat/Bomb.cs#L110)
  still has the diagnostic `Debug.Log` we added during the
  collision-detection bug hunt. Safe to remove now that detonation
  is confirmed working.

**Performance observations (all currently inside budget):**

- [WaterMeshAnimator.cs](../../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)
  writes `_mesh.vertices = …; _mesh.colors = …;` every Update on a
  4 225-vertex grid. Migrating to `mesh.SetVertices` /
  `mesh.SetColors` with `mesh.MarkDynamic()` is a marginal win —
  worth doing if/when the grid grows.
- [Bomb.ApplyAreaDamage](../../Assets/_Project/Scripts/Combat/Bomb.cs)
  allocates a `HashSet<Robot>` + `HashSet<IDamageable>` per
  detonation. Acceptable at current fire rates; revisit if bomb
  spam ever becomes a thing.
- `Camera.main` is called from 9 files; all hot-path consumers
  cache after first call (RobotDrive, WeaponMount, BlockEditor).
  Fine.
- `GameObject.Find` runs in 22 places — mostly editor scaffolders
  (fine); the runtime ones (DevHud, GarageController, ArenaController,
  PlanetArenaController, Robot, WaterArenaController) are name-based
  scene lookups in single-instance scenes. Fragile but acceptable.

**Confirmed healthy:**

- Modern Input System everywhere, modern Rigidbody fields,
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`
  resets where statics hold Unity refs (HitscanGun pool),
  `Physics.RaycastNonAlloc` / `OverlapSphereNonAlloc` on hot paths,
  event-driven `BlockGrid.BlockPlaced` / `BlockRemoving` binders
  (no per-frame `GetComponentsInChildren`), Tweakables-backed
  tuning so subsystem hot-reads are live-tunable.

## CHANGES.md restructure

Split [CHANGES.md](../../CHANGES.md) (854 lines, 12 numbered sessions
+ background + architecture snapshot) into per-session files under
[docs/changes/](.). Naming is `NN-slug.md`, oldest first; this entry
is `12-bomber-bombbay-and-audit.md`. The
[architecture snapshot](architecture.md) was extracted verbatim. Top-
level CHANGES.md is now a slim index pointing at this directory.

**Files touched.**

- Added: [Bomb.cs](../../Assets/_Project/Scripts/Combat/Bomb.cs),
  [BombBayBlock.cs](../../Assets/_Project/Scripts/Combat/BombBayBlock.cs),
  [CombatVfxLibrary.cs](../../Assets/_Project/Scripts/Combat/CombatVfxLibrary.cs),
  [CombatVfxWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/CombatVfxWizard.cs).
- Modified:
  [BlockIds.cs](../../Assets/_Project/Scripts/Block/BlockIds.cs),
  [Tweakables.cs](../../Assets/_Project/Scripts/Core/Tweakables.cs)
  (+4 bomb keys),
  [BlockDefinitionWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs),
  [BlockMaterials.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockMaterials.cs),
  [GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
  (`BuildBomberEntries`, +1 preset slot),
  [ChassisFactory.cs](../../Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
  (`hasWeapon` includes BombBay),
  [RobotWeaponBinder.cs](../../Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs).
- Deleted: `Assets/Polytope Studio/Welcome_Screen/` and `.meta`.
- New asset: `Blueprint_DefaultBomber.asset` (written by
  `BuildAllPassA`).
- Restructured: this changelog directory.
