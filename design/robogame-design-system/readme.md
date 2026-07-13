# Robogame Design System

**Status: direction locked — tokens + first component cards live (Laboratory group).**

## Context

Robogame is a voxel combat sandbox (Unity) about building slapstick contraption-bots piloted by capybaras. Current in-game UI (`uploads/Untitlzed.png`) is placeholder dark-Unity chrome — that is the *starting point*, not the goal.

## Tokens & components (July 2026)

- `styles.css` — canonical token sheet: the paper system (menus/HUD) + the Laboratory night-workshop scope (Lab screen only), elevation set, shape signatures.
- `foundations/` — palette, typography, and elevation cards.
- `components/lab/` — the Laboratory: full interactive screen (drag the sliders — the specimen vial reacts), switchboard slider, concoctions list, vial, actions & name field.

**Fonts locked: Averia Libre (display + UI) + Space Mono italic (annotations).** User pick, supersedes the earlier Yuji Syuku / Xanh Mono direction — see the game repo's `docs/subsystems/ui-direction.md`.

The Laboratory shipped in-game (session 142, `LabKit` + `LabController`); these cards mirror the shipped values, including the de-blueprinted Lab ground (fog banks; no chalk grid or corner ticks on this screen) and the `Concoction.MixedColor` liquid binding (the cards demo the prototype hue formula so they react live).

## Art direction sources

- `uploads/inventor-aesthetic.md` — the steer. Daedalus/da Vinci inventor core: wood + linen, rib-and-membrane, ink construction marks (registration ticks, dashed fold lines, part numbers, mirror-writing easter eggs), vermilion accent restraint, "solid where you shoot it, skeletal where it moves." Musical vibe (Don't Starve-style instrument voices).
- User note (July 2026): add a **painter** layer to the inventor aesthetic — inventor + painter blend. Be esoteric. No default fonts.

## Current explorations (see Design System tab → Experiments)

1. **A — The Drafting Table** (inventor-heavy): sepia ink on linen paper, construction marks as UI chrome, roman-numeral counters, mirror-writing footnotes. Type: IM Fell English + Xanh Mono.
2. **B — Ink & Wash** (painter-heavy): sumi-brush strokes as containers, diluted washes for state, one vermilion splash. Type: Yuji Boku + Klee One.
3. **C — The Painted Workshop** (the blend): hand-painted signage on wood, linen patches, brass hardware, paint daubs. Type: Yeseva One + Special Elite.

Plus a font-options card (6 display candidates) and a palette comparison card.

### Round 2 (C ruled out; mixes of A + B)

- **Mix 1 — Inked Drafting Table**: A's linen ground + construction marks, B's brushstroke fills/buttons. Amarante display.
- **Mix 2 — Painter's Blueprint**: B's paper + brush header + washes, A's dashed cards, tick marks, mirror-writing. Grenze display, IM Fell English SC buttons.
- **Painterly font candidates**: Splash, Kolker Brush, Ma Shan Zheng, Fondamento, Caveat Brush, Yuji Syuku.

Fonts in contention: Amarante, Grenze, IM Fell English SC (buttons).

### Round 3 (more mixes)

- **Mix 3 — Folio & Wash**: quietest blend; washes inside ruled frames, indigo wash panels instead of bordered cards. Yuji Syuku display.
- **Mix 4 — Wet Sketchbook**: painter-forward; soft palette cards, paint drip on hull bar, vermilion Deploy button. Amarante + Fondamento body.

### Round 4 — UNIFIED DIRECTION (locked: Mix 1 + Mix 3)

**Fonts: Yuji Syuku (display + UI) + Xanh Mono italic (annotations, part numbers, figures).** Two fonts only. *(Superseded in implementation by the locked user pick above.)*

Rules so far: linen ground with faint 28px drafting grid · ink #26211A / #2E2820 · indigo wash #4A6E7E for secondary state · vermilion #C33D1F strictly rationed (needle, wax seal, strike-through, splash) · washes sit inside ruled/ticked frames · primary actions are dark brushstroke shapes · secondary actions get an indigo wash underline · roman numerals for counts · alchemical glyphs as part icons · mirror-writing footnotes · registration ticks at screen corners.

Cards (group "Unified"): Main Menu, Controls & Widgets, and three HUD variants — A "Drafting Frame" (Cutive Mono secondary), B "Painter's Margin" (Klee One, bottom-anchored brush baseline), C "Instrument Cluster" (Fondamento, circular hull gauge + ammo pips).

Copy rules updated: no roman numerals, no pirate-speak ("ye" etc), Title Case for labels/menu items; annotations stay lowercase sentence fragments.

## Not done yet

- Paper-system component cards (HUD vitals/hotbar/compass, settings widgets, main menu) as production cards — currently only in `design_handoff_robogame_ui/reference/`.
- SKILL.md, logo (none provided — brand name set in type only).
