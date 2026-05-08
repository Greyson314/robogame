# Session 31 — Cannon, bug fixes, and feel-feature pass

> Status: **shipped, ready for in-engine playtest.** Bombs interpolate
> properly, the hook latches without punting, ground-vs-ground aim
> doesn't drag the camera underground, damage numbers sum per chassis,
> and a new pirate-themed cannon block joins the weapon roster. Three
> additional features ride along: a kill-streak announcer, a
> pause-on-settings toggle, and an aim-line preview.

## Why this session

Two bugs and a fistful of features. The bug list piled up after
session 30's audio + grapple work surfaced edge cases (bombs jittering,
the grapple now punts instead of holds). The feature list is the next
push toward gameplay completeness — a third weapon, better damage
feedback, kill-streak feel, and visual aids for the just-fixed aim
geometry.

## Doc invariant added

[`CLAUDE.md`](../../CLAUDE.md) gained an 8th hard invariant: **every
new feature ships with VFX + audio.** As of session 30 the project
has both pipelines wired (`VfxSpawner` + `AudioRouter` + cue library);
deferring "I'll add the sound later" produces a half-finished feature
that often never gets the polish pass. The rule is to declare cues
upfront, leave clip slots blank if needed, and hook the call site so
the missing-cue logger flags it. The Cannon below ships with both
the muzzle-flash VFX and the `WeaponFireCannon` cue from day one —
this is the model.

## Bug fixes

### Bombs jittered visually as they fell

[`BombBayBlock.DropOne`](../../Assets/_Project/Scripts/Combat/BombBayBlock.cs:147)
created the bomb's Rigidbody without `interpolation`. Default is
`None`, which snaps the visible body to the 50 Hz physics-step
position while the camera renders at display rate. The render
samples between physics ticks land on stale frames, reading as
visible jitter on every fall.

Fix: set `rb.interpolation = RigidbodyInterpolation.Interpolate`.
Same setting the chassis Rigidbody and the existing rope-tip body
already use.

### Hook punted instead of latching

After session 30's grapple-physics fix, the hook still catapulted
low-mass targets — only with softer velocities. Root cause: the
chassis-tip joint's restoring force flowed through the hook's
locked grapple joint to the target body. Even a moderate spring
applied to a 0.5 kg tip transmits a large impulse to a low-mass
target chassis in a single FixedUpdate step, before the grapple's
own `breakForce` trips.

[`HookBlock`](../../Assets/_Project/Scripts/Movement/HookBlock.cs)
fix: while grappled, **bump the tip Rigidbody's mass from 0.5 kg
to 25 kg**. The heavier tip absorbs the chassis-tip joint's impulse
into its own velocity gain before transmitting it through the
locked grapple joint. Spring also dropped further (1500 → 600 N/m).
Net effect: the tip oscillates a bit but the target stays put,
the player swings around it, and `breakForce` trips on real
overload (e.g. flying full speed away).

The PhysX-broke-the-joint recovery path in `FixedUpdate` was
extended to restore the cached tip mass alongside the kinematic
flag and the chassis-tip spring — without it, mass would leak to
25 kg permanently after a `breakForce` release.

### Camera + aim went underground for ground-vs-ground combat

A ground tank aiming at another ground tank had two compounding
issues:

1. **Aim ray hits ground before target.** `RobotDrive.ComputeAimPoint`
   casts a ray from the camera through screen-centre. With the
   camera above the chassis and the target at the same elevation,
   the ray slopes downward; it intersects the flat ground before
   reaching the target, so the aim point lands at the player's feet.
2. **Camera dipped below ground.** The player would tilt the camera
   way down to "find" the target, which moved the orbit pitch into
   negative territory; `FollowCamera`'s SphereCast obstacle
   avoidance pulled the camera in along that axis, and the resulting
   position passed through terrain.

Two fixes, one each:

**Aim ray** — [`RobotDrive.ComputeAimPoint`](../../Assets/_Project/Scripts/Movement/RobotDrive.cs:232)
now does a two-tier resolution. It walks all hits and prefers any
collider whose parent has an `IDamageable`; only if no damageable
is in the swept ray does it fall through to the closest non-self
hit (the original behaviour). A ground tank aiming at an enemy
through a foreground patch of grass still locks the aim onto the
enemy. Allocation-free; the buffer is static.

**Camera floor** — [`FollowCamera.ResolveCameraPosition`](../../Assets/_Project/Scripts/Player/FollowCamera.cs:455)
clamps the desired camera position so it never drops below
`target.y + _minHeightAboveTarget` (default 0.5 m). The new
`_minHeightAboveTarget` SerializeField defaults to 0.5; setting
negative disables the floor for special-case rigs (e.g., a future
underwater chassis cam).

## Damage numbers — per-chassis summation

