# Session 17 — Rotor / aerofoil decoupling, follow-ups (lift works, three new bugs)

> Status: **partial.** Lift now works. The session-16 SetParent
> exception is fixed in both directions (build and teardown). One of
> the three session-16 regressions (R3, no lift) is dead. R1 (garage
> render) is unchanged, R2 (missing tweakable knobs) is still
> deferred. Three *new* gameplay bugs surfaced once lift came back —
> see the bottom of this file.

## Intent

Pick up where session 16 left off: fix the two regressions the user
flagged in their next-session brief — foils visibly attached "1 block
higher than the rotor," and no lift produced. Plus: simplify the
Robogame menu to a single "Build Everything" entry that also saves
scenes, so the user doesn't have to keep track of which scaffolders
to run after each test session.

## What shipped

### 1. `RotorBlock.cs` — SetParent timing fixes (build and teardown paths)

Two distinct Unity timing exceptions were tearing the lift rig before
it could function. Both are now fixed.

**Build-path fix (`BuildLiftRig` + `AdoptAdjacentAerofoils`).** The
adoption loop calls `SetParent(_hub.transform, true)` on each foil
immediately after `new GameObject(RotorHub_…)`. Unity treats a
freshly-created GameObject as "in activation" until the call frame
returns, and rejects `SetParent` into a transitioning parent —
hence the original "Cannot set the parent ... while activating or
deactivating the parent GameObject 'RotorHub_…'" exception. The fix
is the same SetActive dance `ChassisFactory.Build` uses for
`AddComponent` + reflection: deactivate the hub immediately after
creation, do every reparent + `ConfigureRotorMode` call while
inactive, then reactivate. The foils' `OnEnable` re-fires on the
cascade but the existing `_rotorMode && _forceTargetRb != null`
guard at `AeroSurfaceBlock.OnEnable` preserves the just-injected
configuration.

**Teardown-path fix (`OnDisable` + `BuildLiftRig` idempotency).**
[`Robot.CaptureTemplate`](../../Assets/_Project/Scripts/Robot/Robot.cs)
briefly `SetActive(false) → Instantiate → SetActive(true)` on the
chassis at first `Start()` to clone it as a hidden cold-storage
template (so the robot can be rebuilt from scratch on death). That
brief deactivation cascades `OnDisable` through every block. The old
`OnDisable` called `DestroyLiftRig`, which tried to `SetParent`
foils back to their `OriginalParent` (the chassis grid root) — but
the chassis grid root is itself mid-deactivation in that window, and
Unity rejects the reparent for the same reason. The partial failure
left `_hubGo` non-null and `_adoptedFoils` half-cleared, so the
post-snapshot `OnEnable` built a *second* hub on top of the first
and the foils ended up under the orphan. That was the actual reason
"no lift": the hub the rotor was driving each FixedUpdate wasn't the
hub the foils were parented under.

The fix:

- `OnDisable` no longer calls `DestroyLiftRig`. A comment explains
  why (the hub lives at scene root and is unaffected by the chassis
  cascade; the snapshot is transient; real teardown happens on
  `OnDestroy` and on explicit `Rebuild()` for parent-change /
  debris-detach paths).
- `BuildLiftRig` now early-outs if `_hubGo != null`, so the
  post-snapshot `OnEnable` is a no-op rather than a duplicate
  rebuild. Explicit `Rebuild()` callers tear down first via
  `DestroyLiftRig`, so the early-out doesn't block them.

### 2. `RotorBlock.AdoptAdjacentAerofoils` — world-space rotation + position re-assert

Two correctness changes inside the adoption loop, both small but real:

- **World position re-asserted** after `SetParent(_hub.transform, true)`.
  `worldPositionStays: true` is supposed to preserve the world position
  on reparent, but a defensive `aero.transform.position = foilWorldPos`
  immediately after the reparent guarantees pixel-exact placement at
  the placed cell regardless of any tiny accumulated hub-transform
  drift.
- **Pitch axis is now world-space radial, not post-`LookRotation`
  local-+X.** The old code rotated the blade by `_collectivePitchDeg`
  around `Vector3.right` after a `LookRotation(tangent, spinAxis)` —
  which only matched the intended "tilt around this blade's own
  radial line" formulation for the +X blade. The other three blades
  had subtly wrong pitch axes. The new code computes
  `Quaternion.AngleAxis(_collectivePitchDeg, radialWorld)` and
  applies it as a world-space rotation. Visible result: all four
  blades now tilt their leading edges consistently around their own
  radial line.

