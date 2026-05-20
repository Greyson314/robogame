# Building & Garage — From-First-Principles Architecture Review

> **Status.** Review document, not a plan-of-record. Written 2026-05-09 in
> response to the user's concern that the building / garage code is
> trending toward spaghetti.
>
> **Scope (per user's selection).** Placement core (grid, rotation,
> orientation, connection rules, block authoring) and blueprint data
> model (in-memory representation, JSON persistence, spawn ordering, the
> netcode contract). Excludes the broader garage UX shell (camera, input,
> palette UI) and the block-catalog authoring pipeline.
>
> **What this is not.** A line-by-line refactor PR. The "Refactor sketch"
> in §4 is a high-level migration ordering, not a checklist of edits.

---

## 1. Constraints carried in from the rest of the project

These are the rules a placement / blueprint system has to satisfy in
Robogame; everything below this doc derives from them. All sourced from
[CLAUDE.md], [docs/PHYSICS_PLAN.md] § 1, and [docs/NETCODE_PLAN.md] §§ 6–7.

1. **Server-authoritative gameplay.** Even today, structure code so the
   server can be a separate process. The blueprint is the wire payload
   (NETCODE_PLAN § 6 Bucket B).
2. **Block index ordering is part of the netcode contract.** Clients and
   server must arrive at the same `(cell → blockIndex)` mapping when
   they receive the same `SpawnRobotPayload`. NETCODE_PLAN nominates
   "sorted by `Vector3Int`" as the canonical order
   (`docs/NETCODE_PLAN.md`, line 202).
3. **Single Rigidbody per chassis.** Blocks are colliders parented to
   the chassis root, not free bodies.
4. **Building happens only in the garage.** Blueprints are frozen at
   match start. Mid-match the only mutation is *removal* (damage). No
   block additions in-arena.
5. **No per-frame allocations** in `Update` / `FixedUpdate` /
   `OnCollision*`. Pre-size buffers; reuse them.
6. **No `Tweakable` may affect gameplay outcomes.** Per-instance config
   that changes hit detection, lift, or anything visible to other
   players rides on the blueprint, not on local JSON.
7. **Default-zero baseline cost.** A scalable block at default `Dims`
   adds the same number of `Rigidbody`s and colliders as the fixed
   variant it replaces.

The shape of any "ideal" building architecture is essentially
determined by these. There isn't a lot of slack.

---

## 2. The from-first-principles design

I'll describe the system as a stack of layers, lowest to highest. The
test for whether the layering is sound is: each layer references only
the layers below it, and "upgrade in place" when netcode lands changes
nothing in the bottom four layers.

### Layer 1 — **Block schema** (pure designer data)

One `BlockSchema` (today: `BlockDefinition`) per block type. Holds the
identity, stats, and connectivity flags. Stable string ID, never
mutated post-ship. Crucially, the schema *also* declares:

- the block's **connectivity profile** (which faces accept hosts;
  which faces it can mount to);
- its **variant config spec** (what dimensions / parameters the player
  can scale per-instance, and the default-vs-min-vs-max for each);
- its **swept-bounds profile** (a function of `Dims + Up` returning
  AABB extents in chassis-local cells).

Today these three live in three different places: `BlockDefinition`
holds raw flags (`IsLeafBlockRaw`, `SideMountOnlyRaw`); `BlockConnectivity`
holds hard-coded fallbacks; `BlockOccupancy` holds per-id math; and the
variant UI knows the dim ranges. Pulling them onto the schema means
"add a new block" becomes one place to edit, not five.

### Layer 2 — **Blueprint schema** (the wire / disk format)

`Blueprint` is an immutable list of `BlockEntry { id, position, up, configBlob }`,
plus chassis-level metadata (`displayName`, `kind`, scalar flags).

Two non-negotiable guarantees:

1. **Entries are stored in canonical order** (sorted by `Vector3Int`,
   lexicographic on z then y then x — the exact order doesn't matter,
   *that there is one* does). The order is established at construction
   time, frozen by an immutable type, and never re-sorted later. This
   is the netcode contract.
2. **The serializer round-trips losslessly** with a single
   `schemaVersion` knob. No fields are dropped silently. Migration is
   "missing field defaults to X" with the default written into the
   schema, not implicitly via `JsonUtility`'s zero-value.

The blueprint layer should have **zero references to runtime types**
(`BlockBehaviour`, `BlockGrid`, `MonoBehaviour`). It's a pure-data
record that survives schema migrations.

`BlueprintBuilder` (fluent authoring) and `BlueprintValidator`
(pure-data validation) sit at this layer. They have no idea Unity
exists; they're trivially testable in EditMode.

### Layer 3 — **Spatial primitives** (pure functions)

Two static utilities:

- `BlockGeometry` — `OrientationFromUp(up) → Quaternion` and the
  six axis-aligned offsets. This is the "axis-and-orientation" math
  every other layer leans on.
- `BlockMirror` — cell + up reflection across X / Z planes. Pure
  data transform.

These are the ground truth for "how does an integer cell + a mount-up
vector turn into a world-space pose". Today's `BlockGrid.OrientationFromUp`
does this correctly, but lives on the data container instead of in a
geometry helper.

### Layer 4 — **Block graph queries** (pure, allocation-free)

Static helper, e.g. `BlockGraph`, with reusable thread-static buffers:

- `void BfsFrom(grid, root, into Buffer<Vector3Int> result, ignore = null)`
- `bool AreConnected(grid, a, b, ignore = null)`
- `void FindOrphansAfterRemoval(grid, removedCell, into Buffer<...> result)`