Replaced [`FloatingDamageOverlay`](../../Assets/_Project/Scripts/Player/FloatingDamageOverlay.cs)'s
per-hit floater with a per-target accumulator. Hitting the same
chassis 10 times with the SMG now shows `2 → 4 → 6 → ... → 20` in
place rather than ten stacked "2"s. After
`_summationWindow` (default 1 s) without a fresh hit, the
accumulator freezes, animates up + fades out over `_lifetime`, and
the next hit on that chassis spawns a fresh accumulator from the
next damage value.

Implementation:

- Per-chassis lookup is a linear walk over a `List<Accumulator>`;
  bounded by simultaneously-engaged targets (< 8 in practice).
- Allocation-free hot path: a `Stack<Accumulator>` pool refills on
  evict so per-event GC is zero in steady state.
- Hard cap of 32 simultaneous accumulators with frozen-first
  eviction.

## Cannon weapon (new)

Pirate-themed slow-firing gravity-projectile gun. Mounts on top of
or facing forward off any chassis cell — the same yaw + pitch
turret rig as the SMG, but with a chunkier barrel and a brass-tipped
muzzle ring.

| Stat | Default | Notes |
|---|---|---|
| Fire interval | 0.85 s | ~1 shot/sec at full auto |
| Muzzle speed | 80 m/s | Same as SMG; flat trajectory at close range |
| Damage (direct) | 60 HP | Single-target; no splash today |
| Ball mass | 5 kg | Real Rigidbody; gravity arcs the shot |
| Recoil | 28 N·s | Pushes chassis back perceptibly |
| CPU cost | 35 | More than SMG (20), less than BombBay (40) |
| Block mass | 3.5 kg | Chunky relative to other weapon blocks |

**Files:**

- [`BlockIds.Cannon`](../../Assets/_Project/Scripts/Block/BlockIds.cs) — stable id `block.weapon.cannon`
- [`CannonDefinition`](../../Assets/_Project/Scripts/Combat/CannonDefinition.cs) — per-block stat SO (mirrors `BombDefinition`)
- [`Cannonball`](../../Assets/_Project/Scripts/Combat/Cannonball.cs) — Rigidbody projectile, contact damage, no splash
- [`CannonBlock`](../../Assets/_Project/Scripts/Combat/CannonBlock.cs) — turret rig + fire path
- [`RobotWeaponBinder`](../../Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs) — dispatches `BlockIds.Cannon → CannonBlock`
- [`BlockDefinitionWizard`](../../Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs) — `BlockDef_Cannon` + `Cannon_Default` SO

VFX (per the new mandate):

- Muzzle flash at fire (`VfxKind.MuzzleFlash`, 2× scale — chunkier than SMG)
- Procedural shockwave puff at fire (`VfxKind.BombShockwave`, 0.45× scale — cannon smoke)
- Hit spark at impact (`VfxKind.HitSpark`, 1.4× scale — bigger than bullet hit)

Audio (per the new mandate):

- New `AudioCue.WeaponFireCannon` → `EXPLOSIONS/Short/EXPLOSION_Short_01_mono.wav`
- Hit sound piggybacks on `AudioCue.ProjectileImpact` for now

The cannon block appears in the build-mode hotbar's Weapon
category automatically (the hotbar iterates by `BlockCategory`,
so new entries surface without UI work).

## Three additional features

### 1. Kill-streak announcer

[`KillAnnouncer`](../../Assets/_Project/Scripts/Gameplay/KillAnnouncer.cs)
subscribes to `MatchController.KillRegistered` and shows a banner
on player kills:

| Trigger | Banner | Color |
|---|---|---|
| First player kill of the round | `FIRST BLOOD!` | Alert red |
| 2nd kill within 4 s | `DOUBLE KILL!` | Caution yellow |
| 3rd | `TRIPLE KILL!` | Hazard orange |
| 4th | `QUAD KILL!` | Alert |
| 5+ | `RAMPAGE!` | Plasma purple |

Banner fades in/out (15 % attack, 70 % hold, 15 % decay). Bound
by `ArenaController.BindFollowCamera` alongside the other camera
HUDs. Allocation-free repaint.

Audio: new `AudioCue.KillBanner` → `8BIT/Powerups/8BIT_RETRO_Powerup_Spawn_Quick_Climbing_mono.wav`,
solo so a quick double-kill replaces the single-kill ping rather
than overlapping.

### 2. Pause on settings open

New `Tweakables.SettingsPause` (default ON, group `QoL`). When
the settings panel opens, `Time.timeScale = 0` freezes the world.
Closing restores `1f`. Subscribed to `Tweakables.Changed` so
toggling the flag while the panel is open re-applies live.

Safe because every existing time-sensitive system that should
keep running while paused (audio expire sweep, kill-banner fade,
floating-damage animation) already uses `Time.unscaledTime`.

OnDestroy restores time scale to 1 so a scene reload while the
panel was open doesn't leave the next scene paused.

