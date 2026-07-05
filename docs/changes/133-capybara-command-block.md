# 133 — Capybara command block: study → in-game CPU visual

**Date:** July 5, 2026
**Intent:** The missing citizen. Iterate a capybara-pilot command-block
study in Blender (blender-mcp, live review with user), then wire it
into the game as the CPU block's visual.

## Study arc (`artgen/inv_capycube.py`, v1→v6.1, all in git)

- v1 frame-cage + helm + hanging lantern → v2 user redirect: **1×1×2**,
  bottom cell = planked cube with inset open-air cockpit well (walnut
  coaming, rolled linen pad), top cell = capybara from the shoulders up.
- Capybara learnings: lathe heads read generic-rodent — the capy lives
  in rounded-rectangle (superellipse) loft sections, a wide
  barely-tapering snout, and eyes/ears set high and far back. Cuteness
  pass (v3) then full reversal (v4, user reference art): **no applied
  face geometry** — face is painted via per-facet material indices
  (`make_object` face_mat_idx). Eyes = one black side facet each; nose
  = two dark facet-pairs flanking the nose top. v5 tried shaped eye
  decals conformed to the superellipse wall (solver in git); v6
  reverted to facets on user call; v6.1 added `extra_ts` vertex-row
  injection to trim the eye facet 35 % from the bottom.
- Facet-alignment tricks now in the script: half-segment ring phase
  (centres a facet on each side wall, lands a vertex at top-centre to
  separate the nostril slits), `extra_ts` for sub-facet paint edges.
  Windows select facets by centre position — re-tune after ring edits.
- Head+body merged (head sinks into the loaf, no neck step); subtle
  scallop "fluff" modulation on silhouette rings; ears are thin pointed
  flaps angled notably outward.

## In-game wiring

- `CapyCube_Inv.fbx` exported (origin at bottom-cell centre → offset 0;
  entry added to `inv_export.py` STATICS).
- `InventorModelWiring` gained the BlockDef_Cpu row (static path).
  Asset diff audited: model fields only, zero stat re-stamps.
- **`CpuBlockMarker` is model-aware now:** with a static visual model
  on the definition it skips the beacon mast/tip (they'd spear the
  pilot) and keeps the pulsing cyan point light on a mount at local
  y 1.45. Primitive CPUs keep the full beacon unchanged.
- Live-verified in the garage (play mode, real spawn path): model
  attached under BlockModel, host mesh hidden, light pulsing above the
  hatch. Cockpit cube buries into the hull layer and reads as a deck
  hatch — the intended ship language.

## Fix-up leg (same session, user review)

- **Facing:** the capy rode backwards — the study faces Blender +Y but
  Unity forward is Blender −Y (the mortar exporter lesson repeats).
  `inv_export.py` gained `export_capycube()` (bakes 180° about Z);
  moved out of the STATICS table.
- **Preset weapons off the CPU cell:** Tank + Prop Plane weapons →
  front spine (0,1,3); ground starter weapon → (0,1,1). Assets
  regenerated (validator green). Left alone: Helicopter (CPU enclosed
  in the cabin — capy rides inside, invisible) and stress towers
  (rotor-above-CPU is the stress profile). Verified live from
  Bootstrap: Tank shows the capy mid-deck facing the bow gun.
- **AudioRouter domain-reload NREs — fixed (same session, spawned
  task):** a mid-play recompile re-runs OnEnable on the surviving
  router but not Awake, so the non-serialized voice pool was null.
  Three-part fix: `EnsurePool()` on OnEnable + null-guard in Update;
  `BuildVoicePool` adopts surviving `Voice_XX` children by name
  (blind rebuild stacked 24 duplicates per recompile); and
  `EnsureBootstrap` adopts a surviving router instead of cloning one
  per reload (statics reset, the GameObject doesn't). Verified with a
  forced `RequestScriptReload` mid-play: zero NREs, one router, 24
  voices, PlayUI claims and plays through the adopted pool.
- **Face-void bug (user report: "nose non-existent/transparent"):**
  `make_object`'s 0.003 EdgeSoften bevel, applied at export, overlaps
  across the 0.009-wide nose ring bands and corrupts the face-plate
  shading — Unity renders the plate as an unlit void; Blender's
  viewport hides it (no backface culling). Fixed with `_strip_bevel`
  on head/body/ears. **Artgen gotcha for future organic meshes: ring
  spacing must clear 2× the bevel width, or strip the modifier.**

## Suite completion (same session)

- **`inv_foil.py` — the existing plain aerofoil** in rib-and-membrane
  language at FoilDefaults dims (1.0 × 0.9), row y = −11. Deliberately
  neither the bat-wing nor the aerial screw: the composed rotor+foils
  mechanic keeps its current geometry while the macro-component
  decision stays parked.
- **Module family completed** (`inv_modules.py`, y = −8 row): Blink =
  plasma hourglass (new InvPlasma emissive ≈ Plasma token), Shield =
  linen parasol, Smoke = censer + carved wisp, Invis = empty jar with
  a specimen tag, Mines = banded bomb pyramid. Every gameplay block id
  now has an inventor study.
- **All six wired in-game (same session, user go-ahead).** Modules via
  the static path (shield verified on a live placement). The foil is
  the first authored model on a scalable block: `AeroSurfaceBlock`
  hangs `Foil_Inv` under its Wing rig with an inverse-FoilDefaults
  child scale (absolute cube scale → ratio scaling; default dims =
  authored mesh exactly). Rotor mode = +90° Z axis-permutation (root
  on the hub side). Mirrored side mounts flip camber, so
  `SyncWingModel` mirrors via negative X scale when the camber axis
  points below chassis horizontal. Exporter bakes Rz(180)·Ry(−90).
  Verified live: heli rotor = four cambered linen sails; plane span-4
  wings stretch + camber symmetrically; fin keeps its slab pending
  its own wiring. Suite green 378/378.

## Open / design review
- Garage scene holds a parked chassis remnant ~1600 m below origin
  (CPU at grid (0,1,0), never Initialize'd this session — host cube
  still visible, no model). Harmless, worth a cleanup look someday.
- Parked from this session's directional convos: ADR for "parametric
  art for composable parts, authored forms for discrete parts" (the
  aerial-screw / bat-wing vs. generic-propulsion-pillar tension), and
  the accent-color A/B trial (coach green vs. indigo linen) on the
  study row.
