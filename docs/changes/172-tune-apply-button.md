# 172 — Tune panel "Apply to bot" button (LOG-172)

Two user reports after 171 shipped:

1. **Grapple not grabbing trees.** Not a code bug — the user's editor
   never recompiled commit 6ea9672c (landed while the editor was
   unfocused; the compiled `GrappleMagnetBlock` had no `BeginSwingLatch`
   at all, verified by reflection over the bridge). Forced a refresh,
   re-verified the method exists. Note for playtest: foliage has no
   collider — aim at the trunk.

2. **Pogo power tune didn't reach the game.** Root cause: the variant
   panel's two silently-identical modes. Bound (T-mode, red "Tuning —")
   edits propagate + sync per tick; unbound (hotbar-selected, Alt-freed
   cursor) edits write only the next-placement cache — the placed pogo
   and the blueprint never heard about it. The user's saved file DID
   carry `blockConfig: 4.0` from a later bound edit; the whole
   persistence chain (save → JSON → load → `ChassisAssembler` →
   `PogoDefaults.ResolvePower`) verified intact live.

## Fix (user-directed: explicit Apply button)

Blanket-on-drag was prototyped and REVERTED in-session: the
span-isolation session deliberately retired implicit all-blocks
propagation (`BuildSessionInstanceEditTests` encodes it — one foil's
span drag must never rewrite every foil). The user chose an explicit
verb instead:

- **"Apply to bot" button** in the panel title band (top-right), unbound
  mode only (hidden while tune-bound — that flow is live already).
  Click pushes the panel's caches onto every placed block of the type
  and syncs the blueprint. Hover tip explains the next-placement default
  and points at T-mode for single-part tuning. Tip strip confirms
  "Applied to N placed blocks" on click.
- **Seed-on-select**: unbound selection now seeds the caches from the
  first placed block of the id, so the sliders show the bot's current
  tune and Apply pushes what's on screen — not sentinel zeros (which
  would wipe pitch/dims tunes). The eyedropper path passes
  `seedFromPlaced: false` so a pick isn't clobbered by re-seeding.

## Files

- `Assets/_Project/Scripts/Gameplay/BuildSession.cs` —
  `ApplyVariantCachesToPlacedBlocks` (returns count; no-op while an
  instance is bound), `SeedVariantCachesFromPlacedBlock`.
- `Assets/_Project/Scripts/Gameplay/VariantConfigPanel.cs` — button
  build + visibility, seed-on-select flag, `OnApplyClicked`.
- `Assets/_Project/Tests/EditMode/Blueprints/BuildSessionApplyToPlacedTests.cs`
  — new (3 tests): apply writes all-of-id + syncs entries; bound → no-op
  (span-isolation guard); seed copies placed tune / false when absent.

## Open items

- Screenshot check of the title band owed: long unbound titles could
  crowd the 96px button.
- Session 171's swing playtest + profiler capture still owed.