This is the layer that answers "is X reachable from CPU?", "what
becomes an orphan if I remove cell Y?". It has one purpose, it
allocates nothing per call (the buffers live as instance fields on the
caller and get passed in), and it's used by validation, placement,
removal, and damage propagation alike.

### Layer 5 — **Runtime grid** (`BlockGrid` + `BlockInstance`)

`BlockGrid` is a `Dictionary<Vector3Int, BlockInstance>` with mutators
(`Place`, `Remove`, `Detach`) that emit lifecycle events. It stores
state, not policy. Splash damage and connectivity queries are
delegated to `BlockGraph`; the grid's own job is "here's the cells and
their behaviours, here's how to add / remove".

`BlockInstance` (today's `BlockBehaviour`) is the per-cell
`MonoBehaviour`: definition reference, position, up, dims, pitch,
current HP. Its damage-tint visual logic is fine where it is.

The big simplification at this layer: **the grid does not own the
rules**. It just tracks state. The next layer owns rules.

### Layer 6 — **Placement rules engine** (pure, composable)

`PlacementRules.Evaluate(grid, candidate) → PlacementResult` where
`PlacementResult` is either `OK` or a specific
`PlacementError` enum value (`CellOccupied`, `NoAdjacentHost`,
`HostNotCpuReachable`, `HostFaceIsNotConnective`, `MountFaceForbidden`,
`SecondCpu`, `WouldOverlapNeighbour`, `WouldOrphanOnRemoval`).

The engine is composed of independent rule functions, each taking
`(grid, candidate, rules-context)` and returning one error or null.
Rules are pure; the engine is allocation-free given a reused result
buffer.

Two consumers:

- The **runtime build editor** runs Evaluate on every targeting frame
  to drive ghost color and place / remove decisions.
- The **validator** runs every rule against every entry in a loaded
  blueprint at load / save time. If the editor and the validator
  share the rule library, "what is a valid blueprint state" has *one*
  answer.

The current code's editor and validator overlap (CPU connectivity,
swept-overlap) and diverge (leaf-host, side-mount, second-CPU) —
that's the bug. Bring them onto a shared rule list and the divergence
goes away.

### Layer 7 — **Build session** (mutable, plain C#, testable)

`BuildSession` is a small stateful object that owns:

- a *reference* to the live `BlockGrid` (the chassis being edited);
- the variant cache (next-placement `Dims` / `Pitch` keyed by block id);
- mirror state (`Enabled`, `Axis`);
- selected block id.

It exposes verbs:

- `TryPlace(cell, hitFace) → PlacementResult`
- `TryRemove(cell) → PlacementResult`
- `SetVariantDims(blockId, dims)`, `SetVariantPitch(...)`
- `ToggleMirror()`, `SetMirrorAxis(...)`

These verbs apply atomic transactions to the grid, including the
mirror duplicate (best-effort, returns separate result for the mirror
so the view layer can decide whether to play the buzzer once or twice).
After every successful mutation, the session syncs back to the
canonical `Blueprint` — and that sync **enforces sort order**.

Crucially, `BuildSession` is plain C#, *not* a `MonoBehaviour`. It can
be driven by EditMode tests without a scene, exactly like
`MatchController` (per `docs/changes/architecture.md` line 154).

### Layer 8 — **View drivers** (Unity input + UI; thin)

Each driver is small and read-only against the session model:

- `TargetingRaycastDriver` — mouse → cell + face → push to session.
- `BlockGhostRenderer` — given `(def, dims, cell, up, validity)`, render
  a ghost. Knows how to do it for two ghosts (original + mirror).
- `BuildHotbarView` — UGUI for category / slot picker; emits
  `OnSelectedBlockChanged`.
- `VariantPanelView` — UGUI sliders / presets, generic over variant
  spec from the schema, no per-block-id branches.
- `BuildMirrorHud` — banner.

These drivers are pure presentation. They never touch `BlockGrid`
directly; they go through `BuildSession`.

### Layer 9 — **Spawn pipeline** (`ChassisAssembler`)

Single entry point: `ChassisAssembler.Assemble(root, blueprint, library, options) → ChassisHandle`.

Three explicit phases, in order:

1. **Scaffold** — `Rigidbody`, `BlockGrid`, `Robot`, `RobotDrive` —
   the always-on substrate.
2. **Subsystem inference + binder attach** — walk the blueprint, ask
   each schema "what subsystems do you imply?", add the union of
   those. Binders attach in the order their schemas declare. This
   replaces the inline `if (hasWheels) ... if (hasAero) ...` block in
   today's `ChassisFactory.Build` (lines 113–129) and the brittle
   "ORDER MATTERS" comment for tip-vs-rope binders (line 162).
3. **Block placement** — iterate `blueprint.Entries` in canonical
   order, call `grid.PlaceBlock(...)` for each. Binders subscribed in
   phase 2 receive `BlockPlaced` and self-wire.

`ChassisHandle` is a small record bundling `{ root, robot, blueprint,
library, spawnOrder }` so consumers like `RepairPad` can re-place
without re-walking `GameStateController`. This replaces the current
sidechannel of stashing `Robot.Blueprint` and `Robot.Library` directly
onto the `Robot` (`ChassisFactory.Build`, lines 86–87).

The same `ChassisAssembler.PlaceFromEntry(handle, entry)` is what
`RepairPad` calls. Mid-match block restoration goes through one path,
not two.

