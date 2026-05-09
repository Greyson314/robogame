# Scalable Parts — Plan & Handoff

> **Audience.** A future Claude Code session (or human) picking up the
> scalable-parts feature cold.
>
> **TL;DR.** Replace fixed-tier "small wing / medium wing / large wing"
> SKUs with a single block whose physics-relevant dimensions are
> per-instance and authored in the garage. The architecture for this is
> already shipped for `AeroSurfaceBlock` and `RopeBlock`. This document
> commits to the design decisions, names the work that's left, and
> flags the invariants that constrain the implementation.
>
> **Status.** Plan. No code in this branch yet. Implementation should
> follow the planner-first workflow from `CLAUDE.md`.

---

## 1. Why this exists

Robocraft shipped one mesh, one icon, one balance pass per size variant
of every part. A small wing and a large wing were two UI elements with
two definitions, two costs, and two sets of stats. That pattern scales
linearly with content team size, which Robogame does not have, and it
flattens build expression because variants are quantised.

Robogame's existing `AeroSurfaceBlock` math (`Rigidbody.GetPointVelocity`
into the standard lift formula) is already continuous in span / chord /
thickness. The job is to expose that continuity to the player and to
extend the same pattern to other parts where it's a clean win.

This aligns with `GAME_DESIGN_PILLARS.md` §"Generic propulsion
primitives, no special-case archetype blocks": the same instinct that
says "no helicopter-rotor special block, just rotor + aerofoil
primitives" says "no tier 1 / tier 2 / tier 3 wing, just wing with
dims."

## 2. What's already shipped

The conceptual rubicon has been crossed. Specifically:

- **`ChassisBlueprint.Entry`** has a `Dims` field
  (`Assets/_Project/Scripts/Block/ChassisBlueprint.cs`, lines 58–64).
  The struct comment is the authoritative spec for what `Dims` means
  per block type. Today: `AeroSurfaceBlock` reads x=span, y=thickness,
  z=chord; `RopeBlock` reads x=segment count; everything else ignores
  it.
- **`AeroSurfaceBlock`** exposes per-instance min/max ranges
  (`MinChord = 0.20f`, `MaxChord = 2.50f`, `MinThickness = 0.02f`,
  `MaxThickness = 0.40f`) and a `Configure` path that reads from `Dims`
  with default fallbacks. The lift formula in `FixedUpdate` consumes
  the per-instance dims, not the `Aero.*` Tweakables.
- **`Aero.WingSpan/Chord/Thickness` Tweakables are cosmetic-only.**
  Per `PHYSICS_PLAN.md` §5 they drive `_wingMesh.localScale` only. If
  any future PR couples them back to lift / drag / hit-area, they
  must move to per-block blueprint config first. The contract is
  enforced by review.
- **`VariantConfigPanel.cs`** exists in
  `Assets/_Project/Scripts/Gameplay/`. The in-garage UI for editing
  per-block dims is at least scaffolded. State of completeness: needs
  audit before we extend it.
- **Serialization** carries `Dims` through save/load via the existing
  `BlueprintSerializer`. Saved blueprints survive a reload with their
  per-block dims intact.

## 3. Decisions committed (do not re-litigate)

These are settled. Future Claude Code sessions should not waste cycles
re-opening them without explicit user approval.

### 3.1 Hybrid: scaffolding parts scale continuously, effect parts stay discrete

**Continuously scalable** (parts whose physics has a clean scalar
parameterisation):

- Aerofoils — span, chord, thickness (already shipped)
- Wheels — radius, width
- Hover lifters — lift area
- Thrusters — thrust scales with cross-section
- Structural panels / rods — length, thickness

**Discrete archetypes** (parts whose effect doesn't have a clean
physical scale):

- Weapons — damage / RoF / range are balance knobs, not derived
  quantities. Different guns are different blocks.
- Modules (shield emitter, EMP, etc.) — the effect is discrete by
  nature. Different modules are different blocks.
- CPU — there is one CPU block.
- Tip blocks (Hook / Mace) — mass differential is the gameplay
  variable; they're already a clean two-archetype split, see
  `PHYSICS_PLAN.md` §3.

