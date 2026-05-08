# Session 32 — Projectile-system unification

> Status: **shipped, ready for in-engine playtest.** All three weapon
> projectiles (SMG pellet, gravity bomb, cannon shell) now flow
> through a single custom-stepped integrator. The `Bomb`,
> `Cannonball`, and `Projectile` MonoBehaviours are gone. PhysX no
> longer touches projectile travel.

## Why this session

Session 31 shipped the cannon weapon, and successive playtest
patches (collider ignore-pairs, owner immunity windows, trigger
collider) each fixed one symptom and revealed another. The user
asked for a research-first redesign rather than continued patching:
*"I'm very concerned that we don't have a long-term workable
substrate in place on which to build new weapons."*

The research pass surveyed PvP-shooter projectile architecture
across Overwatch, Valorant, Apex, Battlefield, World of Tanks,
Crossout, and converged on a single pattern: **non-Rigidbody,
custom-stepped, swept-cast projectiles owned by a central world
service.** Every game in that list converged here for the same
reasons we did. The pattern is what `Projectile.cs` (the SMG bullet)
already did for hitscan-feel pellets; this session generalises it
to arc weapons (bomb, cannon) and consolidates damage routing, VFX
dispatch, and visual proxies.

## What landed

### New core: [`ProjectileWorld`](../../Assets/_Project/Scripts/Combat/ProjectileWorld.cs)

- Scene-root singleton, auto-bootstrapped via
  `RuntimeInitializeOnLoadMethod`.
- Owns a flat array of active projectiles
  ([`Live` struct](../../Assets/_Project/Scripts/Combat/ProjectileWorld.cs)),
  swap-removed on despawn for tight iteration.
- Integrates analytically each `FixedUpdate`:
  `pos += v·dt; v += g·dt`. Same fixed-step semantics as PhysX,
  fully deterministic.
- Per-step swept query — `Physics.SphereCastNonAlloc` for radius >
  0, `Physics.RaycastNonAlloc` otherwise. Owner-collider
  `HashSet<Collider>` filter excludes the firing chassis from every
  hit. No `Physics.IgnoreCollision`, no first-frame-overlap edge
  case.
- Three damage paths driven by spec fields, in priority order:
  1. **Area splash** (`SplashRadius > 0`) — bomb-style, walks every
     chassis with blocks in radius, quadratic falloff. Uses a
     reusable `OverlapSphereNonAlloc` buffer + reusable
     `HashSet<Robot>` / `HashSet<IDamageable>` dedup sets.
  2. **Ring splash** (`SplashRings != null`) — SMG-style, dispatches
     to `BlockGrid.ApplySplashDamage` on the contacted block.
  3. **Direct hit** (`Damage > 0`) — cannon-style, single
     `IDamageable.TakeDamage`.
- Kind-aware impact VFX/audio dispatch in
  `DispatchImpactFx` — SMG / cannon / bomb each get appropriate
  hit spark scale + the spec-supplied audio cue. CFXR fireball
  prefab still spawns for bombs alongside the procedural
  shockwave.
- Pooled visuals (`ProjectileVisual`) with two pools by visual
  shape: trail-only (SMG) and mesh+optional-trail
  (bomb / cannon). Pool grows on demand, never shrinks.
- Static event
  [`ProjectileWorld.HitLanded`](../../Assets/_Project/Scripts/Combat/ProjectileWorld.cs):
  `Action<Robot, Vector3>` — replaces the old `Projectile.Hit`.

### Spec-passing API

[`ProjectileSpec`](../../Assets/_Project/Scripts/Combat/ProjectileSpec.cs)
is a struct passed by `in` to `ProjectileWorld.Spawn`. Captures
ballistic state (origin, velocity, gravity, lifetime, cast
radius), damage routing (Damage / SplashRings / SplashRadius),
hit filter (LayerMask + owner Robot), and visual hints
(ShowTrail / ShowMesh / VisualTint / VisualMeshDiameter) plus an
explicit `ImpactAudioOverride`.

