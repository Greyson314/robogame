# 121 — Garage texture + liveliness pass

User ask: shrink the new bubble + floor, then add texture and life to the
garage. All decor stays **code-applied** (the session-120 pattern — scene
files kept reverting), now factored out of `GarageController` into:

- `GarageDecor` (static builder, `Gameplay/GarageDecor.cs`) — builds/enforces
  every decor piece idempotently by name on garage load.
- `GarageAmbience` (`Gameplay/GarageAmbience.cs`) — animates it. Per-frame
  path is allocation-free (cached MPB + property ids); owns and destroys all
  runtime-created materials/textures so repeat visits don't leak.

`GarageController.ApplyGarageDecor()` is now a one-line delegate.

## What changed

- **Smaller stage:** platform r~75 → r~35 (scale 150 → 70), bubble r~85 →
  r~45 (scale 170 → 90). Both read cavernous around a <5 m bot.
- **Platform texture:** procedural 128² panel-grid texture (Concrete panels,
  ±5% deterministic value jitter, Slate seams) tiled ×18 over a clone of the
  bay floor material. Palette-pure — art-direction forbids imported
  realistic textures, so no pack stone/dirt was used.
- **Rim:** hazard-stripe step (Hazard token) under the platform edge + an
  additive cyan trim ring (ShieldBubble shader instance).
- **Beacons:** 8 masts around the rim (CPU-beacon motif: mast + glowing tip),
  staggered slow blink via MPB on `_RimIntensity`; real cyan point lights on
  every other mast only (URP forward budget).
- **Holo build-pad ring:** flat additive cyan ring around the podium,
  rotating ~9°/s.
- **Dust motes:** one looping ParticleSystem (~150 live), additive unlit
  squares in Cyan, noise-drifting inside the bubble, prewarmed.
- **Asteroid field:** 7 clusters of 2–4 slate cubes 130–280 m outside the
  bubble, deterministic layout (fixed seed), each cluster slowly tumbling +
  the whole field orbiting 0.15°/s.
- **Star drift:** skybox now instantiated (asset no longer dirtied) and its
  `_Rotation` rotates 0.25°/s.

All colors are palette tokens (`Concrete/Slate/SlateLight/Hazard/Cyan`),
mirrored locally because `WorldPalette` is editor-only. Glow uses only the
owned `Robogame/ShieldBubble` shader; particles reuse the `VfxSpawner`
runtime-material idiom.

## Feedback round (user playtest)

- **Garage now opens in drive mode** — removed the session-120
  `_buildMode?.Enter()` from `GarageController.Start`. The chassis stays
  parked (kinematic) either way; "drive mode" in the garage is just the
  follow-cam non-build state, so the missing platform collider is moot.
- **Brighter:** skybox instance `_Exposure` 0.6 → 1.25 + lifted `_Tint`;
  ambient 0.13/0.15/0.20 → 0.21/0.24/0.31; asteroids re-tinted SlateLight,
  scaled 8–20 (was 5–14) and pulled in to 105–220 m; platform panels now a
  Concrete↔SlateLight patchwork (pure Concrete was invisible at night).
  Note: the Polyverse star cubemap is inherently multi-colored — the RGB
  star tints were always there, just too dim to read before.
- **Dust motes:** rate 10 → 4/s, size 0.06–0.18 → 0.03–0.09, alpha
  0.5 → 0.22, cap 256 → 128.

## Verification

- Headless rig (bridge was down): EditMode 309/310, PlayMode 114/115,
  0 failed (the two non-passes are pre-existing skips/inconclusive).
- Play-mode eyeball via MCP after both rounds, 0 errors / 0 warnings:
  panel grid reads, rim + beacons + holo ring show, dust subtle, asteroid
  visible, entry is drive mode. First round caught the platform rendering
  white — MK Toon clones don't sample a runtime-assigned albedo map (it's
  behind a shader-feature keyword), so the platform material is URP/Lit.
- MCP bridge note: "Connection revoked" recurred twice; recovered on its
  own after editor restart + a second client connecting — the in-editor
  Stop/Start was never actually clicked.

## Notes / known limits

- The disabled square `Floor` still owns the only floor collider; the round
  platform has none (pre-existing from session 120 — garage bot is always
  kinematic, so nothing falls today).
- Cylinder cap UVs slightly distort the panel grid near the disc edge;
  acceptable at gameplay camera angles, revisit only if it reads badly.
- Multi-cell footprint rotation + other session-120 leftovers still tracked
  in `docs/changes/120-playtest-pass.md`.
