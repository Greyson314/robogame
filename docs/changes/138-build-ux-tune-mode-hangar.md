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
- Full headless suite + Unity console check via qa-verifier.
- perf-checker skipped: zero physics objects added (UI + input routing;
  the two highlight shells are collider-less primitives, hover shell
  reused across frames per invariant #6).

## Deferred / parked (user's back-pocket list)

- Thruster L/R facing constraint (decide the mechanic first).
- Concoction system deep-dive (panel now auto-sizes, clearing room).
- Rudder vs. wing vs. fin: answered — wing/fin share `AeroSurfaceBlock`
  physics (different default stats only); rudder is a separate active
  yaw actuator (`RudderBlock`).
