# 169 — Unified physics & tuning pass (ADR-0010)

User directive: deep-dive the bot physics + usability mechanisms, unify
best practices, implement — players must be able to move / resize /
re-tune parts, physics should stay honest, dead sliders go, confusing
labels get tooltips. Research fanned out to three sub-agents (movement
map, tuning/UX inventory, plan); landed as
[ADR-0010](../decisions/0010-unified-physics-and-tuning-surface.md).

## What shipped

- **Move tool (V).** New `BuildMoveMode` (button + hotkey, exclusive
  with Tune mode) + `BuildSession.TryMove`: pick a placed block up,
  ghost previews it with its exact settings, drop re-places it with
  dims / pitch / teeter / yaw / config / concoction ALL preserved.
  Atomic with rollback — a rejected drop restores the source; carrying
  itself mutates nothing. CPU refuses to move; mirror is suspended
  during a move (v1). Escape ladder: cancel carry → exit mode.
- **Ground/hover thrust is per-block (invariant #11 on the ground).**
  `GroundDriveSubsystem` splits surge across GROUNDED wheels at wheel
  positions; `HoverDriveSubsystem` across in-contact pads at their lift
  points (`HoverBladeBlock.WorldLiftPosition` now public). Symmetric
  presets sum to the old CoM push; losing wheels/pads one side pulls
  the bot under throttle. Steering / grip / self-right / jump / caps
  stay grandfathered chassis-level (ADR-0010 rule 1) pending their own
  probe-gated pass.
- **Rotor + pogo consume the drive.** Rotor throttle reads
  `DriveIntent` (Heave = lift rotor, Surge = prop) and pogo air-tilt
  reads the raw `Move` off `LastControl` (Ground scheme zeroes
  pitch/roll intent by design), both with raw-`IInputSource` fallback
  for drive-less rigs. Both go inert while hooked (was: kept responding).
- **Per-foil Control Throw slider** (Advanced, foil family):
  `Entry.BlockConfig` via `FoilDefaults.ResolveControlThrow`
  (1–16°, 0 = shared 8° default). The roll-feel knob the dead
  "Roll Power" slider pretended to be.
- **Dead/dev sliders.** 6 `Dev.Plane.*` Tweakables + orphan
  `DevTuningOverride.ApplyPlane` deleted. `Tweakables.IsPlayerFacing`
  (player groups = Audio, QoL) + `DevSurfacesVisible`: Water / Rope /
  Stress sliders, the Settings "Actions" + "Perf Bisect" sections, and
  the F1 DevHud no longer render in shipping builds (invariant #1 leak).
- **Labels + tooltips.** `TuneField.TipFor`/`SuffixFor` (per-block-id),
  `TunePreset.Tip` + hoverable preset buttons; module "Power" now shows
  its real unit per kind (m / s / HP / N·s / m/s) with per-kind tips
  (`ModuleTuning.PowerUnit/PowerTip`); Collective shows ° and resolves
  its 0-sentinel to the authored 8° (`RotorDefaults.DefaultCollectiveDeg`);
  Teeter marked visual; hover "Size" → "Footprint (cells)"; concoction
  readout got a legend tip (dmg/kb/spr expanded); build-mode keys
  (R rotate had NO hint anywhere) documented in Settings → Keybinds.
- **Bug fixes found by the audit.** Concoction picks on a Tune-bound
  weapon silently did nothing (live-propagation never wrote
  `ConcoctionId`); eyedropper dropped the concoction; `Wing` was
  missing from the CoM/CoL overlay (bat-wing planes showed no CoL) and
  the overlay's thruster weight had drifted to 310 N vs the real 900
  (now single-sourced); CoL markers now sit at the 168 geometric lift
  centres.

## Measured (pv13–pv15 probes, full W from ground spawn unless noted)

| probe | before (168 code) | first slice (grounded-subset split) | shipped (set split @ CoM height) |
|---|---|---|---|
| Tank symmetric | yaw 0.00, drift 0.0 | **snaked: yaw ±3 rad/s, drift ±20 m** | yaw 0.00, drift −0.1 (matches baseline row-for-row) |
| DrillBot symmetric | yaw 0.00, drift 0.1 | **veered 16 m, stalled to v=0** | yaw 0.00, drift 0.4 |
| HoverTank symmetric | yaw 0.00, drift 1.7 | drift 2.5 | yaw 0.00, drift 1.6 |
| Tank, 3 right wheels cut @ t=2.5 (pv14) | drives straight (damage invisible) | — | pirouettes on the live side (yaw → ~20 rad/s held-W, v 14 → 3.4) |
| Helicopter, Space (pv15) | — | — | climbs 4.2 → 6.7 m, settles to hover as climb inflow bleeds blade AoA (intent Heave path live) |
| Prop Plane, W (pv15) | — | — | v 14 → 48 m/s over 6 s (intent Surge path live, matches 168 envelope) |

The first-slice failure is the load-bearing lesson: distributing thrust
over the per-frame GROUNDED subset made the force centroid flicker on
rough terrain, and wheel-height force points added a wheelie moment the
presets were never tuned against. Shipped rule: split over the part
SET (destroyed parts leave it — that is the invariant-#11 signal) at
CoM height, gated on any-contact. The pv14 spin is intense at held
full throttle; the future per-wheel grip pass will moderate it with
skid friction, and players release W.

## Verification

Headless rig green twice on the new code (pre-drafter: EditMode
526/527, PlayMode 141/142; final: EditMode 526/527, PlayMode 146/147 —
+5 drafted tests, 0 failed both runs; the raw-input fallbacks keep
every existing rotor/pogo test exact).
New tests: `GroundDriveWheelAuthorityTests` +
`HoverPadAuthorityTests` (symmetric = no bias, destroyed side = bias;
test-drafter, session 169), FoilControlTests
`ControlThrow_ConfigScalesDeflectionAuthority`. Console clean after
every compile. The qa-verifier / perf-checker agents were NOT
dispatched — their `mcp__UnityMCP__*` tools never registered this
session (raw-HTTP bridge was the workaround throughout); their checks
ran by hand instead: rig + console + live probes above. Perf: no new
physics objects; the per-wheel/per-pad loops iterate the existing
registries (no allocs, same O(parts) the blades/foils already pay) —
a profiler capture is still owed under a populated arena and is
flagged, not skipped silently. Known gap flagged, not hidden:
WheelBlock / HoverBladeBlock forces never participated in CSP replay
(no `SetForceTarget`) — per-wheel thrust inherits that pre-existing
netcode gap; recorded in README known-unknowns for the MP milestone.

## Preset retune (same session, follow-up directive)

User asked the default blueprints to demo the new mechanics. Shipped:
`ScriptedChassisBuilder` gained a config-aware `Place` overload (seeds
the per-id variant cache for one call, mirrored twin included, resets
after), and the aircraft presets now bake differentiated per-foil
Control Throw: **Plane** wings 10° / tails 6° / canards default 8° —
open Tune mode on the preset and the slider reads differently per
foil; **Bomber** 5° on every horizontal foil (the stately contrast
case). Grappler + Prop Plane stay at the 8° reference. Tank (6 spread
wheels, pv14-proven pirouette) and Hover Tank (4 corner pads) already
demo per-part thrust loss; Helicopter/Prop already demo the intent
verbs — no layout changes needed. Asset diff = exactly 10 foil
entries gaining `BlockConfig` (4 Plane, 6 Bomber). The rebake ran in
the test-rig batch Unity: the interactive editor wedged mid-compile
(importer-worker crash earlier in the session; `isCompiling` stuck
true 8+ min) — flagged to the user; a live feel probe of the retuned
Plane/Bomber is owed once the editor recovers (throw scaling is
linear per the `ControlThrow_ConfigScalesDeflectionAuthority` test,
so expected deltas: Plane roll ×1.25, Bomber authority ×0.63).

## Files

New: `Gameplay/BuildMoveMode.cs`,
`docs/decisions/0010-unified-physics-and-tuning-surface.md`.
Preset retune: `Tools/Editor/ScriptedChassisBuilder.cs` (config
overload), `Tools/Editor/GameplayScaffolder.cs` (Plane/Bomber throws),
`Blueprint_DefaultPlane.asset` + `Blueprint_DefaultBomber.asset`
(rebaked).
Edited: `BuildSession` (TryMove), `BlockEditor` (move flow, concoction
propagation + eyedrop), `GarageController`, `PauseMenuHud` (ladder),
`BuildEditMode` (exclusivity, hint slot), `GroundDriveSubsystem`,
`HoverDriveSubsystem`, `HoverBladeBlock`, `RotorBlock`, `PogoBlock`,
`AeroSurfaceBlock` + `FoilDefaults` (per-foil throw),
`TuneSchema`(+Registry), `VariantConfigPanel`, `ModuleTuning`,
`RotorDefaults`, `ThrusterBlock` (const public), `CenterOverlay`,
`Tweakables`, `DevTuningOverride`, `DevHud`, `SettingsHud`,
`physics.md` §2.2–2.3, README known-unknowns.