---

## 3. Diagnosis — where the current code matches, partially matches, or diverges

The shipped architecture already has the right shape in many places.
Calling out the good parts first because the refactor sketch needs to
*preserve* them.

### Things that are right

- **The `BlockDefinition` ↔ `BlockBehaviour` ↔ `BlockGrid` split**
  matches Layer 1 / Layer 5 cleanly. Static designer data on the SO,
  per-instance state on the MonoBehaviour, dictionary container with
  events. (`Assets/_Project/Scripts/Block/BlockDefinition.cs`,
  `BlockBehaviour.cs`, `BlockGrid.cs`.)
- **`BlueprintBuilder` + `BlueprintPlan` is good Layer 2 plumbing.**
  Pure data, fluent API, immutable result, validate-on-build helper
  (`BuildValidated`). Tests exercise it directly.
- **`BlueprintSerializer` has explicit DTOs and a `schemaVersion`
  knob.** v1 → v2 → v3 migration via missing-field defaults is the
  textbook approach.
- **`BlockBinder` (abstract) cleanly extracts the "subscribe to
  BlockPlaced and self-attach" pattern** that every per-block runtime
  behaviour needs.
- **`BlockGrid` events (`BlockPlaced`, `BlockRemoving`) are the right
  seam** for binders + subsystems + future netcode (`NetworkBlockGrid`
  per NETCODE_PLAN § 5).
- **`ChassisFactory.Build`'s "deactivate root → wire reflectively →
  reactivate" trick** (lines 72–74, 243) is the established pattern
  for `OnEnable`-runs-synchronously and is correctly documented in
  CLAUDE.md.
- **Leaf-host strict check** (`BlockEditor.IsValidPlacement`, lines
  324–333) correctly makes the player's aim authoritative — that's
  the right design call for the placement UX.

### Things that are partially right

- `BlockOccupancy` (Layer 3 / 4 hybrid) factored out the swept-bounds
  math into a static dispatcher and is allocation-free per call. The
  shape is right; what's wrong is that foil constants are duplicated
  from `AeroSurfaceBlock` because of an asmdef cycle, papered over by
  a unit test. The fix is moving the constants onto the schema (Layer
  1) so there's no duplication to keep in sync.
- `BlockMirror` is a clean pure-data helper (Layer 3). What's missing
  is that `BlueprintBuilder.MirrorX` / `MirrorZ` (build-time, takes
  `Action<BlueprintBuilder>`) re-implements the same reflection rule
  inline instead of composing over `BlockMirror.MirrorCell`. Two
  implementations of "mirror a cell" — both correct today, both are
  sites where future block-up shapes have to be updated together.
- `BlueprintValidator` runs the right kinds of checks (CPU presence,
  duplicate cells, connectivity, swept-overlap, pitch range), but
  doesn't run the *placement* rules: leaf-host, side-mount,
  second-CPU. A blueprint authored manually that violates one of
  those will load fine and only blow up when the player tries to
  extend it (acknowledged in `docs/changes/40-...` notes).

### Things that are wrong, in priority order

The numbering reflects severity / risk, not ordering of fixes.

#### 3.1 Block-index ordering not enforced (netcode-blocking)

NETCODE_PLAN § 6 promises that block IDs are deterministic from the
blueprint, sorted by `Vector3Int`. Nothing in the current code does
that sort.

- `BlueprintSerializer.ToJson` writes `_entries` in array order
  (whatever was authored / loaded). No sort.
- `BlockEditor.SyncBlueprintFromGrid` writes entries in
  `_grid.Blocks` dictionary iteration order. No sort.
- `ChassisFactory.Build` iterates entries in array order — fine, *but
  the source is unsorted*.
- `RepairPad.cs` line 50 explicitly notes this is unfixed: "CLAUDE.md
  flags a pre-existing concern that the serializer doesn't enforce a
  Vector3Int sort; that's upstream of this feature and is left
  alone".
- `Grep` for `Sort\|OrderBy` in `Assets/_Project/Scripts/Block/`
  confirms: only `UserBlueprintLibrary` sorts file names. No
  `Vector3Int` sort anywhere.

This is the netcode contract that landed in writing but not in code.
Confidence: **high** — verified by reading all four call sites and
grepping the directory.

#### 3.2 Per-frame allocations in `BlockEditor.IsValidPlacement`

Lines 681–682 and 725–726 of `BlockEditor.cs` allocate a new
`HashSet<Vector3Int>` and a new `Queue<Vector3Int>` on every call.
`IsValidPlacement` is called from `UpdateTarget` *every frame* (line
285), and again for the mirror cell when mirror mode is on (line
517). That's four heap allocations per frame for ghost-color updates
alone, plus another two per right-click via `WouldOrphanIfRemoved`.

This violates invariant #6 in CLAUDE.md ("No per-frame allocations").
Build mode is less hot than the arena, but the rule is the rule, and
the GC pressure shows up in the same Profile capture either way.

Fix is mechanical: hoist the buffers to instance fields, reuse them.
Confidence: **high** — verified by reading and grepping.

#### 3.3 `BlockEditor` is doing eleven jobs

The class is ~780 lines and owns:

