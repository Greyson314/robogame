# 134 — Inventor UI, pass 1 (tokens + menu + settings)

Implements the July 2026 "inventor + painter" design handoff
([docs/research/ui-design-handoff.md](../research/ui-design-handoff.md),
copied from the user's Claude-design session) across the theme layer and
the two highest-visibility screens. HUD component redesign (vitals
rulers, compass band, part panel, hotbar pips) is **pass 2 — not started**.

## What landed

- **`InkKit`** (new, `Core/InkKit.cs`) — runtime-generated brush sprite kit:
  blob, bar/wash fills (baked 0.8→0.4→0.08 tail), underline swipe, splat,
  wax seal (baked vermilion), dash tile, 28px grid tile, paper radial,
  registration mark. All white (tint via tokens), baked once at init,
  statics reset via `SubsystemRegistration`. Also loads the two fonts.
- **Fonts** — Yuji Syuku (display/UI) + Cardo Italic (annotations) TTFs in
  `Assets/_Project/Resources/Fonts/` (Google Fonts, OFL). Every UGUI panel
  and the IMGUI `HudStyles.Font` now use Yuji Syuku. Note: Yuji TTF is
  8.4 MB (CJK); subset before shipping if build size matters.
- **`HudStyles` + `UguiPalette` retokened** — ink/paper/indigo/vermilion
  replaces slate/hazard-orange. Team semantics: Accent(player) = indigo,
  Danger(enemy) = vermilion, Healthy = moss-ink, Warning = burnt ochre.
  IMGUI panels become translucent paper with ink text. New tokens: `Ink`,
  `InkHover`, `CreamText`, `Vermilion`, `IndigoText`, `FrameLine`,
  `GridLine`, `ButtonHover`. `UguiPalette.PanelBg` no longer anchors to
  `RuntimePalette.UIBg` (12-token lock is suspended; noted inline).
- **Main menu** — full redesign per `unified-menu`: paper + grid +
  registration marks, Title-Case wordmark over ink brush underline with
  two vermilion splats, Cardo tagline, ink-blob **Begin**, wash-underline
  **Settings** / **Take Leave** (exit hover = faint vermilion),
  mirror-written flavor line. Scene copy updated (MainMenu.unity:
  `Robogame` / `A Bestiary of Contraptions`).
- **Settings** — paper panel + grid, parchment header, brush-blob buttons,
  ruler-frame sliders (dashed ticks + bottom rule, indigo wash fill, ink
  thumb at −3°), ruler-rail toggles (ink splat knob: solid right = on,
  faded left = off), Cardo search placeholder, vermilion "Disabled" marks
  in Perf Bisect, no more ALL-CAPS headers.
- **Readability sweep** — every panel that hosted token text on a
  hardcoded dark face moved to parchment/paper tokens (PauseMenuHud scrim,
  SceneTransitionHud dropdown/template/name field + white labels → ink,
  VariantConfigPanel list/advanced-toggle). All panels off
  `LegacyRuntime.ttf`.

## Deviations from the handoff

- No letter-spacing (legacy `Text` can't track); accepted.
- Slider ruler uses a dashed tick *line*, not 8 discrete divisions.
- Bool tweak rows keep the toggle grammar; wax-seal checkbox sprite is
  baked and ready but unused until a true checkbox appears.
- Combat HUD is retinted via tokens only (paper backings, ink text) —
  component shapes unchanged until pass 2.

## Verification

- Headless rig (EditMode + PlayMode) run mid-pass and at end. 6 PlayMode
  failures in DrillBlock/DigZone + PerfRenderProbe present with UI-only
  diff — believed pre-existing from the drill-orientation session (see
  final run in session notes).
- Live MCP style check (play mode, screenshots): found and fixed — Yuji
  Syuku's tall CJK line box exceeds small rects and legacy `Text`
  truncates the whole line (all panel text now sets
  `verticalOverflow = Overflow`); title underline struck through the
  wordmark (Yuji glyphs render low in their line box — geometric text
  bottom ≠ visual bottom, underline/tagline moved down); secondary wash
  underlines invisible at 0.35 alpha with the thin Underline sprite
  (now BarFill @ 0.65). Menu + settings verified visually; compile clean.

## Known unknowns / follow-ups

- Pass 2: combat HUD components (vitals, compass, part panel, hotbar,
  kill feed splats), controls' stepper/select/tooltip, garage panels'
  full brush treatment (BuildHotbar, VariantConfigPanel, Lab).
- Yuji Syuku subsetting for build size.
- In-editor visual check of Yuji Syuku rendering at small sizes.