This matches the precedent in Besiege (scaffolding scales, effects are
discrete) and avoids the trap of trying to derive damage curves from a
weapon-scale slider.

### 3.2 Option (b) for grid occupancy: swept-volume check at placement time

A scaled block's *visual / physics extent* can exceed its 1×1×1 grid
cell. Without intervention, players can place other blocks into that
extent, which produces visible interpenetration with armour and weapon
blocks.

**Decision: prevent the overlap at placement time.**

Concretely:

- Every scalable block type implements a `GetSweptBounds(Dims, Up)`
  method that returns an oriented bounding volume in chassis-local
  space. AABB is acceptable for v1; oriented bounds are the
  follow-up if AABB proves too conservative.
- `BlockGrid` (or a new `BlockOccupancy` helper alongside it) maintains
  the union of swept bounds for the current chassis.
- Placement validation rejects a placement that would cause the new
  block's swept bounds to intersect any existing block's swept bounds.
- Scaling an existing block via the `VariantConfigPanel` is subject to
  the same check. The slider clamps to the largest dim that doesn't
  collide; the UI shows a red highlight on the offending neighbour
  when the player tries to push past the clamp.
- `BlueprintValidator` gets a new error class for "scaled block
  collides with neighbour" so blueprints loaded from disk that violate
  the rule (e.g. authored before the check existed) fail validation
  with a clear message rather than crashing physics.

The grid claim remains one-cell-per-entry. Connectivity is unchanged:
two blocks are connected if their grid positions are adjacent, not if
their swept bounds touch. This keeps the netcode contract (blocks
sorted by `Vector3Int`) stable.

### 3.3 Cost model: continuous, derived from dims

Continuous scaling without a continuous cost model reinvents the
fixed-tier problem. Decisions:

- Each scalable block type implements a `GetCpuCost(Dims)` method
  returning a single integer / float CPU cost.
- The cost function should be a smooth, monotonic function of the
  dominant axis. Volume (`x * y * z * baseCost`) is the default
  unless a part has a reason to deviate (e.g. wheels probably want
  cost ∝ radius² since rolling resistance and hub mass scale with
  radius non-linearly).
- The build-mode UI shows a live cost readout that updates as the
  player drags a scale slider. Cheap to implement, important for
  tuning intuition.

The cost model lives in the same spirit as `BlockDefinition` today:
data on the SO, code on the BlockBehaviour subclass.

### 3.4 No mid-match scaling

This follows from the existing hard invariant in `CLAUDE.md`:
"Building happens only in the garage. Blueprints are frozen at match
start." Per-block dims are part of the blueprint. They cannot be
edited in-arena.

## 4. Phased work plan

Each phase is independently shippable and testable. Each ends with a
session entry under `docs/changes/NN-slug.md` per the existing dev-log
convention.

### Phase 0 — Audit and de-risk

- Read the current `VariantConfigPanel.cs`. Confirm what it does today,
  what it writes to (must be the blueprint, not Tweakables — see
  invariant 5 in `CLAUDE.md`), and what UI affordances are already
  wired.
- Verify the aerofoil end-to-end path: in-garage scale slider →
  `ChassisBlueprint.Entry.Dims` → `AeroSurfaceBlock.Configure` →
  `FixedUpdate` lift formula. Document any gaps.
- Confirm `BlueprintSerializer` round-trips `Dims` correctly with a
  unit test if one doesn't exist.

**Exit criterion:** a written 1-page note on what's wired today, what's
broken, and where the gaps are. Goes in `docs/changes/NN-scalable-parts-audit.md`.

### Phase 1 — Swept-volume occupancy check

- Define `IScalableBlock { OrientedBounds GetSweptBounds(Vector3 dims, Vector3Int up); }`
  on `BlockBehaviour` or a sibling interface.
- Implement on `AeroSurfaceBlock` first (simplest case, already
  scalable).
- Add an occupancy index to `BlockGrid` (or a sibling `BlockOccupancy`)
  that maintains the swept-bounds union.
