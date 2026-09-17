# CHG-009 build — first-party obsolete-API / unused-symbol warnings

Branch: `chg/009-obsolete-api` @ `9fff4872062b2aae96dca092743d1c6397dfb5d4` (parent `ebb6438a86` on `main`)

Built with git plumbing only (`plumb_branch.py`); the shared clone's working
tree and index were never touched — no checkout, no Unity, no test run.

## Scope

Source: `.utmp/factory/sweeps/warnings-census-editor-2026-09-17.txt` (46
first-party `warning CS` lines in 19 files) + FINDINGS F-024 / F-027.
`GroundDriveSubsystem.cs` (3 CS0414 lines) is excluded per the build brief —
CHG-021 already deletes those fields on another branch.

Of the remaining 43 warning lines in 18 files, 40 are fixed here (23 edited
call/declaration sites, one-line-in-one-line-out each) and 3 are left alone
(see below). No third-party file (MidiPlayer, Water.cs, anything outside
`Assets/_Project`) was touched.

## Files changed (16), sites fixed each

| File | Sites fixed | Warning lines closed |
|---|---|---|
| Scripts/Core/AudioRouter.cs | 1 | 1 |
| Scripts/Core/MusicConductor.cs | 1 | 1 |
| Scripts/Core/UiTween.cs | 1 | 1 |
| Scripts/Core/PerfBisect.cs | 3 | 6 |
| Scripts/Gameplay/GarageDecor.cs | 1 | 2 |
| Scripts/Gameplay/NameplateOverlay.cs | 1 | 2 |
| Scripts/Gameplay/ObjectiveHud.cs | 1 | 1 |
| Scripts/Gameplay/SceneTransitionHud.cs | 1 | 1 |
| Scripts/Gameplay/ScrapCarriedIndicator.cs | 1 | 2 |
| Scripts/Gameplay/VoxelChaserBot.cs | 1 | 2 |
| Scripts/Network/Bootstrap/NetworkSceneFlow.cs | 1 | 2 |
| Scripts/Tools/Editor/GameplayScaffolder.cs | 1 | 1 |
| Scripts/UI/PerformanceHud.cs | 3 | 6 |
| Tests/PlayMode/Movement/RotorBlockTests.cs | 2 | 4 |
| Tests/PlayMode/Perf/PerfRenderProbe.cs | 3 | 6 |
| Tests/PlayMode/Voxel/DigZoneTests.cs | 1 | 2 |
| **Total** | **23 sites** | **40 warning lines** |

## Per-site table

Sites are grouped by replacement pattern. "Ordering reasoning" is filled in
only where sort/pick order could plausibly matter — that's the one place
behaviour could change.

### `FindFirstObjectByType<T>()` → `FindAnyObjectByType<T>()`

| File:line | Old | New | Ordering reasoning |
|---|---|---|---|
| AudioRouter.cs:124 | `FindFirstObjectByType<AudioRouter>()` | `FindAnyObjectByType<AudioRouter>()` | `[DisallowMultipleComponent]` scene-root singleton (`s_instance`/`s_root` pattern, comment: "adopt it rather than stacking one clone per recompile"). At most one `AudioRouter` GameObject is ever expected to exist; there is no second candidate to pick between, so instance-ID ordering was never load-bearing. |
| MusicConductor.cs:94 | `FindFirstObjectByType<MusicConductor>()` | `FindAnyObjectByType<MusicConductor>()` | Same singleton bootstrap pattern (`s_instance`, `ResetStatics` on domain reload) as AudioRouter. One instance by construction. |
| UiTween.cs:89 | `FindFirstObjectByType<UiTween>()` | `FindAnyObjectByType<UiTween>()` | Doc comment at the type declaration explicitly says "singleton, statics reset via SubsystemRegistration, adopts a [surviving instance]" — same pattern as AudioRouter/MusicConductor, comment says it mirrors AudioRouter's bootstrap. |
| ObjectiveHud.cs:128 | `Object.FindFirstObjectByType<ArenaController>()` | `Object.FindAnyObjectByType<ArenaController>()` | `ArenaController`'s type doc: "Lives in the Arena scene" (singular). One controller per loaded Arena scene by design; the call is a throttled startup fallback (comment: "scene-wide scan is a startup fallback only") for the one instance ArenaController normally hands over via `BindMatch`. |
| SceneTransitionHud.cs:111 | `Object.FindFirstObjectByType<EventSystem>()` | `Object.FindAnyObjectByType<EventSystem>()` | Unity's own convention is exactly one active `EventSystem` per scene; the call only runs when none was found on-screen (`if (es == null)`) to create one. Picking "any" `EventSystem` when one exists is the correct behaviour — Unity itself doesn't guarantee or care about ordering among EventSystems. |

### `FindObjectsByType<T>(…, FindObjectsSortMode.None)` → same overload minus the sort-mode arg

Every one of these 11 call sites already passed `FindObjectsSortMode.None`
(unordered) before this change, so dropping the now-obsolete parameter is a
literal no-op on the returned array's order — nothing here could have relied
on ordering, because none existed.

