# Session — Build mode: in-garage block editor (Pass B Phase 3a)

**Intent.** *"Let's focus on how to add a block to a new bot."* After a
short design memo, user picked: **(A) modal toggle** (not always-on),
hotbar OK, and — overriding the default proposal — **block CPU removal
entirely** rather than warn-and-allow. Also delivered as a side quest:
[docs/BEST_PRACTICES.md](../BEST_PRACTICES.md), a 16-section
Robocraft-clone playbook (architecture, block-grid pitfalls, vehicle
physics, URP, GC, pooling, save/load, MP-readiness, profiling, named
pitfalls, perf budgets).

**Shipped — four new components:**
- [OrbitCamera](../../Assets/_Project/Scripts/Player/OrbitCamera.cs) —
  RMB-drag rotate, MMB-drag pan (clamped 4 m radius), scroll-zoom
  (3–20 m). Sibling to FollowCamera; only one enabled at a time. UI
  blocking via `EventSystem.IsPointerOverGameObject`. No cursor lock so
  the HUD stays clickable.
- [BuildModeController](../../Assets/_Project/Scripts/Gameplay/BuildModeController.cs) —
  modal owner. `Enter()` zeros velocity, sets the chassis Rigidbody
  kinematic + FreezeAll, disables `PlayerInputHandler` and
  `FollowCamera`, enables/creates `OrbitCamera`. `Exit()` reverses and
  calls `GarageController.Respawn()` so subsystems reattach to the
  edited blueprint. Public `IsActive`, `Entered`/`Exited` events,
  `Toggle()`.
- [BlockEditor](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs) —
  Camera.main raycast → BlockBehaviour ancestor → face-normal in
  chassis-local space → integer cell. Ghost preview (lazy unit cube
  with translucent URP/Unlit, green/red MaterialPropertyBlock).
  Validation: cell empty + ≥1 occupied 6-axis neighbour + only one
  CPU. LMB place, RMB remove. **CPU cannot be removed** (per user).
  After every mutation: `RecalculateAggregates` + `SyncBlueprintFromGrid`
  regenerates `state.CurrentBlueprint` entries from the live
  `BlockGrid.Blocks` dict — Save Robot is now trivially correct.
- [BuildHotbar](../../Assets/_Project/Scripts/Gameplay/BuildHotbar.cs) —
  procedural Canvas, 7 slots, keys **1–7** map to BlockIds: Cube, CPU,
  Wheel, Steer, Thrust, Aero, Gun. Selected slot tinted hazard orange.
  Visible only while build mode active.

**Wiring.**
- [GarageController](../../Assets/_Project/Scripts/Gameplay/GarageController.cs)
  now lazily attaches the build-mode trio in `EnsureBuildModeWired()`
  and rebinds `SetChassis` after every Respawn. New
  `ToggleBuildMode()` entry point + `BuildMode` accessor.
- [SceneTransitionHud](../../Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
  gains a third stacked garage-only button. Label flips between
  "Build Mode" ↔ "Drive Mode" by subscribing to
  `BuildModeController.Entered/Exited`.

**Why modal.** Always-on edit competes with driving for the same mouse
buttons and same camera. Modal also gives a clean place to freeze the
Rigidbody and swap to an orbit camera that's actually good for
inspection — both of which would be jarring if they happened
implicitly mid-drive.

**Why blueprint sync via "rebuild Entries from grid".** The grid is
already the source of truth at runtime (subsystem auto-binders react
to `BlockPlaced`/`BlockRemoving`). Mirroring back to the blueprint on
each mutation is O(blocks) and keeps Save Robot correct without a
separate diff pipeline. Cheap, idempotent, no edge cases.

**Why CPU-cannot-be-removed (override).** User's call. Removes one
class of surprise — an empty grid, or a CPU-less chassis that fails to
respawn cleanly — without needing a confirm dialog yet. Trivial to
relax later: delete the early-return in `BlockEditor.TryRemove`.

**Known follow-ups (deferred).**
- Hotbar palette is fixed 7 slots; a categorised picker (per
  [BlockCategory](../../Assets/_Project/Scripts/Block/BlockDefinition.cs))
  is the next step once we have more block defs.
- No CPU-budget enforcement yet — `CpuCost` is summed but not gated
  (Robocraft-style CPU cap is a Pass B Phase 3b task).
- Connectivity flood-fill from the CPU is not enforced; current rule
  is "must touch ≥1 existing block", which can produce floating
  islands if the player removes a bridge block. Acceptable for now;
  proper connectivity check goes alongside the CPU cap.
- Garage geometry expansion (bigger walls, grid floor decal,
  back-wall headroom for the orbit camera) is still on deck.