- Wire placement validation in the build-mode editor
  (`Assets/_Project/Scripts/Gameplay/BlockEditor.cs`) so attempted
  overlaps highlight red and block placement.
- Wire scale-time validation in `VariantConfigPanel` so the slider
  clamps at the first overlapping dim.
- Tests in `Assets/_Project/Tests/EditMode/Blueprints/` covering: a
  wing scaled to fit, a wing scaled to overlap (rejected), and a
  blueprint loaded from disk with overlap data (validation error).

**Exit criterion:** can author a 2× wing in the garage, cannot place an
armour cube into its swept volume, blueprint round-trips through
save/load and ASCII dump correctly.

### Phase 2 — Extend pattern to wheels and thrusters

- Add `Dims` interpretation to `WheelBlock` (radius, width) and
  thruster equivalents.
- Update `Configure` paths and the relevant physics math.
- Update `ChassisBlueprint.Entry.Dims` doc comment as new
  interpretations land — that comment is the spec.
- Build-mode UI for editing wheel radius / thruster size.
- Tests for each new scalable block.

**Exit criterion:** a buggy with custom-radius wheels and a plane with
custom-size thrusters drive correctly, persist correctly, and respect
the occupancy check.

### Phase 3 — Cost model

- `GetCpuCost(Dims)` on every scalable block.
- HUD readout in build mode showing total CPU and live delta as the
  player scales a part.
- Default volume-proportional cost model, with per-block overrides
  where the math demands it.
- A balance pass against the existing fixed-stat blueprints (Tank /
  Plane / Buggy / Helicopter) — confirm none of them suddenly cost
  3× what they used to.

**Exit criterion:** continuous cost model in production, default
chassis cost approximately preserved, build-mode UX makes cost
tradeoffs legible.

### Phase 4 — Structural blocks (panels, rods, beams)

This is the largest content-side win and the place where the
fixed-tier problem is most visible to players. Adds a small set of
generic structural primitives (panel, rod, beam) with continuous
length and thickness. Replaces N tiered cube variants with the same
2 or 3 primitives.

**Exit criterion:** the existing tiered structural cubes are
deprecated (kept for blueprint back-compat) and new builds default
to the scalable primitives.

### Phase 5 — Generalise `Entry.Dims` to a `configBlob`

Per `PHYSICS_PLAN.md` §6 open items. When a block needs richer
per-instance state than three scalars (paint colour, fire mode,
named-tag), extend `Entry` to `{blockId, position, up, configBlob}`
and version the serializer. The `Dims` field becomes the special
case for "block has up to three scalars."

This is not blocking phases 1–4 but is the natural follow-up.

## 5. Invariants to respect

Carried over from `CLAUDE.md` and `PHYSICS_PLAN.md`. Violating any
of these is a bug regardless of how clean the diff looks.

1. **No Tweakable affects gameplay outcomes.** The build-mode scale
   slider must write to `ChassisBlueprint.Entry.Dims`, not to a
   `Tweakable`. The `Aero.WingSpan/Chord/Thickness` Tweakables stay
   cosmetic-only (they drive `_wingMesh.localScale` for live preview;
   the lift formula reads from blueprint dims).
2. **Building happens only in the garage.** Scaling a block is a
   build-mode operation. Reject any code path that would mutate
   `Dims` during a match.
3. **Block-index ordering is part of the netcode contract.** Sorting
   by `Vector3Int` stays the canonical order. `Dims` is per-block
   data; it does not affect the index.
4. **Single Rigidbody per chassis.** Scaled aerofoils, wheels, and
   thrusters remain colliders / kinematic children of the chassis
   Rigidbody. No free bodies introduced by scaling.
5. **Default to zero baseline cost.** A scalable block at default
   `Dims` adds the same number of Rigidbodies / colliders as the
   fixed-size version it replaces. Larger dims do not silently
   spawn extra physics objects.
6. **No per-frame allocations.** `GetSweptBounds` and `GetCpuCost`
   must not allocate. Pre-size any helper buffers.
7. **Profile before claiming a perf characteristic.** The occupancy
   check is `O(blocks_on_chassis)` per placement. Profile on a 100+
   block chassis before claiming the validation cost is acceptable.