The split between the spec and the world means:
- Weapon blocks build a fresh spec each fire (no per-projectile
  allocation since the spec is a struct).
- Spec is read-only after spawn — no mutation surface for the
  integrator.
- New projectile kinds add a `ProjectileKind` enum value + a
  switch arm in `DispatchImpactFx`. Damage routing is data-driven
  (which spec fields you set), not class-hierarchy-driven.

### Visual proxy: [`ProjectileVisual`](../../Assets/_Project/Scripts/Combat/ProjectileVisual.cs)

Lightweight follower — owns an optional `TrailRenderer` (SMG
streak) and an optional sphere mesh child (bomb / cannon body).
Pool checkout calls `Configure` to enable / disable each part and
re-tint. `SyncTo(pos, vel)` per-step writes transform position +
look-rotation. No `Update` — driven by `ProjectileWorld`.

### Weapon-block migrations

| Weapon | Before | After |
|---|---|---|
| SMG | [`ProjectileGun.Fire`](../../Assets/_Project/Scripts/Combat/ProjectileGun.cs) used a static pool of `Projectile` MBs that did their own swept-cast | `ProjectileGun.Fire` builds a `ProjectileSpec` and calls `ProjectileWorld.Spawn`. Recoil / muzzle flash / fire audio stay in the gun (chassis-side at fire time). |
| Bomb | [`BombBayBlock.DropOne`](../../Assets/_Project/Scripts/Combat/BombBayBlock.cs) instantiated a Rigidbody bomb GameObject with a SphereCollider; `Bomb` MB owned splash, owner-immunity, OnCollisionEnter | `BombBayBlock.DropOne` builds a `ProjectileSpec` (`SplashRadius=18`, `Damage=80`) and spawns. Visual is a pooled mesh; gravity integrated analytically. |
| Cannon | [`CannonBlock.FireCannon`](../../Assets/_Project/Scripts/Combat/CannonBlock.cs) instantiated a Rigidbody+trigger ball GameObject; `Cannonball` MB owned OnTriggerEnter + owner-immunity | `CannonBlock.FireCannon` builds a `ProjectileSpec` (`Damage=60`, `CastRadius=0.28`) and spawns. The previous "cannonball explodes in barrel at certain angles" bug is structurally impossible — there is no projectile collider. |

The fire trigger / muzzle-flash / recoil / fire-audio logic stays
on each weapon block; they're chassis-side effects at the moment
of fire and don't belong in a global service.

### Deletions

```
- Assets/_Project/Scripts/Combat/Projectile.cs       (151 lines, replaced by ProjectileWorld + ProjectileSpec)
- Assets/_Project/Scripts/Combat/Bomb.cs             (193 lines, replaced)
- Assets/_Project/Scripts/Combat/Cannonball.cs       (132 lines, replaced)
```

Gone with them: every per-projectile Rigidbody, every
`Physics.IgnoreCollision` self-filter, every owner-immunity
timing window, every `OnCollisionEnter` / `OnTriggerEnter`
self-suppression branch.

### Pre-existing damage layer untouched

`BlockGrid.ApplySplashDamage`, `BlockBehaviour.TakeDamage`,
`Robot` aggregates — none of these changed. Only the **delivery
layer** (how projectiles reach a hit point) was rewritten. Block
HP, splash falloff ring profiles, and connectivity flood-fill
behaviour are byte-for-byte identical.

## Failure modes structurally eliminated

| Old failure | Why it can't happen now |
|---|---|
| Cannonball self-overlaps adjacent block at fire → `MomentumImpactHandler` bills self-damage | No projectile collider exists. `MomentumImpactHandler` only sees `OnCollisionEnter` from real colliders. |
| Cannonball self-detonates in barrel at certain pitches | No projectile collider. Owner-collider hash-set filters the swept cast directly; no PhysX-side timing race. |
| Fast SMG bullets tunnel through thin obstacles | Swept cast: `Physics.RaycastNonAlloc(prev, dir, dist)` mathematically can't skip a wall thinner than `v·dt`. |
| Bomb visual jitters on fall (interpolation off) | Visual proxy is a pooled GameObject driven by transform writes per fixed step + interpolation-free analytic position. No PhysX integrator gap to interpolate around. |
| Owner-immunity window vs. permanent self-check duplicate logic across three classes | Single owner-collider filter at the cast layer, applied uniformly to every projectile. |
| Per-shot `Instantiate` GC pressure (bomb / cannon spawned a fresh GameObject each fire) | Visual proxies pool by shape; spec-driven configure on checkout. Steady-state allocs: 0. |