| File:line | Old | New |
|---|---|---|
| PerfBisect.cs:85-86 | `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)` |
| PerfBisect.cs:119-120 | `FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)` |
| PerfBisect.cs:158-159 | `FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)` |
| GarageDecor.cs:124 | `Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)` | `Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)` |
| NameplateOverlay.cs:87 | `Object.FindObjectsByType<Robot>(FindObjectsSortMode.None)` | `Object.FindObjectsByType<Robot>()` |
| ScrapCarriedIndicator.cs:93 | `Object.FindObjectsByType<Robot>(FindObjectsSortMode.None)` | `Object.FindObjectsByType<Robot>()` |
| VoxelChaserBot.cs:103 | `FindObjectsByType<Robot>(FindObjectsSortMode.None)` | `FindObjectsByType<Robot>()` |
| NetworkSceneFlow.cs:143 | `FindObjectsByType<NetworkBlockGrid>(FindObjectsSortMode.None)` | `FindObjectsByType<NetworkBlockGrid>()` |
| PerformanceHud.cs:216 | `Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)` | `Object.FindObjectsByType<Rigidbody>()` |
| PerformanceHud.cs:217 | `Object.FindObjectsByType<Joint>(FindObjectsSortMode.None)` | `Object.FindObjectsByType<Joint>()` |
| PerformanceHud.cs:218 | `Object.FindObjectsByType<Robot>(FindObjectsSortMode.None)` | `Object.FindObjectsByType<Robot>()` |
| RotorBlockTests.cs:374-375 | `Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length` | `Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include).Length` |
| RotorBlockTests.cs:393-394 | same | same |
| PerfRenderProbe.cs:77 | `Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)` |
| PerfRenderProbe.cs:100 | `Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)` |
| PerfRenderProbe.cs:112 | `Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)` |
| DigZoneTests.cs:74-75 | `Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)` | `Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)` |

Note on `VoxelChaserBot.cs:103`: the surrounding comment says "Pick the
first Robot in scene that isn't this bot" — but the array was already
unordered (`FindObjectsSortMode.None`) before this change, so "first" already
meant "arbitrary." This edit doesn't change that; flagging only because the
comment reads like an ordering guarantee that was never actually there.

### CS0219 unused local

| File:line | Old | New |
|---|---|---|
| GameplayScaffolder.cs:378 | `const int xMin = -2, xMax = 2;` | `const int xMax = 2;` |

Confirmed `xMin` has no other reference in the file (`grep -n "xMin"` only
matches the declaration); `xMax` is used at line 397 and is kept.

## Left alone, why

| File:line | Warning | Why not fixed |
|---|---|---|
| ChassisInstancedRenderer.cs:135 | CS0618 `Object.GetInstanceID()` → "Use GetEntityId instead" | `GetEntityId()` returns Unity's `EntityId` struct, not `int` — confirmed in this same project (`DigChunk.cs:351` declares `public EntityId MeshEntityID;` and calls `_mesh.GetEntityId()` without any cast to int) and by a third-party call site (`Routine.cs:1348`, `(int)gameObj.GetEntityId().GetHashCode()`) that casts the *hash code*, not the id itself, implying no implicit `EntityId`→`int` conversion. The call site here is `int key = mat.GetInstanceID();` feeding a `Dictionary<int, Group> byMat` used purely as a per-`Build()`-call de-dup key. A correct fix means retyping the dictionary (`Dictionary<EntityId, Group>`), which is more than the one-line rename this pass sticks to, and I can't confirm `EntityId`'s `Equals`/`GetHashCode` semantics without compiling. Using `.GetHashCode()` to keep the `int` key (mirroring the third-party idiom) would trade GetInstanceID's guaranteed-unique key for a hash that could in principle collide — not zero-behaviour-change. Left alone. |
| PerformanceMenu.cs:66-67 | CS0618 `UnityStats.frameTime` / `UnityStats.renderTime` → "Use FrameTimingManager.GetLatestTimings" | Not a drop-in rename: `FrameTimingManager` requires calling `CaptureFrameTimings()` and then `GetLatestTimings(uint, FrameTiming[])` into an out-array, and the data it returns lags by a few frames (double/triple buffered), unlike `UnityStats`' immediate per-frame read. The same menu item logs 5 other `UnityStats.*` members (`drawCalls`, `setPassCalls`, `triangles`, `vertices`, `shadowCasters`) that are *not* flagged obsolete, so a partial migration would leave the log line mixing an immediate read with a lagged one in the same string — a behaviour change in a diagnostic tool whose whole job is being trustworthy on demand. Left alone. |
| GroundDriveSubsystem.cs:27,28,31 | CS0414 `_acceleration`/`_maxSpeed`/`_turnRate` unused fields | Out of scope per the build brief — CHG-021 (already landed on `chg/021-dead-drive-fields`, commit `9fd2c9a7`) deletes these three fields. Touching this file here would conflict with that branch. |

## Verification the foreground should run

1. `.claude/scripts/run-tests.sh All` on branch `chg/009-obsolete-api` (checked out in the serialized rig — this build never touched the clone, so the branch is a plain fast-forward/merge candidate).
2. A full recompile over the bridge (forced, e.g. after a `CleanBuildCache` like the original census) and `grep -c 'warning CS' Editor.log`. Expected: the first-party count drops by 40 relative to the 2026-09-17T18:32Z census baseline (46 first-party lines total; 3 are GroundDriveSubsystem's, owned by chg/021; 3 remain — ChassisInstancedRenderer's GetEntityId line and PerformanceMenu's two UnityStats lines — both left alone above).
3. Spot-check no new warnings were introduced by re-running the same `grep "_Project"` filter over the new log and diffing file:line entries against this table.
