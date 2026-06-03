# 108 — Combat pass: knockback, mortar, mines

> Status: **In progress.** A research-first feature pass adding three combat
> mechanics in dependency order: shared weapon-hit **knockback** → **mortar**
> weapon (top-mounted, lobbed) → **mines** module. Knockback is the shared
> foundation the other two lean on. Sequenced with a checkpoint per mechanic.
>
> Targeter/design decisions came from a `design-pilot` research round grounded
> in the pillars + Robocraft/WoT/Crossout/TF2 references; the user picked the
> mortar targeter style (start-of-arc preview, camera-offset launch) and the
> SMG debt-buffer.

## Mechanic 1 — Knockback / knockup (shipped, code)

Every damaging weapon hit now imparts an impulse to the *target* chassis,
mirroring the recoil that already kicks the *firer*. Kinetic hits stagger the
target along the shot direction; explosive hits push away from the blast centre
with an upward pop.

**`KnockbackReceiver`** (new, `Combat`) — a per-chassis impulse sink, lazily
added to a `Robot` the first time it's knocked (zero baseline cost — invariant
#5; a never-hit bot never carries it). Two paths, both applied at the chassis
**centre of mass** so knockback is pure translation — a graze on a wing tip
can't barrel-roll a light bot (rotational impact stays the momentum-damage
system's job). All force goes to the single chassis Rigidbody (invariant #4).

- **Immediate** (cannon / mortar / explosion): the impulse lands this physics
  step. Punchy stagger / pop.
- **Smoothed** (rapid-fire SMG): the impulse accumulates into a debt vector that
  bleeds out exponentially over a 0.7 s time constant. A 12 Hz pellet stream
  becomes one bounded push instead of per-frame jitter — the user's SMG concern.
  Total momentum imparted equals one impulse of the summed debt; it's just
  spread in time.

Every impulse is clamped to a **delta-v ceiling scaled by chassis mass**
(3 m/s immediate, 4 m/s accumulated debt), so no weapon can launch a
skeleton-framed light bot to orbit — combat stays readable regardless of how
little a target weighs.

**Wiring.** `ProjectileSpec` gains `Knockback` (N·s) + `KnockbackSmoothed`
(bool). `ProjectileWorld.Resolve` now receives the projectile travel direction
and applies knockback in each damage path: direct + ring → kinetic (horizontal
stagger, vertical dropped), area-splash → explosive (radial + upward bias,
linear distance falloff). Per-weapon magnitude lives on the definition assets
(`WeaponDefinition` / `CannonDefinition` / `BombDefinition`) with inline
fallbacks on the block components, same resolution pattern as recoil. Starting
values: SMG 3 (smoothed), cannon 18 (immediate), bomb 40 (explosive). All tune
in the inspector.

Knockback only fires when damage actually lands (it sits after the `TakeDamage`
calls, so friendly-fire-suppressed hits impart nothing).

## Mechanic 2 — Mortar (shipped, code; needs wizard run + playtest)

A top-mounted indirect-fire weapon that lobs an explosive shell on a ballistic
arc. New `MortarBlock` (`Combat`) mirrors `CannonBlock`'s yaw/pitch yoke rig but
replaces the aim model with a **lob targeter**:

- **Camera-offset launch elevation.** The yoke pitches to `aimPitchUp +
  elevationOffset` (clamped 25–72°), so looking flat ahead still fires a 35°
  lob — you never crane the camera at the sky. Looking up extends range, looking
  down flattens it. The launch direction is the barrel direction, *decoupled*
  from where the reticle points — that decoupling is what makes it a lob, not a
  direct shot.
- **Start-of-arc preview.** A world-space `LineRenderer` draws only the first
  ~0.55 s of the trajectory from the muzzle (same `p = o + v₀t + ½gt²` the
  projectile integrates). It reads the firing *angle* without revealing the
  landing spot — per the user's call. Gated to the player's own mortar
  (`IInputSource` present); refine to local-ownership when netcode lands.

The shell is `ProjectileKind.MortarShell` — an area-splash projectile that
reuses the bomb's explosion VFX/crater treatment on impact and picks up the
explosive-knockback path for free (knockback magnitude 55, immediate, radial +
pop). Chassis-relative gravity so the lob stays correct on planet arenas.

**Placement: top-mount only.** New `BlockConnectivity.RequiresTopMount` +
hardcoded id set; `IsValidMountFace` now rejects any mortar placement whose
mount face isn't +Y. Enforced automatically through the existing
`PlacementRules.CheckMountFace`. The mortar is also a leaf (nothing builds on
it).

**Wiring.** `RobotWeaponBinder` dispatches `BlockIds.Mortar` →
`MortarBlock` (the binder already named mortar as the intended future case).
Stats live on a new `MortarDefinition` SO. `BlockDefinitionWizard` scaffolds
`Mortar_Default` + `BlockDef_Mortar` (Weapon category) — the build hotbar
auto-lists it once the library is rebuilt. Launch FX/audio reuse the cannon
report + bomb explosion cues (invariant #8 satisfied; a dedicated mortar cue
can be authored later).

## Mechanic 3 — Mines (pending)

## Files

Knockback — New: `Combat/KnockbackReceiver.cs`. Edited: `Combat/ProjectileSpec.cs`,
`Combat/ProjectileWorld.cs`, `Combat/WeaponDefinition.cs`,
`Combat/CannonDefinition.cs`, `Combat/BombDefinition.cs`,
`Combat/ProjectileGun.cs`, `Combat/CannonBlock.cs`, `Combat/BombBayBlock.cs`.

Mortar — New: `Combat/MortarBlock.cs`, `Combat/MortarDefinition.cs`. Edited:
`Combat/ProjectileKind.cs` (MortarShell), `Combat/ProjectileWorld.cs` (impact
FX case), `Combat/RobotWeaponBinder.cs` (dispatch), `Block/BlockIds.cs`,
`Block/BlockConnectivity.cs` (top-mount + leaf), `Tools/Editor/BlockDefinitionWizard.cs`.

## Verification

Knockback: compile-reviewed by hand; **Unity MCP was not connected this session**
so the automated compile/console check and `qa-verifier`/`perf-checker` passes
could not run. Needs an editor recompile + console glance, and a playtest to tune
the feel values. No new allocations on the hot path (receiver early-outs when
debt is ~0; lazily added once per ever-hit bot).

## Invariant compliance

- **#1** knockback magnitudes are server-authoritative definition data, no
  Tweakable.
- **#4** all impulses applied to the single chassis Rigidbody at its CoM.
- **#5** `KnockbackReceiver` is added only on first hit — zero cost for an
  untouched bot.
- **#6** receiver `FixedUpdate` allocates nothing and early-outs at rest.
