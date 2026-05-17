# 84 — Relevance-gated chassis outlines (draw-call perf)

> Status: **code complete, machine gate pending green run.** The
> "done" gate per the plan + PERFORMANCE.md §3.3 is a user-side Frame
> Debugger before/after capture (see end). Render-architecture work,
> not terraforming.

## Why

Profiling the full-ground-dig arena showed FPS is **draw-call/SetPass
bound**, not triangle-bound (idle ~106K tris but ~1282 draws / ~401
SetPass; budget <100 SetPass). The user observed draws drop to ~160
with no robots in view and jump hundreds per robot. Root cause is the
documented PERFORMANCE.md §5.4 / §8.2 risk: MK Toon's
`MKToonPerObjectOutlines` runs a **second pass per chassis-block
renderer**, ×every robot. The dig feature was exonerated.

## Approach

Keep the MK Toon HullClip ink line **1:1**; gate *which* chassis get
the outline pass to the local player's chassis (always) + the player's
current aim target. Non-relevant chassis swap every block renderer to
a plain (no-`+ Outline`-shader) material, so the outline pass simply
isn't issued for them.

- New `OutlineMaterialRegistry` (runtime ScriptableObject, Resources-
  loaded) maps each outline material → plain counterpart.
- `BlockMaterialsPlain` (editor) builds the 5 plain `.mat` variants
  (Structure/Cpu/Thruster/Weapon/BombBay) + the registry; wired into
  `BlockDefinitionWizard` right after `BlockMaterials.BuildAll()`.
- `ChassisOutlineController` (per chassis root) caches block renderers
  off `BlockGrid` (same walk as `FollowCamera`, so rotor-adopted foils
  are covered), `SetOutlined(bool)` swaps shared materials, rebuilds on
  `BlockPlaced`/`BlockRemoving` (repair/combat). Allocation-free steady
  state.
- `TargetTracker` (camera) owns the single screen-centre raycast;
  `AimReticle` now reads it (**option B** — no extra per-frame ray).
  Fires `TargetChanged` on change only.
- `OutlineRelevanceManager` (lazy arena singleton, `IRelevanceSource`)
  keeps player always-outlined and the target conditionally outlined;
  MP seam = swap the source for a server "you target X" hint later.
- `ChassisAssembler` adds the controller in Phase 3 and registers the
  chassis (local-player via `AssemblyOptions.AddPlayerInputs`) after
  `SetActive` so the controller's OnEnable has cached the built grid.

## Two judgment-call deviations from the plan

1. **Registry instead of `BlockDefinition._materialPlain`.** Block
   runtime components don't surface their `BlockDefinition`, so a
   placed block can't be asked for its plain material. A renderer-
   keyed registry is decoupled from the block data model and unit-
   testable. (Plan Step 2 dropped.)
2. **Straight to material-swap, no MPB-first.** The MK Toon outline is
   a *separate shader asset* (`… + Outline`), keyword/pass-gated by
   validation — not a runtime float. A `MaterialPropertyBlock`
   `_Outline=0` cannot remove a pass that exists because of the
   shader+keywords, so MPB-first would only ever fail its own check.
   Swapping the shared material to the plain (base-shader) asset is the
   only mechanism that drops the draw. SRP-batch note: plain vs outline
   are already different shaders/batches; swaps happen on target-change
   only (rare), shared assets, no instancing churn.

## Tests

`OutlineMaterialRegistryTests` (PlayMode, pure): outline→plain map,
unknown/null pass-through, lookup rebuild on pair replace. The
manager/controller/assembler integration is validated by the Frame
Debugger gate below (Resources-asset + camera + Robot integration —
not meaningfully unit-testable; the plan made the capture the "done"
definition).

## Validation gate (user-side, required for "done")

1. Run **Robogame ▸ Build Everything** (generates the 5 plain mats +
   `Assets/_Project/Resources/OutlineMaterialRegistry.asset`; without
   it the feature is inert — controllers no-op, zero behaviour change).
2. With ≥2 robots in frame, Frame Debugger / Stats: record draws +
   SetPass with all-outlined (pre) vs player+target-only (post). Expect
   a large drop scaling with robot count. Confirm the player's bot and
   the aimed enemy still show the ink line; others don't.