### 3. `BuildEverythingMenu.cs` — single-button workflow

The Robogame menu now has exactly one entry: **`Robogame/Build Everything`**
(Ctrl+Shift+B). It calls `GameplayScaffolder.BuildAllPassA()` and
then `EditorSceneManager.SaveOpenScenes()`, so a single keystroke
rebuilds and persists the entire project surface — no chance of
forgetting which scaffolder to run after touching block defs vs.
materials vs. scenes.

Every other `[MenuItem]` in the project was removed (commands stay
public/static and callable from code, so no caller path breaks; only
the menu entry is gone). Affected files:

- `BlockDefinitionWizard.cs`, `BuildSettingsConfigurator.cs`,
  `CombatVfxWizard.cs`, `HillsGround.cs` — single `[MenuItem]` removed
  from each.
- `SceneScaffolder.cs`, `GameplayScaffolder.cs` — both `[MenuItem]`s
  and the now-unused `MenuRoot` constants removed; the menu-only
  wrapper `CreateDefaultBlueprintsMenu` is gone (`BuildAllPassA` was
  already calling `CreateDefaultBlueprints` directly).
- `BuildEverythingMenu.cs` — added the `SaveOpenScenes` call,
  removed the now-pointless visual separator.

### 4. First test scaffold

`Assets/_Project/Tests/PlayMode/Movement/RotorBlockTests.cs` and
`AeroSurfaceBlockTests.cs` land alongside this change as the project's
first Unity Test Framework tests. Eight tests total, covering
adoption count, foil-at-cell placement (R1 regression), force-target
routing (R3 regression), the `OnEnable` rotor-mode guard, the
zero-Rigidbody-when-`GeneratesLift=false` contract from
PHYSICS_PLAN §1.2, and the bare-rotor-zero-adoption case.
Tests reflect on private fields (`_adoptedFoils`, `_velocityRb`,
`_forceTargetRb`, `_rotorMode`) since the production classes don't
expose those publicly — pragmatic call given the alternative is
carving an `internal` test surface in the production class.
asmdefs at `Assets/_Project/Tests/PlayMode/Robogame.Tests.PlayMode.asmdef`
and `Assets/_Project/Tests/EditMode/Robogame.Tests.EditMode.asmdef`
(EditMode dir empty — ready for the first pure-logic test).

## Files touched