1. Targeting raycast + face math.
2. Ghost rebuild orchestration (track 4 fields' worth of "what is the
   ghost built for").
3. Mirror-ghost orchestration (5 more fields).
4. Placement validation, composing six sub-rules inline.
5. Placement execution.
6. Removal validation (orphan-check via its own BFS, lines 663–702).
7. Removal execution.
8. Mirror placement / removal.
9. Blueprint sync (the un-sorted `SyncBlueprintFromGrid`).
10. CPU usage aggregation for the HUD readout.
11. Total chassis stats aggregation for the HUD readout.

Most of these belong elsewhere: validation in a rules engine, ghost
rendering in a `BlockGhostRenderer`, stats in a separate `ChassisStats`
service, blueprint sync as a `BuildSession` responsibility. The
targeting raycast is the only thing that *really* needs to live in a
build-mode `MonoBehaviour`.

This one isn't a bug, but it's the file that grows fastest with each
new feature and the place where future features (e.g. select-existing-
block-and-modify, per `docs/changes/38-...` Phase 1.b) will pile on
without a clear seam. Confidence: **high** — direct read.

#### 3.4 CPU-reachability BFS duplicated four ways

Same algorithm, four call sites, four implementations:

- `BlockEditor.BuildCpuReachableSet` (38 lines, runs every frame).
- `BlockEditor.WouldOrphanIfRemoved` (39 lines, runs on right-click).
- `BlueprintValidator.Validate` step 3 (21 lines, runs at load).
- `BlockGrid.GetReachableFrom` (22 lines, called by
  `FindDisconnectedFrom`).

All four implement BFS-from-CPU-via-six-axis-adjacency, with subtle
differences (ignore-cell parameter, leaf-skip historically, etc.).
None share code. The Layer 4 `BlockGraph` static helper this doc
proposes is exactly the consolidation point. Confidence: **high** —
direct read.

#### 3.5 Variant config bleeds across three layers

`VariantConfigPanel.cs` (820 lines) does:

- Owns the `_dimsByBlockId` and `_pitchByBlockId` "next placement"
  caches — model state.
- Builds the entire UGUI panel in code — view.
- Implements preset tables, advanced expander, slider snapping —
  controller.
- Mirrors `AeroSurfaceBlock.FixedUpdate`'s lift formula in
  `EstimateFoilLift` — physics duplication, with constants
  duplicated from `AeroSurfaceBlock` (just like `BlockOccupancy`).
- Hard-codes `Aero / AeroFin / Rope / Rotor` in `IsVariableBlock`,
  `HandleSelectedBlockChanged`, `BuildFoilSection` /
  `BuildRopeSection` / `BuildRotorSection` (each a separate UGUI
  builder).

`BuildHotbar` separately reads `VariantConfigPanel.IsVariableBlock(id)`
to decide whether to render a "VAR" badge — that means the hotbar
takes a dependency on the *Gameplay-layer* variant panel rather than
on a schema-layer flag.

When Phase 2 of `SCALABLE_PARTS_PLAN.md` lands (wheels, thrusters),
this all needs touching: a new section builder, a new entry in
`IsVariableBlock`, a new switch arm in `HandleSelectedBlockChanged`,
a new ghost-shape entry in `BlockGhostFactory`, a new dispatcher arm
in `BlockOccupancy`, and a new physics-formula mirror somewhere.
That's six places to keep in sync per scalable block.

The fix is to put the variant spec on the schema (Layer 1) and have
the panel render generically from it. Confidence: **high** — direct
read of all named files.

#### 3.6 Hard-coded specialization tables

The "what is special about this block" knowledge is scattered:

- `BlockConnectivity.s_hardcodedLeafIds` (13 entries).
- `BlockConnectivity.s_hardcodedSideMountOnlyIds` (2 entries).
- `VariantConfigPanel.IsVariableBlock` (4 entries).
- `BlockGhostFactory.Build` switch (11 cases).
- `BlockOccupancy.ComputeSweptBoundsLocal` switch (1 case so far,
  growing).
- `ChassisFactory.Build` lines 113–129 (block-id checks driving
  subsystem inference).
- `BlueprintBuilder.RotorWithFoils` (composite recipe by id).

Each new block ID needs a check across all these places. SO flags
exist (`IsLeafBlockRaw`, `SideMountOnlyRaw`) but the hard-coded lists
are described as "defensive fallback" — in practice, the SO flags
aren't the source of truth, the hard-coded lists are. Confidence:
**high** — direct read.

#### 3.7 `ChassisFactory.Build` has an "ORDER MATTERS" comment for
binder attach order

Lines 162–171: tip-block binder must be added before rope binder
because both `OnEnable` cascades fire in `AddComponent` order, and
`RopeBlock.OnEnable → TryAdoptTipBlock` looks for components added by
the earlier binder. This works but is fragile: any future binder
that has a similar dependency adds another constraint to a
brittle ordering, with no compile-time guard.

The Layer 9 fix is to make subsystem inference declarative —
schemas declare what they need and what they depend on, the
assembler topologically sorts the binders. Today there's nothing
between "I added the binders in the right order" and "I broke the
chassis silently".

Confidence: **medium-high** — the symptom and comment are right
there in code; whether the topological-sort fix is the right shape
versus a smaller "just declare a phase number" fix depends on how
many binder dependencies grow.

#### 3.8 `BuildModeController.Exit` reaches into `GarageController`

Line 116: `GarageController garage = FindAnyObjectByType<GarageController>(); garage.Respawn();`
This couples build mode to the garage scene specifically. If the
build-mode model later needs to apply outside the garage (e.g. an
in-arena practice loadout, or a network preflight), that
`FindAnyObjectByType` becomes a problem. The cleaner shape is for
`BuildModeController` to fire `Exited` and the garage to
self-subscribe.

Confidence: **medium** — this is a small leak, not a bug today. It's
worth flagging because the build-mode lifecycle is the natural seam
for the eventual "spectator builder" / "view someone's loadout"
features.

#### 3.9 `BuildSession` state is split across four `MonoBehaviour`s

`GarageController.EnsureBuildModeWired` adds five components on the
same `GameObject` (`BuildModeController` + `BlockEditor` +
`BuildHotbar` + `VariantConfigPanel` + `BuildMirrorMode`) and wires
their public properties to each other imperatively, because they're
added at runtime via `AddComponent` and can't be set in inspector.

The state these five components together represent — selected block,
variant cache, mirror toggle, ghost validity, hover target —
*conceptually* belongs to a single "current build session" object.
Today it's spread out, and the wiring is a 27-line method (lines
211–237) that every new build-mode addition touches.

