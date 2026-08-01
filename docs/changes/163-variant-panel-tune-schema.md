# 163 — VariantConfigPanel declarative tune schema

Executes item 5 of the session-161 deferred list ("Skipped — needs
design sign-off"), full-pass scope approved by the user: all 8 slider
families migrate, foil/rotor presets + Advanced expander included.
Only the concoction chooser stays hand-built.

## What changed

- **New `TuneSchema.cs`** — descriptor types: `TuneField` (kind,
  label, per-id `Func<string,float>` min/max, snap, `TuneTarget`
  cache target, format/suffix, warn predicate, Primary/Advanced
  group), `TunePreset` (label + `(target, value)[]` writes),
  `TuneSchema` (per-id title, idle lead, fields, presets, section
  readout), `TuneContext` (null-safe session accessors).
- **New `TuneSchemaRegistry.cs`** — ghost-recipe-pattern registry
  (ADR-0008 trace), one entry per block id: foil (Aero/AeroFin/Wing
  share one schema; per-id bounds via Wing check), rope, rotor,
  hover, module (8 ids, one schema, per-kind bounds), weapon
  (SMG/Cannon), pogo. Foil/rotor presets, the lift estimator, and
  every readout func moved here verbatim.
- **`VariantConfigPanel.cs` 1621 → 1032 lines** — one generic
  section builder + one refresh/write/readout path replace the 7
  bespoke section builders, 10 slider handlers, 6 readout methods,
  preset appliers, and the foil expander plumbing. Per-family height
  constants replaced by `SchemaContentHeight` (derived from row
  counts; reproduces the old values exactly: foil 282/398, rotor
  194, rope 50, scalar 82). Concoction section unchanged; its
  combined-mode stack offset now reads the weapon schema height
  instead of the `ScalarContentH` constant.

Design deltas from the 161 sketch: `default` is subsumed by the
per-field `Resolve` hook (cache → display value, sentinel-aware);
the `chooser` kind was dropped as unused — the concoction list is a
live asset-backed list with pigment chips and stays bespoke.

## Behaviour preserved (verified live)

- Zero-sentinel contract: selecting rotor/weapon/pogo/hover/module
  and touching nothing leaves every cache at 0 (checked via
  `GetConfigForBlock`/`GetDimsForBlock` after selection sweep).
- Per-id bounds and resolved defaults (Wing 1.83/0.20/1.00 vs Aero
  1.00/0.08/0.90), module per-kind range + "Module —" title lead,
  RPM 10-step snap (373 → 370) with cache write + live CPU readout,
  rotor preset writes (Heavy Lift → pitch 12, RPM 360), foil
  Advanced expand/collapse with panel resize, SMG combined
  ammo+concoction stack, concoction list open.

## Verification

- Editor compile: 0 errors all assemblies. Console clean.
- run-tests.sh ×3 with the change: EditMode 495/496 every run
  (pre-existing inconclusive). PlayMode fail, fail, pass — both
  failures were the known MPTK `fluid_voice` Tuba-D1 NRE flake in
  `Garage_Idle_Baseline` (161 flake tally). A clean-HEAD control run
  passed 122/123. Verdict: the plugin race, not a regression, but
  the hit rate was elevated this session (2/3 vs the occasional hit
  in prior sessions) — flake tally updated, worth watching.
- Live Unity MCP game-view screenshots per family: foil collapsed +
  expanded, Wing, rope, rotor (idle + after preset + after slider
  write), hover, module (EMP), SMG combined (closed + open list),
  Mortar, pogo.

## Known quirks (pre-existing, not touched)

- Mortar/Bomb-bay panel: the "Concoction" header overlaps the title
  band — geometry is verbatim from the pre-rework section (label
  rect extends above the section top). Cosmetic; surgical-changes
  rule says leave it for a dedicated pass.
- Foil stall warn (`>18°`) is unreachable from the slider (max 18°)
  — also inherited; the warn path still exists for eyedropper-fed
  values.
