# Robogame — Best Practices

> **Audience.** Future contributors (human or AI) writing code for this
> project. Read once end-to-end; consult per-section when you're about
> to start a new system.
>
> **Scope.** Concrete, opinionated rules tailored to *this* game — a
> Unity 6 / URP, voxel-block-based vehicular combat title with a
> client-server multiplayer endgame. Generic Unity advice that doesn't
> apply here is left out on purpose.
>
> **Tone.** "Do this. Don't do that. Here's why." When research is
> still open we say so explicitly with a 🔬 marker.

---

## Table of contents

1. [The two non-negotiables](#1-the-two-non-negotiables)
2. [Architecture & code organization](#2-architecture--code-organization)
3. [Block grids, compound colliders, and rebuild cost](#3-block-grids-compound-colliders-and-rebuild-cost)
4. [Rigidbody / vehicle physics](#4-rigidbody--vehicle-physics)
5. [Damage, destruction, and structural integrity](#5-damage-destruction-and-structural-integrity)
6. [Rendering performance (URP)](#6-rendering-performance-urp)
7. [Memory, GC, and allocations](#7-memory-gc-and-allocations)
8. [Pooling everything that lives < 1 second](#8-pooling-everything-that-lives--1-second)
9. [Input System](#9-input-system)
10. [ScriptableObject discipline](#10-scriptableobject-discipline)
11. [Save / load + serialization](#11-save--load--serialization)
12. [Multiplayer-readiness rules (enforced now)](#12-multiplayer-readiness-rules-enforced-now)
13. [Editor tooling & scene scaffolders](#13-editor-tooling--scene-scaffolders)
14. [Profiling, debugging, and testing](#14-profiling-debugging-and-testing)
15. [Common pitfalls specific to Robocraft-likes](#15-common-pitfalls-specific-to-robocraft-likes)
16. [Performance budgets (targets, not law)](#16-performance-budgets-targets-not-law)

---

## 1. The two non-negotiables

These two rules subsume half the other rules. If you're about to break
one, stop and think.

1. **Transforms are presentation, not state.** Health, ammo, position-
   on-grid, ownership, "is dead" — those live on components or
   ScriptableObjects. The Transform exists to *display* the result.
   Multiplayer code can only sync state if state isn't smeared across
   `transform.position`, `transform.localScale`, and the GameObject's
   active flag.
2. **The server (host in P2P) is authoritative.** Even in singleplayer
   today: the player sends inputs, a controller mutates state, visuals
   read state. We will *not* be retrofitting netcode onto a codebase
   that mutates state in `Update()` from input handlers.

---

## 2. Architecture & code organization

### 2.1 Assembly definitions are walls, not suggestions

Every gameplay folder under [Assets/_Project/Scripts/](Assets/_Project/Scripts/)
has its own `.asmdef`: `Robogame.Block`, `Robogame.Combat`,
`Robogame.Movement`, `Robogame.Player`, `Robogame.Robot`,
`Robogame.Gameplay`, `Robogame.UI`, `Robogame.Input`, `Robogame.Core`,
`Robogame.Network`. **Don't add a reference to break a circular
dependency** — refactor instead. If `Movement` "needs" to reference
`Combat`, the abstraction belongs in `Core` (an interface) or
`Block` (the data).

### 2.2 Dependency direction (low → high)

```
Core  →  Block  →  Movement / Combat  →  Robot  →  Gameplay  →  UI
                                                 ↑
                              Player ───────────┘
                              Input  ───────────┘
                              Network (planned, sits at Gameplay tier)
```

Tools.Editor and Robogame.Tools.Editor are editor-only and may
reference any runtime assembly (they're scaffolders).

### 2.3 The four communication mechanisms, ranked

| Mechanism | When to use | When *not* to |
|---|---|---|
| **Method call on a held reference** | Same-frame, same-system. The 90% case. | Across system boundaries. |
| **C# `event` / `Action<T>`** | Cross-system notifications inside a single process. | Anything that needs to cross the network — use RPCs. |
| **ScriptableObject as a shared bus** | Designer-tunable global signals (e.g. "round ended"). | Per-frame state — too slow, too magical. |
| **`UnityEvent`** | Inspector-wired UI buttons / sliders only. | Runtime gameplay logic. Slower than `Action`, harder to refactor. |

### 2.4 Singletons

`GameStateController.Instance` is the only sanctioned singleton today,
and it's *bootstrapped explicitly* from the Bootstrap scene — no lazy
`Instance` creation. New singletons need a written justification in
[CHANGES.md](CHANGES.md). Default to passing references.

### 2.5 Lifecycle order

`Awake` → wire up internal state, `GetComponent` cached fields,
subscribe to events.
`OnEnable` / `OnDisable` → subscribe / unsubscribe to *external*
events (other components, singletons). Always pair them. Forgetting
`OnDisable` cleanup is the #1 source of "ghost" callbacks after
scene reload.
`Start` → first frame of being alive; safe to query `GameStateController.Instance`.
`Update` / `FixedUpdate` → see § 4.

---

## 3. Block grids, compound colliders, and rebuild cost

The block grid is the project's hottest performance system. Every
weapon hit, every block destruction, every save/load can mutate it.

### 3.1 Build the chassis once, mutate cheaply after

[ChassisFactory](Assets/_Project/Scripts/Gameplay/ChassisFactory.cs)
materializes a blueprint into one GameObject hierarchy: root
`Robot` (with the single chassis `Rigidbody`), per-block child
GameObjects, and **per-block colliders that are children of the
single root Rigidbody** — i.e. a *compound collider*, not one
Rigidbody per block.

**Why one Rigidbody per chassis, not one per block:**

- PhysX scales linearly with active Rigidbodies. 60 cubes × 16 robots
  × 2 teams = 1,920 Rigidbodies if you got it wrong. ~30 if you got
  it right.
- Compound colliders share a mass tensor; adding a child collider
  re-cooks the inertia tensor at runtime — cheap.
- Detached debris becomes its own short-lived Rigidbody (see
  [Robot.cs](Assets/_Project/Scripts/Robot/Robot.cs) `detached.gameObject.AddComponent<Rigidbody>()`).

### 3.2 Don't rebuild the mesh on every block change

When a block is destroyed, **don't** `Mesh.CombineMeshes` the whole
chassis. The cost is O(blocks) per hit and the GC churn from the
intermediate `CombineInstance[]` is brutal.

Tiers, in order of preference:

1. **Per-block child renderers + GPU instancing.** What we do today.
   The SRP Batcher handles the rest. Cost is ~0 per destruction
   (just disable / destroy the child renderer).
2. **Greedy meshing into chunked combined meshes** (e.g. 8×8×8 block
   chunks). Useful only if (1) becomes draw-call-bound. ~1k+ blocks
   per chassis territory.
3. **Per-chassis combined mesh.** ❌ Don't. Rebuild cost dominates.

### 3.3 MeshCollider is forbidden on dynamic chassis parts

PhysX cooks a BVH for each unique `Mesh` assigned to a
`MeshCollider`. That cooking is *slow* (single-digit ms for a small
mesh, 10s of ms for a big one) and happens on the main thread. A
voxel chassis built from per-block primitive colliders (`BoxCollider`,
`SphereCollider`, `CapsuleCollider`) avoids this entirely.

`MeshCollider` is fine for **static** environment geometry that
never changes after bake (see [HillsGround.cs](Assets/_Project/Scripts/Tools/Editor/HillsGround.cs))
because the cook happens in the editor and is loaded as cached cook
data at runtime.

### 3.4 Mass updates: batch them

`Robot.RecomputeFromGrid` (see comment in `Robot.cs`: *"Recompute
mass, CPU, and block count from the grid; sync to the rigidbody"*)
should be called **once per frame at most**, not once per block
change. Batch destructions inside a single frame and call once at
the end.

### 3.5 Connectivity / structural integrity

When a block dies, the graph may split. Use a **flood fill from the
CPU block** — not from the dead block — to find which blocks
remain "anchored". Anything not reached is debris.

- Cost: O(blocks) once per destruction event.
- Don't do it eagerly on each hit: queue dead blocks during a frame,
  flood-fill once at end-of-frame.
- The flood-fill needs a `Stack<Vector3Int>` and a `HashSet<Vector3Int>`.
  **Pool both** (see § 8). A 200-block chassis allocating these every
  hit is a measurable hitch.

---

## 4. Rigidbody / vehicle physics

### 4.1 The five must-have Rigidbody settings on the chassis

```csharp
rb.interpolation = RigidbodyInterpolation.Interpolate;   // smooth visuals at any framerate
rb.collisionDetectionMode = CollisionDetectionMode.Continuous;  // for the player robot only — fast moving
rb.linearDamping  = 0.05f;  // small, not zero — avoids sliding forever on flat ground
rb.angularDamping = 0.5f;   // helps stabilize spinning chassis
rb.maxAngularVelocity = 12f;  // default 7 is too low for tight turns; cap so destruction doesn't go nuts
```

Note: in **Unity 6** the property is `linearDamping` / `angularDamping`
(the old `drag` / `angularDrag` were renamed and the linear velocity
property is `linearVelocity`, not `velocity`). Mixing the old names
silently still works via `[Obsolete]` shims but the compiler warns.

### 4.2 `FixedUpdate` is the only place forces happen

Reading input in `Update`, applying force in `Update`: don't.

```csharp
// Update: read input, store intent
// FixedUpdate: apply forces based on the latest stored intent
```

This is what [PlayerController.FixedUpdate](Assets/_Project/Scripts/Player/PlayerController.cs#L35)
already does and what every drive subsystem
([GroundDriveSubsystem](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs),
[PlaneControlSubsystem](Assets/_Project/Scripts/Movement/PlaneControlSubsystem.cs),
`AeroSurfaceBlock`) follows. Don't break the pattern.

Default Fixed Timestep is **0.02s (50Hz)**. Leave it. Lowering to
0.01s doubles physics cost. If your wheels feel jittery, the fix is
*interpolation* on the Rigidbody, not a smaller timestep.

### 4.3 `AddForce` vs `AddForceAtPosition` vs setting `linearVelocity` directly

- **AddForce** for thrusters, gravity-like pulls. Force × time = Δv.
- **AddForceAtPosition** for wheels, jets, weapon recoil — anywhere
  the force has a real lever arm and you *want* the torque it implies.
- **Setting `linearVelocity` directly** is for teleports / respawns
  only. Doing it every frame fights the physics solver and produces
  the "spongy" feel that's hard to debug.
- **`AddTorque`** for steering assist; combine with `AddForceAtPosition`
  for tank-like turning.

### 4.4 Center of mass

A 3×3×1 voxel chassis has its CoM at the geometric centre — usually
mid-air relative to the wheels. **Push it down** (e.g.
`rb.centerOfMass = new Vector3(0, -0.4f, 0);`) so the rover doesn't
roll over from a sneeze. Recompute on every block-set change.

### 4.5 Don't use `WheelCollider` for voxel rovers

`WheelCollider` is opinionated, brittle, and assumes a kinematic
suspension axis. Our wheels are voxel-attached: do raycast-based
suspension instead (raycast down from each wheel's transform, apply
spring/damper force at the hit, friction force perpendicular to
wheel forward). Way easier to debug, maps cleanly to multiplayer
state replication.

### 4.6 Joints for hinges, not for blocks

`FixedJoint` between every block and the chassis: ❌ — solver thrashes
with hundreds of joints. Compound collider on a single Rigidbody: ✅.

`HingeJoint` / `ConfigurableJoint` only for parts that *should* move
relative to the chassis: turret yaw, rotor spin, opening hatches.

---

## 5. Damage, destruction, and structural integrity

### 5.1 The damage flow

```
Projectile.OnTriggerEnter / Raycast hit
    → IDamageable on the hit collider's GameObject
    → BlockBehaviour.TakeDamage(amount, hitPoint, normal)
    → if HP <= 0:  queue destruction in Robot._pendingDeaths (HashSet<BlockBehaviour>)
                   schedule a single FlushDeaths() at end-of-frame
```

Don't destroy from inside the projectile callback. Always queue,
flush once. This is the only way the connectivity flood-fill stays
cheap and deterministic.

### 5.2 Hitscan vs. projectile

- **Hitscan**: `Physics.Raycast` once per shot. Use a dedicated
  `LayerMask` (e.g. `Layer_Damageable`) — never `Physics.RaycastAll`
  unless you have a specific reason. Bake `QueryTriggerInteraction.Ignore`
  into the call so triggers don't show up.
- **Projectile**: pooled (§ 8), uses `CollisionDetectionMode.ContinuousDynamic`
  if it's fast (>30 m/s). Below that, plain `Discrete` is fine.
  Don't put rigidbodies on slow projectiles you can simulate
  yourself with a script; PhysX overhead per object is non-trivial.

### 5.3 Debris lifetime

Detached chunks should auto-despawn after **5–10 seconds** to keep
PhysX object count bounded. Use a coroutine on the chunk root or a
dedicated `DebrisLifetime` component reading a constant from a
ScriptableObject so it's tunable in one place.

---

## 6. Rendering performance (URP)

### 6.1 SRP Batcher first, GPU instancing second, static batching third

The SRP Batcher handles "lots of objects, same shader, different
materials" — the case for a voxel chassis with painted blocks.
Conditions: shader must be SRP-Batcher-compatible (URP/Lit and our
custom MK Toon variants are). **Don't use `MaterialPropertyBlock` for
per-block tints if you want SRP-batched** — MPB breaks SRP batching.
Make a small palette of materials and reuse them.

### 6.2 Shadows are the silent killer

Each shadow-casting light × each shadow-casting renderer = a draw.
A robot with 200 cubes casting shadows from one directional light
+ one realtime point light = 400 extra draws.

- Disable cast shadows on small / interior cubes (you won't see them).
- Turn off `Receive Shadows` on chassis blocks if the chassis is
  small relative to the camera distance — saves shadow-map sampling.
- Only the directional sun should cast shadows by default.
- URP Renderer asset → Shadow Resolution `2048` is fine, `4096`
  doubles the shadowmap memory for a barely-visible difference.

### 6.3 Post-processing budget

Bloom is cheap-ish. Tonemapping is free. **Vignette + Color Adjustments**
are cheap. **Depth of Field, Motion Blur, SSAO, SSR**: each is a
non-trivial fraction of a frame. Profile before adding any of them.

[PostProcessingBuilder.cs](Assets/_Project/Scripts/Tools/Editor/PostProcessingBuilder.cs)
keeps the garage / arena profiles minimal on purpose.

### 6.4 Fluff grass is expensive

Shell-based grass renders the same mesh N times (12–16 shells in
our config). On the arena ground that's 12–16× the triangle count
*for grass alone*. Mitigations already in place:

- `_FadeStartDistance = 150`, `_MaximumDistance = 220` — grass turns
  off past 220m.
- Turn off `_FinsEnabled` if low-end perf becomes an issue (fins are
  the per-blade quads; shells alone still look OK).
- LOD bias on the ground mesh.

### 6.5 URP Renderer features add cost — turn off what you don't use

Default URP comes with SSAO, Decals, Render Objects pass slots. If
you're not using them, *remove them from the Renderer asset*, don't
just disable the volume override. The pass setup runs every frame
either way.

---

## 7. Memory, GC, and allocations

### 7.1 The big four allocations to never write

```csharp
// 1. LINQ in hot paths.
foreach (var b in blocks.Where(b => b.IsCpu))           // ❌ allocates iterator + closure
for (int i = 0; i < blocks.Count; i++) if (blocks[i].IsCpu) // ✅

// 2. string concatenation in Update / FixedUpdate.
_label.text = "HP: " + hp + " / " + max;                 // ❌ allocates 2 strings + 1 boxed int
_label.text = $"HP: {hp} / {max}";                       // ❌ slightly less but still GC
_label.SetTextDirty(); /* update only when hp changes */ // ✅

// 3. new List<T>() / new T[] in callbacks.
void OnHit() { var hits = new List<RaycastHit>(); ... } // ❌
                                                          // ✅ field-cache or pool (§ 8)

// 4. Lambdas that capture locals.
button.onClick.AddListener(() => Save(name));            // captures `name`, allocates
button.onClick.AddListener(HandleSaveClick);             // ✅ method group
```

### 7.2 `foreach` on `List<T>` is fine (Unity 6)

Modern Unity / IL2CPP optimises `foreach` on `List<T>` to a struct
enumerator with no allocation. The "always use `for`" advice is
~2017 wisdom. **`foreach` on a `Dictionary<K,V>` still allocates** —
keep a parallel `List<K>` of keys for hot paths.

### 7.3 Cache `WaitForSeconds`

Each `new WaitForSeconds(1f)` allocates. If you reuse a coroutine
delay, cache the instance. Better: use `Awaitable.WaitForSecondsAsync`
(Unity 6 native) which is allocation-free.

### 7.4 Strings in logs: gate them

`Debug.Log($"[Robogame] {robot.Name} took {amount} dmg from {source}")`
allocates the formatted string *even if logging is disabled*. For hot
paths, gate it: `if (DebugFlags.Combat) Debug.Log(...)`. Better: use
`UnityEngine.Debug.unityLogger.logEnabled` early-outs in a wrapper.

---

## 8. Pooling everything that lives < 1 second

Use **`UnityEngine.Pool.ObjectPool<T>`** (built-in since Unity 2021).
Don't roll your own.

```csharp
private readonly ObjectPool<Projectile> _projectiles = new(
    createFunc:        () => Instantiate(_projectilePrefab),
    actionOnGet:       p => p.gameObject.SetActive(true),
    actionOnRelease:   p => p.gameObject.SetActive(false),
    actionOnDestroy:   p => Destroy(p.gameObject),
    collectionCheck:   true,        // editor-only: catches double-release
    defaultCapacity:   32,
    maxSize:           256);
```

Things to pool now:

- Projectiles, muzzle flashes, hit-effect particles
- Damage number popups
- Debris-block prefabs (after detachment)
- Coroutines that fire often (use Awaitable / UniTask instead)
- Stack/HashSet/List buffers for the connectivity flood-fill
  (`UnityEngine.Pool.ListPool<T>`, `HashSetPool<T>`, `StackPool<T>`)

Things **not** to pool:

- The chassis itself (built once per garage-launch).
- ScriptableObjects (they're not GameObjects; just keep references).
- One-shot UI dialogs.

---

## 9. Input System

### 9.1 One `InputActionAsset`, referenced everywhere

[Assets/InputSystem_Actions.inputactions](Assets/InputSystem_Actions.inputactions)
is the single source of truth. `GameStateController.InputActions`
holds the reference. Don't `new InputAction(...)` in code at runtime —
you lose the rebind UI, control schemes, and the input debugger.

### 9.2 Disable on disable

```csharp
private void OnEnable()  => _moveAction.Enable();
private void OnDisable() => _moveAction.Disable();
```

A leaked enabled action is a callback that fires after the GameObject
is destroyed and crashes / no-ops mysteriously.

### 9.3 Read in `Update`, store intent, apply in `FixedUpdate`

See § 4.2. Never sample input in `FixedUpdate` directly — multiple
fixed steps per Update mean you'd miss button-down events.

### 9.4 UI must not steal player actions

The garage HUD canvas needs `InputSystemUIInputModule` (not the legacy
`StandaloneInputModule`) to participate properly with the new Input
System. [SceneTransitionHud.EnsureEventSystem](Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs)
already enforces this — keep it that way.

---

## 10. ScriptableObject discipline

### 10.1 Data, not behaviour

`BlockDefinition`, `WeaponDefinition`, `HillsSettings` describe **what
a thing is**. They don't tick, they don't have state that changes
during play. If you find yourself wanting `Update()` on a SO, you
want a MonoBehaviour or a plain C# class.

### 10.2 Asset reference vs string ID

A blueprint references a block as a **string ID**
(`block.movement.wheel`), not as a `BlockDefinition` asset reference.
This is deliberate:

- Saves stay valid if an asset is moved or renamed.
- JSON / netcode round-trip is trivial (see [BlueprintSerializer.cs](Assets/_Project/Scripts/Block/BlueprintSerializer.cs)).
- A library lookup at materialization time is O(1) via dictionary —
  the cost is invisible.

### 10.3 Don't mutate authored assets at runtime

`GameStateController.CloneBlueprint` exists *because* the player's
working blueprint must be a runtime-`CreateInstance` copy. If you
edit `_defaultBlueprint` directly, you've corrupted the asset on disk
the moment the user saves the project from the editor.

---

## 11. Save / load + serialization

### 11.1 Schema versioning is non-optional

[BlueprintSerializer.cs](Assets/_Project/Scripts/Block/BlueprintSerializer.cs)
writes `schemaVersion: 1`. The day we change the on-disk shape,
older saves break unless we ship a migration. Bump the version,
write the migration in `TryFromJson`, ship.

### 11.2 `Application.persistentDataPath` for user data

Never write to `Application.dataPath` (the Assets folder — read-only
in builds) or `Application.streamingAssetsPath` (also read-only on
many platforms). User saves go to `persistentDataPath/blueprints/`.

### 11.3 Atomic writes

For data the player would cry over losing (their robot collection):

```csharp
File.WriteAllText(path + ".tmp", json);
File.Replace(path + ".tmp", path, path + ".bak");
```

Crash mid-write → `.tmp` is garbage but the original survives.
We don't do this yet — flag it 🔬 for when the library has > 5
saves typical.

### 11.4 `JsonUtility` vs `Newtonsoft.Json`

`JsonUtility` ships with Unity, has no GC overhead beyond the result
string, but **doesn't support polymorphism, dictionaries, or
properties** — only public/`[SerializeField]` fields on
`[Serializable]` types. We use it. If we later need polymorphic
shapes (e.g. multiple module types in one entries array), switch to
Newtonsoft (`com.unity.nuget.newtonsoft-json`).

---

## 12. Multiplayer-readiness rules (enforced now)

These rules cost ~nothing today and save weeks later.

1. **All gameplay state on serializable components.** Health on a
   field, not on `transform.localScale`. Block damage on
   `BlockBehaviour._hp`, not on the material color (color is *derived*
   from hp).
2. **Inputs are commands, not state.** `PlayerController` reads input
   and emits structured commands (`DriveIntent { throttle, steer,
   brake }`). The drive subsystem consumes them. In MP, the same
   commands ship over the wire; the server applies them.
3. **No `Time.deltaTime` in physics code.** Use `Time.fixedDeltaTime`
   in `FixedUpdate`. PhysX is not deterministic across machines, but
   *bug compatibility across machines* is achievable if you're at
   least using the same timestep.
4. **No `Random.value` for gameplay rolls.** Use `System.Random` with
   a seeded instance you can replay (later, for replays / lockstep
   variants). Cosmetic randomness (particle jitter) is fine to use
   `Random.value`.
5. **No `GameObject.Find` in `Update`.** Cache on `Awake` or use
   `FindFirstObjectByType` once at scene-load. (Unity 6 obsoletes
   `FindObjectOfType` — see the lingering call in [DevHud.cs](Assets/_Project/Scripts/UI/DevHud.cs)
   that should be migrated 🔬.)
6. **Authoritative ID strings, not object references, across system
   boundaries.** `BlockIds.Cpu`, weapon-id strings, etc. The same
   thing that makes saves portable makes them netcode-portable.

When we add Netcode for GameObjects:

- The chassis Rigidbody becomes `NetworkObject` + `NetworkTransform`
  on the *root* (interpolated for remote, predicted for local).
- Per-block damage replicates as a `NetworkList<BlockDamage>` with
  delta updates.
- Inputs ship via `RPC` or a `NetworkVariable<InputState>` updated
  at fixed-tick rate (30Hz client-to-server is the sane default).
- Physics simulation runs on the host only; clients run *kinematic*
  rigidbodies and interpolate from the host's snapshots.

---

## 13. Editor tooling & scene scaffolders

### 13.1 Scaffolders must be idempotent

[GameplayScaffolder.cs](Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs)
and [EnvironmentBuilder.cs](Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
both follow the rule: **running them twice produces the same scene
as running them once.** No duplicates, no orphaned objects.

The pattern is `ResetEnvRoot()` → destroy the previous parent →
build fresh. When you add a new piece of decor, parent it under
`Environment` so the next reset cleans it up.

### 13.2 One menu item per coarse action, not per fiddle

`Robogame > Scaffold > Gameplay > Build All Pass A` is correct.
`Robogame > Scaffold > Gameplay > Just the Wheels Please` is not —
that's what running Build All again does for free.

Exception: per-asset live-edit tools like the
[HillsSettingsEditor](Assets/_Project/Scripts/Tools/Editor/HillsSettingsEditor.cs)
"Rebake" button — those edit one asset and don't justify a full
scene rebuild.

### 13.3 Editor-only code must live under an `Editor/` folder

…with an `Editor`-platform `.asmdef`. Otherwise it ships in builds
and breaks the player. [Robogame.Tools.Editor.asmdef](Assets/_Project/Scripts/Tools/Editor/Robogame.Tools.Editor.asmdef)
already has `includePlatforms: ["Editor"]` — don't change that.

---

## 14. Profiling, debugging, and testing

### 14.1 Profile before optimizing — but profile in a build

The Editor profiler over-reports CPU cost (the editor itself ticks).
For real numbers: `Build And Run` a Development Build with the
profiler attached. **5-10× difference** in some metrics is normal.

### 14.2 The four panels you actually use

- **CPU Usage** — find the spike. Right-click → "Show in Hierarchy".
- **Memory** (the package, not the basic tab) — find the GC alloc.
- **Frame Debugger** — count draw calls, see batching grouping.
- **Physics** — count contacts, see solver iterations.

### 14.3 Editor tests live next to the code they test

`Robogame.Block.Tests.asmdef` references `Robogame.Block`. Tests for
pure-data systems (BlueprintSerializer, BlockGrid math, slug
generation) are *cheap* and *high-value* — they catch refactor bugs.

🔬 We don't have a test asmdef yet. Adding one is on the roadmap
the moment a system gets > 200 lines of pure logic worth covering.
`BlueprintSerializer` is now that system.

### 14.4 The "F8 trick"

Bind a debug key in [DevHud.cs](Assets/_Project/Scripts/UI/DevHud.cs)
to dump the current chassis state — block count, mass, CoM, current
linear/angular velocity, last damage event. When something feels
wrong, a one-keypress dump beats a debugger session.

---

## 15. Common pitfalls specific to Robocraft-likes

These are the ones we *will* hit. Document them now so we recognise
them when they show up.

1. **The "soup" chassis.** If every block has its own Rigidbody and
   they're connected with FixedJoints, the solver oscillates,
   chassis wobble, builds disassemble themselves. Fix: one Rigidbody,
   compound colliders. (§ 3.1)
2. **Mass-recompute thrash.** Calling `RecomputeFromGrid` on every
   damage tick during a sustained-fire weapon = 60 mass tensor
   recomputes per second per robot. Batch. (§ 3.4)
3. **The "death by 10,000 cubes" draw call cliff.** SRP Batcher
   hides this until you have ~16 robots × 200 blocks visible
   simultaneously, then draws spike past 4,000. Solution: chunk
   meshing (§ 3.2 tier 2) — but not before you measure.
4. **Collider count physics cost.** PhysX's contact-solver work is
   superlinear in active contacts. 10 robots × 200 box colliders
   each, all touching the ground, generates a lot of contacts even
   if no robot is moving. Mitigation: turn off colliders on
   *interior* blocks at chassis-build time (only the outer hull
   needs to collide).
5. **Wheel snap-spin on hit.** When a wheel block dies and detaches,
   if you forget to remove its drive subsystem reference, the
   surviving subsystem applies torque to a now-detached body and
   rockets the debris into orbit. Hilarious once, embarrassing the
   second time. Always have drive subsystems pull a `null`-safe
   list from the live grid.
6. **CPU destruction grace period.** Robocraft had a "you have ~2s
   after CPU dies before the bot becomes debris". Without a grace
   period the chassis disintegrates the *frame* the killing shot
   lands, which feels bad. Add a small fade.
7. **Hover-jet feedback loop.** A hover thruster that applies upward
   force proportional to (target_height − current_height) without
   *velocity* damping oscillates wildly. Always include a `-velocity
   * damping` term.
8. **Aero surfaces and high speed.** A wing applying lift = ½ρv²·S·CL
   blows up at v > 200 m/s. Clamp the surface velocity used in the
   formula, or you'll teleport planes through the floor.
9. **"Build the robot in the garage, drive a different robot in the
   arena"**, a.k.a. forgetting to clone the blueprint. Always go
   through `GameStateController.SetCurrentBlueprint` (which clones
   for you).
10. **Net-bandwidth blowup from per-block damage.** A 200-block
    chassis taking sustained fire = up to 200 small messages per
    second per robot. Batch into per-tick deltas, send only changes,
    and quantise HP to a byte where possible.

---

## 16. Performance budgets (targets, not law)

> **Performance discipline scales with physics complexity.** Each new
> physics-driven block we add (rotors, hover lifts, multi-rope rigs,
> jointed limbs, future destruction debris) compounds against the
> active-rigidbody and contact-solver budgets below. As more of these
> ship, **performance checking becomes proportionally more important**
> — not optional. Before merging any block that adds Rigidbodies,
> Joints, or per-FixedUpdate force application, take a Profiler
> capture with at least one populated chassis using the new feature
> and confirm:
> 1. **Active Rigidbodies** stays under the alarm threshold (see
>    table below) for a *normal* loadout, not just an empty test
>    chassis.
> 2. **PhysX simulate** per FixedUpdate doesn't spike past 2 ms.
> 3. **GC allocations / frame** stays at 0 B during steady-state
>    play — no per-tick `new` in the new block's hot path.
>
> If the feature can be "visual-only" by default (e.g. a rotor with
> 0 ropes), that path **must** add zero Rigidbodies / colliders —
> exposed via a Tweakable so a builder can opt-in to the heavier
> physics version per chassis. See [RotorBlock](../Assets/_Project/Scripts/Movement/RotorBlock.cs)
> for the established pattern (one scene-root kinematic hub +
> jointed chain, opt-in via `Tweakables.RotorRopeCount`).
>
> See [PHYSICS_PLAN.md](PHYSICS_PLAN.md) for the rope-tech migration
> plan, the stress-test workflow (settings → Stress → "Spawn Rotor
> Tower"), and the rule that gameplay-observable behaviour must
> never depend on a Tweakable.

Numbers we aim at for a desktop PC of ~2020 vintage (GTX 1660, 6-core
CPU). Treat these as **alarms** — going over means stop and think,
not stop and panic.

| Metric | Target | Cliff |
|---|---|---|
| Frame time @ 1080p / 60fps | 16.6 ms | 33 ms |
| CPU main-thread | < 8 ms | 12 ms |
| Render thread | < 5 ms | 10 ms |
| Draw calls (frame) | < 1,500 | 4,000 |
| SetPass calls | < 200 | 500 |
| Triangles (frame) | < 2.0 M | 5.0 M |
| Active Rigidbodies | < 64 | 256 |
| Active colliders (dynamic) | < 4,000 | 16,000 |
| GC allocations / frame | 0 B in steady state | any allocs in `Update` |
| PhysX simulate (per FixedUpdate) | < 2 ms | 6 ms |
| Memory (managed heap) | < 256 MB | 1 GB |

For a 16-player MP arena (later target):

| Metric | Target | Cliff |
|---|---|---|
| Network bandwidth (per-client) | < 64 kbps | 256 kbps |
| Tickrate (server) | 30 Hz | 60 Hz max, 20 Hz floor |
| Lag tolerance (perceived) | < 100 ms with prediction | 250 ms |

---

## Open research items 🔬

Captured here so they don't get lost:

- **Atomic blueprint writes** (§ 11.3): adopt `.tmp` + `File.Replace`
  pattern when the user blueprint count justifies it.
- **Editor test asmdef** (§ 14.3): `Robogame.Block.Tests` covering
  the serializer + (eventually) connectivity flood-fill.
- **`FindObjectOfType` migration** (§ 12.5): one straggler in
  [DevHud.cs](Assets/_Project/Scripts/UI/DevHud.cs#L125) →
  `FindFirstObjectByType<Robot>()`.
- **Chunk meshing decision** (§ 3.2): defer until SRP-Batcher
  becomes draw-call-bound; revisit at the 16-robot arena milestone.
- **Awaitable migration** for coroutines (Unity 6 native) — slow
  rolling, not urgent.
- **Authoritative tickrate**: 20 / 30 / 60 Hz tradeoffs for the
  multiplayer prototype.

---

*This file is a living document. When a rule turns out to be wrong,
update it here in the same PR that breaks the rule, and link it in
[CHANGES.md](CHANGES.md).*
