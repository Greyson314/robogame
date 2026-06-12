# 125 — Rotor throttle, uniform blade tilt, instance-edit, garage-returns-to-build

Playtest follow-ups to 123/124.

## Garage opens in build mode when returning from a match

`GameStateController.ReturningFromArena` is snapshotted inside `EnterGarage`
(before the scene load flips `State`) — true when the prior state was any
arena. `GarageController` reads it once on load: a fresh boot / menu entry
still opens in drive mode (session 121), a return-from-match opens straight
into build mode. Corrects the earlier "garage always enters drive mode."

## Rotor RPM is now a throttle (the slider is the ceiling)

The per-rotor RPM slider became **Max RPM**; live spin = throttle × max.
`RotorBlock` holds a per-instance `_throttle01` (default 1, so untouched
rotors and AI dummies still spin at full max exactly as before).

- A **vertical-axis** rotor (heli main rotor, `|spinAxisLocal.y|>0.7`)
  trims on the vertical input — space climbs, descend-key drops.
- A **forward-axis** rotor (pusher/puller prop, `|spinAxisLocal.z|>0.7`)
  trims on `Move.y` (W/S).
- The input only *moves* the throttle while held; it **holds** on release
  (a trim throttle, not a momentary trigger) so a heli hovers hands-off.
  Since lift ∝ RPM², this is real collective control.
- `RpmOverride` (stress tower) still pins absolute RPM and bypasses throttle.

Input source resolved via `GetComponentInParent<IInputSource>()`, cached
alongside the chassis Rigidbody and invalidated on the same parent-change.

## Rotor blade tilt is uniform relative to the disc

`RotorBlock.AdoptAdjacentAerofoils` now uniformizes adopted-blade **teeter**
(`UniformizeBladeTeeter`): it takes the largest-magnitude teeter the player
dialed and applies it to every blade, so they cone one direction. Each
blade's stored teeter was normalized to its *original* mount face (the 4
lateral faces normalize to different signs), so left alone they coned
inconsistently — the symptom the player hit. After adoption every blade's
local frame is rebuilt from the spin tangent, so one value = one visual
direction. This is the teeter analogue of the existing collective-pitch
override.

Deliberately NOT applied to free wings: a mirrored plane-wing pair forms a
dihedral V (mirror parity, session 123), which is correct — "all one
direction" is right for a rotor and wrong for wings, so the two cases keep
separate rules. Garage caveat: blades aren't adopted while the chassis is
kinematic, so the garage preview still shows per-face teeter; the uniform
cone appears at arena spawn.

## Instance-edit — retune one placed block without orphaning

Closes the "to change a rotor's RPM I have to delete its foils, delete the
rotor, re-place, re-config" pain. The middle-click picker now binds the
clicked block for **per-instance editing** (`BuildSession.EditingInstance`):

- While bound, a variant-slider change applies to **that block only** (live),
  not to every block of its type. `BlockEditor.PropagateVariantToLiveBlocks`
  filters to `EditingInstance` when set; normal placement keeps the
  session-96 propagate-to-all behavior.
- A translucent orange shell highlights the bound block; the panel title
  flips to "EDITING — …" in white.
- Exits on: middle-clicking empty space, middle-clicking the bound block
  again (toggle), placing a fresh block, switching hotbar block type, or
  leaving build mode. Esc is deliberately not an exit — it already owns the
  settings panel + free-cam cursor release. The picker still loads the
  instance's values into the next-placement cache (eyedropper behavior), so
  a later placement inherits them.

No deletion, no orphaning — the block never leaves the grid; its fields are
mutated in place via the existing live `SetPitch/SetTeeter/SetDims/ConfigValue`
paths.

## Parked for later (user decision)

- **Top-mounted mortar on a heli shows no aim/fire.** Wiring is correct
  (identical dispatch to the working cannon); medium-confidence diagnosis is
  physical occlusion — the mortar lobs into the rotor disc directly above and
  the shell detonates overhead. User chose to leave it; revisit with an
  in-play repro before deciding fix-vs-restrict. A blanket "no weapons under
  a rotor" placement ban is the blunt option and risks banning valid spots.
- Rotor blade teeter desync if a single adopted blade is instance-edited
  mid-arena (uniformization runs at adoption). Instance-edit is a garage
  feature and uniformization re-reads at spawn, so the common path is fine.
