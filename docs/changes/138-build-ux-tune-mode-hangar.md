# 138 — Build-mode UX: Tune mode, hangar reframe, rotor pick fix

## Intent

User review of the robot-builder flow surfaced four clunk points: the
Alt-chord needed for every panel interaction, the invisibility of the
instance-edit feature (user asked for a "way to edit a deployed part"
that already existed), the mislabeled "Drive Mode" garage state, and a
middle-click dead zone on the rotor's upper mast. Decision: keep the
locked-cursor reticle for placement (Minecraft/Space Engineers feel,
user preference); make the edit mode the cursor-freeing state instead.

## Changes

- **Rotor pick redirect** — `BuildSession.ResolveMechanismOwnerCell`
  (static, dictionary-driven, EditMode-tested in
  `MechanismOwnerCellTests`). The auto-placed mechanism cube is
  invisible but its collider owns the rotor's upper-mast region; the
  eyedropper, tune-bind and right-click-remove verbs in `BlockEditor`
  now route cube hits back to the owning rotor (spin-axis match). A
  visible cube merely adjacent to a rotor is not rerouted.
- **Tune mode = free cursor** — `BuildEditMode.SetEnabled` drives new
  `BuildFreeCam.ExternalCursorHold`: cursor unlocks for the mode's whole
  duration (sliders work without Alt), mouse-look/flight suspend via the
  existing lock-state gates, click-to-relock is disabled while held.
  Placement keeps the locked reticle; Alt hold still works there.
- **Tune-mode legibility** — button renamed "Tuning Mode [T]" (rebound
  E → T: E is the free-cam's fly-up key, so the old binding jolted the
  camera on every toggle); hint row
  under it while active; hover highlight (faint orange shell, one reused
  object) on tunable blocks; placement ghost + error HUD suppressed in
  the mode; panel title "Editing —" → "Tuning —"; UiClick/UiBack cue on
  toggle. Escape exits tune mode before the pause menu opens (new rung
  in `PauseMenuHud`'s ladder, resolved only on Esc-press frames).
- **Variant panel sizes to content** — `SetContentHeight` +
  per-section height table replaces the fixed 340×460 rect (width now
  360). Rope's 400px of dead cream is gone; expanded foil Advanced and
  the open concoction list grow the panel instead of colliding with the
  tip strip, whose 64px band is now always reserved.
- **Hangar reframe** — garage non-build state is the *hangar* (parked
  showcase + loadout strip), not "Drive Mode": button now reads
  "Build ▶" / "◀ Hangar". New `BuildModeFrame` (wired in
  `GarageController.EnsureBuildModeWired`) draws screen-edge accent bars
  + a "BUILD MODE" tag while building — all `raycastTarget = false`.

## Verification

- EditMode: `MechanismOwnerCellTests` (6 tests) on the redirect rules.
- Headless rig: EditMode 385/386, PlayMode 120/121, 0 failures (the
  stragglers are a pre-existing inconclusive + a pre-existing skip).
- Live MCP play-mode smoke (bridge over direct HTTP :8080, session was
  launched without the UnityMCP registration — now added to `.mcp.json`):
  build-mode frame + labels, tuning-mode cursor free/relock, panel
  content-sizing (aero 398 / rope 166 / rotor 310 / advanced 514), all
  observed in-editor; console clean.
- Live screenshots caught three fixups: the BUILD MODE tag overprinted
  the FPS/net dev lines (moved to y −64), the tuning hint was
  cream-on-cream (→ ink on Backdrop), and the variant panel TITLE had
  never rendered — its offsetMin/offsetMax y-values were swapped since
  the panel was built, giving the rect negative height. Swapping them
  restores "Variant — X" and the red "Tuning — X" bound-state signal.
- perf-checker skipped: zero physics objects added (UI + input routing;
  the two highlight shells are collider-less primitives, hover shell
  reused across frames per invariant #6).

## Round 2 — user playtest notes (same session)

- **WASD flickered HUD buttons** — the default InputSystemUIInputModule
  binds WASD to UI Navigate; once anything is clicked, selection walks
  the buttons. New `HudEventSystem.DisableKeyboardNavigation` nulls the
  module's move action; called from all four `EnsureEventSystem` copies.
  (`UguiNav.IsTextInputFocused` lives in Core — text-field-only hotkey
  guards; the old any-selection checks left T/R dead after any click.)
- **Tuning mode kept the look-around** — hold right-mouse over the world
  to capture the cursor and drag-look (WASD flight rides the same lock);
  release restores the free cursor. WASD also flies with the cursor free
  unless a text field is focused.
- **Deselect without leaving the mode** — right-CLICK (≤8px accumulated
  delta, cursor-lock-safe) or left-click on empty space unbinds the
  tuned part; the mode stays on. Re-clicking another part re-targets.
- **Glow** — highlight shells now fit the part's full rendered bounds
  (whole wing span, not one cell) and the hover shell breathes
  (alpha 0.12–0.24). The per-object outline render feature stays off —
  it was reverted once already for tanking FPS at real block counts.
- Verified live via bridge: navigate action confirmed null in play mode,
  bounds-fitted glow screenshot-confirmed, console clean. (Headless
  cursor warp doesn't register in an unfocused editor — real hover needs
  a human hand; logic path is shared with the verified bind path.)

## Deferred / parked (user's back-pocket list)

- Thruster L/R facing constraint (decide the mechanic first).
- Concoction system deep-dive (panel now auto-sizes, clearing room).
- Rudder vs. wing vs. fin: answered — wing/fin share `AeroSurfaceBlock`
  physics (different default stats only); rudder is a separate active
  yaw actuator (`RudderBlock`).
