# 0008 — Data-driven block classification on BlockDefinition

- Status: **Accepted** (approved in the spring-cleaning session,
  2026-07-30; implementation landed the same session)
- Context: deep-review altitude findings (sessions 156–158 arc)

## Problem

"What kind of block is this?" was answered by hand-maintained id lists
scattered across the codebase:

- Leaf / side-mount / variant-config classification: three "defensive
  fallback" `HashSet<string>` lists in `BlockConnectivity` /
  `BlockVariants`, shadowing SO flags that were authored on 1 of 37
  assets. The lists had already drifted: Pogo/Gyro/Drill were in
  neither (structure could stack on a pogo foot), and
  ModuleMines/ModuleRepair were missing from the variant list its own
  comment says covers every module (their power slider was
  unreachable).
- The tip predicate (`Hook || Mace || Magnet`) was hand-copied at 7+
  sites (BlockGraph ×3, PlacementRules, BlockConnectivity,
  RobotTipBlockBinder, RobotWeaponBinder).
- `ChassisAssembler` inferred drive subsystems from hand id lists
  (`Wheel||WheelSteer`, `Aero||AeroFin`, `HoverBlade`) — the same trap
  that produced the mortar red-cube bug before LOG-132, still armed:
  Wing/Rudder are served by `RobotAeroBinder` but a Wing-only chassis
  got no `PlaneControlSubsystem`.
- The rotor→mechanism-cube companion pairing was re-derived in five
  places (BuildSession place/owner/cascade, BlockConnectivity's
  lateral-face rule, BlockGhostFactory's footprint).

## Decision

1. **The SO flags are authoritative; the fallback lists are deleted.**
   `_isLeafBlock`, `_sideMountOnly`, `_hasVariantConfig` are authored on
   every relevant definition. `BlockConnectivity.IsLeaf` /
   `RequiresSideMount` / `BlockVariants.HasVariantConfig` read only the
   flag. The string-only lookups (`IsLeafId`, `HasVariantConfigId`) are
   removed; callers hold definitions.
2. **Authoring lives in `BlockDefinitionWizard`.** The wizard re-stamps
   assets on every Build-Everything run (see TRACE[LOG-127]), so flags
   passed as `CreateOrUpdate` arguments are the durable source; hand
   edits to the .asset files would be reverted. HoverBlade and
   ModuleRepair are not wizard-managed (hand-authored; the wizard's
   white tint would erase the repair module's green) — their flags are
   authored directly on the assets.
3. **New field `_driveNeed`** (`DriveNeed { None, Ground, Flight,
   Hover }`) declares which chassis-level drive subsystem a block
   implies. `ChassisAssembler` unions the needs over the blueprint.
   Wheel/WheelSteer → Ground; Aero/AeroFin/Wing → Flight; HoverBlade →
   Hover. Rudder stays None deliberately: a rudder alone should not
   grant plane control authority to a ground bot (the old behaviour);
   `RobotAeroBinder` still binds its per-block behaviour.
4. **New companion spec**: `_companionBlockId` (empty = none) +
   `_companionLateralAttachIds`. A block with a companion auto-places
   `companionBlockId` at `cell + up` (companions sit along mount-up by
   contract); ownership resolution, cascade removal, and the
   lateral-face attach restriction all read the spec. Rotor authors
   companion = Cube, lateral = [Aero, AeroFin, Rope].
5. **Tips stay a code predicate** — `BlockIds.IsTipId`, single source
   next to the id constants. A new tip block requires a new `TipBlock`
   subclass and a `BlockIds` const anyway, so the property is
   intrinsically code-coupled; an SO flag would add authoring surface
   without removing a code edit. All 7 sites call the one predicate.

## Consequences

- New blocks classify by filling wizard arguments — placement rules,
  variant panel, assembler, ghost and graph pick them up with no code
  edits (except a genuinely new behaviour class).
- Fixes shipped with the migration: stacking on pogo/gyro/drill is now
  rejected; Wing-only chassis get flight control; Mines/Repair modules
  are tunable.
- The rotor/rope leaf-face exceptions (rotor accepts +up only; rope's
  tip face accepts tips only) remain code in `BlockConnectivity` — they
  are single-block semantics with no second consumer yet. Generalise
  them only when a second compound/chain block needs the hooks.
- An EditMode integrity test walks the shipped library and asserts the
  authored classification (leaf set, side-mount set, variant set,
  drive needs, rotor companion), so a wizard regression or hand edit
  fails loudly instead of silently reverting behaviour.
