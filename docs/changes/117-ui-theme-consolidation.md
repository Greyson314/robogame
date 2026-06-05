# 117 — UI theme consolidation (UGUI palette + HUD tone pass)

> Art & UI deep-dive. Closes art-direction.md's deferred Phase-1 "tone-pass on
> HUDs to palette tokens" and Open Question 7 ("HUD tone deserves its own doc").
> New doc: [ui-direction.md](../subsystems/ui-direction.md).

## Finding (from a full UI inventory)

The 3D/environment art is already on-direction (Fluff grass, Polyverse sky,
on-palette slate props — verified via scene capture). The weak spot is the 2D UI:

- **IMGUI HUDs**: already good — `HudStyles` (Core) is a complete theme helper and
  ~12 overlays use it. Only a few stray off-palette literals remained.
- **UGUI panels**: no theme helper at all. All 8 panels copy-pasted the same
  ~7 hardcoded colours (accent / dim / panel bg / button idle-highlight-pressed),
  with alphas already drifting (0.85 / 0.92 / 0.93). A one-line theme change
  meant editing 8 files.

## What shipped

- **New `UguiPalette` (Core)** — the UGUI counterpart to `HudStyles`. Shared
  semantics (Accent/Text/Danger/Healthy) **derive from `HudStyles`** so HUD +
  panels move together; `PanelBg` is anchored to the locked `UIBg` token. No
  third divergent palette.
- **Migrated panels** off their hardcoded blocks → `UguiPalette`: MainMenu,
  Settings, SceneTransition (main button chrome), Lab, VariantConfig, BuildMirror,
  PlacementFeedback. PlacementFeedback's off-palette salmon error text → unified
  `Danger`.
- **IMGUI fix**: KillAnnouncer's hardcoded plasma → `RuntimePalette.Plasma`.
- **New [ui-direction.md](../subsystems/ui-direction.md)** — the two-helper system,
  the "no `new Color()` in UI code" rule, the token tone guide, and the tracked
  remaining debt. art-direction.md Phase-1 item + Open Q7 updated to point here.

Net visual effect: near-identical (most values already matched the palette);
the win is one-file reskinning + a few coherence fixes (unified panel alpha,
unified error red, plasma on-token).

## Known debt (now one-liners against UguiPalette — tracked in ui-direction.md)

- `BuildHotbar.cs` (14 literals — tabs/slots/CPU bar; build-mode-only).
- A few one-off tints in `SceneTransitionHud` (dropdown viewport, destructive red).
- Scattered IMGUI literals: AimReticle, DeathOverlay, HitMarkerOverlay,
  LowHealthVignetteHud, FloatingDamageOverlay, ScrapCarriedIndicator, CenterOverlay,
  debug shadow colours.
- A UGUI button-colour factory (boilerplate reduction) — deferred.

## Verification

- Live editor (full package set) recompiled clean — **0 console errors**.
- Bridge confirmed `UguiPalette.Accent == HudStyles.Accent`, `PanelBg` = UIBg@0.93.
- (The headless rig run failed only on an `EPERM` while resolving the in-flight
  `com.unity.ai.assistant` package update — unrelated to this change; no `error CS`.)

## Also noted, not done (needs play-mode eyes)

`BlockMaterials.cs` fully configures the MK Toon `+ Outline` variant on hero
blocks, but art-direction Phase-2 shows the **Per-Object-Outlines renderer feature
was never added to `PC_Renderer.asset`** — so hero-block ink outlines may not be
rendering. Verify in play mode before editing the renderer asset.
