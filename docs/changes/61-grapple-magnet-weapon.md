# 61 — Grapple Magnet weapon + Grappler plane preset (Buggy retired)

> Status: **shipped, untested in-engine.** New single-shot
> fire-and-retract weapon that fires a rope + magnet projectile up to
> 24 m; on enemy contact, latches via the standard SpringJoint tether
> the rest of the tip-block family uses (see
> [TIP_BLOCK_ATTACH.md](../TIP_BLOCK_ATTACH.md)). The default Buggy
> preset is gone; its slot in the chassis dropdown is now the
> **Grappler** — a plane with twin thrusters and a grapple magnet on
> the nose.

## Why this session

User: *"new weapon now that we have the magnet and hook in order …
Grapple Magnet … clip-size-1, no ammo limit … fire-and-retract …
hitting an enemy bot acts exactly as a rope + magnet does now … once
this is complete, please 1) delete the buggy default and 2) replace
it with a new plane default, called the grappler, with some extra
thrust power and a grappling magnet weapon"*.

## What it does

```
                                 Ready
                                   │
                            [FirePressed]
                                   ▼
   ┌──────────────────────────  Firing  ──────────────────────────┐
   │ projectile flies + sphere-cast each FixedUpdate              │
   │ visual: stretched cylinder muzzle → projectile               │
   └─────────┬──────────────────────────────────┬─────────────────┘
   [hit enemy Robot]                  [hit static / max range]
             ▼                                  ▼
          Latched                          Retracting
   ┌─────────────────────┐           ┌──────────────────────┐
   │ chassis↔tip leash   │           │ lerp tip → muzzle    │
   │ tip↔target spring   │           │ ease-in over 0.35 s  │
   │ Verlet rope chain   │           │ stretched cylinder   │
   │ player drags target │           │                      │
   └─────────┬───────────┘           └──────────┬───────────┘
   [FirePressed | target dead]               [t ≥ 1]
             │                                  │
             └──────────────┬───────────────────┘
                            ▼
                          Ready
```

Reuses the **same SpringJoint tether design** the MagnetBlock/HookBlock
landed in session 60: rest distance 0, `spring = 320`, `damper = 110`,
`breakForce = ∞`. The rope between chassis and tip is the same
shape RopeBlock builds — Verlet chain plus a chassis-side
`ConfigurableJoint` linear-limit leash at `maxRange` (24 m).

## What changed

### New runtime block

[`GrappleMagnetBlock`](../../Assets/_Project/Scripts/Combat/GrappleMagnetBlock.cs)
in `Robogame.Combat`. State machine + turret rig + projectile lifecycle
+ rope spawn/teardown all in one class:

- **Awake** sets up a `WeaponBlock`-style yaw/pitch turret (yoke +
  muzzle) so the player aims via the camera reticle.
- **Update** ticks the turret aim and polls `IInputSource.FirePressed`
  for one-frame fire / release edges.
- **FixedUpdate** dispatches to the active-phase tick: `TickFiring`
  (sphere-cast advance), `TickRetracting` (eased lerp), `TickLatched`
  (target-dead poll).
- **Firing** spawns a scene-root projectile (kinematic Rigidbody +
  SphereCollider + horseshoe-magnet visual), with chassis colliders
  ignore-paired at spawn so the cast doesn't self-hit.
- **Latch** flips the projectile non-kinematic, adds the chassis↔tip
  `ConfigurableJoint` leash at total range, registers a
  `VerletRopeChain` (12 particles by default), builds the target
  tether `SpringJoint`, and swaps the flight cylinder for a chain of
  rope-segment cylinders driven by `OnPostSolve`.
- **Retract** destroys the chain + tether, freezes the tip kinematic,
  re-spawns the flight cylinder, and lerps `_retractFromWorld → muzzle`
  with an ease-in cubic over `_retractDuration`. On completion all
  state is torn down and we return to Ready.
- **OnDestroy** idempotently calls every teardown helper so a block
  killed mid-latch doesn't leak its projectile / chain / joints.

### New input

[`IInputSource.FirePressed`](../../Assets/_Project/Scripts/Input/IInputSource.cs) —
one-tick edge companion to `FireHeld`. The grapple magnet is the
first single-shot player weapon; held trigger would otherwise
re-fire it every frame.

