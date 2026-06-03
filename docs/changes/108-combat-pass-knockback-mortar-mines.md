# 108 — Combat pass: knockback, mortar, mines

> Status: **Code complete; needs Unity verification + asset bake.** A
> research-first feature pass adding three combat mechanics in dependency order:
> shared weapon-hit **knockback** → **mortar** weapon (top-mounted, lobbed) →
> **mines** module. Knockback is the shared foundation the other two lean on.
> Committed as one checkpoint per mechanic. **Unity MCP was not connected this
> session — none of it is compile-verified, asset-baked, or playtested. See the
> handoff checklist at the bottom.**
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

## Mechanic 3 — Mines (shipped, code; needs wizard run + playtest)

A new `ModuleKind.Mines` (enum value 6) — a deployable that drops a proximity
mine on the ground below the chassis. It slots into the existing module system
with no new plumbing: `RobotModuleBinder` already attaches `ModuleBlock` to any
`ModuleKinds.IsModuleId` block, so adding the id to the four `ModuleKinds`
switches + the `ModuleTuning` row is the whole integration. Power = centre
damage (default 70), cooldown 8 s × power ratio, mine lifetime 30 s.

`ModuleEffects.DeployMine` raycasts down along gravity (planet-aware via
`GravityField`) to rest the mine on the surface, then spawns a `DeployedMine` —
a detached, self-ticking object modelled on `ShieldBubble`:

- **State machine.** Arming (red glow, can't trigger) → after 1.2 s, Armed
  (steady amber) → an enemy entering the 2.2 m trigger radius flags a
  **one-physics-tick fuse** → Detonate. The tick gap is the user's
  "wait a tick to maximize impact" — the victim's contact and the boom are
  cleanly separated (also matters for the future server log).
- **Detonation** routes through the new `ProjectileWorld.Detonate(center,
  radius, damage, knockback, owner, mask, audio)` — a reusable point-explosion
  that runs the area-splash damage + explosive knockback (45) + bomb
  VFX/audio/crater treatment. So mines pick up mechanic 1's knockback for free.
- **No friendly fire, owner-immune.** Trigger detection and splash both skip the
  deployer and its teammates (same neutral-team rule as projectiles). The 1.2 s
  arm delay stops a mine popping the instant it's dropped.
- **Active-mine cap** of 3 per owner; deploying a 4th trims the oldest
  (Crossout's replace-oldest model).
- **"Visible but subtle"** — a small dark disc with a tiny state-coloured glow
  dot, readable from the ground but not from across the arena at speed.
- **Zero physics cost** — no collider; proximity is a per-tick
  `OverlapSphereNonAlloc` against a shared buffer, only while armed.

`DeployMine` takes the mine's damage + lifetime as params and the rest as
constants, so the planned mine *types* later just pass a profile. The
`BlockDefinitionWizard` scaffolds `BlockDef_ModuleMines` (Module category); the
HUD ability tile + power slider surface automatically off the new `ModuleKind`.

## Files

Knockback — New: `Combat/KnockbackReceiver.cs`. Edited: `Combat/ProjectileSpec.cs`,
`Combat/ProjectileWorld.cs`, `Combat/WeaponDefinition.cs`,
`Combat/CannonDefinition.cs`, `Combat/BombDefinition.cs`,
`Combat/ProjectileGun.cs`, `Combat/CannonBlock.cs`, `Combat/BombBayBlock.cs`.

Mortar — New: `Combat/MortarBlock.cs`, `Combat/MortarDefinition.cs`. Edited:
`Combat/ProjectileKind.cs` (MortarShell), `Combat/ProjectileWorld.cs` (impact
FX case), `Combat/RobotWeaponBinder.cs` (dispatch), `Block/BlockIds.cs`,
`Block/BlockConnectivity.cs` (top-mount + leaf), `Tools/Editor/BlockDefinitionWizard.cs`.

Mines — Edited: `Combat/ModuleEffects.cs` (`DeployMine` + `DeployedMine` class),
`Combat/ProjectileWorld.cs` (`Detonate` point-explosion API),
`Combat/ModuleSystem.cs` (activation case), `Block/ModuleKind.cs` (Mines=6),
`Block/ModuleKinds.cs` (4 maps), `Block/ModuleTuning.cs` (row),
`Block/BlockIds.cs`, `Tools/Editor/BlockDefinitionWizard.cs`.

## Invariant compliance

- **#1** knockback / mortar / mine magnitudes are server-authoritative
  definition + `ModuleTuning` data, no Tweakable.
- **#2** mortar + mine are placed in the garage; the deployed mine is an
  in-match effect of a frozen blueprint module, not in-match building.
- **#3** module activation, mine ticking, and detonation gate on
  `NetworkContext.IsServer` (the mine inherits this via `ModuleSystem`).
- **#4** every impulse (knockback, recoil, explosion) goes to the single chassis
  Rigidbody; knockback specifically at CoM.
- **#5** `KnockbackReceiver` is added only on first hit; the mine carries no
  collider and only ticks while armed — zero cost for untouched bots.
- **#6** receiver + mine early-out at rest and use shared NonAlloc buffers;
  arc preview reuses a cached material and writes into a fixed-size line.
- **#8** mortar ships launch FX + audio and the bomb explosion treatment; mine
  ships a deploy cue + glow tell and the explosion treatment. (Both reuse
  existing cues — dedicated mortar/mine audio is a noted follow-up.)

## Handoff — do this in Unity (could not be done headless)

1. **Recompile + check the console.** None of this compiled this session.
2. **Run the block-definition wizard** (the menu that calls
   `BlockDefinitionWizard.CreateTestDefinitions`) to bake `Mortar_Default`,
   `BlockDef_Mortar`, and `BlockDef_ModuleMines`, then rebuild the
   `BlockDefinitionLibrary` so the hotbar lists them.
3. **`.meta` files** for the four new scripts (`KnockbackReceiver`,
   `MortarBlock`, `MortarDefinition`, and the mine code lives in existing files)
   generate on first import — commit them.
4. **Playtest + tune feel:** knockback magnitudes (SMG 3 / cannon 18 / bomb 40),
   the mortar lob (elevation offset 35°, muzzle speed 34, arc-preview length),
   and the mine (1.2 s arm, 2.2 m trigger, 7 m splash, 3-mine cap).
5. **Optional:** author dedicated mortar-launch and mine cues instead of the
   reused cannon/bomb audio.

## Known limitations

- Mortar arc preview is gated on `IInputSource != null`, so an AI bot that
  carries an input source would also render an arc. Refine to a local-player
  ownership check when netcode lands.
- Mortar lob math is chassis-relative (like the cannon); on a steeply banked
  chassis the launch elevation is approximate. Fine for an upright bot.