## Performance contract

- **Hot path** (`ProjectileWorld.FixedUpdate`): one swept cast per
  active projectile per fixed step, one `HashSet.Contains` per
  hit per cast. No allocations in steady state.
- **Spawn path**: one transform write, optional pool pop. Spec is
  a struct passed by `in` — no boxing. Owner collider cache
  builds once per Robot via `GetComponentsInChildren` (only on
  first fire after spawn / cache miss).
- **Wall-clock budget at MP scale**: 16 chassis × 12 SMG/sec + 16
  cannons × 1.2 fire/sec ≈ 215 swept casts/sec. Each cast is
  ~5-10 µs typical → < 0.2 ms total. Comfortable inside the
  PHYSICS_PLAN budget of 2 ms / FixedUpdate.
- **Profiler marker**: existing `PerfMarkers.ProjectileFixedUpdate`
  reused; now wraps the world-level loop instead of per-bullet
  scopes.

## Netcode posture

The per-projectile state is pure data:
`(pos, vel, gravity, age, damage, splash, owner, hitMask, kind)`.
No Unity references except `Owner` (Robot) and the visual
GameObject ref (which exists only on render-side clients).

For server-authoritative MP later: the server runs the integrator
canonically; clients run the same integrator from the same
`Spawn` event for predicted visuals; impacts arrive from server
(authoritative). Server-rewind for lag compensation is plausible
because `Spawn` carries the firing tick timestamp on demand —
the architecture supports it without further restructuring.
This is the path Overwatch, Valorant, and Counter-Strike all use.

## Files touched

```
+ Assets/_Project/Scripts/Combat/ProjectileKind.cs
+ Assets/_Project/Scripts/Combat/ProjectileSpec.cs
+ Assets/_Project/Scripts/Combat/ProjectileVisual.cs
+ Assets/_Project/Scripts/Combat/ProjectileWorld.cs
+ docs/changes/32-projectile-unification.md           (this file)

~ Assets/_Project/Scripts/Combat/ProjectileGun.cs     (now spec-builder; pool gone)
~ Assets/_Project/Scripts/Combat/BombBayBlock.cs      (now spec-builder; Rigidbody gone)
~ Assets/_Project/Scripts/Combat/CannonBlock.cs       (now spec-builder; Rigidbody / trigger collider gone)
~ Assets/_Project/Scripts/Combat/CannonDefinition.cs  (cref refresh; BallMass field kept harmless)
~ Assets/_Project/Scripts/Combat/BombDefinition.cs    (cref refresh)
~ Assets/_Project/Scripts/Player/HitMarkerOverlay.cs  (subscribes to ProjectileWorld.HitLanded)

- Assets/_Project/Scripts/Combat/Projectile.cs        (replaced)
- Assets/_Project/Scripts/Combat/Bomb.cs              (replaced)
- Assets/_Project/Scripts/Combat/Cannonball.cs        (replaced)
```

No asmdef edits.

## Action required after pulling

None — the migration is internal to `Robogame.Combat`. Existing
chassis blueprints (with cannon / bomb-bay / SMG blocks) continue
to work; the binders dispatch by `BlockIds.X` exactly as before
and each block's behaviour now talks to `ProjectileWorld`
instead of building its own projectile.

A note on the cannon: the previous `CannonDefinition.BallMass`
field is still present but no longer consumed. Harmless dead
data; can be pruned in a follow-up if it bothers anyone.

## Open follow-ups

