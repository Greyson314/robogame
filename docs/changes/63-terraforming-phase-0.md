# 63 — Terraforming Phase 0 (foundation interfaces)

> Status: **shipped, zero behaviour change.** Two new files under
> `Robogame.Core`; nothing in the existing codebase references them
> yet. `dotnet build Robogame.Core.csproj` → 0 warnings, 0 errors.
> Arenas play identical to session 62.

## Why this session

User: *"Using absolute best practices and a strong emphasis on
performance, please help me implement TERRAFORMING_PLAN.md."*

The plan estimates 8–12 weeks of focused work split across seven
phases. Pushed back on "implement the whole thing"; agreed to ship
**Phase 0 only** this session — the load-bearing types that every
later phase consumes, with an exit criterion ("existing arenas play
exactly as before") that's verifiable in one sitting.

## What changed

### Two files added, `Robogame.Core`

**[`Scripts/Core/DigField.cs`](../../Assets/_Project/Scripts/Core/DigField.cs)**

Mirrors [`GravityField.cs`](../../Assets/_Project/Scripts/Core/GravityField.cs)
intentionally — same `List<T>` + `RuntimeInitializeOnLoadMethod`
domain-reload reset shape, same `Register` / `Unregister` /
`ContainsPoint`-driven lookup. Two types:

- `IDigZone` — interface for a bounded voxel-terrain volume. Surfaces
  `WorldBounds`, `CellSize`, `ChunkSizeCells`, `ContainsPoint`.
- `DigField` — static registry. `Register` / `Unregister` / `ZoneAt`
  / `ZoneCount`. Zero-baseline cost when no zones are registered
  (PHYSICS_PLAN §1.2).

**[`Scripts/Core/BrushOp.cs`](../../Assets/_Project/Scripts/Core/BrushOp.cs)**

The wire-stable verb set for terrain edits. Four types:

- `BrushKind` enum — `None / SphereSubtract / CapsuleSubtract`. `None`
  is explicit and zero so a default-constructed `BrushOp` is
  recognisably invalid.
- `Vector3Fixed` — int16 × 3 in 1/256 m units. Used for brush
  centre / endpoints. Implements `IEquatable<>`, `==`, `GetHashCode`,
  `ToString`. Range ±128 m, precision ~3.9 mm. The deterministic
  fixed-point form keeps two clients from drifting by a cell over
  many ops — TERRAFORMING_PLAN §2 "Determinism note".
- `BrushOp` — 17-byte POD struct. `kind` + `serverTick` + `p0` +
  `p1` + `radiusFixed`. `[StructLayout(Sequential, Pack = 1)]` so
  the in-memory layout matches the future wire layout exactly.
- `BrushOpBatch` — one tick's ops for one zone. `MaxOpsPerBatch = 32`
  is captured as a `const`.

## Decisions worth flagging

**No `INetworkSerializable`.** The plan's struct snippet implements
it; the project has no netcode dependency yet (no `Unity.Netcode`
imports, no `BlockHitBatch` in source — both are design-doc-only).
Pulling in `com.unity.netcode.gameobjects` for a forward-looking
interface would violate Phase 0's "zero behaviour change" exit
criterion. The structs are POD with explicit `[StructLayout(Pack = 1)]`
so the wire layout is already fixed-point and stable; Phase 6 adds
the interface via partial-class extension when netcode actually
lands.

**No events on `DigField`.** `GravityField` exposes `SourceAdded` /
`SourceRemoved`; the plan's API snippet in §3 specifies only
`Register` / `Unregister` / `ZoneAt`. Matched the plan's narrower
surface rather than mirror events speculatively. One-line addition
later if anything needs them.

**No `DominantAt` analogue.** `GravityField.DominantAt` answers
"which planet am I on?" for camera / UI; dig zones are mutually
disjoint by authoring rule, so `ZoneAt` is sufficient. Skipped
mirror-for-mirror's-sake.

**`Vector3Fixed` clamps at conversion boundary, not in math.** The
field-to-field arithmetic stays integer once values are in fixed
form — the float math only happens at `FromVector3` (player input /
chassis tip position → fixed). Two clients converting identical
input floats with identical conversion code produce identical
int16s; from there it's bit-exact. Astroneer pattern, per § 4.

## What I deliberately did NOT do

1. **No meshing, no chunks, no SDF storage.** Phase 1 territory.
2. **No `DrillBlock` / `BombBayBlock` brush-emission wiring.**
   Phase 3. The audio + VFX call sites those add are also Phase 3
   — Phase 0 has no gameplay-visible behaviour to attach cues to.
3. **No `Robogame.Core.asmdef` reference changes.** The new types
   are pure C# + UnityEngine; no new package dependency.
4. **No editor tooling.** No `.dig` baker, no scene-window brush
   preview. Phase 1+ adds the editor side.

## Files

- **Added:**
  - `Scripts/Core/DigField.cs`
  - `Scripts/Core/BrushOp.cs`
- **Modified:** none.
- **Deleted:** none.

## Hard-invariant check

- **No `Update` / `FixedUpdate` work added** — types only.
- **No `Rigidbody` / `Collider` baseline cost added.** Zero-baseline
  rule satisfied (PHYSICS_PLAN §1.2).
- **No `Tweakable`s added** — gameplay-outcome rule satisfied.
- **No per-frame allocations** — no allocations period at this
  phase; the types don't run yet.
- **`Robogame.Core.dll` builds clean** — `dotnet build` with 0
  warnings, 0 errors.

## What Phase 1 needs from here

When Phase 1 lands (Burst Surface Nets prototype, single chunk,
editor "Apply test brush" button), it will:

1. Implement `IDigZone` on a new `DigZone : MonoBehaviour`
   component, calling `DigField.Register` / `Unregister` in
   `OnEnable` / `OnDisable`.
2. Allocate `NativeArray<sbyte> sdf` per chunk inside that component.
3. Apply `BrushOp` values to the SDF via min-fold.
4. Add Burst as a package dependency (`com.unity.burst`) — the
   T1 risk in TERRAFORMING_PLAN §13 estimates 1 week of "fighting
   Burst" since the codebase has no Burst usage today.

No Phase 0 API changes are anticipated; the shapes here are the
shapes Phase 1 consumes.