Confidence: **high** — direct read.

#### 3.10 Targeting and rules speak different languages

The build-mode targeting model uses Unity's physics raycast against
transform-attached colliders (`BlockEditor.UpdateTarget`, lines
248–286). The placement rules engine uses grid cells. These two
models drift in two ways:

- **The host primitive cube's collider is the targeting volume**, per
  `BlockVisuals.HideHostMesh`'s comment ("collider intentionally
  preserved … for damage raycasts and build-mode targeting"). The
  collider is a unit cube at the cell. The block's *visual* may
  extend further (a span-1.5 wing's mesh extends 0.25 cells past
  its host cell). So a player clicking on the visible part of an
  extended wing actually hits empty space near the wing's base, not
  the wing itself.
- **The host-cube collider is at the *cell*, regardless of mount
  face.** When a player aims at the +Z face of a mechanism cube
  while existing blades occupy the ±X faces, their screen ray can
  land on a sibling blade's host-cube collider instead. The
  targeting code then computes `_targetHitCell = sibling.GridPosition`,
  derives `up` from the sibling's face normal, and the rule engine
  fires "host is leaf → reject" — even though the *intended* host
  (the mechanism cube) is a non-leaf and the placement is legal.

The result: **legal placements look impossible**. The user sees the
ghost flash red (or never appear) and concludes "the rules say no."
Actually the rules say yes for the cell they meant; the targeting
landed on a different cell. The player has no way to debug the
mismatch from inside the game.

Architecturally, this is targeting and rules answering different
questions: targeting asks "which cell is your cursor over?" and
answers via collider physics; rules ask "is this candidate
placement legal?" and answers via grid topology. The two need to
agree on cell intent.

Possible fixes (in increasing scope):

- **Cheap, immediate: surface the rule rejection in the HUD.** Show
  *which* cell the targeting picked and *why* the rule rejected.
  Right now the player only sees a red ghost or no ghost; with a
  one-line "host is leaf at (1,1,0)" overlay, the mismatch becomes
  diagnosable.
- **Medium: cell-snap the targeting to the nearest face the rules
  would accept.** When the raw raycast lands on a leaf, walk
  outward by one cell and check whether the *adjacent* non-leaf
  cell's face is what the player likely meant. This is what
  Robocraft does; it's a few rays in `UpdateTarget` instead of
  one.
- **Large: rebuild targeting on the swept-volume model.** Cast a
  thin ray and pick the cell whose swept bounds the cursor is
  pointing into, regardless of which collider was technically hit.
  Decouples targeting from collider authoring entirely.

Confidence: **medium** — the targeting code path is verified, the
rule-vs-targeting divergence is verified, but I haven't reproduced
the specific "can't place 3rd / 4th blade" repro to confirm this is
*the* root cause for that bug versus a secondary contributor.

---

## 3a. Worked example — four user-reported bugs as instances of one structural problem

Added 2026-05-09 in response to user feedback. These are real bugs the
user has hit in build mode. They share a single root cause and that
shared cause is the load-bearing reason this review exists.

### The four bugs

1. **Mirror produces opposite tilts.** Player sets a heli blade's
   pitch to +18°, places on +X face of the rotor mechanism. The
   blade renders root-low, tip-high (or the inverse, depending on
   sign). Player flies the camera to the −X face and places again.
   Same +18° pitch produces the *opposite* visual: root-high,
   tip-low. The flight surfaces don't behave symmetrically.
2. **Ghost preview ignores pitch.** The ghost cube the player sees
   while aiming reflects span / thickness / chord but is always
   flat. The placed block has the pitch applied; the preview
   doesn't. Player can't see what they're about to commit to.
3. **Placed blade renders as a full-size cube.** Sometimes
   (specific repro unclear), placing an aero block produces a
   plain primitive cube rather than the wing mesh. The block is
   functionally there in the grid, but its visual didn't get
   built.
4. **Can't place blades on remaining sides.** After successfully
   placing two blades on opposite sides of a rotor mechanism, the
   third and fourth sides silently refuse the placement. Reproduces
   with flat blades, so it isn't pitch-related.

### How each traces to the structure

A blueprint `Entry` is a tuple `{ BlockId, Position, Up, Dims, Pitch }`.
Each subsystem in the build pipeline touches a *subset* of those
fields. There is no compile-time guard requiring "if you handle an
Entry, you handle every field of it." When the schema grew `Up` in
v2, `Dims` in v2, and `Pitch` in v3, every subsystem needed manual
threading. The bugs are exactly where that threading was missed.

