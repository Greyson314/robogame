# 102 — CPU budget: spawn-time enforcement + garage fill bar

> Status: **Shipped.** Second `/ideate`-approved item. Like the
> block-graph-damage item before it, most of this was **already built** —
> per-block CPU cost, the cap shape, and a garage readout all existed. The
> net-new work is the part that makes the budget actually *bind*:
> connectivity-preserving strip-at-spawn, plus a visual fill bar.

## What already existed (no rebuild)

- **Per-block CPU cost** — `BlockDefinition.CpuCost`, aggregated into
  `Robot.TotalCpu`.
- **Cap shape** (the "open pillar question") — already decided in code:
  budget = (number of CPU-category blocks) × 250. Was duplicated in
  `BlockEditor` as a serialized `_cpuBudgetPerCpu = 250`.
- **Garage readout** — `BuildHotbar` showed `CPU used / cap` text that
  turned red when over budget, but placements were never rejected
  (explicitly advisory).

## What landed this session

**`Block/CpuBudget.cs`** — one source of truth for the cap. Holds
`BudgetPerCpuBlock = 250`, `UsedCpu` / `Capacity` / `IsOverBudget` over
blueprint entries, and the enforcer:

- `TrimToFit(entries, lib, out removed)` — when over budget, BFS distance
  from the CPU block(s), then remove non-CPU blocks **furthest-first**
  (peel from the periphery so connectivity holds), most-expensive as the
  tiebreak, until used ≤ cap. A final reachability sweep drops anything
  left disconnected. CPU blocks are never removed (they supply the cap);
  a CPU-less blueprint is returned untouched rather than stripped to zero.
- `TrimmedClone(src, lib, out removed)` — `Object.Instantiate` clone with
  trimmed entries, so the shared blueprint asset is never mutated.

**Enforcement at the freeze point.** `ArenaController.SpawnPlayerChassis`
trims a clone before `ChassisFactory.Build` when over budget, gated on
`NetworkContext.Instance.IsServer` (invariant #3; SP offline stub is
always server). This is the match-start freeze (invariant #2). The garage
stays advisory; the cut happens at spawn. Bots/targets are exempt — their
presets are authored within budget.

**`BlockEditor`** now reads `CpuBudget.BudgetPerCpuBlock` instead of its
own serialized field — the cap shape lives in one place.

**Garage fill bar.** `BuildHotbar` gained a horizontal fill bar under the
CPU readout (`Image.Type.Filled`), `fillAmount = used/cap`, green under
budget / red at-or-over. Complements the existing text line.

## Files

New: `Assets/_Project/Scripts/Block/CpuBudget.cs`,
`Assets/_Project/Tests/EditMode/Blueprints/CpuBudgetTests.cs`.

Edited: `Gameplay/ArenaController.cs` (spawn trim),
`Gameplay/BlockEditor.cs` (use shared const, drop local field),
`Gameplay/BuildHotbar.cs` (fill bar).

## Invariant compliance

- **#1** — cap is a code constant, not a Tweakable.
- **#2** — enforcement is at match-start spawn on a frozen blueprint clone.
- **#3** — trim gated on `IsServer`; the netcode-authoritative location.
- **#6** — `TrimToFit` runs once at spawn, not per frame; no steady-state
  allocation. Adds zero physics objects (perf-checker not needed).

## Tests

`CpuBudgetTests` (EditMode): over-budget line strips to fit while keeping
the CPU and a contiguous near-CPU run (connectivity preserved);
within-budget returns the input untouched; a CPU-less pile is not
stripped. Costs read from the live library so a retune doesn't rot them.

## Known follow-ups

- Bots don't enforce the budget (authored presets assumed in-budget).
- The garage still lets you *build* over budget (advisory); the cut is at
  spawn. A place-time hard block was deliberately not added — seeing the
  bar go red is the intended feedback.
