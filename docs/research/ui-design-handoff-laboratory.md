> **Provenance.** Claude Design handoff (July 2026) for the Laboratory
> screen's "evil scientist / night workshop" treatment, copied verbatim
> below from the user's `design_handoff_laboratory` bundle. Implemented in
> session 142 (`LabKit` + `LabController`). Deliberate deviations, per
> locked project conventions: fonts are Averia Libre + Space Mono (the
> user's pick supersedes Yuji Syuku / Xanh Mono — see ui-direction.md);
> the vial liquid uses `Concoction.MixedColor` instead of the prototype's
> standalone hue formula (the game's colour IS the recipe's identity);
> the CPU-surcharge formula line is surfaced (the handoff invites it).
> Reference tier — do not take new direction from this file.

# Handoff: Laboratory (Concoction Crafting Screen)

## Overview
The **Laboratory** is an in-game crafting screen for *Robogame* (a voxel combat sandbox about capybara-piloted contraption-bots). Players tune a "concoction" (a weapon payload) across five stats and save named mixes. The visual direction is an **evil-scientist / night-workshop** take on the game's inventor-painter design system: a dark soot-and-wood ground rather than the parchment used elsewhere, with a single glowing "galvanic" accent color and a live specimen vial that reacts to the mix.

This screen deliberately departs from the parchment-heavy Renaissance treatment used on other screens, while staying inside the locked Robogame system (see Design Tokens → Provenance).

## About the Design Files
The file in this bundle (`Laboratory.dc.html`) is a **design reference created in HTML** — a working prototype showing intended look and behavior. It is **not production code to copy directly.**

It is authored as a "Design Component" (a proprietary HTML component format): a `<x-dc>` template plus a `class Component extends DCLogic` logic block, rendered by an internal runtime (`support.js`, React under the hood). **Do not try to run or port the DC runtime.** Instead, **recreate this design in the target codebase's environment** (the game shell — likely a Unity UI layer, or whatever UI framework the project uses) using its established patterns. If no UI environment exists yet, choose the most appropriate one and implement there. Treat the HTML/CSS values below as the source of truth for appearance; treat the JS logic as a description of behavior.

## Fidelity
**High-fidelity.** Colors, typography, spacing, elevation, and interactions are final and exact. Recreate pixel-faithfully, substituting the codebase's own primitives (sliders, list, buttons) where they exist but preserving the described styling.

## Layout

Full-viewport dark scene, everything centered.

- **Scene** (`min-height: 100vh`): radial-gradient ground, centered flex, `padding: 40px 24px`, `overflow: hidden`.
  - Decorative overlays (pointer-events: none): soot blotches, an optional 28px chalk drafting grid, and four L-shaped registration ticks fixed at the viewport corners (18×18px, 1px `rgba(232,225,210,0.4)`).
- **Panel** (the card): `box-sizing: border-box; width: 1060px; max-width: 100%`, wood gradient, `border: 1px solid #4A3F2E`, `padding: 30px 36px 26px`, drop + inset shadow. Four brass "screw head" dots in the corners (10px radial-gradient circles with a rotated slot).
  - **Header row**: title `The Laboratory` (left) + `Close` button (right, `margin-left:auto`), separated by a 1px bottom rule.
  - **Body**: CSS grid, `grid-template-columns: 240px 1fr 220px; gap: 32px; padding-top: 22px`.
    - **Left column — "Concoctions" list** (recessed well)
    - **Center column — "switchboard"** (raised plate with sliders) + name field + action buttons
    - **Right column — the specimen vial** + `fig. 1` caption

### Elevation language (important)
The screen reads as 2.5D through layered light/shadow — reproduce these relationships:
- **Concoctions list = recessed well**: dark border (`rgba(0,0,0,0.55)`) with a lighter bottom edge, `background: rgba(0,0,0,0.28)`, `box-shadow: inset 0 3px 8px rgba(0,0,0,0.55), inset 0 -1px 0 rgba(232,225,210,0.05)`.
- **Slider plate = raised**: subtle top-lit gradient `linear-gradient(178deg,#2F2820,#28221A)`, top border lighter than sides, `box-shadow: 0 8px 20px rgba(0,0,0,0.45), 0 2px 4px rgba(0,0,0,0.4), inset 0 1px 0 rgba(232,225,210,0.08)`.
- **Slider tracks & name field = inset/sunken**: dark top border + `inset` shadow.
- **Save button = physical**: rests with a drop shadow and depresses on hover (`transform: translateY(2px)`, shadow shrinks).

## Components

### Header
- **Title** `The Laboratory` — Yuji Syuku, 34px, weight 400, color `#E8E1D2`, `letter-spacing: 1px`.
- **Close button** — transparent, `1px solid rgba(232,225,210,0.35)`, text `#E8E1D2`, Yuji Syuku 15px, padding `5px 16px`. Hover: border + text switch to accent color.

### Concoctions list (left)
Vertical stack inside the recessed well. Rows are full-width left-aligned buttons, `padding: 12px 12px`, `1px solid rgba(232,225,210,0.12)` bottom divider, Yuji Syuku.
- **New Concoction row** (first): a `+` glyph (two 2px `#E8E1D2` bars) + label, text `#E8E1D2` 15px. Hover background `rgba(74,110,126,0.18)`.
- **Mix rows**: a small vial-shaped color swatch (15×19px, radius `3px 3px 5px 5px`, 1px light border, tiny brass cork nub on top) + mix name (15px) + right-aligned batch number in **Xanh Mono italic 12px** `rgba(232,225,210,0.4)`.
  - **Selected row**: background `rgba(74,110,126,0.32)` (indigo wash), swatch tinted to the live liquid color with a glow, name text `#E8E1D2`.
  - **Unselected**: transparent bg, swatch `hsl(<hue>, 45%, 42%)`, name `rgba(232,225,210,0.65)`.

### Sliders (center, "switchboard")
Five rows, one per stat. Each row is a grid: `grid-template-columns: 96px 1fr 58px; gap: 16px; align-items:center`.
- **Label** (left): Yuji Syuku 16px `#E8E1D2`. Stats in order: **Damage, Size, Knockback, Speed, Spread**.
- **Track** (center): 34px tall hit-area (`cursor: ew-resize`). Visible track is a 4px brass bar at `top:15px` — gradient `linear-gradient(180deg,#4A3A1C,#6B5226 60%,#9C7A3E)`, dark border, inset shadow. A fill bar of `width: <pct>%` sits on top. Faint **ink tick marks** every 10% (`background-image: linear-gradient(90deg, rgba(232,225,210,0.35) 1px, transparent 1px); background-size:10% 100%`).
- **Pip** (the handle): a 9px circle at `left: <pct>%`, `translateX(-50%)`, `top: 12.5px`. Resting: `#E8E1D2` with a small drop shadow. **While dragging that slider (active):** pip + fill + readout turn accent-colored and gain an accent glow.
- **Readout** (right): Yuji Syuku 18px, `tabular-nums`, right-aligned, `<pct>%`. Resting `rgba(232,225,210,0.88)`, active = accent color.

### Name field (below sliders)
Text input, sunken: `background: rgba(0,0,0,0.35)`, dark borders (darker on top), `inset 0 2px 5px rgba(0,0,0,0.5)`, Yuji Syuku 19px `#E8E1D2`, `width: 300px`, `padding: 7px 12px`, no outline. Holds the current mix name (default `Standard Mixture`).

### Action buttons (bottom of center)
Row, `gap: 14px`.
- **Save** (primary): solid `#E8E1D2` fill, ink text `#26211A`, Yuji Syuku 17px, `padding: 10px 26px`. **Irregular hand-drawn radius**: `border-radius: 46% 54% 51% 49% / 62% 55% 45% 38%` (a "brushstroke blob" — keep it). Rests with drop shadow; hover depresses (`translateY(2px)`) and gains an accent glow, text darkens to `#14100C`.
- **Delete** (tertiary): text-only `rgba(232,225,210,0.6)`, Yuji Syuku 16px. Hover: text brightens to `#E8E1D2` and gets a **vermilion strike-through** (`line-through`, color `#C33D1F`, thickness 2.5px).

### The specimen vial (right)
A small **test-tube style vial**, ~70×230px, built from stacked absolutely-positioned layers, tilted slightly (`transform: rotateX(4deg)` with `perspective:700px` on the parent). Layers, top to bottom:
- **Cork**: 36×20px brass gradient block, radius `3px 3px 2px 2px`.
- **Rolled lip**: 44×7px, light glass border, radius 3px.
- **Tube body**: 36px wide straight tube, rounded bottom (`border-radius: 0 0 18px 18px`), 2px glass border (no top border), glass gradient with a left highlight streak, `overflow:hidden`. Outer glow + inset color tint driven by the liquid color.
  - **Liquid**: fills from the bottom to `<fillLevel>%`, vertical gradient of the live liquid color, gentle opacity flicker (`labFlicker` 3.2s).
  - **Liquid surface**: a bright ellipse riding on top of the fill.
  - **Bubbles**: 3 small bordered circles rising and fading (`labBubble` 2.2–3.6s loops).
- **Ground shadow**: soft radial ellipse under the tube.
- **Wax seal**: the one vermilion element — a ~22px blobby radial-gradient wax dot (`#D9552F → #C33D1F → #8F2B14`), rotated ~-12°, on the tube's right edge.
- **Caption**: `fig. 1` in Xanh Mono italic 13px `rgba(232,225,210,0.5)`.

## Interactions & Behavior
- **Slider drag**: pointerdown on a track begins a drag; the value = clamped `round((clientX - trackLeft) / trackWidth * 100)`, updated on `pointermove`, ended on `pointerup`. The dragged stat is marked **active** (accent highlight on its pip/fill/readout) until release. Values are integers 0–100.
- **Selecting a concoction**: clicking a mix row sets it as selected (indigo-wash highlight; its swatch adopts the live liquid color).
- **New Concoction / Close / Save / Delete**: wired to no-ops in the prototype — implement real handlers (create blank mix, close screen, persist mix, remove mix). Delete should be styled as destructive (vermilion).
- **Name editing**: the text field is two-way bound to the selected mix's name; editing it updates the selected row label live.
- **Live vial reaction** (the signature behavior):
  - **Liquid hue** shifts with the mix: `h = 170 - (Damage-50)*1.6 + (Speed-50)*0.5`, then `hue = ((h % 360)+360)%360`. Liquid colors: solid `hsl(hue,62%,52%)`, glow `hsla(hue,75%,60%,0.55)`, dim tint `hsla(hue,62%,52%,0.22)`. (High damage → warmer/red, high speed → cooler.)
  - **Fill level**: `30 + (Size + Spread)/5` → ranges 30–70%.
  - Color and level transition over `0.3s`.
- **Animations**: `labBubble` (rise + fade, 2.2–3.6s linear infinite), `labFlicker` (0.85↔1 opacity, 3.2s). Note: a `labArc` (lightning) keyframe exists in the file but is **unused/deprecated** — do not implement lightning.

## State Management
- `mixName: string` — current (selected) mix name; default `"Standard Mixture"`.
- `vals: { Damage, Size, Knockback, Speed, Spread }` — integers 0–100, default 50 each.
- `active: string | null` — which stat is mid-drag (drives accent highlight).
- `selected: number` — index of the selected concoction.
- Concoction list data: `{ name, batch, hue }` per entry (prototype seeds three: `Standard Mixture / no. 7`, `Pepper Fog / no. 12`, `Slow Jam / no. 3`). In production this comes from the player's saved mixes.
- Derived (compute from `vals`): liquid colors, fill level, and the stat multipliers shown in `formula` (`dmg ×(Damage/50)`, etc., `+75% of weapon base`) — the formula string is defined in logic but not currently displayed; surface it if design wants it.

## Design Tokens

### Colors
- Ink / darks: `#131009`, `#1B1712`, `#221C15`, `#26211A`, `#262019`, `#28221A`, `#2A241B`, `#2E2820`, `#2F2820`, `#14100C`
- Bone / text: `#E8E1D2` (primary text), plus alphas `rgba(232,225,210, .88 / .75 / .65 / .5 / .4 / .18 / .14 / .12 / .06 / .045)`
- Wood border: `#4A3F2E`
- Brass: `#D8B36A`, `#8A6B33`, `#9C7A3E`, `#6B5226`, `#5C4520`, `#4A3A1C`, `#3A2E14`
- Indigo wash (secondary state): `#4A6E7E` / `rgba(74,110,126, .32 / .18)`
- Vermilion (rationed accent — seal, strike-through): `#C33D1F`, with `#D9552F` / `#8F2B14` for the seal gradient
- **Galvanic accent** (tweakable prop `accent`): default `#7FDCC8`; options `#7FDCC8` (cyan), `#A4E06B` (green), `#B9A6F0` (violet). Glow = accent + `AA` alpha.
- Link colors: `a` `#7FDCC8`, `a:hover` `#A8EBDD`.

### Typography
Two families only:
- **Yuji Syuku** (serif) — all display + UI text. Sizes used: 34 (title), 19 (name input), 18 (readout), 17 (Save), 16 (Delete/labels/heading), 15 (list rows / Close).
- **Xanh Mono**, italic — annotations only (batch numbers, `fig. 1`), 12–13px.
Load via Google Fonts: `Yuji+Syuku` and `Xanh+Mono:ital@0;1`.

### Spacing / structure
- Body grid columns `240px 1fr 220px`, gap `32px`.
- Panel width `1060px`, padding `30px 36px 26px`.
- Drafting grid cell `28px`. Corner ticks `18px`.

### Radii / shape signatures
- Brushstroke button radius (Save): `46% 54% 51% 49% / 62% 55% 45% 38%`.
- Wax seal radius: `48% 52% 55% 45%`, rotate `-12deg`.
- Vial tube bottom radius `18px`.

### Shadows (elevation set)
- Panel: `0 24px 60px rgba(0,0,0,0.55), inset 0 1px 0 rgba(232,225,210,0.06)`
- Raised plate: `0 8px 20px rgba(0,0,0,0.45), 0 2px 4px rgba(0,0,0,0.4), inset 0 1px 0 rgba(232,225,210,0.08)`
- Recessed well: `inset 0 3px 8px rgba(0,0,0,0.55), inset 0 -1px 0 rgba(232,225,210,0.05)`
- Sunken input/track: `inset 0 2px 5px rgba(0,0,0,0.5)` / `inset 0 1px 2px rgba(0,0,0,0.6)`
- Save rest → hover: `0 5px 14px …` → `0 2px 6px …, 0 0 14px <accentGlow>`

### Provenance (Robogame system rules honored)
Two fonts only (Yuji Syuku + Xanh Mono italic); vermilion strictly rationed (seal + strike-through only); indigo wash for secondary/selected state; registration ticks at corners; a faint 28px drafting grid; alchemical/annotation copy in lowercase Xanh Mono; primary action as an irregular brushstroke shape. The **dark ground + galvanic accent + specimen vial** are this screen's evil-scientist extension of that system.

## Assets
No external image/icon assets — every visual (vial, brass hardware, screws, pips, seal, grid, ticks) is pure CSS/DOM. Fonts load from Google Fonts. No logo (Robogame brand is type-only).

## Files
- `Laboratory.dc.html` — the design reference (template + logic). Read the inline styles for exact per-element values; read the `class Component` block for the drag math, liquid-color formula, and fill-level formula.