| Bug | Root cause | Subsystem missing a field | Connects to |
|---|---|---|---|
| 1. Mirror opposite tilts | `BlockMirror` reflects cell + up, not pitch. `TryMirrorPlace` (`BlockEditor.cs` line 643) literally has the comment "Pitch is scalar — passes through unchanged. Symmetric foil trim depends on it" — which is geometrically backwards. | `BlockMirror` has `MirrorCell` + `MirrorUp`. Missing `MirrorPitch`. | §3.6 (specialization scattered), the schema being the spec. |
| 2. Ghost ignores pitch | `BlockGhostFactory.BuildWing` (`BlockGhostFactory.cs` line 140) reads dims, doesn't read pitch. The placed block's `AeroSurfaceBlock.ApplyOrientationToVisual` (line 491–502) reads both. | Two implementations of "render this foil" diverge by exactly one field. | §3.5 (variant config split across layers). |
| 3. Cube fallback | `BlockGrid.PlaceBlock` (line 142–151) creates a primitive cube as the host when `Definition.Prefab` is null. `AeroSurfaceBlock.EnsureRig` (line 397–404) is then *supposed* to hide that cube and build `_wingMesh`. If the chain breaks anywhere — binder timing, `OnEnable` order, missing component data — the host cube stays visible. | Visual rebuild is implicit, not part of the schema. The ORDER MATTERS comment in `ChassisFactory.Build` line 162 is the same shape. | §3.7 (binder ordering fragile), §3.6 (specialization scattered). |
| 4. Can't place 3rd / 4th blade | Hypothesis (medium confidence): targeting raycast lands on a sibling blade's host-cube collider instead of the mechanism's face. Rules engine sees `host = leaf foil` and rejects. | Targeting uses physical colliders; rules use grid cells. They disagree on "what cell is the player aiming at." | New diagnosis §3.10. |

Three of the four (1, 2, 3) are different masks of the same
underlying issue: per-block knowledge is spread across files that
each handle a subset of the relevant fields, and there's no
single place that says "this is what an Aero block IS." The fourth
(bug 4) is a separate architectural divergence between targeting
and rules that doesn't come up in normal vehicle-builder reviews
but is the right framing once you've seen it.

### What this means for the worry

The user's framing — "the agent doesn't seem like it's grasping the
set-of-rules-and-relations nature of the garage" — maps onto a
concrete property of the code: a blueprint Entry's fields are
*coherent relations* (mount-up determines orientation, which
determines what pitch means, which determines what mirror should do
to pitch, which determines what the ghost should render), but the
code treats them as orthogonal scalars. Each new feature touches
two or three fields and ships, the others drift, and a future
feature that exercises the missed combination produces a "weird
bug."

This *will* keep happening until the schema becomes the single
source of truth (refactor §4 step 5) and every subsystem that
operates on an Entry is required by interface to handle the full
Entry (refactor §4 step 3). Until then the spot fixes for these
specific four are cheap (most are < 30 minutes), but they're
treating symptoms.

### Spot fixes vs. the structural fix

Each of the four bugs has a small, targeted fix that ships value
this week without waiting for a refactor:

- **Bug 1**: add `BlockMirror.MirrorPitch(pitch, axis, up) → float`
  that flips the sign when the mirror reverses the chord-axis
  mapping. Update `TryMirrorPlace` to call it. Half a day with
  tests.
- **Bug 2**: thread `pitchDeg` through `BlockGhostFactory.Build` and
  `BuildWing`; have `BuildWing` apply
  `Quaternion.AngleAxis(pitchDeg, Vector3.forward)` to the spawned
  wing primitive's localRotation. Quarter day. The same constants
  / formula already live in `AeroSurfaceBlock.ApplyOrientationToVisual`
  — duplicating once more is not great, but it's no worse than
  what's there. The structural fix is to share via the schema.
- **Bug 3**: instrument the build path. When a foil is placed, log
  loud + visible ("FOIL VISUAL FAILED — host cube is showing") if
  `_wingMesh` isn't set N frames after placement. Won't fix the
  underlying cause but turns a silent failure into a loud one. Then
  fix the actual cause once you can repro it.
- **Bug 4**: add the cheapest variant of the §3.10 fix — show the
  raw target cell and the rule rejection reason in a small overlay
  text under the ghost. Player can see "host = leaf at (1,1,0) →
  not the mechanism cube you wanted; orbit camera and try again."
  Half day.

These are all worth doing. They don't substitute for the structural
fix; they buy time for it.

The structural fix is refactor §4 step 5 (push per-block knowledge
onto the schema) plus the new §4 step 3 (`IBlueprintEntryTransform`)
that gives a compile-time guard against subsystems silently dropping
Entry fields. When both land, "knowledge of a block" lives in the
schema, "knowledge of an Entry" is enforced by the interface, and
this class of bug stops shipping.

---

## 4. Refactor sketch

Listed in suggested execution order. None of these is a "rewrite the
build mode" — each is a focused extraction that leaves shipped
behaviour intact.

### Step 1 — Lock the netcode contract: enforce `Vector3Int` sort

The single highest-leverage fix. About a day's work.

- Add a static `BlockEntries.SortCanonical(entries)` helper (compare
  on z, y, x lexicographically — the exact order is arbitrary, *that
  there is one* is what matters).
- Make `ChassisBlueprint.SetEntries` call it. That's the chokepoint:
  every path that mutates the entries array goes through there.
- Update `BlueprintSerializer.TryFromJson`'s final `bp.SetEntries(...)`
  to rely on this (it does, transitively).
