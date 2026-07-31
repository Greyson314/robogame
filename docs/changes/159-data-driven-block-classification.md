# 159 — Data-driven block classification (ADR-0008)

The unification pass from the spring-cleaning review: block-type
knowledge moves off hand-maintained id lists onto the definitions.
Full rationale in [ADR-0008](../decisions/0008-data-driven-block-classification.md).

## What changed

- **`BlockDefinition` grew the classification schema**: `_topMountOnly`
  (new — the mortar rule had no SO flag at all), `_driveNeed`
  (None/Ground/Flight/Hover), `_companionBlockId` +
  `_companionLateralAttachIds`. Existing `_isLeafBlock` /
  `_sideMountOnly` / `_hasVariantConfig` are now authoritative.
- **All four hardcoded fallback lists deleted** (leaf 18-id set,
  side-mount, top-mount, variant 17-id set) plus the string-only
  `IsLeafId` / `HasVariantConfigId` lookups. `VariantConfigPanel.
  IsVariableBlock` takes a definition now.
- **`BlockIds.IsTipId`** replaces the 7 hand-copied Hook‖Mace‖Magnet
  checks (BlockGraph ×3, PlacementRules, BlockConnectivity,
  RobotTipBlockBinder, RobotWeaponBinder, BlockEditor).
- **`ChassisAssembler`** unions `DriveNeed` over the blueprint instead
  of id lists; weapon detection stays category-based (LOG-132).
- **Companion spec consumed everywhere the rotor pairing was
  hand-coded**: BuildSession auto-place / owner-cell resolve / cascade
  removal, BlockConnectivity's lateral-face rule, and the
  leaf-exception "+mount-up stays connective" in both
  `IsConnectiveFace` and `AcceptsPlacement` now derive from
  `HasCompanion` — no `BlockIds.Rotor` checks remain in placement.
- **`SingleBlockBinder<T>`** base collapses the line-identical
  RobotPogoBinder / RobotGyroBinder to three lines each.
- **Authoring lives in `BlockDefinitionWizard`** (it re-stamps assets
  every run, so wizard arguments are the durable source). All ~35
  calls carry their flags; the wizard ran and the .assets are stamped.
  HoverBlade (`_driveNeed: Hover`) and ModuleRepair (variant flag)
  authored directly — they're not wizard-managed, and adding them
  would clobber the repair module's green tint.

## Behaviour fixes shipped by the data

- Stacking on Pogo / Gyro / Drill is now rejected (review finding —
  they were in neither the list nor the flags).
- Wing-only chassis get `PlaneControlSubsystem` (the id list only knew
  Aero/AeroFin). Rudder deliberately stays `DriveNeed.None`.
- ModuleMines / ModuleRepair power sliders are reachable (variant-list
  drift — the list's own comment claimed every module was covered).

## Tests

- `BlockConnectivityTests` / `BlockVariantsTests` rewritten as
  library-integrity suites: they walk the real
  `BlockDefinitionLibrary.asset` and pin the authored classification
  (leaf set, mounts, drive needs, rotor companion, tip predicate), so
  a wizard regression fails loudly.
- Synthetic-def factories in `PlacementRulesTests` /
  `MechanismOwnerCellTests` mirror the wizard classification via
  reflection (rotor companion spec, foil leaf flags).

## Deferred (phase 3 of the plan, separate sessions)

- BlockGhostFactory per-block rig recipes (ghost visuals still switch
  on id; footprint could read the companion spec).
- VariantConfigPanel declarative tune schema.
- Flip / hook-release verbs into IInputSource.
- Rope's tip-face exception stays code until a second chain block
  needs the hook.
