# 119 — Arena terrain: "Sunken Crossing" (diggable hills/ridges/valleys)

> User intent: make the combat arena feel like a real place — cliffy /
> hilly / mountainy / valley-y — leaning into "my side vs your side",
> while staying performant. Standing rule restated this session: **all
> terrain is diggable by default** (static is the exception). Driven by
> design-pilot + planner research passes; the layout + scope forks were
> signed off via AskUserQuestion before any code.

## Decisions (signed off)

- **Height: hybrid.** Playfield relief stays ≤ ~13.5 m so it fits the
  existing **single-chunk-tall** voxel volume (no 2nd Y-layer → no
  over-cliff triangle risk, fully diggable, budget-safe). Tall drama is a
  **non-diggable backdrop range beyond the ±170 m walls** — the stated
  exception to diggable-by-default.
- **Symmetric** north↔south (fair, MP-ready; central valley = no-man's-land).
- **Props:** ridge-crown rocks + a light tree scatter.

## What shipped (all code; needs a `Build Everything` to hit the scene)

**1 — Heightmap redesign.** [`HeightmapField.Sample`](../../Assets/_Project/Scripts/Voxel/HeightmapField.cs)
gains three structural terms on top of the legacy two-octave rolling
detail, all **even in z** (mirror-symmetric) and clamped to [−10, 13.5]:
- **Diagonal ridges** — two Gaussians across the lines z = ±x, windowed
  to leave an open centre gap (r < 56 m flat) and clear flank corridors
  near the walls. The X never sits on x = 0 at height, so depot↔depot
  sightlines stay open.
- **Central valley** — a shallow E-W trench along z ≈ 0 (`field`-gated so
  it never digs the spawn pad).
- **Base bowls** — symmetric raised rims minus a central dip at (0, ±92),
  floor pinned near y = 0 so the team depot pads land flush; rolling
  detail is suppressed toward each bowl centre for predictable ground.

Geometry (where features sit) is fixed in `HeightmapField`; the three
heights are tunable knobs: `ridgeAmp 9.5`, `valleyDepth 2.5`, `bowlAmp
6.5` on [`HillsSettings`](../../Assets/_Project/Scripts/Tools/Editor/HillsSettings.cs)
(+ the live `.asset`). The amps default 0 in the struct, so the new terms
are **inert for every pre-119 consumer** (fast-out path). Both the grass
mesh ([`HillsGround`](../../Assets/_Project/Scripts/Tools/Editor/HillsGround.cs))
and the voxel SDF seed sample the same params, so the two layers stay
aligned — `HillsGround.ToHeightmapParams` + `EnvironmentBuilder` reflection
push carry the three new fields.

**2 — Retired the fake pyramids.** `SceneScaffolder.PopulateTestTerrain`
gained `buildMountains = false` (default off); the in-arena tiered-cube
ring no longer builds. Walls unchanged.

**3 — Set-dressing.** New [`ArenaProps`](../../Assets/_Project/Scripts/Tools/Editor/ArenaProps.cs)
(called from `EnvironmentBuilder.BuildArenaEnvironment`): two staggered
rings of craggy non-diggable peaks beyond the walls (38–82 m, colliders
stripped — unreachable horizon), rock columns crowning the diagonal
ridges (micro-cover), and a deterministic golden-angle **tree scatter**
on the mid-slopes (rejects ridge crowns, base bowls, flat combat box, and
the valley floor). All terrain-sampled so props sit ON the ground; all
reuse `ArenaWall`/`ArenaGround` tokens — no new materials.

**4 — Repair pad regrounded.** Old (55,55) sat exactly on a ridge crown
(~9 m float). `GameplayScaffolder.BuildArenaPassA` now pushes it to
(40, 0.1, −25) — flat player-side inner box. Depots (0, ±90) and spawn
(0,0) need no move: the bowls + the flat centre handle them.

## Tests (machine gate)

[`HeightmapFieldTests`](../../Assets/_Project/Tests/EditMode/Voxel/HeightmapFieldTests.cs)
+6: default-zero == legacy, spawn-flat-with-structure, ridge crown >
centreline (sightline), valley lowers midline, bowls mirror + floor near
0, and the whole playfield stays ≤ 13.5 m (voxel ceiling). Projection
round-trip extended for the three new knobs. Existing tests untouched
(they never set the new amps).

## Known follow-ups (user / visual)

1. **`Robogame > Build Everything` required** — rewrites Arena.unity
   (env + dig-zone heightmap params + grass rebake + objective pushes).
   Not run headlessly here.
2. **Visual verify** via the Unity MCP bridge — it was **down** this
   session (re-approve: Project Settings → AI → Unity MCP), or use the
   headless ScreenCapture rig. Confirm: ridges/valley/bowls read; depots
   sit flush in their bowls; trees/rocks aren't floating; backdrop range
   reads as a horizon; no chassis float on combat-target spots.
3. **Runtime terrain-grounding** for spawned pads/dummies is deferred —
   handled this pass by keeping the inner combat box (r < 56 m) flat. If a
   visual pass shows floaters, the clean fix is sampling `HeightmapField`
   (or a downward ray) at spawn in `ArenaController`.
4. **Perf pass** — profile idle tris / SetPass after the regen (INV-7);
   the playfield stays 1 Y-layer so the budget story is unchanged, but the
   new relief raises per-chunk active-cell counts a little.