### 3. Aim-line preview

[`AimLinePreview`](../../Assets/_Project/Scripts/Movement/AimLinePreview.cs)
draws a faint hazard-orange line from the player's chassis to the
resolved `RobotDrive.AimPoint`. Cheap (one `LineRenderer`, one
draw call) and allocation-free. Bound only on player chassis (in
`ChassisFactory.Build`'s `addPlayerInputs` branch) — bots don't
render their own line, which would clutter the screen.

Especially useful with the camera/aim fix above: the line gives
the player an immediate visual confirmation that the aim is
landing on the intended target, not on the ground in front of it.

## Files touched

```
+ Assets/_Project/Scripts/Combat/CannonDefinition.cs
+ Assets/_Project/Scripts/Combat/Cannonball.cs
+ Assets/_Project/Scripts/Combat/CannonBlock.cs
+ Assets/_Project/Scripts/Movement/AimLinePreview.cs
+ Assets/_Project/Scripts/Gameplay/KillAnnouncer.cs
+ docs/changes/31-cannon-bugfixes-features.md          (this file)

~ CLAUDE.md                                            (+ VFX/audio invariant)
~ Assets/_Project/Scripts/Block/BlockIds.cs            (+ Cannon)
~ Assets/_Project/Scripts/Core/AudioCue.cs             (+ WeaponFireCannon, KillBanner)
~ Assets/_Project/Scripts/Core/Tweakables.cs           (+ QoL.PauseOnSettings)
~ Assets/_Project/Scripts/Combat/BombBayBlock.cs       (Rigidbody.interpolation)
~ Assets/_Project/Scripts/Combat/RobotWeaponBinder.cs  (Cannon dispatch)
~ Assets/_Project/Scripts/Movement/HookBlock.cs        (heavy-tip + softer spring + recovery)
~ Assets/_Project/Scripts/Movement/RobotDrive.cs       (damageable-prefer aim resolution)
~ Assets/_Project/Scripts/Player/FollowCamera.cs       (Y-floor in ResolveCameraPosition)
~ Assets/_Project/Scripts/Player/FloatingDamageOverlay.cs (per-target accumulator)
~ Assets/_Project/Scripts/Gameplay/SettingsHud.cs      (pause + Time.timeScale gate)
~ Assets/_Project/Scripts/Gameplay/ArenaController.cs  (KillAnnouncer bind)
~ Assets/_Project/Scripts/Gameplay/ChassisFactory.cs   (AimLinePreview on player)
~ Assets/_Project/Scripts/Tools/Editor/BlockDefinitionWizard.cs (Cannon definition)
~ Assets/_Project/Scripts/Tools/Editor/AudioCueWizard.cs (cue rows)
```

No asmdef edits.

## Action required after pulling

1. Re-run `Robogame → Scaffold → Block Definitions` (or the
   equivalent build-all menu) to pick up the new `BlockDef_Cannon`
   asset. The hotbar will show it in the Weapon category.
2. The wizard's auto-rebuild will refresh `AudioCueLibrary.asset`
   on next editor focus to pick up the two new cues.
3. The `_minHeightAboveTarget` field on existing FollowCamera
   instances will default to 0.5 since it's a new SerializeField.
   No scene edits needed.
4. The `_startMatchKey` change from session 30 still applies —
   backtick (`` ` ``) starts the round.

## Open follow-ups

- **Cannon: forward-mount placement convention.** The block can
  technically be placed anywhere; "forward / top mounted" is
  player convention rather than enforced. A future build-mode
  validator could reject placements that would leave the barrel
  embedded in chassis geometry.
- **Cannonball pooling.** Today each shot allocates a GameObject.
  Fine at 1 shot/sec; revisit if a future variant cranks the rate.
- **Heavier-tip side effects.** A 25 kg tip while grappled may
  feel "sticky" — the chassis can't easily yank it back. If
  playtesting reveals an issue, the constant in `HookBlock` is
  one line.
- **Aim-line color logic.** Always hazard orange today; could
  flip to red when the aim is on a damageable target (subtle
  cue that "your shots will land here, on this enemy"). Deferred.
- **Match-restart announcer reset.** `KillAnnouncer.BindMatch`
  resets streak state on bind. If the user implements
  in-place match restart later, the binder needs to fire again
  to clear stale state.

## Future-session starter

1. Read this file (latest in `docs/changes/`).
2. The new VFX+audio invariant in `CLAUDE.md` § "Hard invariants"
   applies to any new feature — declare cues + VFX kinds upfront.
3. Tune cannon stats by editing `CannonDefinition` ScriptableObject
   defaults in `BlockDefinitionWizard`, or per-instance in the
   inspector after place.
4. The kill announcer's streak window + display time live as
   SerializeFields on the component — adjust on the main camera
   in the Arena scene if it feels too tight or too lingering.