- `PlayerInputHandler` implements it via `InputAction.WasPressedThisFrame()`.
- `GroundBotInputSource` / `AirBotInputSource` stub `false` (bots
  don't author single-shot weapons yet).

### Block plumbing

- [`BlockIds.GrappleMagnet`](../../Assets/_Project/Scripts/Block/BlockIds.cs)
  = `"block.weapon.grapple_magnet"`.
- [`BlockConnectivity`](../../Assets/_Project/Scripts/Block/BlockConnectivity.cs)
  — added to the hard-coded leaf list (it's a terminating weapon
  block, nothing mounts on its faces).
- [`BlueprintAsciiDump`](../../Assets/_Project/Scripts/Block/BlueprintAsciiDump.cs)
  — `'X'` glyph (capital because it's a launcher, not a tip).
- [`RobotWeaponBinder`](../../Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs)
  — dispatch arm calls `grapple.Bind(_mount)`.
- [`BlockGhostFactory.BuildGrappleMagnet`](../../Assets/_Project/Scripts/Gameplay/BlockGhostFactory.cs)
  — build-mode ghost: chunkier body + wider barrel + two cyan magnet-
  pole hints in the muzzle.
- [`BlockDefinitionWizard`](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs)
  — `BlockDef_GrappleMagnet`, `mass: 4.5 kg`, `cpuCost: 45`,
  `maxHealth: 140`.
- New asset
  [`BlockDef_GrappleMagnet.asset`](../../Assets/_Project/ScriptableObjects/BlockDefinitions/BlockDef_GrappleMagnet.asset)
  + meta, registered in
  [`BlockDefinitionLibrary.asset`](../../Assets/_Project/ScriptableObjects/BlockDefinitionLibrary.asset).

### Default presets: Buggy → Grappler

- Removed `Blueprint_DefaultBuggy.asset` + meta from disk.
- [`GameplayScaffolder.cs`](../../Assets/_Project/Scripts/Tools/Editor/GameplayScaffolder.cs):
  - Renamed const `DefaultBuggyPath` → `DefaultGrapplerPath`.
  - Replaced `BuildBuggyPlan` with `BuildGrapplerPlan` — same plane
    skeleton as the standard Plane preset, with **two thrusters along
    the spine top** at z = -3 and z = -1 (both `up = +Y`, hosted by
    the spine cubes directly below them) and a `GrappleMagnet` at the
    nose top in place of the SMG. No tail rope+hook on this preset;
    the grapple magnet is the only swung tool. The first cut tried
    one thruster top, one bottom — that failed validation with
    `HostMissing` because `up = +Y` is the only orientation that
    resolves `ThrusterBlock.transform.forward` to chassis +Z, and
    there's no spine cube under the tail to host a -Y-mount.
  - `_presetBlueprints[2]` (was Buggy) now points at the live
    Grappler blueprint asset.
- `RopeTip.cs` had a historical "vs the Buggy" code comment;
  rephrased to "vs wheeled ground vehicles" so the explanation
  survives the preset retirement.

## Files

- **New:**
  - `Scripts/Combat/GrappleMagnetBlock.cs`
  - `ScriptableObjects/BlockDefinitions/BlockDef_GrappleMagnet.asset` (+ meta)
- **Edited:**
  - `Scripts/Block/BlockIds.cs`
  - `Scripts/Block/BlockConnectivity.cs`
  - `Scripts/Block/BlueprintAsciiDump.cs`
  - `Scripts/Combat/RobotWeaponBinder.cs`
  - `Scripts/Gameplay/BlockGhostFactory.cs`
  - `Scripts/Gameplay/AirBotInputSource.cs` — stub `FirePressed`.
  - `Scripts/Gameplay/GroundBotInputSource.cs` — stub `FirePressed`.
  - `Scripts/Input/IInputSource.cs` — new `FirePressed` member.
  - `Scripts/Input/PlayerInputHandler.cs` — implement `FirePressed`.
  - `Scripts/Movement/RopeTip.cs` — anachronism fix (Buggy → wheeled ground vehicles).
  - `Scripts/Tools/Editor/BlockDefinitionWizard.cs` — author the new BlockDef.
  - `Scripts/Tools/Editor/GameplayScaffolder.cs` — preset path + plan + preset-list slot.
  - `ScriptableObjects/BlockDefinitionLibrary.asset` — registration.
- **Deleted:**
  - `ScriptableObjects/Blueprints/Blueprint_DefaultBuggy.asset` (+ meta)

## Hard-invariant check

- **No Tweakable affects gameplay.** All grapple tuning lives on
  `[SerializeField]` inspector fields on `GrappleMagnetBlock`. Range,
  speed, spring constants, retract duration — none read from
  `Tweakables`. PHYSICS_PLAN § 1.5: clean.
- **Server-authoritative shape.** Projectile is a Rigidbody +
  Collider on a scene-root GameObject; `SphereCast`, `SpringJoint`,
  `ConfigurableJoint`, and `VerletRopeChain` all already run on the
  server side. When MP lands, this replicates as a deterministic
  state machine driven by a single `FirePressed` event + the chassis
  Rigidbody state.
- **Single Rigidbody per chassis.** Projectile lives at scene root,
  same as `RopeBlock`'s tip body. No child-of-chassis rigidbody.
- **No per-frame allocations.** Visuals reuse existing transforms
  per chain segment; the flight cylinder is single-spawn. Pull /
  cast / lerp loops are alloc-free.
- **VFX + audio.** `MuzzleFlash` on fire (`WeaponFireCannon` audio
  cue piggybacked — chunkier than SMG, no new cue yet); `TipImpact`
  + `FlipBurst` on latch. No bespoke retract cue yet; library
  missing-cue logger will surface it once we want one.
- **Tip-block exemption holds.** The projectile has no `TipBlock`
  component AND no `IDamageable`, so
  `MomentumImpactHandler`'s tip-block guard *and* its IDamageable
  fallback both safely no-op against it. Magnet doesn't destroy
  itself when its target slams into it.

## Tuning note: latched pull field added for parity

Second playtest after the leash-limit + tip-mass fixes: target still
felt unmovable. The missing ingredient was the **continuous pull
field** that `MagnetBlock.ApplyPullForces` runs every FixedUpdate
on the chassis-attached rope+magnet. Once latched, the magnet
keeps pulling every nearby non-kinematic body toward itself at
600 N × falloff. The latched target sits inside that sphere (its
centre-of-mass typically 1–3 m from the tip depending on where the
magnet hit), so it eats hundreds of newtons of force every tick on
top of the SpringJoint tether's restoring force.

`GrappleMagnetBlock.TickLatched` now runs the same pull tick. Same
defaults as MagnetBlock (6 m radius, 600 N peak, linear falloff).
Without this, the tether spring alone (320 N/m × stretch) was the
only thing pulling the target — strong enough to *hold*, weak enough
to feel like nothing when you tried to *drag*.

## Tuning note: leash limit = deployed length, not max range

First playtest reported "the plane gets jerked around and I can
barely move the target." The chassis↔tip `ConfigurableJoint` had
its `linearLimit = _maxRange` (24 m), but the projectile typically
lands at 5–15 m. That left (maxRange − deployed) metres of *slack*:
the plane could fly forward freely with zero pull on the target,
then the spring would engage suddenly and yank the chassis without
ever building sustained tension on the tether. Fix: set the leash
limit to the *deployed* distance at latch time — same shape as
RopeBlock's chassis↔tip joint (`limit = totalLen` = the rope's rest
length). Now any forward chassis motion immediately stretches the
spring and force flows chassis → leash → tip → tether → target.

Also bumped tip body mass `2.5 → 3.4 kg` to match the rope+magnet's
adopted-tip effective mass (rope segment ≈ 0.4 + magnet block 3.0).
Same inertial scale means same fraction of the leash impulse
survives the tip body to reach the tether → consistent perceived
pull strength across both delivery methods.

## Known follow-ups

- **Bot use of the grapple magnet.** Bots return `FirePressed =
  false` so they never fire it. Once we want AI grappling, give
  the bot input sources a "request fire" pulse + a target-selection
  policy.
- **Multi-grapple chassis.** Two grapple magnets on one chassis
  fire independently. They share a `WeaponMount` (same aim), but
  each has its own state machine — both will launch on a single
  `FirePressed`. Fine for v1 (more grapples = more catch chances);
  if it feels OP, gate via a chassis-wide cooldown component.
- **Manual release vs target-dead release.** Today both flow through
  `BeginRetract` cleanly. A "release at chassis" vs "release in
  place" distinction could let the player drop a target without
  reeling all the way back.
- **Audio.** Piggybacks `WeaponFireCannon` for the fire and
  `TipImpact` for the latch. Bespoke cues (`GrappleFire`,
  `GrappleLatch`, `GrappleRetract`) are deferred — the AUDIO_PLAN
  process is "declare the cue, leave the entry blank, let the
  audio pass fill it." Defer until the pass.
- **Latched-state HUD.** No on-screen indicator that you're
  currently latched. The rope visual and the dragged target are
  the implicit cue; a small overlay (`GRAPPLE  LATCHED`) would be
  a nice add but doesn't gate playability.
- **Retract-while-firing fast-fire abuse.** If the player taps fire,
  hits a wall, and waits 0.35 s for retract, they can fire again.
  That's intentional ("fire-and-retract" as a single beat) but
  experienced players might tap-tap-tap-tap effectively as a
  100 ms shot-interval rope spam. If that reads bad in playtest,
  add a small `_postRetractCooldown` before Ready unlocks.

## Verification

1. **Grappler preset spawns.** Open the garage → chassis dropdown
   includes "Grappler" in slot 2 (was "Buggy"). Selecting it
   spawns a plane with two thrusters on top of the spine (at z=-3
   and z=-1) and a grapple magnet on the nose. Launch into the arena.
2. **Fire on Ready.** Tap LMB → muzzle flash, a magnet head
   launches from the nose, a cyan-tinted cylinder extends from
   muzzle to magnet as it travels.
3. **Miss → instant retract.** Aim at the ground; fire. The
   projectile hits dirt → cylinder reels back to the muzzle over
   ~0.35 s with ease-in → ready to fire again.
4. **Hit → latch + drag.** Aim at the friendly tank (or any enemy);
   fire. The magnet snaps to the target, a 12-segment Verlet rope
   materialises between chassis and target, and the chassis can
   drag the target around the arena.
5. **Re-fire while latched.** Tap LMB during Latched → tether
   releases, the rope tears down, the projectile reels back, ready
   re-arms.
6. **Target dies while latched.** Bash the latched target into a
   wall until it's destroyed (or use the dev-tool kill). The
   tether auto-releases and reels back to the muzzle.
7. **Self-collision sanity.** Fire backward (within yaw limits) so
   the projectile sweeps past the wings → no hit on the chassis,
   no damage taken by either side from chassis-vs-projectile
   contact. Tip-block exemption from session 60 + the spawn-time
   ignore-pair both keep this clean.
