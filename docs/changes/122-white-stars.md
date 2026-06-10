# 122 — White procedural starfield

User report after the session-121 brightening: stars rendered as
red/green/blue star-sprite shapes. Root cause: the garage skybox material's
cubemap is Polyverse Skies "Stars 5Corners" — **channel-packed**, R/G/B are
three independent star layers meant for the pack's own shader (which samples
and tints each channel separately). The built-in Skybox/Cubemap shader the
garage uses renders the raw RGB, so each layer reads as a colored sticker.
Every stars texture in the pack is packed the same way, so no texture swap
fixes it. The dark pre-121 tint/exposure had been hiding the colors.

## What changed

- `GarageDecor.BuildStarCubemap()` — runtime-generated 512²-per-face
  cubemap: 650 stars/face, white/grey only (1 px dim singles, ~10% brighter
  2×2, ~1.5% full-white 3×3) over near-black night blue. Deterministic seed,
  no mips, built once per garage load, owned/destroyed via
  `GarageAmbience.Owned`.
- Skybox instance `_Tex` ← the generated cubemap; `_Tint` → neutral grey
  0.5 (identity for Skybox/Cubemap — the old blue-ish tint would re-tint
  the white stars). `_Exposure` stays 1.25.
- `_Rotation` star drift unaffected (same shader).

Also this session (no code change needed): audited the parallel session's
commit `e6d8fb82` after a cross-session edit collision — its content was
exactly the requested drive-mode/brightness/dust changes; nothing to revert.

## Verification

- Unity console after refresh: 0 errors; only pre-existing CS0618 warnings.
- Star density/brightness values are first-guess — eyeball in Play.
