# Handoff: Robogame UI/HUD — "Inventor + Painter" Direction

## Overview
A unified visual direction for Robogame's UI and HUD (voxel combat sandbox, Unity 6, uGUI). The look: a da Vinci drafting table crossed with a painter's sketchbook — linen-paper grounds, ink construction marks as UI chrome, brushstroke fills for state, one rationed vermilion accent. Locked in a design exploration session July 2026.

## About the Design Files
The files in `reference/` are **design references created in HTML** — they show intended look and proportion, not production code. The task is to **recreate these in Unity uGUI** (Canvas + TextMeshPro + Image/RawImage), using the project's existing patterns. Do not attempt to embed or port the HTML.

- `unified-hud-v2.html` — **THE combat HUD design** (the chosen one)
- `unified-controls.html` — settings widgets: toggle, slider, stepper, select, checkbox, tooltip
- `unified-menu.html` — main menu (Start/Settings/Exit restyled)
- `unified-hud-b-alt-layout.html`, `unified-hud-c-alt-layout.html` — rejected layout alternates, kept for context only
- `inventor-aesthetic.md` — the original art-direction note

## Fidelity
**High-fidelity for style, mid-fidelity for layout.** Colors, typography, shape language, and state treatments are final — copy them exactly. Absolute pixel positions were composed for a 700×400 specimen card; re-anchor elements to screen corners/edges responsively (anchors given per component below).

## Design Tokens

Colors:
- `--paper`: radial falloff `#F6F0E0` (center) → `#EDE4CC` → `#E1D5B6` (edges). In Unity: a paper texture or subtle radial vignette, NOT flat.
- `--grid-line`: `rgba(51,44,33,0.03)` 1px lines every 28px, both axes (faint drafting grid on paper surfaces)
- `--ink`: `#26211A` (solid fills, primary buttons, brush strokes)
- `--ink-text`: `#2E2820` (primary text)
- `--ink-faded`: `#6E6350` (annotations, secondary text)
- `--frame-line`: `rgba(46,40,32,0.5)` rules/ticks; `0.3` for minor ticks; dashed borders at `0.55`
- `--indigo-wash`: `#4A6E7E` — secondary state color, always used as a gradient wash fading to transparent (e.g. `0.8 → 0.4 → 0.08` alpha left-to-right)
- `--indigo-panel`: gradient `rgba(74,110,126,0.16) → 0.02`, 135° (wash panels)
- `--vermilion`: `#C33D1F` — STRICTLY RATIONED. Only: gauge needles, compass heading tick, spent-slot strike-through, kill-feed splats, wax seals. Never large fills, never more than a few small marks per screen.
- `--cream-text`: `#F1E9D4` (text on ink)
- `--indigo-text`: `#5B7280` (labels on wash panels)

Typography (both on Google Fonts — download TTFs, create TMP font assets):
- **Yuji Syuku** (regular) — display + all UI text, labels, numerals, hotkeys. Letterspacing ~0.08em on labels, 0.16em on menu buttons.
- **Cardo Italic** — annotations, part numbers, flavor lines, compass letters, kill-feed causes. Has lining numerals: numbers MUST sit on the text line (this is why Fondamento was rejected).
- Rule: **numerals are never oldstyle**. If a face drops digits below the baseline, don't use it.
- Sizes at 700×400 reference scale: labels 15px, annotations 13.5px, part-card title 20px, big count 30px, menu title 54px, menu buttons 20px. Scale proportionally (reference card ≈ 700×400 ≈ a 1440p screen at ~half scale, so roughly ×2 for 1440p).

Shape language:
- **Brushstroke fills**: irregular border-radius like `3px 45% 4px 50% / 55% 6px 60% 8px` + rotate ~-0.4 to -1°. In uGUI: author 3–4 brushstroke 9-sliced sprites (or SDF shapes) — a bar fill, a button blob, a thin underline swipe, a splat dot. Never perfect rectangles for filled state.
- **Ruled frames**: quantities live inside ruler-like frames — 1px bottom rule + evenly spaced vertical ticks (8 divisions). The wash fill sits inside/over the ruler.
- **Dashed containers**: 1px dashed ink for empty/available slots.
- **Registration marks**: small + crosses at screen corners (12px, ink @ 45% alpha) — the "printed off the drafting table" signature.
- Corner radius: none on ruled/dashed elements; brush shapes have organic radii as above.
- Shadows: essentially none. Flat ink on paper. (Tooltips/menus may use a very soft `rgba(46,40,32,0.22)` drop.)

## Screens / Components