## 6. Open questions for the user

These need a decision before phase 3 lands. None block phases 0–2.

- **Cost-curve shape per block type.** Volume-proportional is the
  default but probably wrong for wheels (rolling-resistance scales
  with radius²) and possibly wrong for aerofoils (lift scales with
  area, not volume). Each scalable block type needs a one-line
  decision: "cost ∝ what?". Recommend doing this in a single
  spreadsheet pass after phase 2 lands so you can see all the
  numbers next to each other.
- **Discrete-snap vs continuous slider in the UI.** The underlying
  storage is continuous, but the build-mode UI could snap to 0.25-m
  increments to make builds more legible. Recommendation: snap by
  default, hold a modifier (Shift?) for fine-grained continuous.
- **Default for new placements.** When the player picks "wing" from
  the block palette, what `Dims` does it spawn at? Recommend the
  current fixed-size value (so build behaviour matches today's feel
  out of the box) with the slider visible immediately.
- **Migration of existing blueprints.** The four shipped presets
  (Tank, Plane, Buggy, Helicopter) currently have `Dims = zero` on
  every entry, which means "use block-default." That keeps working.
  But once Phase 4 deprecates tiered structural cubes, the migration
  needs to map old IDs to new (id, dims) pairs. Recommend a
  migration shim in `BlueprintSerializer.LoadVersionN` rather than
  rewriting the preset assets.

## 7. Non-goals

- **Sub-grid placement.** Blocks still snap to integer cell positions.
  Scaling changes a block's extent within / beyond its cell; it does
  not allow placement at non-integer positions.
- **Multi-cell grid claims.** A scaled wing visually extends beyond
  one cell but only claims one grid cell. Claiming multiple cells is
  a different feature with different connectivity implications.
- **Scaling weapons or modules.** See decision 3.1.
- **In-arena scaling.** See invariant 2.
- **Animated / runtime-changing dims.** Dims are set at build time and
  frozen until the next garage visit.

## 8. References

- `Assets/_Project/Scripts/Block/ChassisBlueprint.cs` — `Entry.Dims`
  field and its spec comment (lines 58–64).
- `Assets/_Project/Scripts/Movement/AeroSurfaceBlock.cs` — reference
  implementation. Constants at lines 84–90, Configure at ~354.
- `Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs` —
  in-garage UI scaffolding. Audit before extending.
- `Assets/_Project/Scripts/Block/BlueprintSerializer.cs` — save/load
  path. Already round-trips `Dims`; verify with test.
- `Assets/_Project/Scripts/Block/BlueprintValidator.cs` — extend
  with overlap-detection rule in Phase 1.
- `Assets/_Project/Scripts/Block/BlockGrid.cs` — occupancy index
  lives here or in a sibling.
- `Assets/_Project/Scripts/Gameplay/BlockEditor.cs` — placement
  validation hook.
- `docs/PHYSICS_PLAN.md` §5 — Tweakables-vs-blueprint contract.
- `docs/PHYSICS_PLAN.md` §6 — `configBlob` generalisation roadmap.
- `docs/GAME_DESIGN_PILLARS.md` §"Generic propulsion primitives" —
  the design instinct this plan operationalises.
- `docs/NETCODE_PLAN.md` §6 Bucket B — blueprint replication. `Dims`
  rides the same blob; cost is ~6 extra bytes per entry.
- `CLAUDE.md` — hard invariants. Re-read before starting.

## 9. How to start

Per the project workflow in `CLAUDE.md`:

1. Run the Planner subagent (`.claude/agents/planner.md`) over Phase 0
   to produce a concrete audit checklist.
2. Read `VariantConfigPanel.cs`, `AeroSurfaceBlock.cs`, and
   `BlueprintSerializer.cs` end-to-end.
3. Land the audit note in `docs/changes/NN-scalable-parts-audit.md`.
4. Phase 1 onward, run the Test Drafter in parallel — occupancy
   validation is exactly the kind of system that benefits from tests
   landing alongside the code rather than after.

---

*Plan written: 2026-05-08. Update this file when phases land — it's
the source of truth for the feature, not a write-once doc.*
