# Session — Polish: foam wake on chassis + connectivity flood-fill at placement

**Intent.** Two follow-ups from the water-visuals session, picked off the
roadmap: *"yep, let's do 1 and 3"* — connectivity flood-fill at placement
time, and foam-on-collision wake where chassis cut through the surface.

**Shipped.**

- **Foam wake.** [BuoyancyController.cs](../../Assets/_Project/Scripts/Gameplay/BuoyancyController.cs)
  now keeps a static `Active` registry (HashSet, OnEnable add / OnDisable
  remove) and a per-instance `SurfaceContacts : IReadOnlyList<Vector2>`.
  Each `FixedUpdate` clears the list and re-appends the world XZ of every
  block whose submerged fraction lies in (0.05, 0.95) — i.e. blocks
  straddling the waterline, the natural hull-meets-surface points.
  [WaterMeshAnimator.cs](../../Assets/_Project/Scripts/Gameplay/WaterMeshAnimator.cs)
  reads the registry once per Update and, in the per-vert loop, computes a
  smooth-falloff foam halo around each contact (`_wakeFoamRadius=2.5 m`,
  `_wakeFoamStrength=0.85`). Max-blended with perimeter and crest foam so
  the result stays in [0,1] and the shader never saturates back to white
  the way it did pre-explicit-vertex-colour.
- **Connectivity flood-fill at placement.** [BlockEditor.cs](../../Assets/_Project/Scripts/Gameplay/BlockEditor.cs)
  gained `BuildCpuReachableSet()` — same BFS pattern as
  `WouldOrphanIfRemoved`, but rooted at the CPU and returning the full
  reachable set. `IsValidPlacement` now requires the new cell to be
  adjacent to a CPU-reachable block, not merely *any* block. In normal
  play this is identical to the old "any neighbour" rule (every existing
  block is CPU-reachable by induction); the change defends against
  loading a hand-edited or corrupted blueprint that came in with a
  disconnected island — you can no longer extend the orphaned cluster,
  only the CPU's component. Empty-grid case still allows the very first
  block.

**Cost notes.** Wake is 4 225 verts × ~1 chassis × 5–40 contact points =
~30–170 k distance tests/frame, well under budget. Connectivity BFS runs
once per `UpdateTarget` (≈ once per frame while build mode is active) over
≤ 100 blocks — sub-microsecond.
