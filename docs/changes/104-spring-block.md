# 104 — Spring block (jump) + modular spring physics

> Status: **Shipped (code).** New movement block, surfaced by the user
> during a `/ideate` run. Plus a reusable `SpringSolver` so spring math
> stops being copy-pasted. Run `Robogame → Build Everything` to bake the
> new block def + SpringBot preset into the live scenes.

## What landed

**`SpringSolver` — the reusable spring primitive.** `Robogame.Movement.SpringSolver`
(new) holds two pure, allocation-free, Unity-free statics:
`HookeDamped(stiffness, damping, displacement, target, velocity)` (Hooke
spring minus velocity damping, clamped ≥ 0 — a spring pushes, never pulls)
and `ResolveImpulse(configValue, defaultImpulse)` (blueprint-ConfigValue-
or-default, mirroring `ThrusterBlock.MaxThrust`). `HoverBladeBlock`'s inline
spring-damper was migrated onto `HookeDamped` (behaviour-identical, five-line
change) — the user's "generalise spring mechanics, they'll keep mattering"
directive is what the file exists for; future suspension / bumper / pogo
blocks call in rather than re-deriving.

**`SpringBlock` — a jump.** Standalone `MonoBehaviour` in the
`HoverBladeBlock` mould (no extra Rigidbody/joint — invariants #4/#5; refs
cached in OnEnable, steady-state FixedUpdate is arithmetic — invariant #6).
On the rising edge of the jump input (`IInputSource.Vertical` crossing
+0.5, latched in our own FixedUpdate — no `IInputSource` change, no
WasPressedThisFrame-in-FixedUpdate footgun) it fires a cooldown-gated
`AddForceAtPosition(..., ForceMode.Impulse)` on the chassis Rigidbody.
Launch direction is `-transform.up` = the chassis-INWARD normal of the
mount face: an underside spring jumps the bot up, a side spring dashes it
sideways, and it's derived from the chassis pose so it works on flat and
spherical arenas. Launch strength rides `BlockBehaviour.ConfigValue`
(blueprint-authoritative) with a `SpringTuningConfig.Default` fallback
(14 N·s, 1.2 s cooldown).

**VFX + audio (invariant #8).** New `VfxKind.SpringBurst` — a short cone of
slate/dust fragments kicked along the launch axis (slate→dust palette,
of-a-piece with debris dust). New `AudioCue.SpringLaunch` — the 8-bit
upward "boing" (reuses the proven `8BIT/Powerups/..._Climbing` clip, so it's
wired, not a missing-cue no-op). Plus a coil visual that squashes on launch
and eases back out.

**Dev tuning + placeability.** `SpringTuningConfig` + `DevTuningOverride.ApplySpring`
+ two compile-stripped `Dev.Spring.*` Tweakables (impulse / cooldown) —
session-98 pattern, invariant #1 clean (the override surface doesn't exist
in shipping builds; gameplay strength is the blueprint ConfigValue).
`BlockIds.Spring` + a `BlockDefinitionWizard` entry (Movement, HP 80,
mass 1.8, CPU 20) make it a placeable garage block; the library auto-
discovers it. `RobotSpringBinder` (mirrors `RobotHoverBladeBinder`) attaches
the behaviour, wired into `ChassisAssembler` after the hover-blade binder.

**SpringBot preset.** A compact ground rover (CPU + 3×3 floor + 4 wheels)
with two springs on the underside rear; Space pops it up with a slight
nose-up kick. Added to `CreateDefaultBlueprints` + the HUD preset list
(slot 9, array size 9 → 10).

## Files

New: `SpringSolver.cs`, `SpringBlock.cs`, `RobotSpringBinder.cs` (Movement);
`Tests/EditMode/Movement/SpringSolverTests.cs`,
`Tests/PlayMode/Movement/SpringBlockTests.cs`.

Edited: `HoverBladeBlock.cs` (migrate to SpringSolver), `BlockIds.cs`,
`DevTuningOverride.cs` (+SpringTuningConfig), `Tweakables.cs` (2 dev keys),
`AudioCue.cs` + `AudioCueWizard.cs` (SpringLaunch), `VfxKind.cs` +
`VfxSpawner.cs` (SpringBurst), `ChassisAssembler.cs` (binder),
`BlockDefinitionWizard.cs` (block def), `GameplayScaffolder.cs` (SpringBot
preset + preset list).

## Tests

EditMode `SpringSolverTests` (6): HookeDamped force / ≥0 clamp / damping /
overdamp, ResolveImpulse config-vs-default. PlayMode `SpringBlockTests` (4):
underside rising-edge launches +Y, cooldown swallows an immediate refire,
destroyed spring applies no impulse, side mount launches along -X.

## Design provenance

Surfaced during the session-104 `/ideate` run. The user passed on three
generated slates (objective modes → bold combat blocks → defensive
deception) and authored the Spring directly. The three deception ideas
(Phantom Shell, Lodestone, Shard Launcher), the four bold blocks (Morph
Hinge, Pinch, Reactive Armor, Splinter), and a flagged
smoke/stealth/clone theme are all parked as `proposed` in
`docs/research/idea-backlog.md` for a future round.

## Invariant compliance

- **#1** — strength via blueprint ConfigValue; dev overrides compile-stripped.
- **#4/#5** — no new Rigidbody/joint/collider; zero baseline cost.
- **#6** — refs cached OnEnable; FixedUpdate allocation-free.
- **#8** — ships VFX (SpringBurst) + wired audio (SpringLaunch).

## Followups / known gaps

- Hold-to-charge (bigger jump for a longer press) deliberately out of v1.
- Bots return `Vertical` ≈ 0, so AI doesn't jump yet — a bot jump heuristic
  is a future input-source change.
- Pre-existing: `DefaultHoverTank` has no `CreateOrUpdateBlueprint` call in
  `CreateDefaultBlueprints` (only a preset-list slot). Left as-is; SpringBot
  was wired the complete way (create-call + slot).
