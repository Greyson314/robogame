# UI Direction (HUD + panels)

> How Robogame's **on-screen UI** looks and stays consistent. The sibling of
> [art-direction.md](art-direction.md) for the 2D layer — it closes that doc's
> Open Question 7 ("HUD tone… deserves its own doc"). Same rule applies: every
> visible colour is a palette token, never an ad-hoc literal.

> **Current look (July 2026): "inventor + painter"** — ink on paper, indigo
> wash, strictly rationed vermilion, Averia Libre (primary) + Space Mono
> (secondary; italicized for annotations — user pick over the handoff's
> Yuji Syuku / Cardo pairing), brush shapes from the runtime-generated
> [`InkKit`](../../Assets/_Project/Scripts/Core/InkKit.cs). Canonical spec:
> [research/ui-design-handoff.md](../research/ui-design-handoff.md); rollout
> log: [changes/134](../changes/134-inventor-ui-pass-1.md) (pass 1: tokens,
> main menu, settings; combat-HUD component shapes are pass 2).

## The two theme helpers (both in `Robogame.Core`)

The UI is built procedurally in C# — no prefabs, no UXML. It splits two ways,
each with one source-of-truth theme helper:

| Layer | Renderer | Theme helper | Used by |
|-------|----------|--------------|---------|
| **HUD overlays** | IMGUI (`OnGUI`) | [`HudStyles`](../../Assets/_Project/Scripts/Core/HudStyles.cs) | scoreboard, stats, kill feed, reticle, module bar, nameplates, damage numbers, dev HUDs |
| **Menus / panels** | UGUI (procedural) | [`UguiPalette`](../../Assets/_Project/Scripts/Core/UguiPalette.cs) | main menu, settings, scene-transition, build hotbar, lab, variant panel, mirror banner |

`UguiPalette` **derives** its shared semantics (accent / text / danger / healthy)
from `HudStyles`, so the HUD and the panels move together — there is never a
third divergent palette. Both ultimately trace to the locked 12-token palette in
[`RuntimePalette`](../../Assets/_Project/Scripts/Core/RuntimePalette.cs) /
art-direction.md.

## The one rule

**No `new Color(...)` literals in HUD or panel code.** If you're typing an RGBA
in a `*Hud.cs` / `*Controller.cs` / `*Panel.cs`, you're drifting off-palette —
reach for a token instead. The compiler can't enforce this; discipline + review
does (same as the art palette).

- IMGUI: `HudStyles.Accent`, `.TextPrimary`, `.TextMuted`, `.Danger`, `.Healthy`,
  `.Warning`, `.PanelBg`, `.PanelBgHeavy`, `.PanelEdge`; styles via
  `HudStyles.Bold(size, color)` / `.Label(...)`; shared `HudStyles.Font`.
- UGUI: `UguiPalette.Accent`, `.AccentPressed`, `.Text`, `.TextDim`, `.Danger`,
  `.Healthy`, `.PanelBg`, `.Backdrop`, `.ScrimDim`, `.ButtonIdle`, `.Header`.

A standard UGUI button is `ButtonIdle` face, `Accent` highlight, `AccentPressed`
pressed, white `normalColor`. A destructive button swaps in a red highlight.

## Token tone guide

| Semantic | Token | When |
|----------|-------|------|
| Player / primary action / headers | `Accent` (hazard orange) | launch button, active tab, selected slot, group headers |
| Enemy / error / destructive | `Danger` (red) | enemy side, placement errors, delete button, low HP |
| Affirmative / healing | `Healthy` (mint) | HP-full bar, repair, friendly annotations |
| Energy / module / rampage | `Plasma` (purple) | module FX, rampage banner |
| Body text | `Text` / `TextPrimary` | readouts, labels |
| De-emphasised text | `TextDim` / `TextMuted` | secondary labels, hints |
| Panel chrome | `PanelBg` (= UIBg token) | every panel background |
| Modal focus | `Backdrop` (near-opaque) / `ScrimDim` (over gameplay) | Lab overlay / Settings pause |

## Status

- ✅ `HudStyles` — IMGUI HUDs are essentially fully palette-compliant.
- ✅ `UguiPalette` created; migrated: MainMenu, Settings, SceneTransition (main
  chrome), Lab, VariantConfig, BuildMirror, PlacementFeedback; IMGUI KillAnnouncer
  plasma → token. See docs/changes/117.
- ⏳ **Known debt** (mechanical, now one-liners against `UguiPalette`):
  `BuildHotbar.cs` (14 literals — tabs/slots/CPU bar), a few one-off tints in
  `SceneTransitionHud` (dropdown viewport, destructive-button red), and the
  scattered IMGUI literals the UI inventory flagged (AimReticle, DeathOverlay,
  HitMarkerOverlay, LowHealthVignetteHud, FloatingDamageOverlay, ScrapCarriedIndicator,
  CenterOverlay, debug shadow colours). Migrate opportunistically when touching
  each file.

## Open questions

1. A UGUI **button factory** (`ApplyButtonColors(Button)`) would remove the
   repeated `ColorBlock` wiring in every panel. Deferred — `UguiPalette` colours
   are the load-bearing win; the factory is boilerplate reduction. Needs a home
   in a UI-referencing asmdef (Core has no `UnityEngine.UI` dep).
2. Resolution scaling: IMGUI HUDs use fixed pixel sizes (readable at 1080p, not
   yet DPI-scaled). Revisit if targeting 4K / Steam Deck.
3. A future custom UI font (currently OS monospace for HUD, LegacyRuntime for panels).
