# 114 — Laboratory: player-authored explosive concoctions (ADR-0004)

> Large feature, /ideate-adjacent. A garage "Laboratory" where the player
> crafts custom explosive payloads ("concoctions"): three sliders (damage /
> size / knockback) that raise the recipe's CPU cost as you raise power, chosen
> per explosive block via the variant panel. First **player-content
> persistence layer** in the project — see [ADR-0004](../decisions/0004-concoction-persistence.md).

## What shipped (5 phases)

### Phase 1 — Data layer (`Robogame.Block`)
- `Concoction` — `{ id, displayName, damagePct, sizePct, knockbackPct }` (0..1,
  default 0.5). Piecewise multiplier curve: **50% = baseline** (0% → 0.5×,
  50% → 1.0×, 100% → 2.0×). `CpuSurcharge(baseCpu) = baseCpu × sliderSum × 0.5`
  (all-min → +0, all-default → +0.75×, all-max → +1.5×). `Validate()` clamps.
- `ConcoctionSerializer` (schema v1 JSON), `ConcoctionLibrary` (disk façade under
  `persistentDataPath/concoctions/`, mirrors `UserBlueprintLibrary`),
  `ConcoctionRegistry` (runtime lookup, `SubsystemRegistration` reset,
  clamp-on-register). `Entry.ConcoctionId` + blueprint **serializer v7**
  (v1–v6 load with `""` → no change). 14 EditMode tests pin the curve,
  surcharge, validation, JSON + v7 round-trip, pre-v7 back-compat.

### Phase 2 — CPU budget
`CpuBudget.EffectiveCpuCost(entry, def)` folds the surcharge in; `UsedCpu` +
`TrimToFit` route through it, so the garage spend bar **and** server
strip-at-spawn both price concoctions. Zero surcharge when no recipe is set
(INV-5). `ConcoctionRegistry.IsConcoctableBlock` (Bomb + Mortar) is the single
scope predicate.

### Phase 3 — Lab UI
`LabController` — full-screen garage overlay: saved-mix list, name field, three
sliders, live "dmg/size/kb ×, +N% weapon CPU" readout, Save/New/Delete. Saves
per-id files (overwrite-on-edit), reloads the registry on save so new recipes
are pickable immediately. Reached by a **"Laboratory" button** in
`SceneTransitionHud` (garage-only). `AudioCue.LabSave` declared (clip blank for
the audio pass — INV-8 partial-ship).

### Phase 4 — Variant-panel chooser
`VariantConfigPanel` gains an explosive section: a click-to-open concoction
dropdown ("(none)" + every saved mix) with a per-block CPU-surcharge readout.
`BuildSession` carries a `_concoctionByBlockId` "next placement" cache
(get/set/reset) and stamps `placed.ConcoctionId`; `SyncBlueprint` persists it
onto the entry. `BlockVariants` lists Bomb/Mortar so the VAR badge shows.

### Phase 5 — Fire-time application
`BombBayBlock.DropOne` + `MortarBlock.FireMortar` resolve the carrier's
`ConcoctionId` from the registry and scale damage / `SplashRadius` / knockback.
Scaling `SplashRadius` also scales the shockwave VFX + crater downstream (INV-8,
no `ProjectileSpec` change). `ChassisAssembler` stamps `placed.ConcoctionId`
from the entry (mirrors the `ConfigValue` precedent). `ArenaController` reloads
the registry before spawn (server-authoritative load; clamp-at-use).

## Invariants
INV-1: a concoction is blueprint-baked, server-loaded, server-clamped,
CPU-budget-governed build customization (governed exactly like foil pitch /
`BlockConfig`), **not** a dev Tweakable — full rationale in ADR-0004. INV-2:
frozen at match start; lab is garage-only. INV-3: registry populated + clamped
server-side. INV-5: zero cost with no recipe. INV-6: registry lookups are dict
hits, no per-frame alloc. INV-8: explosion VFX scales with size; LabSave cue
declared.

## Verification
- `run-tests.sh` (batch compile across all asmdefs + suites). Backend slice
  (phases 1/2/5): EditMode 302/303 (+14 new), PlayMode 113/114, 0 failed. Full
  feature incl. UGUI: [result on completion].
- Unity MCP bridge reconnected mid-session; the batch run is the compile gate.

## Follow-ups (queued via /ideate)
**Rider Effects** + **Concoction Identity** approved for after the core (see
idea-backlog). **Volatile Mixes** left `proposed`. Cannon deferred (Phase-1
scope was Bomb + Mortar). Re-editing a placed block's concoction follows the
existing "next placement" limitation of the variant system.
