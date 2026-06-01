# 105 — Modules: multi-slot, per-block, tweakable ability system

> Status: **Shipped (code).** Run `Robogame → Build Everything` to bake the six
> module block defs, rebuild the library, and rebake the presets + scenes
> (the DefaultGround `ActiveModule` block → `ModuleEmp`, SpringBot → spring
> module). Until then two preset-validation tests fail on the stale
> `block.module.active` id baked in the old `.asset` — that's the bake step's job.

## What landed

The single-slot active-module system (session 101: one Q-bound ability per
bot, EMP/Blink/Shield, chosen via `ChassisBlueprint.ActiveModuleKind`) is
**unified** into a multi-slot, per-block system. A **Module** is now a block
whose *type* IS its ability; a chassis carries up to four, each on its own
cooldown, surfaced on a MOBA-style ability bar (keys **1–4**), each
independently tunable with a power slider that trades power for a longer
cooldown. The spring is the first module.

**Data spine (`Robogame.Block`, cycle-free).** `ModuleKind` gains `Spring`,
`Smoke`, `Invisibility`. New `ModuleKinds` maps block id ↔ kind (the block
self-describes its ability — no per-chassis kind field). New `ModuleTuning`
is the single source of truth for the power↔cooldown trade:
`cooldown = baseCooldown × clamp(power/defaultPower, 0.5, 2)`, plus per-kind
defaults. New `ModuleBudget` (`MaxModules = 4`) with a spawn-time `TrimToFit`.
`ChassisBlueprint.ActiveModuleKind` is **removed**; serializer bumped **v5→v6**
(a v5 JSON's `activeModuleKind` is now ignored — module ids self-describe).

**Runtime (`Robogame.Combat`).** `ActiveModuleSystem`→`ModuleSystem` holds a
`List<Slot>`, one per registered module, in canonical blueprint order (so a
key always drives the same module for the match — invariant #2). Each slot has
its own server-authoritative cooldown. `ActiveModuleBlock`→`ModuleBlock`
resolves its kind from its own block id, carries the spring coil visual +
grounded gate (`ContextAvailable`), and is the destructible carrier whose death
empties its slot. `ModuleEffects` gains `SpringLaunch`; EMP/Blink/Shield
unchanged. The old Movement `SpringBlock` + `RobotSpringBinder` are retired
(the spring launch folds into `ModuleEffects`; the unified `RobotModuleBinder`
binds every module id incl. spring). The cheap Space-hop is gone; the spring is
ability-triggered on a 10s base cooldown, default power 70 N·s (½ the old 140),
sliderable 0.5–2× for a proportional cooldown.

**Smoke + Invisibility.** Smoke is a visual-only obscurant: a lingering cloud
VFX + the deployer's healthbar surrogate hidden for the cloud's lifetime, no
stat change. Invisibility fades the bot's mesh renderers to ~6% alpha
(`StealthVisual`, a per-renderer `sharedMaterial` reference swap — cheap,
reverts exactly, can't bleed across bots) + hides the healthbar, ending at its
duration **or** after cumulative 5%-of-total-HP damage (tracked via the static
`BlockBehaviour.DamageDealt` event). Both raise `ModuleSystem.HealthbarHidden`,
which `VehicleStatsHud` consults to conceal the BLK line (the in-arena stand-in
for "your healthbar"; there's no world-space enemy bar yet).

**Input (wire-format change).** `IInputSource.ModulePressed` (one bool) →
`GetModulePressed(int slot)`. Player maps **1/2/3/4** (R is taken by reload /
hook-release; the digit row is free in arena). `NetworkInputSource` packs four
bits into a `byte ModuleMask` — a deliberate `InputCommand` wire bump.

**Garage.** Module blocks surface in the existing **Module** hotbar category.
`VariantConfigPanel` gains a module section: one "Power" slider (writes the
per-block `ConfigValue` through the already-plumbed
`SetVariantConfig`→`BlockConfig`→spawn path) with a live cooldown readout; its
range reconfigures per kind. The 4-module cap is enforced at **placement** (new
`PlacementError.ModuleLimitReached` + feedback message) and at **spawn**
(`ModuleBudget.TrimmedClone` beside the CPU trim in `ArenaController`). The
chassis-level `ModuleSelectHud` kind-picker is retired.

**HUD.** `ModuleHud` (single bottom-centre readout) → `ModuleBarHud`: a
bottom-centre row of up to four tiles, each with the ability name, its 1–4
keybind, a cooldown fill that rises as it recharges, and a greyed state when
unavailable — on cooldown, contextually blocked (spring tile reads "AIR" while
airborne), or carrier destroyed.

**VFX + audio (invariant #8).** New `VfxKind.SmokeCloud` (billowing slate
cloud) + `CloakShimmer` (pale-cyan phase sparkle); `AudioCue.SmokeDeploy`
(compressed-air fwoomp) + `Cloak` (digital air-lock sweep) — wired to proven
clips in the audio wizard, not missing-cue no-ops. Spring reuses
`SpringBurst`/`SpringLaunch`.

## Tests

EditMode `ModuleDataTests` (7): id↔kind round-trip, non-module rejection,
power→cooldown clamp + linearity, invis duration=power, budget trim drops the
5th + keeps the CPU. EditMode `BlueprintSerializerTests`: v6 module-block +
power round-trip, v5 tolerant load. PlayMode `ModuleSystemTests` (5):
slot-ready, two modules with independent cooldowns, destroy empties slot,
spring grounded-gate + launch, smoke hides the healthbar. All green; Unity
compiles clean (376/380; the 2 failures are the unbaked presets above, the
others pre-existing DigZone/MinimalArena gaps).

## Files

New: `Block/ModuleKinds.cs`, `Block/ModuleTuning.cs`, `Block/ModuleBudget.cs`,
`Combat/ModuleSystem.cs`, `Combat/ModuleBlock.cs`, `Combat/StealthVisual.cs`,
`Player/ModuleBarHud.cs`, `Tests/EditMode/Modules/ModuleDataTests.cs`,
`Tests/PlayMode/Combat/ModuleSystemTests.cs`.
Retired: `ActiveModuleSystem`, `ActiveModuleBlock`, `ModuleDefinition`,
`Movement/SpringBlock`, `RobotSpringBinder`, `ModuleSelectHud`, `ModuleHud`,
`BlockDef_ActiveModule`/`Module_Default` assets, old spring/module tests.
Edited: see git diff — Block (ids/kind/blueprint/serializer/variants/rules),
Combat (effects/binder), Input (interface + 4 impls + network),
Gameplay (assembler/arena/garage/variant-panel/feedback), Player (stats),
Core (vfx/audio), editor wizards + scaffolder.

## Invariant compliance

- **#1** power/cooldown ride `ConfigValue` + `ModuleTuning` code constants, no
  gameplay Tweakable. **#2** module set + power + slot order frozen at spawn.
  **#3** cooldowns + effects + stealth lifetime server-gated. **#4/#5** no new
  Rigidbody/joint on module blocks; dormant until a module registers. **#6**
  refs cached; per-frame is arithmetic; stealth swap is once-per-activation.
  **#8** every module ships VFX + wired audio.

## Follow-ups / known gaps

- **Run `Robogame → Build Everything`** to bake the six block defs + library +
  presets + scenes. Clears the two `block.module.active` preset failures.
- Bots return all-false `GetModulePressed`, so AI doesn't use modules yet.
- Smoke/invis healthbar-hide is local-only (no world-space enemy bar exists);
  `HealthbarHidden` is shaped server-authoritative for when one does.
- Dead code: `SpringTuningConfig` + `DevTuningOverride.ApplySpring` + the two
  `Dev.Spring.*` Tweakable keys are now orphaned (spring tunes via
  `ModuleTuning` + `ConfigValue`). Left compiling; safe to delete when the
  Tweakables dev-key registration is next revisited.
- Distinct per-kind module visuals (only spring has a bespoke coil today) and a
  bot module heuristic are future work.