- Update `BlockEditor.SyncBlueprintFromGrid` to rely on this (it
  does).
- Update `GameStateController.CloneBlueprint` to rely on this (it
  does, via `SetEntries`).
- Add a regression test in `Tests/EditMode/Blueprints/` asserting
  that `Entries` is canonically sorted after every mutation.

This is a one-line behavioural change with zero gameplay impact and
satisfies NETCODE_PLAN § 6 line 202. Highest-priority because it
removes a future-Claude-Code session's "wait, this needed to land
*before* you started phase 1 of NGO".

### Step 2 — Extract `BlockGraph` and remove the per-frame allocations

Half a day's work.

- New `Robogame.Block.BlockGraph` static class with reusable buffers
  passed in as `out`-style ref args (or, simpler, a small
  `BlockGraphBuffers` struct the caller holds as a field).
- Methods: `BfsFrom(grid, root, ignoreCell, into Buffer<Vector3Int> visited)`.
- Replace `BlockEditor.BuildCpuReachableSet` and
  `BlockEditor.WouldOrphanIfRemoved` with calls into it. Hoist the
  buffer fields onto `BlockEditor`.
- Replace `BlockGrid.GetReachableFrom` and the BFS in
  `BlueprintValidator` step 3 with the same calls.
- Test that the per-frame allocation count drops to zero in Profile.

Removes invariant #6 violation, removes 110+ lines of duplicated BFS,
gives every future placement / damage / removal feature a
well-typed primitive to call.

### Step 3 — Add an `IBlueprintEntryTransform` interface

Half a day; very high leverage. Added 2026-05-09 in response to user
bug reports — see §3a.

The bugs in §3a all arise from subsystems handling a *subset* of
Entry fields and silently shipping when a new field is added that
they should also handle. Define an interface every entry-touching
subsystem implements:

```csharp
interface IBlueprintEntryTransform {
    BlockId TransformBlockId(BlockId id);
    Vector3Int TransformPosition(Vector3Int pos);
    Vector3Int TransformUp(Vector3Int up);
    Vector3 TransformDims(Vector3 dims);
    float TransformPitch(float pitch);
}
```

Default implementation is identity. Real implementations (mirror,
ghost factory, serializer, validator) override the methods they
care about. Adding a new field to `Entry` becomes a compile error
in every implementer — they have to make a deliberate choice
("identity" or "actually transform this").

This is the compile-time guard that would have prevented bug 1
(`BlockMirror` missing `Pitch`) and bug 2 (`BlockGhostFactory`
missing `Pitch`). Land before step 4 because it's the contract
step 4 enforces against.

### Step 4 — Extract a `PlacementRules` engine

Half-week's work, mostly mechanical.

- New `Robogame.Block.PlacementRules` with `PlacementResult`,
  `PlacementError` enum, and one function per rule
  (`CheckHostExists`, `CheckHostIsCpuReachable`,
  `CheckHostIsConnective`, `CheckMountFace`, `CheckSecondCpu`,
  `CheckSweptOverlap`, `CheckOrphanOnRemoval`).
- Each rule is allocation-free; takes `(grid, candidate, buffers)`.
- An `EvaluateAll(grid, candidate, buffers, out PlacementError)`
  that runs the rules in priority order, short-circuits on first
  failure.
- Refactor `BlockEditor.IsValidPlacement` to call `EvaluateAll`.
- Refactor `BlueprintValidator.Validate` to run the same rules over
  every entry.

Net: editor and validator agree on validity, the ghost can show
*specific* failure reasons (e.g. red ghost + "second CPU" tooltip),
and a future feature like "select existing block and resize" gets a
single function to call.

### Step 5 — Move per-block specialization onto the schema

Couple of days' work.

- Extend `BlockDefinition` with the connectivity profile (per-face
  bitmask is the natural shape; binary leaf flag is the v1 special
  case), the variant config spec (`IVariantConfigSpec` with name,
  range, default, snap), and the swept-bounds profile (a function
  pointer or a small `IBlockOccupancy` interface).
- Move `BlockConnectivity.IsLeaf` / `RequiresSideMount` to read from
  the schema directly. Keep the hard-coded fallback list as an
  *editor-only* migration aid that's stripped from runtime.
- Move `VariantConfigPanel.IsVariableBlock` to a schema flag.
- Move `BlockOccupancy.ComputeSweptBoundsLocal`'s per-id switch into
  a dispatch table populated from schemas (or a virtual call on a
  per-schema strategy).
- Move `BlockGhostFactory`'s per-id switch the same way.

Per the asmdef-cycle constraint, the schema layer can't reference
`Robogame.Movement` directly (where `AeroSurfaceBlock` lives). The
existing `BlockDefinition.ComponentData` field (a `ScriptableObject`
sidecar with a per-kind cast helper, lines 89–119 of
`BlockDefinition.cs`) is the precedent for this — extend that pattern
so foil-specific data lives on a `FoilBlockData` SO referenced from
the cube `Aero` definition. `AeroSurfaceBlock` reads its constants
from there. `BlockOccupancy` reads them too. No more duplication, no
asmdef cycle.

This is the largest of the steps; the deliverable that makes
"adding a new block type" go from six places to one.

### Step 6 — Extract `BuildSession` and split `BlockEditor`

Couple of days.

- New `Robogame.Gameplay.BuildSession` plain C# class (not a
  MonoBehaviour). Owns: live grid reference, variant cache, mirror
  state, selected id. Verbs: `TryPlace`, `TryRemove`,
  `SetVariantDims`, etc. Returns `PlacementResult` so views can
  decide what to render.
