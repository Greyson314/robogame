# 101 — Active Module Slot (EMP / Blink / Disc Shield)

> Status: **Shipped.** First `/ideate`-approved feature built end-to-end.
> A garage-chosen keybind ability (Q) with a server-authoritative
> cooldown, tied to a destructible carrier block. Highest-payoff item
> from the session-100 ideation run.

## What landed

A new **Module-category** block (`block.module.active`) grants one
active ability, chosen per-chassis in the garage and frozen at match
start (invariant #2). Press **Q** in the arena to fire it; a cooldown
bar shows recharge. Destroy the carrier block and the ability goes dark
(functional disable, the same `isActiveAndEnabled` pattern weapons use).

Three abilities, one shared `ModuleDefinition` with per-kind tuning:

- **EMP Burst** — `Physics.OverlapSphereNonAlloc` disables enemy
  `ProjectileGun`s in radius for a few seconds (15 s cd / 3 s / 8 m).
- **Blink** — teleports the chassis Rigidbody forward, sphere-cast
  clamped so it never lands inside terrain (10 s cd / 12 m).
- **Disc Shield** — transient kinematic bubble that physically blocks
  incoming projectiles; the owner shoots through it because the bubble
  collider is folded into the owner's projectile-exclusion filter via
  `ProjectileWorld.InvalidateOwnerColliders` (20 s cd / 4 s / 2.5 m).

## Architecture

- `ModuleKind` enum lives in **Robogame.Block** (not Combat) so
  `ChassisBlueprint.ActiveModuleKind` can reference it without an asmdef
  cycle (Block ↛ Combat). The combat side references it the other way.
- `ActiveModuleSystem` (chassis root) owns the cooldown clock + input
  poll + effect dispatch. Cooldown tick and effect execution gate on
  `NetworkContext.Instance.IsServer` — the server-authoritative location.
  SP runs unchanged (offline stub is always server); the NGO seam is a
  one-line `TODO` pointing at `NetworkRobotCombat.FireCommandServerRpc`.
- `ActiveModuleBlock` (per-block) is the destructible carrier: resolves
  Kind from the blueprint + per-kind tuning from the `ModuleDefinition`,
  registers with the system on enable, unregisters on
  `BlockBehaviour.Destroyed` and on disable.
- `RobotModuleBinder : BlockBinder` attaches the block component;
  unconditional in the assembler (drag-on works), system added only when
  a module block is present (zero baseline cost, invariant #5).
- `ModuleEffects` holds the three pure executors + the transient
  `EmpDisable` / `ShieldBubble` helper components.

Input: `IInputSource.ModulePressed` added to all four implementers
(player reads **Q** directly like `ReloadPressed` reads R — no
InputActionAsset edit needed; bots stub false; `NetworkInputSource` +
`InputCommand` carry it on the wire for the future NGO phase).

VFX + audio (invariant #8): three new `VfxKind`s (`EmpBurst`,
`BlinkArrive`, `ShieldActivate`) with palette-locked recipes; two new
`AudioCue`s (`ModuleActivate`, `ModuleReady`) wired in the cue wizard.

HUD: `ModuleHud` (arena) shows ability name + cooldown fill, hidden when
no live module. `ModuleSelectHud` (garage) shows EMP/Blink/Shield
buttons, hidden unless the build carries a module block.

## Persistence

`ChassisBlueprint.ActiveModuleKind` serialises in `BlueprintSerializer`
(schema **v4 → v5**). v1–v4 saves load with the `EmpBurst` default.

## Files

New: `ModuleKind.cs`, `Combat/ModuleDefinition.cs`,
`Combat/ModuleEffects.cs`, `Combat/ActiveModuleBlock.cs`,
`Combat/ActiveModuleSystem.cs`, `Combat/RobotModuleBinder.cs`,
`Player/ModuleHud.cs`, `Gameplay/ModuleSelectHud.cs`,
`Tests/.../ActiveModuleSystemTests.cs`.

Edited: `BlockIds.cs`, `ChassisBlueprint.cs`, `BlueprintSerializer.cs`,
`IInputSource.cs`, `PlayerInputHandler.cs`, `GroundBotInputSource.cs`,
`AirBotInputSource.cs`, `NetworkInputSource.cs`, `NetworkRobotMovement.cs`,
`AudioCue.cs`, `VfxKind.cs`, `VfxSpawner.cs`, `AudioCueWizard.cs`,
`ChassisAssembler.cs`, `GarageController.cs`, `ArenaController.cs`,
`BlockDefinitionWizard.cs` (+ `Module_Default` asset),
`GameplayScaffolder.cs` (module block on the default Tank),
`DigZoneTests.cs` (interface stub).

## Assets

`BlockDef_ActiveModule` + `ModuleDef_Default` authored via the wizard;
library re-populated; default Tank preset now carries a module block on
its rear deck so the feature is immediately playable.

## Invariant compliance

- **#1** — all ability stats are config on `ModuleDefinition`, no Tweakable.
- **#2** — module choice frozen on the blueprint at match start.
- **#3** — cooldown + effects gate on `IsServer`; netcode seam marked.
- **#4** — Blink moves the existing chassis Rigidbody; the shield is a
  separate object with its own kinematic body (not part of the compound).
- **#5** — zero baseline cost without a module block.
- **#6** — EMP uses a shared buffer; no per-frame allocation.
- **#8** — ships with VFX + audio.

## Known follow-ups

- Bot AI never triggers modules (`ModulePressed` stubs false).
- Cooldown tuning is first-pass; needs a playtest balance pass.
- NGO: wrap the press in a ServerRpc when multiplayer lands.
