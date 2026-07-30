# 156 — Dead-code purge + shared-helper consolidation

Cleanup batch from the deep code review (spring-cleaning session). All
dead-code claims were grep-re-verified before deletion, including
`.unity`/`.prefab`/`.asset` GUID references.

## Deleted (git history preserves everything)

- **Vestigial tuning-SO cluster.** `PlaneControlSubsystem._tuning` + six
  inline floats and `ThrusterBlock._tuning` + three floats were never
  read (live values ride the blueprint configs). Deleted the fields, the
  `PlaneControlTuning`/`ThrusterTuning` SO types, their two orphaned
  `*_Default.asset` files, and zero-caller `TuningAssets.cs`.
  `ChassisTuning`/`GroundDriveTuning` SOs are live and untouched.
- **Dev spring-override cluster.** `SpringTuningConfig`, `ApplySpring`,
  and both `Dev.Spring.*` Tweakable registrations (dead since the spring
  became a module, session 105; the dev HUD sliders moved nothing).
- **`BlockEditor.GetCpuUsage` + `CpuUsage`** — zero callers, hand-synced
  twin of the `GetChassisStats` pricing loop.
- **WeaponMount fallback aim + mount rotation** — effect-dead (nothing
  reads the mount's rotation; `ChassisAssembler` always adds RobotDrive,
  so the camera-ray fallback was unreachable). Notably the rotation code
  carried the world-Y-up bug TurretYoke was extracted to fix.
- **`WeaponAmmoState.GetCurrent/GetMax/IsReloading`** — HUDs use
  `EnumeratePools`.
- **Dead publics:** `GameStateController.SetCurrentBlueprint` (bypassed
  `PresetChanged` — a desync trap for a future caller),
  `BuildSession.ClearBindings`, `GravityField.DominantAt`,
  `ScrapDepot.IsRobotInsideAnyDepot` + its never-read
  `s_robotsInsideAnyDepot` static set (incl. the cross-depot Exit prune
  loop that only served it), `OrbitCamera.RecenterOnTarget`,
  `ConcoctionColor.LeverPigment`, `ConcoctionRegistry.RegisterAll`.
- **HudPointerGuard IMGUI-rect half** — `RegisterGuiRect` / the
  double-buffer / `PointerOverGuiRects` had zero production callers
  (modal overlays use `SetModalOpen`). Deleted with its rect tests;
  modal-owner tests kept.
- **`dev/null/` at the worktree root** — literal folder of git-lfs hook
  copies from a Windows redirect accident; real hooks already installed.

## BlueprintBuilder → test-only (deviation, deliberate)

The task said "migrate tests to ScriptedChassisBuilder and retire it,"
but validator tests *need* raw entry authoring — they build deliberately
invalid layouts the rules engine refuses by construction. So instead:
`BlueprintPlan` split into `Block/BlueprintPlan.cs` (production — used
by validator/ASCII dump/scaffolder), and `BlueprintBuilder` moved into
`Tests/EditMode/Blueprints/` where the asmdef boundary makes it
unreachable from production. Same protection, no lost test coverage.

## Consolidated (drifted copies were latent bugs)

- **`Movement/ChassisRaycast`** — self-filtered nearest/any raycast;
  WheelBlock, PogoBlock, HoverBladeBlock delegate `RaycastIgnoringSelf`,
  ModuleBlock delegates `IsGrounded`. One buffer, one skip rule.
- **`RuntimeMaterials.Tint`** (Core) — MPB tint with cached property ids
  and a reused block, writing `_AlbedoColor`/`_BaseColor`/`_Color` (+
  emission overload for RotorBlock). Eleven hand copies now delegate.
  Fixes ModuleEffects' drifted copy, which omitted `_AlbedoColor` and so
  silently no-opped on the MK Toon block shader.
- **`Core/UguiKit`** — `NewChild` (8 copies, one drifted) and the
  full-parameter `AddText` primitive behind the panels' 4 local
  signatures.
- **WheelBlock suspension** now calls `SpringSolver.HookeDamped`
  (session-104 mandate; push-only clamp lives in one place).
- **RopeBlock** per-frame `GetComponentInParent` safety net replaced
  with the RotorBlock cached-ancestor pattern (perf §2.5), including a
  fake-null recheck so destroyed-in-place chassis bodies still rebuild.

## Deferred

- Bot-AI base-class extraction (task item 14) — blocked on the AirBot
  LowHealth-exit fix in the second-tier-bugs batch; do both together.
- The `_BaseColor`/`_Color` id pairs in files doing *material* writes
  (BlockBehaviour damage visuals, CpuBlockMarker, ProjectileGun,
  ScrapDepot, BlockGrid) — different semantics from MPB tinting; left
  for the block-visuals pass.
