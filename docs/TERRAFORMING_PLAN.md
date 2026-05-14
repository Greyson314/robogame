# 🪏 Terraforming Plan — Smooth Voxel Dig

> Long-form design document for adding **smooth, NMS/Astroneer-style destructible terrain** to Robogame. Players drop a bomb and a real hole appears; a drill carves a real tunnel; pre-authored underground POIs become reachable. **Dig-only — no terrain addition.** That constraint is the load-bearing simplification this whole plan is built around.
>
> **Audience:** future me, future contributors, future AI agents working on this codebase.
>
> **Status:** design doc, no code yet. Feature has not been started.
>
> **Bias:** **performance discipline first, everything else second.** This is the most ambitious single feature in the project — bigger than spherical arenas, comparable to netcode in scope. Honest estimate is 8–12 weeks of focused work for a first shippable version, ~5–6 if we crib heavily from a reference implementation and accept Surface Nets (smooth) over Dual Contouring (sharp). Every decision below either earns its place against the perf budget or doesn't ship.
>
> **Companion docs.**
> [`PHYSICS_PLAN.md` § 1](PHYSICS_PLAN.md#1-non-negotiables-read-this-first) for the non-negotiables that apply to every physics-driven feature (zero-baseline cost, single chassis Rigidbody, zero per-frame allocations).
> [`NETCODE_PLAN.md` § 6–7](NETCODE_PLAN.md#6-state-replication-strategy) for the state-replication taxonomy and `BlockHitBatch` precedent that the brush-op contract here mirrors.
> [`PERFORMANCE.md` § 7](PERFORMANCE.md#7-performance-budgets-extended) for the budget table this document extends.
> [`SPHERICAL_ARENAS.md` § 9](SPHERICAL_ARENAS.md#9-authoring-a-planet) for the hand-authored-arena philosophy that dig zones live inside of.

---

## Table of contents

- [1. Goals & Non-Goals](#1-goals--non-goals)
- [2. The dig-only invariant and what it buys us](#2-the-dig-only-invariant-and-what-it-buys-us)
- [3. Storage model](#3-storage-model)
- [4. Brush operations — the wire-friendly verb set](#4-brush-operations--the-wire-friendly-verb-set)
- [5. Meshing pipeline](#5-meshing-pipeline)
- [6. Physics — MeshCollider strategy](#6-physics--meshcollider-strategy)
- [7. Rendering — triangle budget and LOD](#7-rendering--triangle-budget-and-lod)
- [8. AI pathing on voxel terrain](#8-ai-pathing-on-voxel-terrain)
- [9. Authoring dig zones](#9-authoring-dig-zones)
- [10. Netcode contract](#10-netcode-contract)
- [11. Performance budgets (extended)](#11-performance-budgets-extended)
- [12. Phased rollout](#12-phased-rollout)
- [13. Risks & open questions](#13-risks--open-questions)
- [14. References](#14-references)

---

## 1. Goals & Non-Goals

### Goals

1. **Real holes from bombs.** A grenade or bomb creates a crater whose shape players can read and exploit. Not a decal, not a stamped prefab — actual carved geometry.
2. **Real tunnels from drills.** A drill block carves through terrain, leaves a tunnel, AI can follow you through it.
3. **Underground POIs become reachable.** Pre-authored chambers (loot caches, mini-bosses, lore) are placed underneath dig zones; players reach them by digging.
4. **Server-authoritative.** All terrain edits originate as server-validated operations. The server decides what got dug.
5. **Inside the existing budgets.** PERFORMANCE.md § 16 budgets are the contract. This feature does not get to blow past them.
6. **Opt-in per arena.** An arena without dig zones pays zero terraforming cost — no chunks loaded, no jobs scheduled, no per-frame work. This is non-negotiable (PHYSICS_PLAN § 1.2).

### Non-Goals

- ❌ **Adding terrain.** No "terraforming" in the constructive sense. No filling holes, no building mountains. **This is the constraint that makes the rest of the design tractable** — see [§ 2](#2-the-dig-only-invariant-and-what-it-buys-us).
- ❌ **Voxel chassis.** Robots remain cubic block grids. The voxel system never extends to anything attached to a chassis.
- ❌ **Voxel water.** Water remains flat-plane or sphere-shell per [SPHERICAL_ARENAS.md § 10](SPHERICAL_ARENAS.md#10-water-on-a-planet).
- ❌ **Whole-planet voxels.** Dig zones are bounded volumes inside an arena. Surface meshes outside the dig zone are the existing authored `HillsGround` / `Planet` mesh. The voxel system never colonises the whole arena.
- ❌ **Dual Contouring at v1.** Surface Nets gives us a smooth NMS-style look at ~30–40% the meshing cost. If we discover a feel reason to want sharper features later, we revisit.
- ❌ **Procedural dig-zone generation.** Every dig zone is hand-authored, the same way every planet and arena is hand-authored.
- ❌ **Per-block destructibility on the terrain.** A voxel is not a block. Terrain has no HP, no `BlockBehaviour`, no event hooks. It has SDF and it has brush ops applied to it.
- ❌ **Cubic / Minecraft-style aesthetic.** We chose smooth voxels deliberately and don't ship a "blocky" mode.

### What "dig-only" rules out at the gameplay level

- Players cannot repair a hole they accidentally drilled into a critical structure.
- Players cannot build cover during a match.
- A dig zone that gets fully excavated stays excavated for the rest of the match.

These are accepted costs. The perf and netcode wins below are worth them.

---

## 2. The dig-only invariant and what it buys us

> **The invariant.** For every cell, `sdf[c]` is monotonically non-increasing over the lifetime of a match.

Once a cell goes more-empty, it never goes less-empty again. Stated formally:

```
sdf[c, t+1] = min(sdf[c, t], brush(op, c))   for every applied op
```

This is the load-bearing simplification of the whole design. Five concrete wins drop out of it:

**1. Brush ops are commutative under `min()`.** Applying `A` then `B` gives the same SDF as applying `B` then `A`:

```
min(min(initial, A(c)), B(c)) = min(initial, A(c), B(c)) = min(min(initial, B(c)), A(c))
```

This eliminates the entire class of "two clients applied ops in different orders" desyncs.

**2. Network packet loss / reorder doesn't desync.** Out-of-order delivery converges to the same final SDF as in-order. We still want reliable delivery for the *visible-quickly* property, but correctness doesn't depend on ordering.

**3. Late-join replay is order-free.** Send the cumulative brush-op log in any order; the client converges to the right state.

**4. No per-cell history needed.** Current SDF alone is sufficient. Memory cost per cell is `sizeof(sdf) + sizeof(material)`, full stop. Compare to a non-monotonic system where you'd want either undo history or periodic snapshots to support add-then-subtract correctness.

**5. Worst-case terrain is bounded a priori.** "Fully excavated dig zone" is the worst case — there is no "player built a 50m spire" possibility. We can budget the triangle count, MeshCollider cost, and memory against `initial_triangle_count + delta_to_fully_dug` rather than against an unbounded growth term. **This is why dig-only fits the budget and free-form terraforming wouldn't.**

The cost of the invariant is gameplay flexibility (see § 1 non-goals). Worth it.

### Determinism note

Commutativity is correctness given identical per-cell brush values. Float math for `length(p - c) - radius` can drift by ~1 ulp between machines, which produces tiny per-cell SDF differences. Two mitigations are available:

- **Fixed-point brush math.** Brush center/radius stored as int16 with 1/256 m precision; cell coordinates are integers; all math integer. Astroneer-class. No drift possible.
- **Float math with tolerance.** Accept that two machines may differ by ~1 ulp in SDF per cell. Visually identical; pathing identical; only matters for sub-cell-precision queries (which we don't have).

**Default to fixed-point** — it's not appreciably more work to implement and removes a whole class of "MP feels weirdly different than SP" bugs.

---

## 3. Storage model

### Per-cell data

```
struct Cell {
    sbyte sdf;       // signed distance, fixed-point (1 unit = cellSize / 64)
                     //   sdf <  0  → interior (solid)
                     //   sdf >= 0  → exterior (empty / open)
                     //   sdf near zero → near the surface; cell emits triangles
    byte  material;  // material ID; 0 = "use chunk default material"
}
// sizeof(Cell) = 2 bytes
```

`int8` gives 256 SDF levels. At a 0.5m cell size that's ~8mm precision, well below the visual noise floor. We considered 4-bit packed SDF (16 levels) and rejected it — quantisation banding becomes visible in the meshed surface at low edit densities.

Material is byte-indexed into a per-arena material table. 256 materials per arena is enormously more than we need. The `0 = default` convention saves the byte on unedited cells when we pack the chunk for memory.

### Chunking

Cells are grouped into **32³ chunks**. At 0.5m cells, that's a 16m × 16m × 16m volume per chunk. 32K cells × 2 bytes = **64 KB per fully-allocated chunk**.

32³ is the standard choice for smooth-voxel systems (Astroneer, the various open-source voxel-engine references). Meshing throughput peaks around this size — chunks much smaller pay disproportionate per-chunk overhead; chunks much larger blow the L2 cache on the mesher.

### Sparse chunk allocation

A chunk has three possible states:

1. **`Empty`** — uniform interior (all cells `sdf < 0` with the chunk's default material). No cell array allocated. Backed by an 8-byte struct: `{ chunkCoord, defaultMaterial, sdfState = AllInterior }`.
2. **`Exterior`** — uniform exterior (chunk above the dig zone surface or pre-carved). Same 8 bytes. Mesher emits zero triangles for these.
3. **`Mixed`** — cell array allocated. 64 KB.

The state-1 default means an arena's dig zone allocates almost no memory until a player actually digs into a chunk. A 1500m-radius planet with a 30m shell at 0.5m cells is theoretically 850M cells = 1.7 GB if fully realised — **with sparse allocation, an unedited zone is one struct per chunk** (~85K chunks × 8 bytes = 680 KB metadata), which is fine.

A heavy-session-worst-case is roughly 100 chunks touched × 64 KB = **6.4 MB working set** for the voxel system. Comfortable.

### What the server stores vs. what the client stores

Server stores the SDF cells and the cumulative brush-op log. Server does **not** mesh. Server uses cells for spatial validation only (raycast for damage, occupancy for AI).

Client stores the SDF cells, the cumulative brush-op log (for late-join compat — recomputed from server's authoritative state at connect), AND the meshed chunk output (vertex/index buffers + the MeshCollider).

The asymmetry is the point — meshing is the expensive part and the server avoids it entirely.

### `Robogame.Core` additions

```csharp
namespace Robogame.Core
{
    public interface IDigZone
    {
        Bounds WorldBounds { get; }
        float  CellSize { get; }
        int    ChunkSizeCells { get; }   // 32 for v1
        bool   ContainsPoint(Vector3 worldPosition);
    }

    public static class DigField
    {
        public static void Register(IDigZone zone);
        public static void Unregister(IDigZone zone);
        public static IDigZone ZoneAt(Vector3 worldPosition);  // null if outside any zone
    }
}
```

Mirrors the `IGravitySource` / `GravityField` pattern from [SPHERICAL_ARENAS.md § 6](SPHERICAL_ARENAS.md#6-new-components). Arenas without a registered `IDigZone` get zero terraforming cost (zero-baseline rule, PHYSICS_PLAN § 1.2).

---

## 4. Brush operations — the wire-friendly verb set

The set of brush ops is small and closed. Each brush has an enumerated kind and a fixed-size parameter struct. This is what gets sent over the wire, what gets logged for late-join, and what the meshing system applies to chunk SDFs.

### Enumerated ops

```csharp
public enum BrushKind : byte
{
    SphereSubtract  = 1,    // bombs, grenades, explosive shells
    CapsuleSubtract = 2,    // drill swept along last-tick → this-tick path
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BrushOp : INetworkSerializable
{
    public BrushKind kind;          // 1 byte
    public ushort    serverTick;    // 2 bytes — for ordering / replay determinism
    public Vector3Fixed p0;         // 6 bytes — int16 × 3, 1/256 m precision
    public Vector3Fixed p1;         // 6 bytes — capsule end-point; equals p0 for SphereSubtract
    public ushort    radiusFixed;   // 2 bytes — int16, 1/256 m precision, max ~256m
    // Total: 17 bytes per op. Padded to 18 on the wire.
}
```

`Vector3Fixed` is three int16s representing world-space position in 1/256 m units. ±128m range from world origin per axis; arenas are authored at world origin (SPHERICAL_ARENAS § 15 risk S5).

That's it. No `SphereAdd`. No `Sculpt`. Two ops. Adding a third (say, a directional cone for a flamethrower-dig) is a single enum value and a fixed-size struct; the framework supports it but v1 ships with the two above.

### Why these two

A bomb's natural shape is a sphere — the existing `BombBayBlock.cs` already passes a radius. A drill's natural shape between two physics ticks is the swept capsule from where the tip *was* to where the tip *is now*. At 30 Hz tick rate with a typical drill speed of ~10 m/s, that's a 0.33m sweep per tick — capsule, not sphere.

The capsule sweep also avoids the failure mode where a fast-moving drill tunnels through one cell per tick and leaves a dashed-line tunnel; the capsule fills the gap continuously.

### `BrushOpBatch` (the wire packet)

```csharp
public struct BrushOpBatch : INetworkSerializable
{
    public ushort     digZoneId;   // 2 bytes — which dig zone (multi-zone arenas eventually)
    public ushort     serverTick;  // 2 bytes
    public BrushOp[]  ops;         // variable; capped at 32 ops per batch
}
```

One `BrushOpBatch` per server tick per dig zone with at least one edit. Sent as a `ClientRpc` with reliable delivery. Mirrors the `BlockHitBatch` shape from [NETCODE_PLAN.md § 7](NETCODE_PLAN.md#7-the-hard-problem-replicating-block-based-robots).

### Bandwidth math

A continuous drill emits 1 op per tick = 18 bytes + ~8 bytes batch overhead per tick × 30 Hz = **~780 B/s per drilling player**. At 16 players all simultaneously drilling, ~12.5 KB/s = **100 kbps total terrain traffic**. Per-client receive is `(N-1) × 780 B/s ≈ 12 kbps` — comfortably inside the 64 kbps/client budget.

A bomb crater is one op = 18 bytes. Bombing rate is limited by weapon cooldown (~1 Hz), so bomb-bandwidth is tiny.

Late-join replay: cumulative log of 1000–5000 ops per 30-min match = 18–90 KB. Send compressed at connect.

---

## 5. Meshing pipeline

### Algorithm choice: Surface Nets

We use [Naive Surface Nets](https://0fps.net/2012/07/12/smooth-voxel-terrain-part-2/) (Lysenko, 2012):

- One vertex per **active** cell (a cell whose SDF straddles zero somewhere in its 2×2×2 corner neighbourhood).
- Vertex position = SDF-weighted centroid of the cell's eight corners.
- Connectivity: for each active edge in the grid (an edge whose two corner SDFs have different signs), emit a quad connecting the four neighbouring cells' vertices.

Why Surface Nets and not the alternatives:

- **Marching Cubes** emits up to 5 triangles per active cell with case-table lookups. Surface Nets emits ~2 triangles per active cell on average and runs ~1.4× faster. Output looks slightly smoother than MC's faceted variant.
- **Dual Contouring** produces sharper features (cliffs, drill-bit corners) via per-cell QEF solves. ~2.5–3× slower than Surface Nets. NMS / Astroneer use DC-class techniques. Surface Nets matches their visual quality for the "soft earth" aesthetic; we revisit DC only if we ship a "hard rock" biome that demands sharper geometry.
- **Transvoxel** is an extension for LOD seam handling, not a base algorithm. See § 7.

### Job pipeline

Meshing runs as Burst-compiled IJobParallelFor on the worker pool:

```
[BurstCompile] struct GenerateSurfaceNetsJob : IJobParallelFor {
    [ReadOnly] NativeArray<sbyte> sdfWithApron;   // 33³ — 32³ cells + 1-cell apron from neighbours
    [WriteOnly] NativeArray<Vertex> vertices;     // pre-sized to chunkCells (over-allocated; we track count)
    [WriteOnly] NativeArray<int>    indices;
    NativeArray<int> activeCellToVertexIndex;     // sentinel = -1 for inactive
    ...
}
```

The **apron** is the trick that lets chunks mesh in parallel without seam cracks: each chunk reads its own 32³ cells *plus* a one-cell-thick rim of neighbour cells, so vertex positions on shared edges agree between adjacent chunks. Apron data is 1KB per face × 6 faces ≈ 6 KB per chunk; cheap.

### When does a chunk re-mesh?

A chunk is marked **dirty** when any cell in its volume changes. Dirty flag is per-chunk, not per-cell. A single `BrushOp` touching 200 cells in one chunk produces one dirty flag.

Brush ops within the same `FixedUpdate` are coalesced — all ops for tick T are applied to the SDF, *then* dirty chunks are remeshed. This means a 5-cell drill sweep over 5 ticks produces 5 remeshes (one per tick), not 5 per tick. And it means the bomb crater that touches 8 chunks meshes each of them exactly once, not once per affected cell.

### Burst is mandatory

Plain managed C# meshing of a 32³ chunk takes 3–8 ms on the target CPU profile (community benchmarks; medium confidence pending our own measurement). Burst with `[NoAlias]` and SIMD intrinsics gets the same job to **sub-millisecond, typically 0.3–0.6 ms**. There is no graceful degradation between these — 5 ms per dirty chunk is a frame-killer at any sustained edit rate.

This is the single most important commitment in this plan: **Burst is a hard dependency, not a nice-to-have.** Our codebase has no Burst usage today; adopting it has friction (Burst can't compile `class` references, lambdas that capture, certain managed APIs). Budget 1 week of "fighting Burst" for the first system that needs it; subsequent systems are cheap.

### Zero per-frame allocations

Per [`PERFORMANCE.md` § 2.1](PERFORMANCE.md#21-zero-allocations-per-steady-state-frame), no allocations in the steady-state remesh loop:

- Vertex / index `NativeArray`s are sized to chunk-cell-count at chunk creation; reused on every remesh.
- The Mesh object backing the chunk uses `Mesh.SetVertexBufferData` / `Mesh.SetIndexBufferData` against pre-sized buffers; no per-remesh `mesh = new Mesh()`.
- Dirty-chunk list is a pre-sized `NativeList<ChunkId>` with a hard cap.

---

## 6. Physics — MeshCollider strategy

A chunk's MeshCollider is the expensive lifecycle. Cooking a non-convex 32³-worth of triangles takes **5–15 ms on the main thread** by default (medium confidence — depends on triangle count, which depends on how excavated the chunk is). That's a frame-killer for any sustained-drill scenario.

### Async bake via `Physics.BakeMesh`

Unity has supported `Physics.BakeMesh` on worker threads since 2020 ([docs](https://docs.unity3d.com/ScriptReference/Physics.BakeMesh.html)). The pattern:

```
1. Chunk's SDF changes; chunk goes on the dirty list.
2. At end of FixedUpdate, meshing job(s) run on worker pool (Burst).
3. When meshing completes, schedule a Physics.BakeMesh job for the new triangle data.
4. When bake completes, on the main thread:
   a. Build a new MeshCollider with the pre-baked mesh.
   b. Atomically swap: old collider stays enabled until new one is enabled, then disable + destroy old.
```

The atomic-swap rule matters: never have a moment where the chunk has no collider, or a colliding robot's wheel will sink into the terrain for one frame. PHYSICS_PLAN § 1.1 (single Rigidbody per chassis) is unaffected; chunks are static colliders, not part of any chassis.

### Deferred dirty flush

Adjacent drill ticks within the same chunk should coalesce. Pattern:

```
Per FixedUpdate:
  apply brush ops to SDF
  mark chunks dirty

Per N FixedUpdates (default N=2 → 25 Hz remesh rate):
  flush dirty chunks → schedule meshing jobs
```

A drill at 50 Hz physics tick produces a remesh at 25 Hz. The visible lag is ~40 ms, well under the perceptual threshold for "instant." Bombs flush immediately (high-impact event, expected to feel instant).

### Free-body debris

When the player drills a chunk loose, do we spawn debris? Per the dig-only invariant: **no — debris is decorative VFX, not a free-body collider**. Detached chunks of "dirt" should be `VfxSpawner` particles only, parented under scene root, lifetime ~3 seconds, no Rigidbodies. This matches the [`PHYSICS_PLAN.md` § 1.1](PHYSICS_PLAN.md#1-non-negotiables-read-this-first) "default to zero baseline cost" stance.

If we want larger debris chunks later for visual impact (a boulder-sized chunk tumbles when undermined), that's a follow-up feature with its own perf review; not v1.

### Robot interaction

Drills hit the terrain via the existing `TipBlock` damage model (see [TIP_BLOCK_ATTACH.md](TIP_BLOCK_ATTACH.md)). When a drill tip's collision-stay event lands on a chunk MeshCollider belonging to a `DigZone`, the tip's `DrillBlock` component (new, sibling to `HookBlock` / `MaceBlock`) emits a server-side `BrushOp` via its drive ServerRpc. The capsule's `p0` is the tip position from last tick; `p1` is the current tip position; `radius` is the drill bit's authored radius.

Drills do **not** apply damage to terrain in HP terms — terrain has no HP. The brush op is the effect.

---

## 7. Rendering — triangle budget and LOD

### The triangle problem, quantified

Surface Nets emits roughly **2 triangles per active cell**. A chunk that has been carved into a tunnel has ~30% of cells active. 32³ × 0.30 × 2 = ~20K triangles per fully-tunneled chunk.

PERFORMANCE.md § 7 sets the target at < 1.5M triangles, cliff at 3M. A 100-chunk dig zone fully-excavated would emit ~2M triangles — over target, under cliff, with no headroom for the rest of the scene.

**The triangle budget is the binding constraint on dig-zone size.** Three discipline mechanisms enforce it:

### Mechanism 1: Authored dig-zone size limits

A dig zone's worst-case triangle count is `chunks × 20K`. We author dig zones to a worst-case budget:

- **Small dig zone** — ≤ 16 chunks (4×4×1 horizontal layout, e.g. a 64m × 64m × 16m deep zone). Worst case ~320K tris. Comfortable.
- **Medium dig zone** — ≤ 64 chunks. Worst case ~1.3M tris. At-budget.
- **Large dig zone** — ≤ 100 chunks. Worst case ~2M tris. Approaches cliff; needs LOD to stay shippable.

Arenas budget worst-case for the *aggregate* of all dig zones, not per-zone. An arena ships with one medium zone OR three small zones, not three large zones.

### Mechanism 2: Chunk LOD with transvoxel seams

Far chunks mesh at half resolution (16³ instead of 32³, ~5K tris worst case). Standard transvoxel-style seam handling at LOD boundaries — chunks at the boundary emit transition cells that join the high-res chunk to the low-res chunk without cracks. Reference: [Eric Lengyel's Transvoxel paper](http://transvoxel.org/).

LOD threshold: chunks beyond `2 × cellSize × 32` ≈ 32m from the local player camera drop to half-res. Beyond 64m drop to quarter-res. Chunks behind the camera and not in the player's frustum mesh at lowest LOD and update at low priority.

LOD-on-edit: if a low-LOD chunk gets edited (a remote player drilling far away), it remeshes at low res. When the player camera approaches, it remeshes at high res. Cumulative ops are stored at full SDF resolution; LOD only affects mesh extraction.

### Mechanism 3: Render-pipeline discipline

- **No MK Toon outlines on voxel terrain.** Per [`PERFORMANCE.md` § 5.4](PERFORMANCE.md#54-rendering--toon-outlines--srp-batcher), outlines are already the dominant MP-scale render cost on chassis blocks. Multiplying that cost by terrain triangle count is catastrophic. Outlines exist to read silhouettes; terrain reads fine without them. Voxel terrain ships on a non-outlined material.
- **No Fluff grass on voxel terrain.** [`PERFORMANCE.md` § 5.3](PERFORMANCE.md#53-fluff-grass--the-dominant-gpu-cost-and-how-to-diagnose-it): Fluff's geometry shader multiplies triangle count by 22× per close triangle. On a 20K-triangle chunk that's 440K post-geom tris per chunk. Hard no. Voxel terrain regions render on a `Mat_DigZoneEarth` material (dirt / stone / ice), not the Fluff arena ground material. Grass stops at the dig-zone boundary.
- **One material per dig zone biome.** Material per chunk inflates SetPass calls (§ 16 budget: < 100 / frame). One material per zone — variation comes from per-vertex material indices sampled from a texture array in-shader, not from per-chunk material instances.
- **Shadow casting on chunks.** Static shadowmaps are baked at chunk-mesh time. Real-time shadows on dynamic terrain are expensive; defer until we have a profile capture saying they're worth it.

### What about Fluff at the boundary?

The arena ground (Fluff'd) and the dig zone (non-Fluff) abut at an authored seam — typically a low retaining wall, a rim of rock, or a transition mesh. The Fluff ground's `_MaximumDistance` doesn't extend into the dig zone because Fluff samples from the original ground mesh, which doesn't exist inside the dig-zone volume. No special shader work needed; it falls out of the authoring rules.

---

## 8. AI pathing on voxel terrain

NavMesh runtime rebake on voxel edits is **forbidden** — it is the single most expensive operation on any "voxel + AI" feature list, and the rebake cost grows with mesh complexity, which is exactly where we can't afford spikes.

### Replacement: coarse occupancy grid

Each dig zone maintains a **3D occupancy grid at 2m resolution** (i.e., 1 occupancy cell per 4×4×4 voxel cells). Each occupancy cell is one byte:

- `0` = solid
- `1` = open and floor-adjacent (AI can stand here)
- `2` = open but no floor (AI can fly here if flying; pathable only for flying enemies)

At 2m resolution a 16m chunk is 8×8×8 occupancy cells = 512 bytes per chunk. For 100 dirty chunks: ~50 KB. Negligible.

### Incremental updates

When a chunk remeshes, its occupancy grid recomputes — locally, no flood-fill, no whole-graph rebuild. The op is `O(occupancy cells in chunk)` = 512 cells = sub-millisecond.

A change in occupancy invalidates affected adjacency edges in the pathfinding graph. We use A* over a graph whose nodes are occupancy cells and whose edges are 6- or 26-connected (configurable per AI type). Edge weights are computed from cell-pair geometry, cached per edge, invalidated on dirty.

### Why this is enough

Underground POI enemies don't need 0.5m precision pathing — they need "is this region traversable." A 2m occupancy grid for "AI walks the tunnel toward the player" is the right resolution; the AI's collider is ≥ 1m wide.

If a future feature needs finer pathing (e.g. AI that uses cover behind individual voxel features), we revisit — but I'd bet against ever needing it.

---

## 9. Authoring dig zones

### Editor pipeline

A dig zone is a Unity prefab containing:

```
DigZone_Crater (prefab root)
└── DigZoneVolume
    ├── DigZone (component, IDigZone)
    │   ├── cellSize: 0.5
    │   ├── chunkSize: 32
    │   ├── bounds: Bounds (worldBounds in the scene)
    │   └── initialMaterial: 1 (dirt)
    ├── DigZoneInitialStateAsset (reference)
    │   └── (a binary asset baked once; see below)
    └── (child) Surface mesh stub for the un-dug visual (optional, see below)
```

The **initial SDF state** is baked once at authoring time as a `.dig` binary asset. The baker pipeline:

1. Designer drops the `DigZone` prefab into a planet scene.
2. Designer authors the un-dug *visual* mesh in Blender (or uses a procedural primitive — a sphere, a Perlin-displaced plane, etc.).
3. Editor menu: `Robogame > Dig Zone > Bake Initial State` — voxelises the authored mesh into the zone's chunks at the configured cell size. Writes to `Assets/_Project/DigZones/<sceneName>_<zoneName>.dig`.
4. Same menu: `Bake POIs` — places authored POI prefabs (chambers, loot, enemies) inside the volume, carving them into the SDF at bake time so they exist in the initial state.

The `.dig` asset is the source of truth at scene load. Brush ops are deltas on top.

### POI authoring

A POI is a Unity prefab whose origin is placed inside the dig zone at author time. The bake pipeline:

- POI prefab has a `DigZonePoiVolume` child marking the carved-out space (a primitive shape or a low-poly mesh).
- Baker subtracts the POI volume from the initial SDF, leaving an air pocket.
- POI prefab's *interior* contents (loot containers, enemies, lights) are instantiated as regular GameObjects at runtime. Not voxel.

This means POIs are pre-carved at match start — players don't have to dig to reveal them; they have to dig to *reach* them through the surrounding voxel material.

### Designer-facing knobs

- `cellSize` (per zone) — default 0.5m. Larger = chunkier / coarser dig precision, lower triangle count.
- `chunkSize` (per zone) — default 32. Rarely changed.
- `digMaterial` (per zone) — visual material for the carved surface.
- `bounds` — the authored XYZ extent. Defines worst-case triangle budget; baker rejects bounds that exceed the per-arena tri budget cliff.

### Content hash

The `.dig` asset hash participates in the NETCODE_PLAN § 6 bucket A content check on connect. Modified `.dig` assets fail the handshake — anti-cheat-relevant (a client can't load a "no walls" version of an arena).

---

## 10. Netcode contract

This section extends [`NETCODE_PLAN.md` § 6–7](NETCODE_PLAN.md#6-state-replication-strategy). Read those first.

### Replication taxonomy placement

- **Bucket A (configuration, sent once):** The `.dig` initial state asset. Content-hashed at connect.
- **Bucket B (per-match construction):** Not applicable — dig-zone identity is part of the arena scene, not a per-match construction.
- **Bucket C (continuous):** Not applicable — terrain doesn't have continuous state, only discrete edit events.
- **Bucket D (discrete events):** `BrushOpBatch` ClientRpc per server tick per dirty zone. **This is where terrain lives.**
- **Bucket E (UI / cosmetic):** Decorative dig debris VFX — fire-and-forget locally, never replicated.

### Server-side flow

```
PerFixedUpdate:
  1. Drain DrillTipFireQueue → produce candidate BrushOps for each drilling chassis
  2. Validate each candidate:
     - Is the drill block still alive on the chassis?
     - Is its position within ChassisAimReachableBounds (anti-cheat)?
     - Is the brush capsule entirely inside a registered IDigZone?
  3. Apply validated BrushOps to authoritative SDF (min-fold per cell)
  4. Mark affected chunks dirty in SERVER tracking (not visual — server has no mesh)
  5. Append validated BrushOps to per-match cumulative log
  6. Build BrushOpBatch per dirty zone, send ClientRpc to all clients
```

### Client-side flow

```
On receive BrushOpBatch:
  1. Apply ops to local SDF (min-fold)
  2. Mark affected chunks dirty in local tracking
  3. (Optional) Schedule remesh job; on completion, swap MeshCollider

On predict-fire-drill (owning client only):
  - Predicted op applied locally with serverTick = local predicted tick
  - On server confirmation, the authoritative op overwrites — but since min() is monotonic and brush math is deterministic, the predicted op and confirmed op should produce identical SDF.
  - If they differ (server rejected, or aim diverged), local SDF rolls back to last-known-server state. RARE in practice given the validation rules.
```

CSP for drill ops is much easier than CSP for movement because of the monotonic invariant. Worst case the client over-predicts a dig that the server then rejects; the SDF *unwinds* by replaying the cumulative log from a checkpoint. This is heavy but rare and only affects visual chunk geometry, not gameplay outcomes (server is authoritative on whether the drill reached the cell).

### Late-join

Late-joiners receive:

1. Initial `.dig` asset (content-hashed; already on disk if they have the game installed).
2. Cumulative `BrushOp[]` array since match start, compressed via simple LZ4.

A 30-minute match with sustained drilling is ~5000 ops × 18 bytes = 90 KB raw, ~30 KB compressed. Sent reliable. Late-joiner applies in any order (commutativity), meshes all dirty chunks in priority order (closest to camera first).

### Checkpointing (Phase 6+, not v1)

As an op-log compaction strategy: every 5 minutes, the server snapshots each dirty chunk's full SDF (~64 KB raw, ~10–20 KB run-length-compressed since most cells are uniform interior or uniform exterior). Ops below the snapshot tick are dropped from the log. Late-joiners receive the most recent snapshot + ops since.

Not needed for v1's 30-min match length but the design supports it cleanly when match length grows.

---

## 11. Performance budgets (extended)

This extends [`PERFORMANCE.md` § 7](PERFORMANCE.md#7-performance-budgets-extended).

### Singleplayer + active dig zone

| Metric | Target | Cliff | Notes |
|---|---|---|---|
| Voxel CPU cost / frame (steady state) | 0 ms | 0.5 ms | Idle terrain costs nothing. If you're paying frame cost when no one is digging, something is wrong. |
| Voxel CPU cost / frame (heavy drilling) | < 2 ms | 5 ms | Sum of Burst meshing jobs (worker), main-thread mesh upload, MeshCollider swap. Worker time is hidden but bounded. |
| Voxel main-thread spike on bomb crater | < 3 ms | 8 ms | 8 chunks remesh + swap. Mesh upload is main-thread; bake is worker. Spike is the upload + swap. |
| Voxel memory (steady state) | < 8 MB | 32 MB | ~100 dirty chunks × 64 KB |
| Voxel allocations per frame | 0 B | any | Hard rule. NativeArrays only, all pre-sized. |
| Triangles from voxel terrain | < 1M | 2M | Per-arena budget across all dig zones at worst-case excavation. |
| Active MeshColliders from voxel terrain | < 100 | 256 | One per dirty chunk; statics, not Rigidbodies. |
| PhysX BakeMesh time (worker) | < 5 ms/chunk | 15 ms | Async, doesn't block main thread, but tracks our cost. |

### 16-player MP arena + active dig zone

| Metric | Target | Cliff | Notes |
|---|---|---|---|
| Terrain bandwidth / client (sustained) | < 16 kbps | 64 kbps | Inside total per-client 64 kbps NETCODE_PLAN target. |
| Server SDF apply cost / tick | < 0.5 ms | 2 ms | Pure data ops, no meshing on server. |
| Cumulative op-log size (30-min match) | < 100 KB | 1 MB | Compaction at 1 MB. |
| Late-join handshake terrain payload | < 50 KB | 200 KB | Compressed log + content hash. |
| Predicted-vs-confirmed dig divergence | 0 cells | > 4 cells | Anti-cheat / desync canary. |

### Triangle budget — the binding constraint reiterated

A medium dig zone (64 chunks) at worst-case excavation emits ~1.3M triangles. Add the rest of the scene (chassis blocks at ~150 each × 16 chassis × ~50 tris each = ~120K) and you're at ~1.4M, near the cliff of 3M but well under the 1.5M target.

A large dig zone (100 chunks) at worst-case is ~2M voxel triangles alone — over the 1.5M target. Either LOD it aggressively, accept reduced visual quality, or split into multiple medium zones with non-voxel rim geometry between.

**Authoring discipline > runtime mitigation.** A dig zone that fits the budget at worst-case excavation doesn't need clever LOD tricks to stay shippable.

---

## 12. Phased rollout

Each phase is a shippable internal milestone with a tag and a `docs/changes/NN-slug.md` entry.

### Phase 0 — Foundation interfaces (2–3 days, zero behaviour change)

- Add `IDigZone`, `DigField`, `BrushKind`, `BrushOp`, `BrushOpBatch` to `Robogame.Core`.
- Add `Vector3Fixed` int16 helper.
- No meshing yet. No chunks yet. Just the types.

**Exit criterion:** existing arenas play exactly as before. No visible regression.

### Phase 1 — Single-chunk SP prototype (1–2 weeks)

- Burst-compiled Surface Nets job for one 32³ chunk.
- `DigZone` component with a single hard-coded chunk.
- Test scene `DigZone_Test.unity` with a flat plane and a single chunk on top.
- Editor button: "Apply test brush" — applies a `SphereSubtract` at the cursor click point.
- No drill block yet, no bombs yet.

**Exit criterion:** clicking in the scene removes a smooth spherical chunk of voxel terrain. Remesh < 1 ms (Burst, profiled). No allocations. Looks like NMS / Astroneer.

### Phase 2 — Multi-chunk dig zone + async MeshCollider (1 week)

- Multi-chunk `DigZone` with sparse chunk allocation.
- Apron-based meshing for seam-free chunk boundaries.
- Async MeshCollider bake via `Physics.BakeMesh`, atomic swap.
- `.dig` asset baker (editor tool) for initial state.

**Exit criterion:** a 16-chunk dig zone with hand-authored initial geometry. Drive a robot onto it. Click to dig. Physics works (wheels rest on dug surface). No frame hitches.

### Phase 3 — Drill block + bomb integration (1 week)

- `DrillBlock` (sibling to `HookBlock` / `MaceBlock`): tip-block that emits `CapsuleSubtract` brush ops on contact with dig zones.
- `BombBayBlock` extended: bombs detonating inside a dig zone emit `SphereSubtract`.
- Drill rate is gated by physics (drill tip has to actually reach the cells; you can't drill the air).
- VFX: particle dust at drill tip, particle debris at bomb impact, both via existing `VfxSpawner` pipeline.

**Exit criterion:** SP playthrough — drive up to dig zone, deploy drill, tunnel through to a pre-authored chamber. Drop a bomb, see a crater. No regressions in existing arenas.

### Phase 4 — Chunk LOD + transvoxel seams (1–2 weeks)

- Distance-based LOD with transvoxel transition cells.
- LOD-on-edit (far chunk edited remotely re-meshes at low res; high-res on camera approach).
- Profiling pass against the worst-case triangle budget.

**Exit criterion:** a 100-chunk dig zone at worst-case excavation maintains the < 1.5M tri target with LOD on.

### Phase 5 — AI occupancy grid + underground enemies (1–2 weeks)

- 2m occupancy grid per dig zone, incrementally maintained on remesh.
- A* pathfinding over the grid.
- One or two pre-authored underground POI enemies driving on / flying through the dig zone.

**Exit criterion:** AI enemy in a POI chamber notices the player drilling in, paths through the new tunnel to attack.

### Phase 6 — Network the dig zone (gated on NETCODE Phase 1–4)

- `BrushOpBatch` ClientRpc per dirty zone per tick.
- Server-side validation (chassis owns drill, drill in reachable bounds, capsule inside dig zone).
- Late-join replay (cumulative compressed log).
- Content-hash check on `.dig` assets.

**Exit criterion:** 4-client MPPM session in a dig-zone arena. All clients see the same terrain after sustained drilling. No desyncs over 10 minutes of mixed bombing and drilling.

### Phase 7 — Op-log checkpointing (deferred; when match length grows)

- Periodic per-chunk SDF snapshots.
- Op-log compaction below the snapshot tick.

**Not v1.** Only needed when match length exceeds ~30 min or op rate exceeds the bandwidth budget.

---

## 13. Risks & open questions

| # | Risk | Mitigation |
|---|---|---|
| T1 | Burst adoption friction — the codebase has no Burst usage today; first system to adopt pays the "fighting Burst" tax (no class refs, no captured lambdas, no `Debug.Log` in hot paths). | Budget 1 week of Burst onboarding inside Phase 1. Document the patterns in `docs/BURST_NOTES.md` for the next system. |
| T2 | Triangle budget blown by overzealous dig-zone authoring — designer ships an arena with 200 chunks because "the perf passes look OK at the start of the match." | `.dig` baker rejects zones whose worst-case (fully-excavated) tri count exceeds the per-arena budget. Hard fail at bake time. |
| T3 | MeshCollider recook spike under sustained drilling — async bake is the mitigation but the main-thread swap is still cost. | Deferred dirty flush (§ 6) coalesces edits within N FixedUpdates. The Burst meshing is hidden on workers; only the upload + collider swap is main-thread. Profile at "16 simultaneous drillers" before claiming OK. |
| T4 | Determinism drift between server and client due to float math in brush ops. | Fixed-point brush math (Vector3Fixed, int16 radius). Astroneer-style. Specified in § 4. |
| T5 | LOD seam cracks at chunk boundaries. | Transvoxel transition cells. Reference implementation linked in § 7 (Eric Lengyel). Don't roll our own — port the reference. |
| T6 | Cumulative op-log grows unbounded for long matches. | Phase 7 checkpointing. Not v1. Track the actual op rate in a closed beta before committing dev time. |
| T7 | Authoring tool cost — designers need a competent `.dig` baker, a worst-case-tri visualiser, and a brush-ops scrubber for QA. | All editor-only, all small. Budget ~1 week of editor tooling inside Phase 2. |
| T8 | Fluff / outline render features creep into voxel terrain by accident. | Mat_DigZoneEarth material is the only voxel-terrain material at v1. Authoring rule: voxel chunks render this material only. Code review enforces. |
| T9 | NavMesh runtime rebake creeps into the AI design because a contractor / agent "fixes" the pathing the easy way. | Documented hard-no in § 8. The occupancy grid is the only pathing mechanism for voxel terrain. |
| T10 | Players discover a "drill the floor out from under the spawn pad" griefing exploit. | Dig zones don't overlap spawn pads or critical structures by author rule. `.dig` baker validates: spawn-pad bounds + ~2m skirt must lie outside any dig zone. Hard fail. |
| T11 | Players drill outside the dig zone (engine bug or aimbot). | Server validation rejects brush capsules that aren't entirely inside a registered `IDigZone`. Logged as a candidate cheat signal. |
| T12 | A "fully excavated" dig zone leaves a gameplay-dead region — nothing to do there for the rest of the match. | Authoring concern, not engineering. Either authoring rule says "POIs are reachable when ≤ 30% excavated" or we add a Phase 7 "match-end refresh" feature. Park for now. |
| T13 | The voxel mesh's silhouette reads wrong against the toon-shaded chassis art direction. | Visual playtest in Phase 1 with the actual art palette. If wrong, switch to flat-shaded chunks (cheap shader change, no perf cost). Worst case we revisit Dual Contouring for sharper features. |
| T14 | Predicted-vs-confirmed drill divergence produces visible chunk pops on the local client. | Most drill predictions will match the server (drilling against your own dig zone has no inter-player conflict). The rare divergence is acceptable; the local client just experiences a single chunk pop. If unacceptable in playtest, defer drill prediction and accept ~RTT latency on the visual carve. |
| T15 | First-time Burst-job authors land bugs that only manifest in IL2CPP builds, not the editor. | CI runs an IL2CPP build of `DigZone_Test.unity` on every PR touching the voxel system. Phase 0 includes setting this up. |

### Open design questions to revisit at each phase boundary

- **Q1**: Cell size — 0.5m default; should it vary per zone (e.g. fine-grain for combat-relevant dig zones, coarse for cosmetic ones)? Probably yes; the storage and meshing don't care, but authoring discipline gets harder. Decide at end of Phase 2.
- **Q2**: Should drilling apply damage to enemies caught in the brush capsule (the drill-as-weapon use case)? Currently terrain is non-damaging — adding damage means the brush op also has to broadcast a damage event. Cleanest answer: drill damage is a `TipBlock`-side concern (see [`TIP_BLOCK_ATTACH.md`](TIP_BLOCK_ATTACH.md)) and is independent of the brush op. Confirm at end of Phase 3.
- **Q3**: Should bomb craters' shape derive from the bomb's explosion parameters (radius, damage falloff curve) or be a designer-tuned `BrushKind` constant? Probably the former — bomb radius already exists. Confirm at end of Phase 3.
- **Q4**: Does the voxel terrain receive shadows from chassis blocks? Cheap; probably yes. Shadow casters from voxel terrain onto chassis blocks? Expensive at chunk count × cascade count. Probably no for v1. Revisit in Phase 4 perf pass.
- **Q5**: When a player tunnels into a chamber containing flammable barrels, does the structural integrity of the chamber roof care about the tunnel below? Currently chambers are pre-authored static prefabs with no notion of "supported by voxel above." If we want collapse mechanics later, that's a separate feature with its own design doc.

---

## 14. References

### Algorithm references

- [Mikola Lysenko — *Smooth voxel terrain (part 2)*](https://0fps.net/2012/07/12/smooth-voxel-terrain-part-2/) — the canonical Naive Surface Nets write-up. Read in full before writing the mesher.
- [Mikola Lysenko — *Meshing in a Minecraft game*](https://0fps.net/2012/06/30/meshing-in-a-minecraft-game/) — greedy meshing comparison; useful context even though we're not using it.
- [Eric Lengyel — *Transvoxel Algorithm*](http://transvoxel.org/) — the reference for LOD seam handling. Port directly; don't reinvent.
- [Tao Ju et al. — *Dual Contouring of Hermite Data* (2002)](http://www.frankpetterson.com/publications/dualcontour/dualcontour.pdf) — for when we revisit DC for sharp features.

### Game-specific design references

- [Astroneer — *Real-Time, Editable, Smooth Terrain in System Era's Astroneer* (GDC 2017)](https://www.gdcvault.com/play/1024292/) — closest commercial analog to what we're building. Watch this before Phase 1. Their determinism story (fixed-point SDF, integer brush math) directly informs § 4.
- *No Man's Sky* engine writeups — Hello Games published less than Astroneer did, but the public talks on their voxel system inform the cumulative-op model in § 10. (Low confidence on specific references — search "Sean Murray NMS voxel" for the latest.)
- [Petter Henriksson — *Voxel Game Engine in Unity* (free GitHub references)](https://github.com/petterhenriksson) — Unity-specific reference implementations; useful for the Burst + Job pipeline patterns even if the meshing algorithms differ.

### Unity-specific

- [Unity Manual — `Physics.BakeMesh`](https://docs.unity3d.com/ScriptReference/Physics.BakeMesh.html) — off-main-thread MeshCollider cook. Mandatory reading for § 6.
- [Unity Manual — Burst overview](https://docs.unity3d.com/Packages/com.unity.burst@latest/) — read before Phase 0.
- [Unity Manual — Job System overview](https://docs.unity3d.com/Manual/JobSystem.html) — read before Phase 1.
- [Unity Manual — `Mesh.SetVertexBufferData` / `SetIndexBufferData`](https://docs.unity3d.com/ScriptReference/Mesh.SetVertexBufferData.html) — zero-allocation mesh upload path.

### Internal docs (this repo)

- [README.md](../README.md) — architecture principles
- [PHYSICS_PLAN.md](PHYSICS_PLAN.md) — non-negotiables (§ 1) apply to voxel terrain too
- [NETCODE_PLAN.md](NETCODE_PLAN.md) — § 6 state-replication taxonomy, § 7 BlockHitBatch precedent
- [PERFORMANCE.md](PERFORMANCE.md) — § 7 budget table this doc extends; § 5.3 Fluff cost; § 5.4 outline cost
- [SPHERICAL_ARENAS.md](SPHERICAL_ARENAS.md) — § 6 IGravitySource pattern this doc mirrors with IDigZone
- [BEST_PRACTICES.md](BEST_PRACTICES.md) — § 16 perf budgets; coding conventions
- [TIP_BLOCK_ATTACH.md](TIP_BLOCK_ATTACH.md) — DrillBlock will subclass TipBlock

---

*Last updated: 2026-05-14 — initial draft. Update on every phase boundary.*