- **Owner-collider invalidation.** The cache is keyed by Robot
  ref and lives for the session. Stale entries (destroyed Robots,
  detached blocks) are filtered via Unity's fake-null check on
  read, so behaviour is correct, but the dictionary slowly leaks
  ~16 entries per match. Add explicit `InvalidateOwnerColliders`
  call from `Robot.OnDestroy` when MP scale makes this matter
  (the API is already exposed).
- **Planet-aware gravity.** Bombs and cannon shells use
  chassis-relative `down` baked at fire time. A long-flight shot
  on a small planet won't curve around the surface. Worth
  upgrading to per-step gravity recompute against
  `PlanetGravity` when the planet arena gets a meaningful combat
  loop.
- **Bomb expiry detonation.** Today bombs that timeout (no
  contact for 8 s) silently despawn. The old `Bomb.cs` did the
  same. If we want sky-high-detonation bombs, dispatch
  `DispatchImpactFx(Bomb, ...)` on lifetime expiry too.
- **Per-projectile rotation for visual realism.** Bomb and
  cannon visuals look-rotate to velocity each step, but a
  spinning iron ball would read better. Random angular velocity
  applied on spawn + integrated into the visual transform; cheap.
- **Visual proxy lerping.** `ProjectileVisual.SyncTo` writes the
  raw fixed-step position. Smoother visuals would lerp between
  fixed states each `Update`. Polish, not blocker.
- **Lifetime expiry → unified resolve.** Today an expired-by-time
  projectile despawns silently; only contact triggers
  `DispatchImpactFx`. For bombs we may want the lifetime cap to
  also fire the explosion (sky-burst). Easy to wire when needed.

## Future-session starter

1. Read this file (latest in `docs/changes/`).
2. Adding a new projectile weapon is now: add a `ProjectileKind`
   enum value, add a switch arm in
   `ProjectileWorld.DispatchImpactFx`, build a `ProjectileSpec`
   in your weapon block. No new MonoBehaviour. No new Rigidbody.
   No new self-collision plumbing.
3. The research summary that motivated this rewrite — and the
   four runner-up architectures we rejected — is in the
   prior turn of the session-31 chat log; if a future contributor
   wants to revisit, the rationale is in CLAUDE.md's "every new
   feature ships with VFX + audio" invariant + the references
   linked from `docs/AUDIO_PLAN.md`'s networking section.

## Addendum — post-rewrite fixes

Two issues surfaced after the initial rewrite landed:

### 1. Visible "rays" pointing at nothing

**Symptom.** Sometimes a long line would render between unrelated
points in the world after firing the SMG, with no relation to the
mouse cursor.

**Cause.** Pool checkout order. When a `ProjectileVisual` was
recycled from the trail pool, the sequence was:
`SetActive(true)` → `Configure()` (which calls
`TrailRenderer.Clear()` + `emitting = true`) → return → `SyncTo()`
moves the GameObject to the spawn position. The trail was
cleared at the visual's *previous-release* world position. Its
first emit sample landed there; the second landed at the spawn
position; the trail drew a connecting line between two unrelated
world points.

**Fix.** Split `AcquireVisual` into pop-only
(`AcquireVisualInactive`) plus a separate `ConfigureVisual`. New
order: pop inactive → `SyncTo` (positions the GameObject) →
`SetActive(true)` → `ConfigureVisual` (clear at the correct
position). The trail now starts cleanly at the spawn point.

### 2. Damage-number summation regressed silently

**Symptom.** Per-target accumulating damage numbers from session
31 stopped appearing.

**Cause.** [`ArenaController.BindFollowCamera`](../../Assets/_Project/Scripts/Gameplay/ArenaController.cs)
contained legacy code that *destroyed* any `FloatingDamageOverlay`
on the camera with a stale "opt-in: re-enable manually" comment.
Predates the session-31 rewrite. The component itself
(per-target accumulator, freeze window, animation) was intact —
just never attached.

**Fix.** Replaced the `Destroy` with `EnsureComponent`-style
add-if-missing, parallel to how `AimReticle`, `HitMarkerOverlay`,
etc. are wired in the same method. Numbers sum correctly again.