- [Assets/_Project/Scripts/Movement/RotorBlock.cs](../../Assets/_Project/Scripts/Movement/RotorBlock.cs)
- [Assets/_Project/Scripts/Tools/Editor/BuildEverythingMenu.cs](../../Assets/_Project/Scripts/Tools/Editor/BuildEverythingMenu.cs)
- [Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
- [Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs](../../Assets/_Project/Scripts/Tools/Editor/SceneScaffolder.cs)
- [Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
- [Assets/_Project/Scripts/Tools/Editor/BuildSettingsConfigurator.cs](../../Assets/_Project/Scripts/Tools/Editor/BuildSettingsConfigurator.cs)
- [Assets/_Project/Scripts/Tools/Editor/CombatVfxWizard.cs](../../Assets/_Project/Scripts/Tools/Editor/CombatVfxWizard.cs)
- [Assets/_Project/Scripts/Tools/Editor/HillsGround.cs](../../Assets/_Project/Scripts/Tools/Editor/HillsGround.cs)
- New: `Assets/_Project/Tests/PlayMode/Movement/RotorBlockTests.cs`
- New: `Assets/_Project/Tests/PlayMode/Movement/AeroSurfaceBlockTests.cs`
- New: `Assets/_Project/Tests/PlayMode/Robogame.Tests.PlayMode.asmdef`
- New: `Assets/_Project/Tests/EditMode/Robogame.Tests.EditMode.asmdef`

## Outstanding bugs (the user's call-outs after this session)

These are the things that *still* don't work, or that surfaced once
lift came back online. They are **not yet fixed** and are the
starting point for session 18.

### B1 — Rotors still don't render in the garage

Carry-over from session 16's R1. The four `Aero` cells around the
helicopter's main rotor at `(0,1,0)` don't appear visually in the
garage view. Likely overlaps with the `RotorsGenerateLift` flag flip
running in the garage path: `BuildLiftRig` reparents the foils under
a scene-root hub even in the garage, and the garage's static-display
logic (chassis Rigidbody kinematic, transforms locked) probably
relies on the foils being children of the chassis. Fix is either:
suppress `BuildLiftRig` entirely while in the garage, or have the
garage's display path explicitly walk both the chassis hierarchy
and any rotor hubs at scene root.

### B2 — Helicopter body rotates with the rotors

Even at low RPM, the chassis itself spins around the rotor's spin
axis. The rotor mode in `AeroSurfaceBlock` is supposed to suppress
drag and sideslip exactly to prevent reaction-torque from a symmetric
blade ring being dumped into the chassis (see PHYSICS_PLAN §2 and
the class-level remark in `AeroSurfaceBlock.cs`). With drag/sideslip
suppressed, four symmetric blades applying pure-vertical lift at
ring-symmetric positions should produce zero net torque on the
chassis. The fact that yaw is being induced suggests one of:

1. **Asymmetric blade configuration.** Only some of the four foils
   are getting `_rotorMode = true` and the rest are still applying
   drag (which would torque the chassis around the spin axis).
   Sanity check: the diagnostic logs added this session
   (`[RotorBlock] adopted '<foil>' world=…`) should show all four
   foils adopted. If fewer than four log, that's the cause.
2. **Lift axis tilted off-vertical.** With the new world-space pitch
   formulation, each blade's `transform.up` is `spinAxisWorld` tilted
   by `_collectivePitchDeg` around its own radial. The horizontal
   component of that tilted lift, projected onto the spin tangent,
   is what would produce yaw torque. With ring symmetry the
   per-blade tangential components should cancel — but only if the
   pitch sign is consistent across blades. Worth verifying that all
   four blades tilt the *same way* (leading edge up everywhere) and
   not in a way that produces a net tangential force at the ring.
3. **The hub's `MoveRotation` is leaking torque into the chassis
   via PhysX.** Shouldn't — kinematic bodies don't react to forces
   and don't push their parent. But the foils, while now correctly
   parented under the kinematic hub, are still applying lift to the
   chassis Rigidbody. If the chassis's COM and the rotor's COM are
   close, the lever arm is small but non-zero, and tiny per-step
   floating-point asymmetries could spiral via the rigidbody's
   angular drag (or lack of it).

### B3 — Helicopter becomes a "spinning, destructive mess" instantly on launch, even at low RPM

Almost certainly the same root cause as B2 — the chassis spin
spiraling out of control once it starts. PhysX integrates angular
velocity, and without a counteracting torque (no anti-torque tail
rotor; no reaction-torque modeling at all by design — see
PHYSICS_PLAN §2) any seed yaw rate will keep growing if there's a
positive feedback loop. Need to confirm whether the chassis's
angular drag is set to a sane value (default 0.05 is too low for an
arcade flight model) and whether the spin is truly diverging or
merely slow to settle.

### Bright spot — lift works

For the first time since session 14 ripped open the rotor block,
the helicopter actually rises. The full path is:
hub spins kinematically → PhysX synthesises tangential velocity at
each foil's world position → `AeroSurfaceBlock.FixedUpdate` reads
that velocity, computes AoA × speed² lift along the foil's `up`,
applies the force to the chassis Rigidbody. End-to-end is now
proven; what remains is making the result *stable*.

## Notes for next session

- The diagnostic `Debug.Log` statements added in
  `AdoptAdjacentAerofoils` and `BuildLiftRig` are still in. Strip
  them once B1 / B2 / B3 are diagnosed and the lift-generation path
  is no longer load-bearing on per-session console inspection.
- B2 / B3 are likely a single fix. Investigate in this order: (a)
  confirm all four foils log as adopted; (b) inspect the chassis
  Rigidbody's `angularDrag` and `angularVelocity` in Play; (c) if
  needed, add per-fixed-step diagnostic of the chassis angular
  velocity and total applied torque from the foils.
- B1 is independent and probably doesn't need new architecture —
  just an explicit "don't build the lift rig in the garage" gate.
  Garage shows the chassis as a static, kinematic display; lift
  doesn't matter there.
