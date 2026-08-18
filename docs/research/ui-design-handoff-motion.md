# Handoff: Robogame UI — Ink & Motion Kit (home screen + motion system)

## Overview
Extends [ui-design-handoff.md](ui-design-handoff.md) (the "inventor + painter"
direction, shipped pass 1 in [changes/134](../changes/134-inventor-ui-pass-1.md))
with the layer that handoff left thin: **motion, tactility, and UI sound**, plus
a home-screen recomposition. Colors, fonts, and shape language are unchanged —
this is the *feel* layer and the reusable kit behind it.

**Canonical reference: [prototypes/ink-motion-kit.html](prototypes/ink-motion-kit.html)**
(interactive; open in a browser — also published as a Claude artifact). The
prototype's stage is 1920×1080, so element positions transfer 1:1 to the Unity
CanvasScaler reference resolution. Tabs: Home Screen (the redesign), Motion
Language (tokens + sound), Components (per-control contract), Unity Plan.

Status: **proposed — awaiting Grey's direction sign-off.** Nothing here is
implemented in Unity yet.

## The metaphor that generates everything

**Ink behaves like ink.** Four verbs cover every animation in the game's UI:

| Verb | Meaning | Spec |
|------|---------|------|
| **Draw** | entrances | strokes/washes scale in from their origin, `t-draw`, ease-draw, staggered once per screen |
| **Wet** | value changes | wash width follows value, gradient tail = wet edge, `t-settle` |
| **Stamp** | confirmations | scale 1.5 → 1 in 150 ms, opacity in 80 ms, lands dead (no bounce) |
| **Blot** | exits | fade + 4–6 px settle downward, ~240 ms — always softer than the entrance |

Never bouncy. Paper is calm; the slapstick lives in the arena. A second
unifying conceit: **every screen is a numbered sheet from the same notebook**
(drafting title block bottom-right: `sheet no. 01 — home`, `02 — garage`, …),
and moving between screens is an **ink wipe** (a brush edge crosses the frame).

## Motion tokens (UiMotion)

| Token | Value | Easing | Used for |
|-------|-------|--------|----------|
| `Tick` | 80 ms | linear/settle | hover washes, color fades |
| `Stroke` | 180 ms | ease-settle | press/release, toggles, hover wash draw |
| `Settle` | 260 ms | ease-settle | panels, modals, value washes |
| `Draw` | 420 ms | ease-draw | entrance stroke draw-ins |
| `Page` | 640 ms | ease-page | full-screen ink wipe |
| `Stagger` | 70 ms | — | between sibling entrances |
| `PressScale` | 0.96 + 1 px down | ease-settle | every pressable face |

Easings: ease-settle `cubic-bezier(0.2,0,0,1)`, ease-draw
`cubic-bezier(0.215,0.61,0.355,1)`, ease-page `cubic-bezier(0.4,0,0.2,1)`.

Rules: tweens are **retargetable, never restarted** (hover-off mid-anim re-aims
from current value); exits softer than enters; idle-motion budget one element
per screen; motion is never the only feedback channel; a reduced-motion setting
collapses entrances to one 150 ms fade and freezes idle loops.

## Sound — cues in the game's D-minor world

Hover = nib tick (±3% pitch jitter, rate-natural); commit = woodblock stamp;
primary = felt-piano D3–F3–A3 flourish (45 ms apart); back = lone A2; toggle =
up/down knock; slider = ratchet tick per ruler division (pitch tracks value,
rate-capped); seal/splat = stamp + soft D3; page wipe = brush swish + timpani
D2 on the land. Existing cues `UiHover`/`UiClick`/`UiBack` stay; add
`UiConfirm, UiToggleOn, UiToggleOff, UiSlideTick, UiSealStamp, UiPageTurn`.
Prototype voices are WebAudio sketches; final voices through AudioRouter
(woodblock + felt piano via the MPTK soundfont, or one-shots in the WAV bank).

## Home screen (sheet no. 01) — layout at 1920×1080

- Menu column left third: title 96 px at (140, 208); brush underline (588 w)
  draws left→right, two vermilion splats stamp off its right end; tagline
  Space Mono italic at y 372; Begin blob 400×86 at (140, 512); Settings /
  Take Leave wash buttons below (gap 30). Hover reveals a Space Mono
  annotation beside each button ("— to the workshop", "— calibrate the
  instruments", "— close the notebook").
- Right two-thirds: **ink diagram of the aerial screw**, capybara on the deck
  (fig. 1 annotations, dimension lines, mirror-written note). Draws in on
  entrance; after that only the spin-arc dashes march (idle budget). Hovering
  a menu button makes the diagram answer: leader line draws + related group
  tints indigo ("the pilot is ready" / "tension the works" / "tie her down
  for the night").
- Bottom-left: Esc hint + mirror flavor line (kept). Bottom-right: **drafting
  title block** replaces the bare version string (project / sheet / version
  rows).
- Entrance: paper 0 → grid 80 → reg marks 120+ → title 160 → underline 300 →
  tagline 480 → buttons 560/640/720 → splats 660/730 → diagram 430–1250 →
  footer 1040+. Any input skips to end. Begin = flourish + ink wipe to Garage.

## Unity architecture (the ecosystem)

1. `Core/UiMotion.cs` — tokens + easing evaluators (constants above).
2. `Core/UiTween.cs` — ONE driver MonoBehaviour (Bootstrap-spawned, statics
   reset via SubsystemRegistration), fixed-capacity struct pool, unscaled dt.
   Channels: CanvasGroup.alpha, RectTransform anchoredPosition/localScale/
   localEulerZ, Image.fillAmount, Graphic.color. Zero steady-state alloc (INV-6).
3. `UI/InkButton.cs` — press/hover/cue behavior; closes ui-direction.md open
   question #1 (button factory) with motion included.
4. `UI/PageWipe.cs` — `PageWipe.To("Garage")` ink-wipe scene transition.
5. `UI/SheetTitleBlock.cs` — parameterized title block per screen.
6. InkKit additions: `StrokeMask`, `WipeBrush`, `LeaderArrow`, `SpinArcTile`.
7. AudioCue additions listed above (wired at birth, INV-8).

Rollout: Home → Settings → garage panels (BuildHotbar, VariantConfig, Lab) →
combat HUD pass 2 (the handoff's planned pass; vitals needle-jump + wash
catch-up = Wet, kill-feed splats = Stamp).

## Open choices (for Grey)

1. Diagram subject: aerial screw vs. the player's current bot as ink lines.
2. Composition: left column + diagram (proposed) vs. centered column kept.
3. Cue voicing: MPTK soundfont vs. authored one-shot WAVs.
4. Ship the reduced-motion setting with this pass?
