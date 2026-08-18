# 164 — Home screen redesign: Ink & Motion Kit (proposal + prototype)

Design session, no Unity code. Grey asked for a home-screen redesign with
better animation, tactility, and design, plus "a UI ecosystem I can repeat
throughout the project." The inventor + painter *look* (134) already exists;
what was missing is the *feel* layer — the current menu has one canvas fade
and UGUI color tints, and no tween utility exists anywhere in Scripts.

## What landed

- **[docs/research/prototypes/ink-motion-kit.html](../research/prototypes/ink-motion-kit.html)**
  — interactive prototype, real project fonts embedded (Averia Libre + Space
  Mono), palette verbatim from UguiPalette/HudStyles. Four tabs: redesigned
  home screen on a 1920×1080 stage (coordinates map 1:1 to the CanvasScaler
  reference), motion-token spec with replayable demos, a 15-component gallery
  (each captioned with exact motion + audio + Unity mapping), and the Unity
  implementation plan. WebAudio sketches of the UI sound palette (D-minor:
  nib ticks, woodblock stamps, felt-piano D–F–A flourish, timpani page-turn).
  Also published as a Claude artifact (same file).
- **[docs/research/ui-design-handoff-motion.md](../research/ui-design-handoff-motion.md)**
  — the durable spec: the "ink behaves like ink" verb system (Draw / Wet /
  Stamp / Blot), motion tokens (Tick 80 / Stroke 180 / Settle 260 / Draw 420 /
  Page 640 ms, stagger 70, press 0.96), sound-cue table, home-screen layout,
  and the ecosystem architecture (UiMotion, UiTween driver, InkButton,
  PageWipe, SheetTitleBlock, 4 InkKit sprites, 6 AudioCue additions).
- Pointer added to [docs/subsystems/ui-direction.md](../subsystems/ui-direction.md).

## Design decisions proposed (NOT yet approved)

- Home recomposition: menu column left third; right two-thirds is an ink
  aerial-screw diagram (capybara pilot) that *answers* menu hover with drawn
  leader-line annotations; drafting **title block** bottom-right ("sheet
  no. 01 — home") replaces the bare version line — every screen becomes a
  numbered notebook sheet, scene changes become ink wipes.
- All copy from 134 survives (Begin / Settings / Take Leave, mirror flavor).

## Verification

- Headless Chrome renders of all four tabs inspected; fixed: absolute
  children inside transformed entrance wrappers resolving against the wrong
  containing block, SVG group highlights unable to recolor stroked children,
  transition-shorthand collision putting entrance delays on hover recolors,
  helix reading as a globe (taper + edge seams), title-block/mirror-text
  collision. DOM probes confirmed end-state geometry, focus wiring, and
  control logic (toggle/checkbox/select/stepper).
- No Unity changes → headless test rig not run; qa-verifier/perf-checker not
  applicable this session.

## Follow-ups

- Grey: pick on the four open choices (diagram subject, composition, cue
  voicing, reduced-motion toggle) — then implementation per the handoff.
- At implementation: consider a short ADR making UiTween the sanctioned UI
  animation path (no per-panel coroutine tweens), same spirit as ADR-0002.
- Worktree note: this session ran in the home-screen-ui-redesign worktree but
  all artifacts land in the main checkout per the worktree-edit guard; the
  worktree/branch can be discarded.