### Combat HUD (`unified-hud-v2.html`)
- **Vitals** (anchor top-left, 30px inset): two ruled bars 280px wide, 20px tall, 8 tick divisions. "Hull" = ink wash fill (58%), with a 2px vermilion needle at the current value overhanging the frame 3px top/bottom. "Wind in the Membranes" = indigo wash (82%). Label above each bar, Yuji Syuku 15px.
- **Part panel** (anchor top-right): 224px wash panel (indigo-panel gradient, organic brush radii `4px 24px 5px 28px / 22px 6px 26px 7px`). Contents: `Part No. 131-B — equipped` (Cardo 12.5px, indigo-text), `Flapping Wing` (Yuji Syuku 20px), annotation line, then a **mirror-written** flavor line (horizontal flip, 60% opacity — da Vinci easter egg).
- **Weapon cluster** (left, below vitals): `Mortar Battery` (20px) over a 140×6px ink brush underline, then 8 ammo pips (13px organic blobs; filled = ink, empty = 1.2px ink outline), then Cardo italic reload line, cooldown seconds in indigo.
- **Compass** (right, mid): 190px band, 1px rules top+bottom, cardinal letters in Cardo, fixed 2px vermilion tick at center overhanging 4px. `bearing 292` centered below.
- **Kill feed** (left, lower): rows of `[vermilion splat 9px] Name — cause`. Name Yuji Syuku 14px, cause Cardo italic 13px faded. Older rows fade (splat opacity 0.6).
- **Hotbar** (anchor bottom-center): 58px square slots, 10px gap. Default: dashed ink border, glyph centered, qty bottom-right 12px, hotkey number floating 16px above (Yuji Syuku 11px). **Active**: no border — solid ink brush blob, cream glyph, rotate -1°. **Spent**: glyph at 25% + vermilion brush strike-through, -4°.
- Registration marks at all four screen corners.

### Controls (`unified-controls.html`) — for Settings
- **Toggle**: 52×22px. Rail = 1px rule with end ticks (a tiny ruler). Knob = 18px organic blob; ON = solid ink at right, OFF = outlined transparent at left. Slide 150ms ease.
- **Slider**: ruler frame (ticks + bottom rule) + indigo wash fill + 10×22px ink brush thumb (rotate -3°). Value readout above-right, Cardo italic.
- **Stepper**: −/+ in 26px brush-radius outline squares, value between (Yuji Syuku 19px).
- **Select**: value + `▾` over a 1px bottom rule. Menu: paper `#F4EDDA`, soft shadow, organic radii; selected option gets an indigo wash gradient bar; hover = ink @ 7%.
- **Checkbox**: 17px square 1.5px ink outline; checked = **wax seal** (radial vermilion blob `#E06843 → #C33D1F → #8F2812`) replacing the box.
- **Tooltip**: dark ink card (`#26211A`, organic radii, -0.4° rotate) with cream title + faded Cardo italic body, small diamond caret.
- **Rows**: label left / control right, 1px dashed separator between rows.

### Main Menu (`unified-menu.html`)
- Centered column. `Robogame` Yuji Syuku 54px, 0.1em tracking, Title Case (not all caps), over a full-width 10px ink brush underline; two small vermilion splats off the right end.
- Subtitle `A Bestiary of Contraptions` Cardo italic.
- Buttons: primary `Begin` = 240px ink brush blob, cream text, 0.16em tracking; secondary `Settings` / `Take Leave` = plain text with a 7px indigo wash underline behind (hover: wash darkens; exit hover: wash goes faint vermilion).
- Bottom-left: `Esc — Open Settings` + mirror-written flavor line. Bottom-right: version, Cardo italic.

## Interactions & Behavior
- Hover: ink surfaces lighten to `#322B21`; transparent surfaces gain ink @ 7–8% fill; wash underlines deepen.
- Press: no scale tricks in reference; suggest 1px translate-down or wash darken.
- State changes (health, ammo) should read as ink drying/wetting — animate wash width + the trailing alpha gradient, ~200ms ease-out.
- Damage: hull needle jumps in vermilion, then wash catches up (nice slapstick beat).
- Audio direction (from art doc): UI clicks/shots are instrument notes — piano flourish on mortar volley.

## Copy & Tone
- Labels/menu items: Title Case. No roman numerals. No pirate-speak ("ye" etc.).
- Annotations: lowercase sentence fragments, workshop-journal voice: "rib-and-membrane, ash + linen", "wings tattered, limping home", "listen for the piano".
- Part numbering scheme: `Part No. 131-B` style. Mirror-writing reserved for flavor lines only.

## Unity/uGUI Implementation Notes
- **Fonts**: download Yuji Syuku and Cardo (Google Fonts, OFL) TTFs; generate TextMeshPro SDF assets. Yuji Syuku is a large CJK font — build the TMP atlas from the Latin+numerals subset used (or use a static atlas with a character list) to keep it light.
- **Brush shapes**: author as 9-sliced PNG sprites or use a rounded-rect SDF shader with per-corner radii + slight rotation on the RectTransform. A tiny set covers everything: bar fill (2 tints: ink, indigo, with alpha-gradient tail baked in or via gradient shader), button blob, underline swipe, splat/pip blob, wax seal.
- **Paper ground**: full-screen paper texture (or vertex-colored radial) + a faint 28px grid overlay texture (tiling). Menus/settings only; in-combat HUD sits directly on the 3D view — give HUD text/marks a very subtle paper-colored soft backing where legibility demands, never a solid box.
- **Ruled frames**: cheap procedural option: an Image with a tiling tick sprite + a 1px line Image; avoids per-element textures.
- **Mirror writing**: RectTransform scale.x = -1 on a TMP text.
- **The engineering rules in the game's `subsystems/art-direction.md` (MaterialPropertyBlock-only, perf budgets) remain in force** per the art doc.

## Assets
No binary assets in this bundle — all shapes are CSS-drawn in the references. Fonts come from Google Fonts (OFL licensed). Alchemical part glyphs (🜁🜂🜃) in the HUD are unicode placeholders — replace with real part icons drawn in the ink style.

## Files
All references in `reference/`. Open the HTML files in a browser at 700×400 to see them as designed.