- `BlockEditor` shrinks to a `MonoBehaviour` driver: reads mouse,
  asks session "what would happen at this cell?", drives
  `BlockGhostRenderer`. ~150 lines instead of 780.
- New `BlockGhostRenderer` MonoBehaviour: one or two ghosts (mirror),
  pose them, color them. Owns the `_ghostBuiltFor*` cache fields.
- `VariantConfigPanel` keeps its UGUI but reads / writes through the
  session. Loses `_dimsByBlockId` / `_pitchByBlockId` (now session
  state).
- `BuildMirrorMode` becomes a thin view + hotkey adapter over
  `BuildSession.Mirror{Enabled,Axis}`.

Tests appear naturally: `BuildSession` is plain C#, drive it with
authored grids, assert on `PlacementResult`s.

### Step 7 — `ChassisAssembler` refactor with declarative subsystem inference

Half a week.

- Rename `ChassisFactory` → `ChassisAssembler`.
- Replace `Build` and `BuildTarget` with one `Assemble(root,
  blueprint, library, AssemblyOptions)` — the player-vs-bot-vs-target
  difference becomes a flag set on `AssemblyOptions`.
- Each subsystem registers itself: `[SubsystemFor(BlockId.Wheel)]
  RobotWheelBinder`. Assembler iterates blueprint, asks "which
  subsystems do these block ids imply?", attaches in declared
  dependency order.
- Tip-binder before rope-binder dependency goes from "ordering by
  how I wrote AddComponent calls" to "tip-binder declares it
  satisfies a dependency that rope-binder requires".
- Returns `ChassisHandle { root, robot, blueprint, library,
  spawnOrder }`. `RepairPad` reads from the handle, doesn't reach
  into `Robot.Blueprint`.

This is the biggest step in raw line count but mostly mechanical
once the registration mechanism is in place. It pays off the moment
you add a new propulsion type (per
`SCALABLE_PARTS_PLAN.md` Phase 4 structural blocks, etc.).

### Step 8 — Decouple `BuildModeController` from `GarageController`

Half a day.

- `BuildModeController.Exit()` fires `Exited` and stops there. No
  `FindAnyObjectByType<GarageController>`.
- `GarageController` subscribes to `Exited` in `EnsureBuildModeWired`
  and calls its own `Respawn` on the event.

Trivial change, but it's the seam that makes the eventual "view a
friend's loadout in a non-garage scene" feature possible without
redoing build-mode lifecycle.

### Out of scope but worth noting

- The blueprint **wire format** (per NETCODE_PLAN § 6 Open Question
  Q3) — needs measuring. The current JSON is fine for disk; netcode
  needs a packed binary form. That's a Phase 0 net-readiness item
  (NETCODE_PLAN § 15 Phase 0), separate from this cleanup.
- `BlueprintAsciiDump` and `UserBlueprintLibrary` weren't reviewed in
  depth — they're well-bounded utilities and don't show in the
  diagnosis. If they touch the sort-order bug after Step 1 lands,
  worth a glance.
- The garage UX shell (camera, hover-clamp, weapon-disable) was
  excluded per the user's scope selection. It's fine where it is;
  the only thing that touches the rest of this doc is
  `BuildModeController.Exit`'s reach into `GarageController`,
  covered in Step 7.

---

## 5. What would be left after this refactor

The aim is a structure where:

- A new block type lives in **one place** (its `BlockDefinition` SO +
  optional sidecar component-data SO).
- Adding it to the build editor is **zero code edits** — the schema's
  variant spec drives the panel, the schema's swept-bounds drives
  the ghost factory, the schema's connectivity flags drive the
  rules engine.
- Build-mode logic is **testable** without a scene because
  `BuildSession` is plain C#.
- The blueprint's wire shape is **deterministic across machines**
  because every mutation chokepoints through `SetEntries`'
  canonical sort.
- The placement rules are **one library**, used at load (validator)
  and at click (editor). One source of truth for "what is a valid
  blueprint state".
- The chassis spawn pipeline is **explicit and declarative**: schema
  declares dependencies, assembler resolves them. No "ORDER
  MATTERS" comments.

None of those changes the player-visible behaviour. They reshape
where the knowledge lives so that the next ten features don't pile
on the same five files.

---

## 6. References

- [CLAUDE.md] — hard invariants.
- [docs/PHYSICS_PLAN.md] § 1 — non-negotiables (single Rigidbody, no
  per-frame allocations, blueprint-not-Tweakables for gameplay
  outcomes).
- [docs/NETCODE_PLAN.md] §§ 6–7 — server-authoritative blueprint
  contract, sorted-by-`Vector3Int` block ordering.
- [docs/SCALABLE_PARTS_PLAN.md] — Phase 1 occupancy (shipped),
  Phase 2 wheels / thrusters (queued), Phase 4 structural primitives
  (queued).
- [docs/changes/40-garage-mirror-and-connectivity.md] — leaf-host
  rule + mirror feature design.
- [docs/changes/38-scalable-parts-phase1-occupancy.md] —
  `BlockOccupancy` rationale, `BlockBehaviour.Up` migration.
- [docs/changes/44-foil-pitch-phase4-and-fixes.md] — leaf-skip BFS
  drop in `BuildCpuReachableSet`.

---

*Review written: 2026-05-09. Update if a refactor step lands and
the diagnosis it addresses is no longer current.*
