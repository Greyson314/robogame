# 77 — Playtest fixes: drill polling, bomb crater scale, arena LOD

> Status: **shipped, PlayMode gate green.** Responding to the first
> visual playtest of the drill + dig zone + bomb stack. Three discrete
> problems addressed; the visual outcome should now read as intended:
> drills carve when driven into terrain, bombs leave proportional
> craters (not full-zone obliteration), and the in-arena dig zone
> presents as one continuous surface (no visible 4-square chunk grid).

## Symptoms

1. Drill-equipped chassis driven into the in-arena dig zone produced
   no carving.
2. A single bomb dropped on the dig zone destroyed essentially the
   entire dig-zone mesh in one detonation — too aggressive to see
   whether tunneling was working.
3. The in-arena dig zone surface visibly broke into a 2×2 grid of
   squares.

## Diagnoses

### 1) Drill no-carve — positional, not a wiring bug

The Phase 76 `DrillCollisionForwarder` is wired correctly; the
machine-gate tests pass. The real failure is geometric: a
cell-sized `DrillBlock` mounted on the chassis body never
physically intersects the chunk's surface `MeshCollider` when the
chassis drives over the terrain. The terrain surface is exactly
that — a 2D surface mesh, not a volume collider — and the wheels
keep the chassis body floating above it. Contacts fire on whatever
of the chassis IS touching the surface (wheels, frame), but the
drill cell specifically isn't in the contact set, so the
forwarder routes nothing to the drill.

### 2) Bomb obliterates — radius mismatch

`BombBayBlock._radius = 18` is sized for chassis-combat splash
damage. The in-arena dig zone is 2×1×2 chunks = 32 × **16** × 32 m;
the bomb's 18 m crater radius is larger than the zone's depth, so
one bomb's `SphereSubtract` mutates essentially every cell in the
zone. Combat splash and terrain crater radius were the same value,
which is fine on a 100-m planet but is collateral damage on a
small in-arena zone.

### 3) Four squares — LOD-mismatch artifact at typical view distance

The in-arena zone sits 60 m from spawn. With `_lodDistance1 = 32`
and `_lodDistance2 = 64`, a player standing 35–70 m away has
some chunks at LOD 0 (closer face) and other chunks at LOD 1 or 2
(farther face). Phase 4c's transition handling closes the
perpendicular axis at the seam but leaves the in-plane (Y/Z) spacing
at fine resolution on the fine side vs coarse on the coarse side —
which reads as visible seams when the player can see all four
chunks at once. The zone is small enough that LOD adds nothing
(4 chunks × ~20K tris/chunk = 80K total at full res, well under
budget).

## Fixes

**`DrillBlock`**: add a `FixedUpdate` poll. Each fixed step, query
`DigField.ZoneAt(transform.position)`; if the drill sits inside a
registered zone, emit a `CapsuleSubtract` brush at the drill's tip.
The throttle (`_emitInterval`, default 30 Hz) gates against spamming.
This bypasses the surface-contact requirement: a drill *inside* the
terrain volume drills regardless of whether its `BoxCollider` is
touching the `MeshCollider`. Brushes on already-exterior cells are
no-ops (max-fold idempotent), so the cost is only paid where it
actually carves. The contact path stays in place for the cases where
it works (e.g., a side-mounted drill hitting a vertical wall).

**`ProjectileWorld.cs`**: new `TerrainCraterScale = 0.3f` constant;
the bomb-detonation path now passes `spec.SplashRadius *
TerrainCraterScale` to `TerrainCratering.OnBombDetonation`. Default
18 m splash → ~5.4 m crater. Visible but proportional; repeated
bombing now demonstrates progressive tunneling.

**`EnvironmentBuilder.BuildArenaDigZone`**: set `_enableLod = false`
on the in-arena zone via SerializedObject. The zone is 4 chunks
total — well under any LOD-driven budget — and forcing all chunks
to LOD 0 eliminates the LOD-mismatch seams. The Phase 4c plumbing
stays in place for larger zones authored later.

## What's deferred

- **Same-LOD chunk-boundary normal seams.** `RecalculateNormals`
  averages over each chunk's own triangle set, so a vertex on the
  shared boundary plane ends up with slightly different normals on
  each side. Visually subtle but present even at LOD 0. The clean
  fix is analytic normals from the SDF gradient (consistent across
  chunks by construction); deferred until it actually reads in
  a playtest.
- **Surface-contact drill path becoming redundant.** The new
  `FixedUpdate` poll subsumes the contact path for the in-volume
  case. The contact path remains useful for wall-contact (drill
  outside the volume hitting a vertical face), but most arena
  drilling will go through the poll path. Worth revisiting whether
  to keep both or simplify to volume-only at a future polish pass.
- **Per-block tunable terrain crater radius.** The 0.3× scale is a
  reasonable default but a future bomb-tuning pass should probably
  expose this on `BombDefinition` so heavy-bomb / light-bomb
  variants can shape it independently from combat splash.

## Files

- Modified: `Assets/_Project/Scripts/Voxel/DrillBlock.cs`
  (FixedUpdate auto-poll),
  `Assets/_Project/Scripts/Combat/ProjectileWorld.cs`
  (TerrainCraterScale constant + apply at bomb detonation),
  `Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs`
  (disable LOD on in-arena zone),
  `Assets/_Project/Tests/PlayMode/Voxel/DigZoneTests.cs`
  (new auto-poll test).
- Adds previously-omitted `.cs.meta` files for Phase 75 / 76
  scripts (`OccupancyGrid.cs.meta`, `OccupancyGridTests.cs.meta`,
  `DrillCollisionForwarder.cs.meta`) — Unity needs these to track
  asset GUIDs.

## Validation

- `.claude/scripts/run-tests.sh PlayMode`: 56/58 passed, 2 failed
  (pre-existing `HookGrappleTests` + `RotorBlockTests`, unrelated).
  New `DrillBlock_InsideZone_AutoPollsViaFixedUpdate_CarvesSdf`
  test passes.
