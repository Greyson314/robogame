# 128 — Mouse feel + UI overlap pass

User-reported friction: constant ESC-dancing to click HUD buttons, the
Netcode Dev panel burying garage buttons, the bottom-right stats panel
burying the arena's Garage button. Root causes were verified with rect
math, not guessed (see docs/subsystems/hud-layout.md, new this session).

## Diagnosis

1. **IMGUI draws over UGUI and the EventSystem can't see IMGUI.** The
   arena "◀ Garage" UGUI button `[W−200, W−20] × [H−60, H−20]` sat fully
   inside VehicleStatsHud's IMGUI panel `[W−238, W−18] × [H−170, H−18]`.
   NetDevHud's v-centered 260×170 panel overlapped the garage's UGUI
   button stack whenever the game view was shorter than ~958 px.
2. **Escape was double-booked**: FollowCamera released the cursor AND
   SettingsHud toggled its panel on the same press.
3. **Click-to-recapture was IMGUI-blind**: clicking the match-end
   "Return to Garage" IMGUI button also re-locked the cursor; the lock
   then survived the scene load into a garage whose fresh FollowCamera
   didn't own it — stranded locked cursor.

## Changes

- **`Core/HudPointerGuard`** (new): frame-double-buffered IMGUI rect
  registry + modal-owner registry, unioned with the UGUI EventSystem
  check. Consumers: FollowCamera (capture / ADS / scroll), OrbitCamera
  (drag gates), BuildFreeCam (recapture / dolly). 7 EditMode tests.
- **Hold-Alt cursor** (FollowCamera + BuildFreeCam): holding LeftAlt
  frees the cursor for HUD clicks without surrendering capture; release
  re-locks unless a modal opened meanwhile. Serialized `_holdCursorKey`.
- **`Gameplay/PauseMenuHud`** (new, self-bootstrapping): sole Escape
  owner. Escape ladder: settings → pause → gameplay. Resume / Settings /
  Return-to-Garage (arena states only). Registers as modal; participates
  in the QoL.PauseOnSettings time-scale gate. UiClick/UiBack cues.
- **SettingsHud**: no longer polls Escape (opened via pause/main menu);
  registers as modal in SetOpen.
- **Fire gate** (PlayerInputHandler): FireHeld/FirePressed now require
  `Cursor.lockState == Locked` — a free cursor is always UI mode.
  Replaces the IMGUI-blind pointer check. R/digit gates untouched.
- **FollowCamera.OnEnable**: releases a stale cursor lock it doesn't own
  (fixes the stranded-lock path after match-end transitions). Escape
  handling removed.
- **MatchEndOverlay**: registers as modal while visible so its button
  click can't re-capture.
- **SceneTransitionHud**: corner button is garage-only ("Launch ▶") —
  the buried arena "◀ Garage" button is deleted; returning lives in the
  pause menu + match-end overlay.
- **NetDevHud**: 260×170 panel → one change-gated status line under the
  FPS counter (y 34–54). Hotkeys unchanged.

## Deliberately not changed

- DevHud keeps its transparent-UGUI raycast blocker (it prevents
  click-through to UGUI beneath, which the guard doesn't cover).
- Keyboard gates (R / digit keys) keep the EventSystem pointer check —
  the modal dims already suppress them; typing-focus detection is a
  separate problem.
- DrillBlock debug readout vs PerformanceHud top-right collision: both
  dev-only, debug flag off by default. Documented in hud-layout.md.

## Verification

- EditMode + PlayMode suites (see result note below).
- qa-verifier console check after implementation.
- perf-checker skipped per workflow rule: zero physics objects, no
  per-frame allocations (guard reuses two pre-sized lists; NetDevHud
  line and pause menu are change-gated / event-driven).

## Known unknowns

- Hold-Alt default (LeftAlt) may collide with future loadout keybinds —
  serialized per-camera, cheap to remap.
- Pause menu deliberately doesn't render over the match-end overlay;
  if a future round-restart flow keeps the arena alive after RoundEnded,
  revisit CanOpen's MatchEndOverlay gate.
