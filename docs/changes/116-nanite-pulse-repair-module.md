# 116 — Nanite Pulse: field self-repair module (new gameplay axis)

> New block. The game had no in-field healing — only the repair PAD that
> rebuilds DESTROYED blocks at base. The Nanite Pulse module tops up DAMAGED
> blocks mid-fight, opening a support/sustain build axis. Built overnight,
> autonomously, with the Unity bridge down (verified via the headless test rig).

## What it is

A `Module`-category block (`block.module.repair`, "Nanite Pulse"). Like every
module, it occupies an ability-bar slot, costs CPU, is frozen at match start,
and is server-authoritative. On activation it restores HP to the chassis's own
still-alive blocks within 8 m of the carrier — no offense, no enemy/ally effect.
The per-instance power (`ConfigValue`) sets HP healed per block; cooldown scales
with power like every other module (default 60 HP / 14 s).

Placement matters: the pulse is centred on the carrier, so a central mount
covers more of the hull than a corner one.

## Why a module (not a new system)

The module spine already gives slots, cooldowns, CPU budgeting, the ability bar,
trim-at-spawn, and serialization. Adding an ability is: enum value + id map +
one `Activate` arm + one effect method + tuning row. `BlockBehaviour.Heal`
already existed. So this is additive and low-regression — it touches no existing
behaviour, only adds a new branch.

## Files

- `ModuleKind.Repair = 7` (append-only); `BlockIds.ModuleRepair`.
- `ModuleKinds` — added to all four maps (`IsModuleId` / `ForBlockId` /
  `BlockIdFor` / `Label "REPAIR"`).
- `ModuleTuning` — `Repair => Row(60, 14, 0)`, instantaneous.
- `ModuleEffects.RepairPulse(owner, center, healPerBlock)` — heals own alive
  blocks within `RepairRadius` (8 m); iterates the live grid safely (healing
  never removes a block, unlike the splash-damage path).
- `ModuleSystem.Activate` — `case Repair` → `RepairPulse` + `VfxKind.RepairGlow`
  (reused, INV-8) + `AudioCue.ModuleActivate`.
- `BlockDef_ModuleRepair.asset` (+ `.meta`, guid `39679b06…`): Module category,
  90 HP, 2 kg, 24 CPU, healing-green tint, no mesh/material (primitive fallback).
- `BlockDefinitionLibrary.asset` — appended the new guid so the hotbar's Modules
  tab lists it (the hotbar enumerates `lib.Definitions` by category).

## Verification

- Headless rig: EditMode 304/304 (+1 Repair tuning test; the existing
  `ModuleKinds_RoundTripsEveryKind` auto-covers the new kind's maps). PlayMode
  `RepairPulse_MendsDamagedOwnBlock_InRangeOnly` pins heal + clamp + radius gate.
- INV checks: INV-1 (power is blueprint `ConfigValue`, not a Tweakable),
  INV-2 (module frozen at match start), INV-3 (server-authoritative effect),
  INV-5 (zero baseline cost — dormant until placed), INV-8 (VFX + audio on use).

## Editor-side finishing (2-minute morning step — bridge was down)

The CODE + asset + library wiring are on disk and test-green. To make it live +
pretty in the editor:
1. Open Unity (the library guid is already wired; the block should appear in the
   build hotbar's **Modules** tab). If not, run the block-definition-library
   populate wizard to re-scan.
2. Optional polish: author a dedicated emissive-green material (currently uses
   the primitive + green tint fallback) and a brief dedicated heal VFX (currently
   reuses `RepairGlow`).
3. Playtest the *feel*: place it, take damage, fire — confirm the heal reads and
   the cooldown feels like sustain, not invulnerability. Balance pass on
   60 HP / 14 s expected.

## Follow-ups

Proposed a new-blocks batch in `docs/research/idea-backlog.md` (Scatter Cannon,
Emissive Decor, Wedge/Slope set, Afterburner, Ram Spike) — ranked by
payoff-per-risk against existing patterns, for greenlight.
