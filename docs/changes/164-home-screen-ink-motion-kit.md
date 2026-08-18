# 164 — Home screen redesign: Ink & Motion Kit (proposal + prototype + implementation)

Grey asked for a home-screen redesign with better animation, tactility, and
design, plus "a UI ecosystem I can repeat throughout the project." The
inventor + painter *look* (134) already exists; what was missing was the
*feel* layer — the old menu had one canvas fade and UGUI color tints, and no
tween utility existed anywhere in Scripts. Two phases in one session: the
prototype/spec below (committed first), then — after Grey picked drafting-
board layout / player-bot diagram / MPTK voicing / home+kit scope — the
Unity implementation (second commit).

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

## Implementation (same session, after sign-off)

Grey's picks: drafting-board layout · **player's current bot** as the
diagram from day one · MPTK-soundfont voicing · home + kit scope.

- **New Core**: `UiMotion` (tokens + LUT-baked bézier easings, reduced-motion
  gate), `UiTween` (ONE driver, 160-slot struct pool, retargetable handles,
  unscaled time, zero steady-state alloc — INV-6), `UiCues` (D-minor
  composites: Begin flourish = 3× pitched `StingerPianoNote` via
  `PlayScheduled`, page-land = octave-down `StingerTimpaniNote`),
  `InkButton` (press 0.96 + 1px dip, hover wash fill/tint, annotation
  reveal, cue routing — closes ui-direction.md open question #1),
  `SheetTitleBlock`, `PageWipe` (ink cover + baked `WipeBrush` edges,
  DDOL, raycast-blocking, reduced-motion fade path).
- **`BotInkDiagram`** (Gameplay): `CurrentBlueprint` → iso union-outline
  (per-cell +Y/+X/+Z faces; edges counted, drawn when seen once) with
  depth-faded alpha (0.62 near → 0.28 far) in place of hidden-line removal;
  CPU beacon motif; fig./dimension/mirror annotations; hover-focus leaders
  ("the pilot is ready" / "tension the works" / "tie her down for the
  night"); dashed construction ring on a nested canvas, rotating 4°/s — the
  screen's whole idle budget. Empty-shelf fallback when no bootstrap.
- **MainMenuController** rebuilt: sheet-01 layout, staged entrance
  (paper → grid → regs → title → underline draw → splat stamps → buttons →
  diagram → footer), any-input skip via `UiTween.CompleteAll()`, hover →
  `SetFocus`, Begin → `PageWipe.To("Garage")`. Serialized fields kept, so
  MainMenu.unity needed no scene edit.
- **Audio**: 5 cues appended (`UiToggleOn/Off`, `UiSlideTick`,
  `UiSealStamp`, `UiPageTurn`) + wizard rows (pack voices; library rebuilt:
  **71 wired, 0 missing**). `UiConfirm` is deliberately enum-less — it's
  the `UiCues.Confirm()` composite.
- **Settings**: `QoL.ReduceUiMotion` Tweakable (INV-1-safe, presentation
  only) — collapses entrances to one fade, freezes the ring.
- `UguiPalette.InkPressed` token added; InkKit gained `WipeBrush` +
  `ArrowTip` bakers.

## Implementation verification

- Compile: 0 errors / 0 warnings across all assemblies (forced refresh).
- Live play (MCP): entrance completes and drains (ActiveCount 0); default
  Tank blueprint renders as fig. 1 (33 blocks, union outline + depth fade);
  `SetFocus(Pilot)` draws the leader + note and tints CPU lines indigo;
  `PageWipe.To("Garage")` covers, stamps "sheet no. 02 — The Garage",
  loads, sweeps off, self-destroys. Screenshots checked at each state.
- Remote-editor gotcha (ops note): an unfocused editor idles the player
  loop, so tweens crawl between MCP calls — `EditorApplication.Step()`
  bursts are the way to drive/verify time-based UI remotely. Not a code
  bug; verified ActiveCount drains to 0 once frames flow.
- Pre-existing, unrelated: `BusNotFoundException: [FMOD] Bus not found
  'bus:/'` fires at play start (third-party FMOD init; no project script
  references 'bus:/'). Also observed: UI colors render brighter than
  authored (linear color space vs sRGB-authored tokens — cover ink #26211A
  screenshots as ~#6B6156, exactly linear→sRGB math). This affects the
  entire 134 UI equally and is the de-facto approved look; flagging for a
  future art pass, untouched here.
- perf-checker skipped (zero physics objects; steady-state cost = one ring
  transform write + the tween driver's slot scan). qa-verifier not
  dispatched — build/console/visual/flow were all verified first-hand
  above.
- Tests: test-drafter added `Tests/EditMode/UI/UiMotionTests.cs` (5 —
  easing endpoints exact, clamping, monotonic no-bounce sweep) and
  `Tests/PlayMode/UI/UiTweenTests.cs` (13 — retarget-from-current,
  CompleteAll through delay holds, stale-handle safety, delay hold,
  destroyed-target release, RotZ short path, exact end values on all six
  channels, 160-slot eviction). Two CS0104 ambiguous-`Object` fixes applied
  to the draft. Final suites: EditMode **500/500**, PlayMode **135/136,
  0 failed** (the one skip is the pre-existing annotated SpawnBot ignore).
  Known drafter-declared gaps: `UiMotion.Reduced` pass-through untested;
  cross-channel same-frame retarget untested; INV-6 zero-alloc left to
  perf tooling.

## Post-review tweaks (Grey, Aug 18)

- PageWipe reworked to a **serpentine paint-over**: 5 horizontal strokes
  alternating left→right / right→left down the page, 85 ms stagger
  (cover now ~0.66 s vs 0.40 s — Grey wanted to try it knowing it might
  read slow).
- Copy de-piratified: "Take Leave" → "Quit", "tie her down for the night" →
  "shutter the workshop for the night", "abeam" → "wide" (×2);
  `BotInkDiagram.Focus.Moor` → `Focus.Rest`. Rule recorded in the
  handoff-motion doc: plain words for actions, workshop-journal voice for
  annotations, no nautical/pirate register.
- **PauseMenuHud gained "Main Menu"** — the missing way out of the garage.
  Row 3 (slides up to row 2 when Return-to-Garage is hidden; panel grows
  to fit in arenas), hidden when already on the main menu, exits via
  `PageWipe.To("MainMenu", "no. 01", "Home")` after the same clean-close
  discipline as Return to Garage.
- **Begin hover pose**: `InkButton.WithHoverPose` — the blob scales to
  1.02 and straightens from −0.7° to −0.25° on hover, press still stamps
  to 0.96 (bigger down-beat from the raised pose), release lands on the
  hover pose while the pointer stays.
- **Diagram went wordless**: fig. caption, block-count line, dimension
  line + "≈ N cells wide", and the mirror note all removed from
  `BotInkDiagram` — only the hover-focus answers remain as text.

## Follow-ups

- Settings / garage panels / HUD pass 2 adopt the kit (rollout order in the
  handoff-motion doc).
- Ear pass on the five placeholder pack voices (percussionist's veto
  stands); MPTK-rendered woodblock one-shots if the mechanism clacks read
  too metallic.
- Consider a short ADR making UiTween the sanctioned UI animation path (no
  per-panel coroutine tweens), same spirit as ADR-0002.
- Linear-space UI washout: decide once whether to keep values-as-rendered
  or compensate tokens project-wide.
- Diagram polish later: Dims-aware scalable parts (foils render as unit
  cells today), optional hidden-line removal.
- Worktree note: this session ran in the home-screen-ui-redesign worktree
  but all artifacts land in the main checkout per the worktree-edit guard;
  the worktree/branch can be discarded.
