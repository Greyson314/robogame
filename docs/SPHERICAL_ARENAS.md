# 🪐 Spherical Arenas Plan

> Long-form design document for moving Robogame matches off flat battlefields and onto **small, hand-authored planets** — drive over the horizon, shoot bots on the far side, the world is actually a sphere with real spherical gravity.
>
> **Audience:** future me, future contributors, future AI agents working on this codebase.
>
> **Reference vibe:** *Outer Wilds*, but a class larger — bodies in the **1.5–2 km radius** band (see [§9](#9-authoring-a-planet) for the math), real Newton-style local gravity, walk all the way around them, no camera tricks, no procedural generation, no quantum/time tricks. Just: the arena is a planet. Outer Wilds itself uses ~200–600 m bodies, but at our peak vehicle speeds those radii produce uncomfortable camera-up rotation rates; we trade a smaller "the entire planet" feel for a much calmer optic flow.
>
> **Bias:** boring physics. We are not making a planet renderer or a procedural terrain engine. We are making "Robocraft, but the floor curves and your `up` vector is a function of where you are."

---

## Table of Contents

- [1. Goals & Non-Goals](#1-goals--non-goals)
- [2. The "Two Layers" Distinction (And Why Curved World Is Out)](#2-the-two-layers-distinction-and-why-curved-world-is-out)
- [3. Gravity Model](#3-gravity-model)
- [4. Orientation Model — `up` Is a Function of Position](#4-orientation-model--up-is-a-function-of-position)
- [5. Existing Code: What Survives, What Adapts](#5-existing-code-what-survives-what-adapts)
- [6. New Components](#6-new-components)
- [7. Combat on a Sphere](#7-combat-on-a-sphere)
- [8. Cameras on a Sphere](#8-cameras-on-a-sphere)
- [9. Authoring a Planet](#9-authoring-a-planet)
- [10. Water on a Planet](#10-water-on-a-planet)
- [11. Skybox & Lighting](#11-skybox--lighting)
- [12. Edge Cases & Sharp Corners](#12-edge-cases--sharp-corners)
- [13. Netcode Implications](#13-netcode-implications)
- [14. Phased Rollout](#14-phased-rollout)
- [15. Risks & Open Questions](#15-risks--open-questions)
- [16. References](#16-references)

---

## 1. Goals & Non-Goals

### Goals

1. **Matches happen on a fixed, hand-authored planet.** A specific sphere — call it `Planet_Tundra` — sits at the world origin. Robots drive on its surface. The horizon curves because the floor *is* curved, not because a shader is bending pixels.
2. **Real local gravity.** Each robot's `up` vector points away from the planet's center. Drive far enough and you'll loop back to where you started. Drop something off a cliff and it falls toward the planet, not in `-Y`.
3. **Combat works in spherical space.** Aim, fire, ballistic arc, hit detection — all derive their notion of "down" from the local gravity vector, not from `Vector3.up`.
4. **Existing block-based robot architecture is preserved.** A robot is still a `Robot` + `BlockGrid` + `Rigidbody`. Drive subsystems still apply forces. Projectiles still raycast. The "what" doesn't change — just the "what's down."
5. **Singleplayer-first, multiplayer-ready.** Same rule as netcode: the server simulates spherical physics; clients interpolate. No client-side curvature trickery. (See [§13](#13-netcode-implications).)

### Non-Goals

- ❌ **Procedural planets.** Hand-authored mesh + colliders only. Each planet is just a Unity prefab.
- ❌ **Multiple planets you can fly between in one match.** A scene may *contain* a moon for skybox flavor (see [§11](#11-skybox--lighting)) but matches happen on one body. Crossing planet SOIs (sphere-of-influence) is a Phase D+ research question, not a v1 feature.
- ❌ **Camera-trick curvature.** No Curved World shader, no "fake the horizon." If a player can see it, the geometry is genuinely there.
- ❌ **Realistic orbital mechanics.** No `1/r²` gravity. Constant magnitude inside the planet's SOI; abrupt zero outside. We are simulating "you are on a thing with a center" not Kepler's laws.
- ❌ **Atmospheric flight transitions.** No re-entry heat, no thinning air. If we add flight (jets), they just work, with the same gravity vector pulling them back down.
- ❌ **Heightmap-on-sphere terrain or tessellated geometry LOD.** A planet is one mesh. We accept that the surface is "blocky" if we model big features (mountains as separate meshes parented to the planet).
- ❌ **Time loops, quantum gravity, or any other Outer Wilds *narrative* tricks.** This is exclusively the *spatial* idea: the world is a ball.

### What we explicitly cribbed from Outer Wilds (and what we didn't)

| Borrowed | Skipped |
|---|---|
| Small bodies (~1.5–2 km radius — small enough that the horizon visibly curves and you can lap the planet in 12–15 minutes at ground speed) | Multiple bodies all gravitationally interacting |
| Constant-magnitude local gravity per body | Realistic Newtonian `1/r²` |
| Body-fixed reference frames; no rotating planets at v1 | Rotating bodies (Coriolis, centrifugal — interesting, deferred) |
| Hand-authored geometry, not procgen | Atmospheric / spaceflight transitions |
| Real physics — what you see is the simulation | Anything narrative, mechanical, or quantum-themed |

---

## 2. The "Two Layers" Distinction (And Why Curved World Is Out)

Most "planet" effects in Unity games are one of two things, and they do **not** compose:

| Layer | What it is | What we want |
|---|---|---|
| **Visual curvature** | A vertex shader bends rendered geometry around a focal point (Curved World, *Animal Crossing*-style). The simulation is flat; the *image* is curved. Camera-anchored — no two players see the same curve. | ❌ Not what we're building. |
| **Spatial curvature** | The geometry actually *is* a sphere. Colliders, raycasts, gravity, locomotion all operate on the real curved surface. Two players standing on opposite sides genuinely have opposite `up` vectors. | ✅ This is the design. |

The Curved World shader cannot help with Layer 2: it has no notion of colliders, gravity, or shared world-space. Two players on the same Curved World level are still on a flat floor; the visual curvature is anchored to *their* camera. We'd be lying to one of them.

> **Decision:** Curved World is not part of this plan. If we ever want a stylistic horizon-curl for a *non-spherical* arena (e.g. an endless-runner training mode), revisit. Otherwise we don't even buy it.

---

## 3. Gravity Model

### Constant magnitude inside the SOI, abrupt zero outside

```
gravityDir = (planet.center - robot.position).normalized
gravityMag = 9.81 m/s²            // tunable per planet
F = robot.mass * gravityMag * gravityDir          if |robot.position - planet.center| < SOI_radius
F = 0                                              otherwise
```

Why constant magnitude (not `1/r²`):

- Predictable jump heights, bullet drop, fall damage.
- No "low-orbit drift" weirdness when a robot launches off a hill.
- Outer Wilds and *Astroneer* both use constant-magnitude local gravity for exactly these gameplay reasons. The realism we lose is invisible at the scales we care about (200–600 m radius).

Why abrupt zero outside SOI:

- v1 has one body per match, and the SOI is "everywhere a player can plausibly reach." Players can't escape it at gameplay velocities.
- If they *do* (a glitch, a launch bug), we don't want them in some half-gravity limbo. They go zero-G, fall is replaced by drift, they get a "Out of Bounds — returning to surface" warning, and after 5 seconds we teleport-respawn them.

### Implementation surface

A single new Core type:

```csharp
namespace Robogame.Core
{
    public interface IGravitySource
    {
        /// <summary>
        /// Returns the gravity vector (m/s²) acting on a body at <paramref name="worldPosition"/>.
        /// Zero if the position is outside this source's sphere of influence.
        /// </summary>
        Vector3 GetGravityAt(Vector3 worldPosition);

        /// <summary>True if the position is within this source's SOI.</summary>
        bool ContainsPoint(Vector3 worldPosition);
    }
}
```

And a registry:

```csharp
public static class GravityField
{
    public static Vector3 SampleAt(Vector3 worldPosition);  // sums all registered IGravitySources
    public static IGravitySource DominantAt(Vector3 worldPosition);  // for "what planet am I on?"
}
```

Anything that needs a "down" vector — `WheelBlock`, `GroundDriveSubsystem`, `Projectile`, `FollowCamera`, `BuoyancyController` — calls `GravityField.SampleAt(transform.position)` instead of using `Vector3.down` / `Vector3.up`.

Singleplayer scenes with no `IGravitySource` registered get a default flat-world fallback (`Vector3.down * 9.81`). This means **adding `GravityField` is non-breaking** — flat arenas keep working as-is.

### Why a registry, not just a singleton

Because Phase D might want a moon as an inert physics body in the same scene as the main planet (visual, not gameplay-meaningful). Even if v1 has only one source, the registry costs nothing to author for and saves a refactor later. Same shape as Unity's `WindZone` system.

---

## 4. Orientation Model — `up` Is a Function of Position

A robot on a flat arena has a stable `up = Vector3.up`. A robot on a planet has `up = -gravityDir(position)` — which changes as it drives. Three things must follow.

### 4.1 The robot self-rights to local up

`GroundDriveSubsystem` already has self-righting torque pointing toward `Vector3.up`. Replace with the local up:

```csharp
Vector3 localUp = -GravityField.SampleAt(_rb.position).normalized;
Vector3 axis = Vector3.Cross(transform.up, localUp);
float angle = Mathf.Asin(Mathf.Clamp(axis.magnitude, -1f, 1f));
_rb.AddTorque(axis.normalized * (angle * UprightStrength), ForceMode.Acceleration);
```

This is a one-line change at one call site (currently [GroundDriveSubsystem.cs#L156](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs#L156)). Same shape, just substituting the up reference.

### 4.2 Wheels raycast toward local down

[WheelBlock.cs#L158](Assets/_Project/Scripts/Movement/WheelBlock.cs#L158) currently raycasts `Vector3.down` for ground. That becomes `gravityDir.normalized`. Suspension force was `Vector3.up * force`; that becomes `(-gravityDir).normalized * force`. Two trivial substitutions.

The win here, and the reason this whole feature is *tractable*: **we never adopted Unity's `WheelCollider`**. We rolled our own raycast suspension precisely so we'd own the math. `WheelCollider` is hard-wired to `+Y` world-up and is the single biggest reason most "planet" games end up writing custom locomotion. We already did. ✅

### 4.3 Yaw axis is local up, not world up

Steering torque, yaw extraction, and lateral-grip math in `GroundDriveSubsystem` all use `Vector3.up` in a few places (e.g. `Vector3 torque = Vector3.up * (control.Move.x * TurnRate)` at [L143](Assets/_Project/Scripts/Movement/GroundDriveSubsystem.cs#L143)). Each becomes `transform.up` or `localUp`. Concentrate this into a `localUp` cached at the start of `ApplyDrive` — one line up top, ~6 substitutions in the body.

### 4.4 Jump impulse points along local up

`_rb.AddForce(Vector3.up * JumpImpulse, ForceMode.Impulse)` → `_rb.AddForce(transform.up * JumpImpulse, ForceMode.Impulse)`. Trivial.

### Why I'm spelling out specific call sites

Because the cost of this feature is **dominated by finding all the up-vector assumptions**, not by any single one of them. A grep for `Vector3.up`, `Vector3.down`, `Physics.gravity` across the project surfaces ~20 sites, and the design rule is: **every one of them either becomes a `GravityField` query, or it becomes `transform.up` (already a local-frame value), or it stays world-up because it's UI/menu code**. There are no mysterious "engine just does it" places once `WheelCollider` is off the table.

---

## 5. Existing Code: What Survives, What Adapts

Audit, with verdicts.

### ✅ Survives unchanged

- **`BlockGrid`** — coordinate math is in the robot's local frame. The grid doesn't care that the robot is on a sphere.
- **`Robot`** — aggregate stats, destruction, structural integrity. Frame-agnostic.
- **`BlockBehaviour`** — health, damage events. Frame-agnostic.
- **`ChassisFactory` / `BlueprintSerializer`** — building a robot from a blueprint is local-frame work.
- **`PlayerInputHandler`** — pure input abstraction, no spatial assumptions.
- **`PlayerController`** — wires input to drive. Spatial-agnostic.
- **`RobotDrive`** — orchestrates `IDriveSubsystem`s. Spatial-agnostic.
- **`HoverDrive`** / `JetDrive` (when they exist) — pleasantly works as-is, since "hover above the surface" already wants a `gravityDir` query rather than `-Y`.

### ⚠️ Adapts (the local-up substitutions)

- **`GroundDriveSubsystem.cs`** — the bulk of the change. Cache `localUp` at the top of `ApplyDrive`, substitute throughout. ~6 call sites.
- **`WheelBlock.cs`** — ground raycast direction + suspension force direction. 2 call sites.
- **`Projectile.cs`** — gravity becomes `Vector3` not `float`. Re-sample `GravityField` per `FixedUpdate` step (cheap; one dot/sqrt). Velocity update becomes `_velocity += gravityVec * dt` instead of `_velocity += Vector3.down * (_gravity * dt)`. [Projectile.cs#L106](Assets/_Project/Scripts/Combat/Projectile.cs#L106)
- **`ProjectileGun.cs`** — when launching a projectile, pass the gravity vector at the muzzle position (or pass `null` and let the projectile sample). Tiny.
- **`WeaponBlock.cs`** / **`WeaponMount.cs`** — `Quaternion.LookRotation(dir, Vector3.up)` becomes `Quaternion.LookRotation(dir, localUp)` so turret yaw aligns with the local horizon. 3 call sites.
- **`PlaneControlSubsystem.cs`** — bank signal currently uses `Vector3.up`; becomes `localUp`. The fact that this *just works* on a sphere is a reward for keeping aircraft physics frame-relative from the start. 1 call site.
- **`BuoyancyController.cs`** — buoyancy force direction changes from `Vector3.up` to `localUp`. Plus the water-plane assumption needs to become a water-shell assumption (see [§10](#10-water-on-a-planet)).
- **`FollowCamera.cs`** / **`OrbitCamera.cs`** — `lookAt = target.position + Vector3.up * _height` becomes `+ localUp * _height`. Up-axis for the camera's roll math becomes `localUp`. See [§8](#8-cameras-on-a-sphere) for the full camera story.
- **`AimReticle.cs`** — projection of aim ray. Already operates from camera; mostly survives because camera frame is already local. Verify.
- **`ArenaBuilder.cs`** (editor tool) — currently spawns scaffolds that assume flat ground. Add a sibling `PlanetBuilder.cs` that scaffolds a planet prefab + spawn arrangement. The flat builder stays for non-spherical arenas.

### ❌ Does NOT survive unchanged

- **Flat-plane water (`WaterSurface`, `WaterVolume`, `WaterMeshAnimator`)** — these all assume `y = constant` is the water surface. Spherical arenas need a sphere-shell water (or no water at v1). See [§10](#10-water-on-a-planet).
- **`HillsGround.cs`** terrain mesh tool — assumes a heightmap on a flat plane. Useless for a sphere; planet authoring uses a different path (see [§9](#9-authoring-a-planet)).
- **Skybox sun-direction sync from `EnvironmentBuilder`** — still works, but visually weird if the player walks to the night side of a small planet and the sun stays "overhead" because the camera's local frame rotated. We may want a "solar sky" mode that derives sun visibility from the player's current position relative to the planet center. See [§11](#11-skybox--lighting).

---

## 6. New Components

### `Robogame.Core` additions

```csharp
public interface IGravitySource
{
    Vector3 GetGravityAt(Vector3 worldPosition);
    bool ContainsPoint(Vector3 worldPosition);
}

public static class GravityField
{
    public static void Register(IGravitySource source);
    public static void Unregister(IGravitySource source);
    public static Vector3 SampleAt(Vector3 worldPosition);          // Σ over registered sources, plus default fallback if empty
    public static IGravitySource DominantAt(Vector3 worldPosition); // for "which planet am I on?" UI / respawn
    public static event Action<IGravitySource> SourceAdded;
    public static event Action<IGravitySource> SourceRemoved;
}
```

### `Robogame.Gameplay` additions

```csharp
[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]   // the actual ground
public sealed class Planet : MonoBehaviour, IGravitySource
{
    [SerializeField, Min(1f)]   private float _radius = 1500f;       // see §9 for the comfort-vs-curvature derivation
    [SerializeField, Min(0.1f)] private float _surfaceGravity = 9.81f;
    [SerializeField, Min(1f)]   private float _soiPadding = 50f;       // extra slack above surface where gravity still applies

    public Vector3 GetGravityAt(Vector3 worldPosition)
    {
        Vector3 toCenter = transform.position - worldPosition;
        if (!ContainsPoint(worldPosition)) return Vector3.zero;
        return toCenter.normalized * _surfaceGravity;
    }

    public bool ContainsPoint(Vector3 worldPosition)
        => (worldPosition - transform.position).sqrMagnitude < (_radius + _soiPadding) * (_radius + _soiPadding);

    private void OnEnable()  => GravityField.Register(this);
    private void OnDisable() => GravityField.Unregister(this);
}
```

```csharp
/// <summary>
/// Samples GravityField every FixedUpdate, sets the rigidbody's
/// gravity vector accordingly, and (optionally) torques the body
/// toward local-up. Replaces the implicit Physics.gravity on a robot.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class GravityBody : MonoBehaviour
{
    [SerializeField] private bool _alignToGravity = true;
    [SerializeField, Min(0f)] private float _alignStrength = 6f;
    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false; // the whole point — we own the gravity
    }

    private void FixedUpdate()
    {
        Vector3 g = GravityField.SampleAt(_rb.position);
        _rb.AddForce(g, ForceMode.Acceleration);
        if (_alignToGravity) ApplyAlignmentTorque(-g.normalized);
    }
    /* ... torque math identical to GroundDriveSubsystem self-right ... */
}
```

`GravityBody` is added to:
- Every robot (in addition to existing components — `GroundDriveSubsystem` keeps its self-righting; the two are compatible because both apply torque acceleration toward the same target).
- Every projectile? **No** — projectiles handle gravity inside their own `FixedUpdate` already; adding `GravityBody` would conflict. They sample `GravityField` directly.
- Detached debris (so chunks fall toward the planet, not in `-Y`).

### `Robogame.Tools.Editor` additions

```
Assets/_Project/Scripts/Tools/Editor/
└── PlanetBuilder.cs       // menu: Robogame → Build → Planet Arena
```

Spawns a `Planet` GameObject at world origin with:
- Sphere mesh (configurable subdivisions, 4–7 by default)
- `SphereCollider` matching the visual radius
- Ring of spawn points around the equator (or hemispherical pattern for >2 players)
- Optional surface decoration prefabs (rocks, structures) parented to the planet so they orbit with it if we ever animate planet rotation
- Materials sourced from existing `_Project/Art/Materials/` palette so visual cohesion is automatic

---

## 7. Combat on a Sphere

### Projectiles

Already easy. `Projectile.cs` is a swept raycast that integrates its own velocity. Two changes:

1. **Gravity is a vector field, not a scalar.** Replace the `float _gravity` parameter with a re-sampled `Vector3` per `FixedUpdate` step. Physical accuracy improves slightly for long shots (gravity direction changes mid-flight as the projectile rounds the curve toward another part of the planet).

2. **Sweep raycast layer mask still works.** The planet is a `SphereCollider` on the ground layer; bullets stop on it like any other ground collider. No special handling.

Code change is < 10 lines, isolated to [Projectile.cs](Assets/_Project/Scripts/Combat/Projectile.cs) and the call to `Launch` from [ProjectileGun.cs](Assets/_Project/Scripts/Combat/ProjectileGun.cs).

### Aiming

The crosshair is a screen-space raycast from the camera. The camera's local frame is already aligned to `localUp` (see [§8](#8-cameras-on-a-sphere)), so aim direction *just works* — `camera.ScreenPointToRay(Input.mousePosition).direction` is already in the right frame.

The only thing to verify: the raycast distance is long enough to hit a target across the planet's diameter without exceeding the sweep budget. For a 1 500 m radius planet, `MaxAimDistance = 4 000 m` is comfortable.

### Hit feel

A robot driving over the horizon out of sight is a real "I shot at a body that's now occluded by the planet itself" case. This is a **feature**, not a bug — it's exactly the spatial gameplay this design unlocks. UI implication: nameplate occlusion needs to honor the planet sphere collider, not just other robots' colliders. Current nameplate code (Phase 7 of NETCODE_PLAN) already plans for line-of-sight checks; planet occlusion drops in for free.

### Splash / area damage

Splash is a sphere overlap query in world space — frame-agnostic. Survives.

---

## 8. Cameras on a Sphere

The single most "feel" sensitive subsystem.

### The problem

`FollowCamera` and `OrbitCamera` build their look-at target via `target.position + Vector3.up * _height`. On a planet this is wrong — the offset should be along *local* up, otherwise the camera floats off into space as the player drives toward the equator.

### The fix

Both cameras gain a one-line change at their look-at and roll-axis sites:

```csharp
Vector3 localUp = GravityField.DominantAt(_target.position) is { } src
    ? -src.GetGravityAt(_target.position).normalized
    : Vector3.up;   // flat-arena fallback
Vector3 lookAt = _target.position + localUp * _height;
```

And the camera's own up vector for `Quaternion.LookRotation`:

```csharp
transform.rotation = Quaternion.LookRotation(_target.position - transform.position, localUp);
```

### Smoothing the rotation

The camera's `up` vector changes continuously as the player drives. A naive per-frame `LookRotation(forward, localUp)` produces a stable result but the orbit-yaw input now needs to operate **relative to the local frame** — yawing left should mean "left from the player's perspective on this hill," not "left in world space."

Conversion: convert mouse delta into a quaternion in the local frame, apply, convert back:

```csharp
Quaternion localFrame = Quaternion.LookRotation(forward, localUp);
Quaternion yawDelta = Quaternion.AngleAxis(mouseX * sensitivity, Vector3.up);
forward = localFrame * yawDelta * Quaternion.Inverse(localFrame) * forward;
```

Pitch operates on the local right axis (`Vector3.Cross(localUp, forward)`) — same trick.

### "Camera passes through the planet" failure mode

If the camera's spring follow puts it momentarily *inside* the planet sphere (a common case driving up a hill), we need a sweep-test that pushes the camera back out along `localUp`. Mostly already handled by `OrbitCamera`'s collision logic; verify the collision sphere-cast direction is `localUp`-relative, not `Vector3.up`.

### Don't roll the camera with the body

Player preference, but consensus from Outer Wilds, Astroneer, Mario Galaxy is: the camera follows the player's *up* (yes, roll the world image as you drive around the planet), but does **not** follow the player's *roll* (don't make players seasick when their robot tilts on a bump). Decouple: camera's `up` = `GravityField` sample, not `transform.up` of the chassis.

---

## 9. Authoring a Planet

A planet is just a Unity prefab. No procedural generation, no streaming, no LOD.

### Minimum viable planet

```
Planet_Tundra.prefab
└── Planet_Tundra (GameObject)
    ├── Planet (component, IGravitySource)
    ├── SphereCollider (radius = 300)
    ├── MeshRenderer + MeshFilter (icosphere or cube-sphere mesh)
    └── Surface/
        ├── Rock_LargeA (positioned on surface, baked to planet)
        ├── Structure_Bunker (positioned on surface)
        ├── ...
```

### Mesh authoring

- **Icosphere** subdivision 5–6 gives ~10K–40K tris for a 300 m planet. Plenty for stylized art direction.
- Optional **Cube-sphere** for nicer UV unwrapping if we want to texture-paint biomes. Either works.
- Authored in Blender and imported. **Not** generated at runtime.
- The mesh doubles as the collider via a `MeshCollider` if we need ground-following terrain features. Or — simpler — keep the visual mesh and use a `SphereCollider` for physics, with separate placed mesh-collider rocks/cliffs as obstacles. Recommendation: SphereCollider for the planet itself (cheap, perfect normal everywhere), mesh colliders for hand-placed obstacles. Hybrid is industry standard.

### Surface placement helpers

Editor tooling: a `[CustomEditor(typeof(Planet))]` script that lets you click on the planet in the scene view and have the selected prefab snap to the surface, oriented along the local up. Roughly 50 LOC, saves enormous time when authoring rocks & buildings.

### Spawn points

Each planet authors `SpawnPoint` children on the surface, each one storing its planet-local up. At round start, the `ArenaController` picks N spawn points spread around the sphere (using a spherical-Voronoi distance check or just a hand-curated set), instantiates each player's robot at the spawn point, and orients the robot so its `up` matches the spawn point's local up. The robot's `GravityBody` does the rest.

### How big should planets be? (the comfort-vs-curvature math)

Naively, smaller planets feel "more planety" — the horizon is right there, you can see your own back, and lapping the world takes a minute. The catch is that **a small planet under a moving player is mostly experienced as the camera's up vector spinning**, and a fast-spinning up vector is the textbook recipe for VR-grade motion sickness even on a flat-screen monitor. So the sweet-spot question is: *how big does the planet have to be before the optic flow stops nauseating people, while still being small enough that the curvature is visible?*

Three formulas drive the trade-off. Let `r` = planet radius, `v` = player ground speed, `h` = camera eye height above the surface (~2 m for our follow camera).

- **Camera-up rotation rate** (the motion-sickness driver — the speed at which the world image rolls under the cursor as you drive):
  $$\omega = \frac{v}{r}\ \text{rad/s} = \frac{v}{r} \cdot \frac{180}{\pi}\ \text{°/s}$$
- **Horizon dip angle** (how much the horizon visibly drops below the geometric horizontal — this is what *makes* the planet read as curved):
  $$\alpha \approx \sqrt{\frac{2h}{r}}\ \text{rad}$$
- **Circumnavigation time** ("can I lap this thing in a match?"):
  $$T = \frac{2\pi r}{v}$$

VR research gives us the comfort thresholds for $\omega$ (Oculus comfort guidelines, generalized to non-VR motion):

| Camera-up rotation rate | Subjective effect |
|---|---|
| < 1 °/s | Imperceptible |
| 1–2 °/s | Comfortable; reads as "I'm moving, the world isn't" |
| 2–5 °/s | Fatiguing over a 5–10 minute match |
| > 5 °/s | Acutely uncomfortable for sensitive players |

Applying the math to our actual speed envelope. `GroundMaxSpeed` is currently 13.5 m/s (a `Tweakables` knob); peak future speed for jets is assumed ~40 m/s. Eye height `h` ≈ 2 m.

| Radius | ω at 13.5 m/s | ω at 40 m/s (peak) | Horizon dip α | Circumnavigation @ 13.5 m/s |
|---|---|---|---|---|
| 6 371 km (Earth) | 0.0001 °/s | 0.0004 °/s | 0.045° | 12.3 days |
| 5 000 m | 0.15 °/s | 0.46 °/s | 1.62° | 38 min |
| **2 000 m** | **0.39 °/s** | **1.15 °/s** | **2.56°** | **15.5 min** |
| **1 500 m** | **0.52 °/s** | **1.53 °/s** | **2.96°** | **11.6 min** |
| 1 000 m | 0.77 °/s | 2.29 °/s | 3.62° | 7.8 min |
| 500 m | 1.55 °/s | 4.58 °/s | 5.13° | 3.9 min |
| 300 m | 2.58 °/s | 7.64 °/s | 6.62° | 2.3 min |
| 100 m | 7.73 °/s | 22.9 °/s | 11.5° | 47 s |

**The sweet spot is the band where the horizon dip is comfortably visible (≥ ~2°) AND peak-speed rotation rate stays below the fatigue threshold (≤ 2 °/s):**

- Lower bound (peak comfort): $\omega_\text{peak} \le 2\ \text{°/s} \Rightarrow r \ge \frac{40}{2 \cdot \pi/180} \approx 1\,143\ \text{m}$
- Upper bound (curvature still readable): $\alpha \ge 2° \Rightarrow r \le \frac{2h}{(2 \cdot \pi/180)^2} \approx 3\,300\ \text{m}$

**Intersection: 1 200 – 3 300 m.** Pick the lower half of that band so curvature still feels strong:

> **Default v1: 1 500 – 2 000 m.** Per-planet override.
>
> At 1 500 m: ground-speed camera-up rotation is **~0.5 °/s** (imperceptible), peak-speed rotation is **~1.5 °/s** (still below the comfort line), horizon dip is **~3°** (clearly curved), circumnavigation is **~12 minutes** (one full lap = roughly one match length).
>
> At 2 000 m: rotation drops to ~0.4 °/s ground / ~1.1 °/s peak, dip ~2.6°, lap ~16 min — the same shape, dialled toward calmer cameras.

**Orbital velocity sanity check** ($v_\text{orbit} = \sqrt{g \cdot r}$): 121 m/s at 1 500 m, 140 m/s at 2 000 m — a ~3× safety margin over peak vehicle speed, leaving headroom for jets without players accidentally launching into orbit. Compared to 300 m's 54 m/s orbital velocity (only 1.4× over peak), 1 500 m+ is the first radius where intentional aerial play stays bounded.

**Side benefit:** the [§12](#12-edge-cases--sharp-corners) `Cosmetic.LockCameraRoll` setting becomes optional rather than load-bearing at 1 500 m+. At 300 m it was a real accessibility need; at 2 000 m most players don't notice the camera roll at all.

*If we ever ship a "micro" arena variant for novelty (300 m, 1 minute lap), we ship it with the camera-roll smoothing forced on and a settings-screen warning. Don't ship one as the default.*

---

## 10. Water on a Planet

### Option A — Skip water at v1

Easiest. Planets are cratered rock or icy. Water is a Phase D problem. ✅ Recommended.

### Option B — Sphere-shell water

When we want water:

- Water surface = a slightly larger sphere (`waterRadius = planetRadius + offset`) with a translucent shader.
- `BuoyancyController` checks `(robot.position - planet.center).magnitude < waterRadius` to decide submerged-ness, then applies buoyancy along `localUp`.
- Water *animates* as a normal-mapped scrolling shell. No mesh deformation; the curvature would fight any tile-based wave we'd want to author.
- Fundamentally different from `WaterSurface.cs` / `WaterMeshAnimator.cs` which assume a flat plane. The flat versions stay for non-spherical arenas; a new `SphericalWaterSurface` handles the planet case.

### Option C — Polar caps / lakes only

Hand-authored "water" is just a flat sphere-cap mesh placed at the bottom of a crater, oriented along its local up. Acts like water for buoyancy purposes (a small invisible volume) but is rendered as flat at its location. Cheaper than a full water shell and looks fine because the cap is small relative to the planet.

**Recommendation:** ship v1 with no water (Option A). Add Option C in Phase D if a particular planet design wants it. Option B is real engineering and we don't need it.

---

## 11. Skybox & Lighting

### The skybox is fine

[`SkyboxBuilder`](Assets/_Project/Scripts/Tools/Editor/SkyboxBuilder.cs)'s Polyverse-driven sky is a distance-rendered cubemap-equivalent — it doesn't care about world position. Stars, sun halo, gradient, drifting clouds: all work unchanged.

### Sun direction syncing

Currently `EnvironmentBuilder` syncs the scene directional light to the skybox sun. Still works, but visually:

- On a small (1 500 m) planet, walking to the antipode means the sun is now directly *underneath* the player. The directional light shines up from the floor. Looks weird.
- Two fixes:
  1. **Lock sun direction in world space** (current behavior). Day/night side of the planet really exists. Players walk into shadow as they round the planet. ✅ This is the *correct* behavior and we should embrace it — it's a great visual and a navigational cue.
  2. Camera-relative sun (sun always above camera). Cheap, looks consistent, kills the "I'm on a planet" feeling. ❌ Don't.

### Ambient & fog

Distance fog reads world position. On a 1 500 m planet, fog distance can be set to ~planet diameter (≈ 3 km) so the far side of the planet is hazy — sells the "small world" feel. Cheap visual win.

### Optional: a moon

For visual flavor only — a non-gameplay sphere parented to the scene root, 5–10 km away, scaled to look fist-sized in the sky, slowly orbiting. Not an `IGravitySource`. Pure cinematics. Easy to add per-planet in the prefab.

---

## 12. Edge Cases & Sharp Corners

### "Robot drives too fast for the gravity to keep it stuck"

If `forward speed² > gravity × radius` (orbital velocity), the robot launches off the surface. For a 1 500 m planet at 9.81 m/s² that's **~121 m/s**; for a 2 000 m planet, **~140 m/s**. Our `GroundMaxSpeed` tweakable is currently 13.5 m/s — a ~9× safety margin — and even peak jet velocity (~40 m/s assumed) sits at a comfortable ~3× margin. Future faster vehicles can absolutely hit this — and they should, that's the gameplay payoff. Just sanity-check that `_alignStrength` on `GravityBody` isn't fighting an intentional launch. (At 300 m the orbital velocity was only ~54 m/s — one of the reasons the default was bumped, see [§9](#9-authoring-a-planet).)

### "Robot is upside-down on a steep slope"

Self-righting torque only fights tilt past ~30°. On a small planet driving up a near-vertical surface relative to your starting orientation is fine — your *new* `localUp` matches the slope, so to the robot it's flat ground. The whole point of the gravity-aligned frame.

### "Two robots on opposite sides of the planet — does physics still simulate them both?"

Yes, no changes needed. Unity's PhysX simulates everything in the scene every tick regardless of distance. Performance is fine for ≤ 16 robots on a 1 500 m planet.

### "What if a robot leaves the SOI?"

Detect via `GravityField.DominantAt(rb.position) == null`. Apply a gentle "return-to-surface" force (10% of normal gravity, pointing toward the nearest planet center) for 5 seconds. If still out of SOI, teleport-respawn at the nearest spawn point.

### "Pose interpolation across the curve for non-owning clients"

A robot's transform smoothly interpolating from `(planet_north, +Y up)` to `(planet_south, -Y up)` via straight-line lerp would have it tunnel through the planet center for one frame. Two fixes:

- Snapshot interpolation operates on **planet-local position** (a vector from planet center), not world position. Slerp the position vector around the planet, lerp the magnitude (altitude). Lerp orientation as a quaternion (already correct on the unit sphere).
- For projectiles: their flight path is short enough that linear interpolation is fine.

This is a netcode concern; flagged in [§13](#13-netcode-implications).

### "Building blueprints in the garage assumes flat ground"

Garage stays flat. The garage is a different scene with no `Planet` registered — `GravityField` falls back to the default flat gravity. Building works exactly as today. The first thing the player sees on their robot in a planet arena is "huh, I'm sideways relative to my garage," which is correct and cool.

### "Sub-360° players who get motion sickness from rolling cameras"

Settings option: `Cosmetic.LockCameraRoll` — clamps the camera's `up` to a slow-lerped version of the gravity sample, smoothing the roll over 1–2 seconds. Doesn't change physics, just camera comfort.

---

## 13. Netcode Implications

Spherical arenas are **almost** transparent to the netcode plan. The few real interactions:

### What stays the same

- Server simulates physics (including spherical gravity). Clients don't compute gravity at all for non-owning robots — they receive a pose snapshot and play it back.
- `GravityField` is deterministic given identical inputs (constant magnitude, no `1/r²` floating-point sensitivity), so server and predicting client agree on the gravity vector to ~1 ulp. CSP reconciliation isn't burdened.
- All RPCs are spatial-frame agnostic. `BlockHitBatch`, `FireCommand`, `ProjectileSpawnEvent`: nothing in any of them references "up."

### What changes

1. **Snapshot interpolation must be planet-relative for far-flung remote players.** See [§12](#12-edge-cases--sharp-corners) tunneling case. Implementation: `SnapshotInterpolator.cs` learns about a `planetCenter` from the same source and interpolates in spherical coordinates when one is registered. Falls back to linear interp for flat arenas.

2. **CSP replay must re-sample `GravityField` per replayed tick.** Otherwise a reconciliation that snaps the local rigidbody to `(pose, velocity)` and replays 100 ms of inputs will use the gravity vector from the *current* position, not the position-at-replayed-tick. Trivial — `GravityField.SampleAt(rb.position)` inside the replay loop, just like the live physics step.

3. **`SpawnRobotPayload` may want a `planetId` field.** v1 only has one planet per match, so this is just future-proofing. Cost: 4 bytes.

4. **Latency between server and client for a transitioning gravity SOI is irrelevant** because v1 has only one body and no SOI transitions in normal gameplay.

### No new buckets in the [§6 state replication taxonomy](NETCODE_PLAN.md#6-state-replication-strategy)

All planet state is **Bucket A** (configuration, sent once via prefab/scene load). Per-match, a planet is immutable.

---

## 14. Phased Rollout

Five phases. Earlier phases are cheap and useful even if we never ship the later ones.

### Phase A — `GravityField` + flat-arena no-op (1–2 days)

- Add `IGravitySource`, `GravityField`, `GravityBody` to `Robogame.Core`.
- No `Planet` yet. No code paths change visible behavior.
- Audit existing `Vector3.up` / `Vector3.down` sites; sub for `GravityField.SampleAt(position).normalized` where appropriate, with a fallback to `Vector3.up` so existing scenes work identically.
- `Robot` prefab gains `GravityBody`. Default flat gravity still pulls `-Y`, so flat arenas behave identically.

**Exit criterion:** existing flat arena plays exactly as before. No visible regressions.

### Phase B — Single planet, single player, no combat (3–4 days)

- `Planet` component + `PlanetBuilder.cs` editor tool.
- Test scene: `Planet_Test.unity` with a 300 m planet at origin and one robot.
- Drive around the planet. Verify:
  - Self-righting works at any latitude.
  - Wheels stay on ground over the horizon.
  - Camera's `up` follows local gravity smoothly.
  - Driving full circle returns to start.

**Exit criterion:** can drive a robot all the way around a 300 m planet without breaking suspension, camera, or self-righting.

### Phase C — Combat on the sphere (2–3 days)

- `Projectile` switch to `Vector3` gravity sampled per step.
- Weapon turret yaw aligned to local up.
- Aim reticle verified.
- Splash damage spot-checked.
- Two AI bots driving on the planet, shooting each other across the curve.

**Exit criterion:** firefight on opposite sides of a planet works, including bullet drop and splash, with no perceived weirdness.

### Phase D — Polish & arena variety (1–2 weeks, opportunistic)

- Author 2–3 distinct planets (icy, rocky, lava-cratered) using existing art palette.
- Spawn-point distribution spec.
- Optional moon for skybox flavor.
- Optional polar-cap water (Option C from [§10](#10-water-on-a-planet)).
- Performance pass: verify 8 robots + 50 active projectiles maintain 60 fps.

**Exit criterion:** three shippable planets that feel distinct.

### Phase E — Network the planetary arena (rolls into NETCODE Phase 1–4)

- `SnapshotInterpolator` planetary path.
- CSP gravity re-sampling during replay.
- Sync test: two networked clients on opposite sides of a 300 m planet, fire, confirm hit registration and pose interpolation.

**Exit criterion:** all the [§13](#13-netcode-implications) items working in MPPM, then over UTP-with-Relay.

### Phase F — Future research (don't commit now)

- Multiple bodies in one match.
- Rotating bodies (Coriolis effects on bullets!).
- Heightmap-perturbed planet meshes for true terrain on a sphere.
- Atmosphere transitions (a low-altitude flight ceiling above which gravity weakens).

---

## 15. Risks & Open Questions

| # | Risk | Mitigation |
|---|---|---|
| S1 | Wheel suspension misbehaves on tightly-curved surfaces (raycast finds ground, but the contact normal isn't local-up because the planet surface is mathematically planar across the wheel base only in the limit). | At the new 1 500 m default the deviation across a 0.5 m wheel base is ~3 × 10⁻⁵ rad — invisible. (At 300 m it was ~10⁻⁴ rad — also fine but only just.) For micro arenas (≤ 100 m, novelty only) revisit. |
| S2 | Self-righting torque conflicts with player intent during deliberate flips (jets, ramps, jumps). | Keep self-righting weak (it's already an *acceleration*, not a hard constraint). Tunable per drive subsystem. |
| S3 | Camera roll induces motion sickness for some players. | Settings option to lerp camera-up over 1–2 s. Already common in space games (No Man's Sky has this). |
| S4 | A robot achieves orbital velocity (rare but possible with fast jets) and the plan doesn't have stable orbits. | Either: clamp max speed inside SOI to sub-orbital, or accept the launch and respawn after 5 s out-of-SOI. v1 ships with the latter. |
| S5 | PhysX floating-point precision degrades far from world origin. With the planet at origin and players on the surface (≤ 2 km from origin at 2 000 m radius), we are deep in the safe regime — PhysX float precision concerns kick in past ~10 km. | Author all planets at world origin. Multi-body scenes (Phase F) need a proper origin-shifting scheme — flagged. |
| S6 | Authoring planets in Blender is more work than authoring flat scenes in the Unity editor. | Accept it — that's the cost of the feature. Mitigate with `PlanetBuilder` editor tooling and a 1-hour Blender process documented in `docs/PLANET_AUTHORING.md` (when the feature ships). |
| S7 | The `GravityField` registry's `Vector3.up` fallback in flat scenes silently masks a missing `Planet`. | Add an editor warning if a `GravityBody` exists in a scene with no registered `IGravitySource` and the scene name doesn't contain "Garage" or "Flat" (allowlist). Catches "I forgot to drop a Planet in this arena" bugs. |
| S8 | Existing `BuoyancyController`, `WaterSurface` etc. assume flat water; if we ship water on a planet, they need to be replaced not adapted. | Don't ship water at v1 of spherical arenas. Defer to Phase D. |

### Open design questions to revisit at each phase boundary

- **Q1**: Constant gravity magnitude vs. linear-falloff above surface? Current plan: constant. Re-evaluate if launching feels too floaty at altitude.
- **Q2**: One planet per match forever, or multiple bodies in Phase F? Multiple bodies are *cool* (Outer Wilds-like) but real engineering. Defer the question — single-body design generalizes cleanly to multi-body via the registry pattern, no architectural lock-in.
- **Q3**: Do we want rotating planets? Adds Coriolis on projectiles, centrifugal lift on robots near the equator. Realistic, gameplay-affecting, fun, ~1 week of work. Probably Phase F.
- **Q4**: Rotational alignment for the *player input* — should "forward" mean "forward in the camera's local frame" (current) or "forward along the great circle the player is currently on" (a navigation-aware reframe)? They're equivalent for short-range driving and only diverge for very tight turns at high speeds. Current plan: camera-frame forward. Revisit on feel.

---

## 16. References

### Inspirations / proof-of-concept reading

- [Outer Wilds — design retrospective (Mobius Digital GDC 2019)](https://www.youtube.com/watch?v=LbY0mBXKKT0) — small bodies, real local gravity, designed for spatial gameplay. The closest commercial analog to what we're building.
- [Astroneer — locomotion on curved terrain (Reddit AMA + GDC 2017)](https://www.gdcvault.com/play/1024292/) — wheel-on-sphere locomotion at scale, no `WheelCollider`.
- [Mario Galaxy — gravity wells & planet physics (GDC 2010)](https://www.youtube.com/watch?v=8YJ4D7Hwh-c) — the original "gameplay on small planets" reference. Different tone, same spatial idea.
- [Sebastian Lague — *Coding Adventure: Solar System*](https://www.youtube.com/watch?v=7axImc1sxa0) — explicit Unity implementation of N-body gravity + planetary alignment. Code is more than we need but the approach is exactly correct.

### Unity-specific

- [Unity Manual — Rigidbody & forces](https://docs.unity3d.com/Manual/RigidbodiesOverview.html) — `useGravity = false` + `AddForce` is the documented approach.
- [Brackeys — *Gravity in Unity* (older but the math is timeless)](https://www.youtube.com/watch?v=Z-mh21BjAEk) — covers the substitution pattern this doc is built on.

### Internal docs (this repo)

- [README.md](../README.md) — architecture principles
- [docs/NETCODE_PLAN.md](NETCODE_PLAN.md) — the multiplayer plan this design has to coexist with; see [§13](#13-netcode-implications)
- [docs/BEST_PRACTICES.md](BEST_PRACTICES.md) — coding standards
- [docs/ROBOCRAFT_REFERENCE.md](ROBOCRAFT_REFERENCE.md) — original game's design

---

*Last updated: May 2, 2026 — initial draft. Update on every phase boundary.*
